using System.Runtime.Intrinsics.X86;

namespace Borealis.Core.Hardware;

/// <summary>
/// CPU-Temperatur von AMD Zen (Familie 17h–1Ah) aus dem SMN-Raum der CPU: Tctl und je CCD.
/// Gelesen über das PawnIO-Modul AMDFamily17 (<c>ioctl_read_smn</c>: Index 0x60/Daten 0x64 des Host-Bridge 0:0.0),
/// unter der PCI-Sperre, die auch der ASUS-Lüfterdienst nimmt. Formeln wie Linux k10temp / LibreHardwareMonitor.
/// </summary>
public sealed class AmdCpuTemperature : IDisposable
{
    const uint ThmTconCurTmp = 0x00059800;

    readonly BusLock pci = new(BusLock.Pci);
    readonly PawnIoModule smn;
    readonly uint ccdBase;
    readonly int ccdCount;

    public int Family { get; }
    public int Model { get; }

    AmdCpuTemperature(PawnIoModule smn, int family, int model)
    {
        this.smn = smn;
        Family = family;
        Model = model;
        (ccdBase, ccdCount) = (family, model) switch
        {
            (0x19, 0x61) or (0x1A, 0x44) => (0x00059B08u, 8),   // Raphael (Ryzen 7000), Granite Ridge (Ryzen 9000)
            (0x1A, <= 0x2F) => (0x000599F0u, 16),                 // Turin
            _ => (0x00059954u, 8),
        };
    }

    /// <summary>Nur für AMD-CPUs ab Zen; sonst null. Wirft <see cref="PawnIoMissingException"/>, wenn PawnIO fehlt.</summary>
    public static AmdCpuTemperature? Create()
    {
        if (!X86Base.IsSupported) return null;
        var (_, ebx, ecx, edx) = X86Base.CpuId(0, 0);
        // „AuthenticAMD“
        if (ebx != 0x68747541 || edx != 0x69746E65 || ecx != 0x444D4163) return null;
        var (eax, _, _, _) = X86Base.CpuId(1, 0);
        int baseFamily = (eax >> 8) & 0xF, family = baseFamily == 0xF ? baseFamily + ((eax >> 20) & 0xFF) : baseFamily;
        int model = ((eax >> 4) & 0xF) | (((eax >> 16) & 0xF) << 4);
        if (family < 0x17) return null;
        return new AmdCpuTemperature(PawnIoModule.Load("AMDFamily17"), family, model);
    }

    /// <summary>Tctl (Steuertemperatur, nach der AMD und die Lüfter regeln) und die Temperaturen der aktiven CCDs.</summary>
    public (double Tctl, List<double> Ccds) Read()
    {
        if (!pci.Enter()) throw new TimeoutException("PCI-Sperre nicht frei.");
        try
        {
            uint raw = ReadSmn(ThmTconCurTmp);
            double tctl = (raw >> 21) * 0.125;
            // RANGE_SEL bzw. TJ_SEL: Wert ist um 49 °C verschoben
            if ((raw & 0x80000) != 0 || (raw & 0x30000) == 0x30000) tctl -= 49;
            var ccds = new List<double>();
            for (int i = 0; i < ccdCount; i++)
            {
                uint c = ReadSmn(ccdBase + (uint)i * 4) & 0xFFF;
                if (c == 0) continue;
                double t = ((int)c * 125 - 305000) * 0.001;
                if (t < 125) ccds.Add(t);
            }
            return (tctl, ccds);
        }
        finally { pci.Exit(); }
    }

    uint ReadSmn(uint smnAddress) => (uint)smn.Execute1("ioctl_read_smn", smnAddress);

    public void Dispose()
    {
        pci.Dispose();
        smn.Dispose();
    }
}
