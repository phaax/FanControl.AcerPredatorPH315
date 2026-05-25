namespace FanControl.AcerPredatorPH315.Ec;

/// <summary>
/// Register map for the Acer Predator PH315-53, transcribed from the NBFC config
/// "Acer Predator PH315-53.xml" by Arghadip Deb and corroborated by the
/// only-laptop-fans Python implementation. NBFC stores registers in decimal — the
/// hex values are added here as comments for sanity.
///
/// Fans report "RPM" as a value in 0..MaxRawSpeedRead that NBFC treats as a 0..100
/// scale, NOT a real RPM. The reading is also flaky because the ACPI driver
/// frequently wins arbitration on the read path — writes are reliable, reads are
/// best-effort.
/// </summary>
internal static class PredatorRegisters {
    // RPM (word reads, little-endian over reg, reg+1)
    public const byte CpuFanReadLo = 19;  // 0x13
    public const byte GpuFanReadLo = 21;  // 0x15
    public const ushort MaxRawSpeedRead = 6122;

    // Fan duty writes (0..100)
    public const byte CpuFanWrite = 55;  // 0x37
    public const byte GpuFanWrite = 58;  // 0x3A

    // Init regs — flip the EC into manual-fan mode and disable CoolBoost.
    public const byte CpuManualReg = 34;  // 0x22
    public const byte CpuManualOn = 12;  // 0x0C
    public const byte CpuManualOff = 4;   // 0x04   (BIOS default; restored on Close)

    public const byte GpuManualReg = 33;  // 0x21
    public const byte GpuManualOn = 48;  // 0x30
    public const byte GpuManualOff = 16;  // 0x10

    public const byte CoolBoostReg = 16;  // 0x10
    public const byte CoolBoostOff = 0;
    public const byte CoolBoostOn = 1;
}
