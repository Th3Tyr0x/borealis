using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Borealis.Core.Windows;

public enum ServiceState { NotInstalled = 0, Stopped = 1, StartPending = 2, StopPending = 3, Running = 4, ContinuePending = 5, PausePending = 6, Paused = 7 }

public enum ServiceStartType { Boot = 0, System = 1, Automatic = 2, Manual = 3, Disabled = 4 }

/// <summary>
/// Windows-Dienste abfragen, starten, stoppen und umkonfigurieren (advapi32, ohne NuGet-Pakete).
/// Wird für den ASUS-LightingService gebraucht, der sonst in die eigene Ausgabe hineinstreamt (§1.9).
/// </summary>
public static class ServiceControl
{
    /// <summary>Der ASUS-Dienst, der Aura-Effekte streamt.</summary>
    public const string AsusLightingService = "LightingService";
    /// <summary>Der ASUS-Lüfterdienst; er schreibt beim Start seine Kurven in den Lüfter-Chip (§2.3).</summary>
    public const string AsusFanService = "AsusFanControlService";

    public static ServiceState GetState(string name)
    {
        using var scm = OpenScm(Native.SC_MANAGER_CONNECT);
        using var svc = TryOpen(scm, name, Native.SERVICE_QUERY_STATUS);
        if (svc == null) return ServiceState.NotInstalled;
        return (ServiceState)Query(svc).dwCurrentState;
    }

    public static ServiceStartType? GetStartType(string name)
    {
        using var scm = OpenScm(Native.SC_MANAGER_CONNECT);
        using var svc = TryOpen(scm, name, Native.SERVICE_QUERY_CONFIG);
        if (svc == null) return null;
        Native.QueryServiceConfig(svc, IntPtr.Zero, 0, out uint needed);
        IntPtr buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!Native.QueryServiceConfig(svc, buf, needed, out _)) throw new Win32Exception();
            // QUERY_SERVICE_CONFIGW: dwServiceType, dwStartType, …
            return (ServiceStartType)Marshal.ReadInt32(buf, 4);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>Stoppt den Dienst und wartet, bis er steht. Gibt false zurück, wenn er nicht installiert ist.</summary>
    public static bool Stop(string name, int timeoutMs = 20000)
    {
        using var scm = OpenScm(Native.SC_MANAGER_CONNECT);
        using var svc = TryOpen(scm, name, Native.SERVICE_STOP | Native.SERVICE_QUERY_STATUS);
        if (svc == null) return false;
        var st = Query(svc);
        if (st.dwCurrentState != (int)ServiceState.Stopped && st.dwCurrentState != (int)ServiceState.StopPending)
        {
            var status = new Native.SERVICE_STATUS();
            if (!Native.ControlService(svc, Native.SERVICE_CONTROL_STOP, ref status))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != Native.ERROR_SERVICE_NOT_ACTIVE) throw new Win32Exception(err);
            }
        }
        WaitFor(svc, ServiceState.Stopped, timeoutMs);
        return true;
    }

    public static bool Start(string name, int timeoutMs = 20000)
    {
        using var scm = OpenScm(Native.SC_MANAGER_CONNECT);
        using var svc = TryOpen(scm, name, Native.SERVICE_START | Native.SERVICE_QUERY_STATUS);
        if (svc == null) return false;
        if (Query(svc).dwCurrentState != (int)ServiceState.Running)
        {
            if (!Native.StartService(svc, 0, IntPtr.Zero))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != Native.ERROR_SERVICE_ALREADY_RUNNING) throw new Win32Exception(err);
            }
            WaitFor(svc, ServiceState.Running, timeoutMs);
        }
        return true;
    }

    public static void SetStartType(string name, ServiceStartType startType)
    {
        using var scm = OpenScm(Native.SC_MANAGER_CONNECT);
        using var svc = TryOpen(scm, name, Native.SERVICE_CHANGE_CONFIG)
            ?? throw new InvalidOperationException($"Dienst {name} ist nicht installiert.");
        if (!Native.ChangeServiceConfig(svc, Native.SERVICE_NO_CHANGE, (uint)startType, Native.SERVICE_NO_CHANGE,
                null, null, IntPtr.Zero, null, null, null, null))
            throw new Win32Exception();
    }

    /// <summary>Legt einen Dienst (eigener Prozess, LocalSystem) an oder aktualisiert Pfad und Starttyp eines vorhandenen.</summary>
    public static void Install(string name, string displayName, string description, string binaryPath, ServiceStartType startType)
    {
        using var scm = OpenScm(Native.SC_MANAGER_CONNECT | Native.SC_MANAGER_CREATE_SERVICE);
        string quoted = binaryPath.StartsWith('"') ? binaryPath : $"\"{binaryPath}\"";
        // SERVICE_START: nötig, um Neustart nach Absturz als Fehleraktion einzutragen
        var svc = TryOpen(scm, name, Native.SERVICE_CHANGE_CONFIG | Native.SERVICE_START);
        if (svc == null)
        {
            svc = Native.CreateService(scm, name, displayName, Native.SERVICE_CHANGE_CONFIG | Native.SERVICE_QUERY_STATUS | Native.SERVICE_START,
                Native.SERVICE_WIN32_OWN_PROCESS, (uint)startType, Native.SERVICE_ERROR_NORMAL, quoted,
                null, IntPtr.Zero, null, null, null);
            if (svc.IsInvalid) throw new Win32Exception();
        }
        else if (!Native.ChangeServiceConfig(svc, Native.SERVICE_NO_CHANGE, (uint)startType, Native.SERVICE_NO_CHANGE,
                     quoted, null, IntPtr.Zero, null, null, null, displayName))
            throw new Win32Exception();
        using (svc)
        {
            var desc = new Native.SERVICE_DESCRIPTION { lpDescription = description };
            Native.ChangeServiceConfig2(svc, Native.SERVICE_CONFIG_DESCRIPTION, ref desc);
            // Nach einem Absturz neu starten (nach 5 s, 5 s, 30 s); der Zähler beginnt nach einem Tag neu
            var actions = new Native.SC_ACTION[]
            {
                new() { Type = Native.SC_ACTION_RESTART, Delay = 5000 },
                new() { Type = Native.SC_ACTION_RESTART, Delay = 5000 },
                new() { Type = Native.SC_ACTION_RESTART, Delay = 30000 },
            };
            var pinned = GCHandle.Alloc(actions, GCHandleType.Pinned);
            try
            {
                var failure = new Native.SERVICE_FAILURE_ACTIONS { dwResetPeriod = 86400, cActions = (uint)actions.Length, lpsaActions = pinned.AddrOfPinnedObject() };
                if (!Native.ChangeServiceConfig2(svc, Native.SERVICE_CONFIG_FAILURE_ACTIONS, ref failure)) throw new Win32Exception();
            }
            finally { pinned.Free(); }
        }
    }

    /// <summary>Stoppt und entfernt einen Dienst. Gibt false zurück, wenn er nicht installiert ist.</summary>
    public static bool Uninstall(string name)
    {
        if (!Stop(name)) return false;
        using var scm = OpenScm(Native.SC_MANAGER_CONNECT);
        using var svc = TryOpen(scm, name, Native.DELETE);
        if (svc == null) return false;
        if (!Native.DeleteService(svc)) throw new Win32Exception();
        return true;
    }

    static void WaitFor(Native.ScHandle svc, ServiceState target, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Query(svc).dwCurrentState != (int)target)
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException($"Dienst erreicht den Zustand {target} nicht.");
            Thread.Sleep(200);
        }
    }

    static Native.SERVICE_STATUS Query(Native.ScHandle svc)
    {
        var st = new Native.SERVICE_STATUS();
        if (!Native.QueryServiceStatus(svc, ref st)) throw new Win32Exception();
        return st;
    }

    static Native.ScHandle OpenScm(uint access)
    {
        var h = Native.OpenSCManager(null, null, access);
        if (h.IsInvalid) throw new Win32Exception();
        return h;
    }

    static Native.ScHandle? TryOpen(Native.ScHandle scm, string name, uint access)
    {
        var h = Native.OpenService(scm, name, access);
        if (!h.IsInvalid) return h;
        int err = Marshal.GetLastWin32Error();
        h.Dispose();
        if (err == Native.ERROR_SERVICE_DOES_NOT_EXIST) return null;
        throw new Win32Exception(err);
    }

    internal static class Native
    {
        public const uint SC_MANAGER_CONNECT = 0x0001, SC_MANAGER_CREATE_SERVICE = 0x0002;
        public const uint SERVICE_QUERY_CONFIG = 0x0001, SERVICE_CHANGE_CONFIG = 0x0002, SERVICE_QUERY_STATUS = 0x0004,
            SERVICE_START = 0x0010, SERVICE_STOP = 0x0020, DELETE = 0x10000;
        public const uint SERVICE_CONTROL_STOP = 1;
        public const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
        public const uint SERVICE_WIN32_OWN_PROCESS = 0x10, SERVICE_ERROR_NORMAL = 1, SERVICE_CONFIG_DESCRIPTION = 1,
            SERVICE_CONFIG_FAILURE_ACTIONS = 2, SC_ACTION_RESTART = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_DESCRIPTION { public string? lpDescription; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_FAILURE_ACTIONS
        {
            public uint dwResetPeriod;
            public string? lpRebootMsg;
            public string? lpCommand;
            public uint cActions;
            public IntPtr lpsaActions;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SC_ACTION { public uint Type; public uint Delay; }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateServiceW")]
        public static extern ScHandle CreateService(ScHandle scm, string name, string displayName, uint access, uint type,
            uint startType, uint errorControl, string binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies,
            string? account, string? password);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool DeleteService(ScHandle svc);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ChangeServiceConfig2W")]
        public static extern bool ChangeServiceConfig2(ScHandle svc, uint infoLevel, ref SERVICE_DESCRIPTION info);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ChangeServiceConfig2W")]
        public static extern bool ChangeServiceConfig2(ScHandle svc, uint infoLevel, ref SERVICE_FAILURE_ACTIONS info);
        public const int ERROR_SERVICE_DOES_NOT_EXIST = 1060, ERROR_SERVICE_NOT_ACTIVE = 1062, ERROR_SERVICE_ALREADY_RUNNING = 1056;

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_STATUS
        {
            public int dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
        }

        public sealed class ScHandle : SafeHandle
        {
            public ScHandle() : base(IntPtr.Zero, true) { }
            public override bool IsInvalid => handle == IntPtr.Zero;
            protected override bool ReleaseHandle() => CloseServiceHandle(handle);
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenSCManagerW")]
        public static extern ScHandle OpenSCManager(string? machine, string? database, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenServiceW")]
        public static extern ScHandle OpenService(ScHandle scm, string name, uint access);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CloseServiceHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool QueryServiceStatus(ScHandle svc, ref SERVICE_STATUS status);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool ControlService(ScHandle svc, uint control, ref SERVICE_STATUS status);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "StartServiceW")]
        public static extern bool StartService(ScHandle svc, uint argc, IntPtr argv);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryServiceConfigW")]
        public static extern bool QueryServiceConfig(ScHandle svc, IntPtr buf, uint size, out uint needed);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ChangeServiceConfigW")]
        public static extern bool ChangeServiceConfig(ScHandle svc, uint type, uint startType, uint errorControl,
            string? binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? account, string? password, string? displayName);
    }
}
