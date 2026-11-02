using System.Runtime.InteropServices;

namespace Borealis.Service;

/// <summary>
/// Minimal Windows service host via advapi32 (StartServiceCtrlDispatcher, RegisterServiceCtrlHandlerEx, SetServiceStatus); replaces Microsoft.Extensions.Hosting.WindowsServices without NuGet.
/// Minimaler Windows-Dienst-Rahmen über advapi32 (StartServiceCtrlDispatcher, RegisterServiceCtrlHandlerEx, SetServiceStatus); ersetzt Microsoft.Extensions.Hosting.WindowsServices ohne NuGet.
/// </summary>
sealed class ServiceHost
{
    readonly string name;
    readonly Func<CancellationToken, Task> run;
    readonly CancellationTokenSource stopping = new();
    // Keep the delegates alive, or the GC collects them while Windows still calls them
    // Delegaten festhalten, sonst sammelt der GC sie ein, während Windows sie noch aufruft
    readonly Native.ServiceMain mainDelegate;
    readonly Native.HandlerEx handlerDelegate;
    IntPtr statusHandle;
    Native.SERVICE_STATUS status;

    public ServiceHost(string name, Func<CancellationToken, Task> run)
    {
        this.name = name;
        this.run = run;
        mainDelegate = ServiceMain;
        handlerDelegate = Handler;
    }

    /// <summary>Blocks until the service ends; false if the process wasn't started by the SCM.
    /// Blockiert, bis der Dienst endet; false, wenn der Prozess nicht vom SCM gestartet wurde.</summary>
    public bool RunAsService()
    {
        var table = new Native.SERVICE_TABLE_ENTRY[]
        {
            new() { lpServiceName = name, lpServiceProc = Marshal.GetFunctionPointerForDelegate(mainDelegate) },
            new() { lpServiceName = null, lpServiceProc = IntPtr.Zero },
        };
        if (Native.StartServiceCtrlDispatcher(table)) return true;
        const int ERROR_FAILED_SERVICE_CONTROLLER_CONNECT = 1063;
        if (Marshal.GetLastWin32Error() == ERROR_FAILED_SERVICE_CONTROLLER_CONNECT) return false;
        throw new System.ComponentModel.Win32Exception();
    }

    void ServiceMain(int argc, IntPtr argv)
    {
        statusHandle = Native.RegisterServiceCtrlHandlerEx(name, handlerDelegate, IntPtr.Zero);
        status.dwServiceType = Native.SERVICE_WIN32_OWN_PROCESS;
        SetState(Native.SERVICE_START_PENDING, 0);
        var task = Task.Run(() => run(stopping.Token));
        SetState(Native.SERVICE_RUNNING, Native.SERVICE_ACCEPT_STOP | Native.SERVICE_ACCEPT_SHUTDOWN | Native.SERVICE_ACCEPT_POWEREVENT);
        int exitCode = 0;
        try { task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Borealis.Core.Diagnostics.Log.Error("Dienst beendet mit Fehler", ex);
            exitCode = 1;
        }
        status.dwWin32ExitCode = exitCode;
        SetState(Native.SERVICE_STOPPED, 0);
    }

    /// <summary>Called on standby/resume (PBT_APMRESUMEAUTOMATIC = 0x12, PBT_APMSUSPEND = 0x4).
    /// Aufruf bei Standby/Fortsetzen (PBT_APMRESUMEAUTOMATIC = 0x12, PBT_APMSUSPEND = 0x4).</summary>
    public event Action<int>? PowerEvent;

    int Handler(int control, int eventType, IntPtr eventData, IntPtr context)
    {
        switch (control)
        {
            case Native.SERVICE_CONTROL_STOP:
            case Native.SERVICE_CONTROL_SHUTDOWN:
                SetState(Native.SERVICE_STOP_PENDING, 0);
                stopping.Cancel();
                return 0;
            case Native.SERVICE_CONTROL_POWEREVENT:
                PowerEvent?.Invoke(eventType);
                return 0;
            case Native.SERVICE_CONTROL_INTERROGATE:
                return 0;
            default:
                return Native.ERROR_CALL_NOT_IMPLEMENTED;
        }
    }

    void SetState(int state, int accepted)
    {
        status.dwCurrentState = state;
        status.dwControlsAccepted = accepted;
        status.dwWaitHint = state is Native.SERVICE_START_PENDING or Native.SERVICE_STOP_PENDING ? 10000 : 0;
        Native.SetServiceStatus(statusHandle, ref status);
    }

    static class Native
    {
        public const int SERVICE_WIN32_OWN_PROCESS = 0x10;
        public const int SERVICE_STOPPED = 1, SERVICE_START_PENDING = 2, SERVICE_STOP_PENDING = 3, SERVICE_RUNNING = 4;
        public const int SERVICE_ACCEPT_STOP = 0x1, SERVICE_ACCEPT_SHUTDOWN = 0x4, SERVICE_ACCEPT_POWEREVENT = 0x40;
        public const int SERVICE_CONTROL_STOP = 1, SERVICE_CONTROL_INTERROGATE = 4, SERVICE_CONTROL_SHUTDOWN = 5, SERVICE_CONTROL_POWEREVENT = 13;
        public const int ERROR_CALL_NOT_IMPLEMENTED = 120;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_TABLE_ENTRY
        {
            public string? lpServiceName;
            public IntPtr lpServiceProc;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_STATUS
        {
            public int dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void ServiceMain(int argc, IntPtr argv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int HandlerEx(int control, int eventType, IntPtr eventData, IntPtr context);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "StartServiceCtrlDispatcherW")]
        public static extern bool StartServiceCtrlDispatcher(SERVICE_TABLE_ENTRY[] table);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegisterServiceCtrlHandlerExW")]
        public static extern IntPtr RegisterServiceCtrlHandlerEx(string name, HandlerEx handler, IntPtr context);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool SetServiceStatus(IntPtr handle, ref SERVICE_STATUS status);
    }
}
