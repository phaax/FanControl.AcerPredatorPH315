using FanControl.AcerPredatorPH315.PawnIO;
using System;
using System.Threading;

namespace FanControl.AcerPredatorPH315.Ec;

/// <summary>
/// ACPI Embedded Controller access for Acer Predator PH315-53, driven through the
/// PawnIO LpcACPIEC module. The module exposes ioctl_pio_read / ioctl_pio_write on
/// ports 0x62 (data) and 0x66 (status/command); we build the EC handshake on top.
///
/// Synchronization: the LpcACPIEC module requires us to hold the named mutex
/// "Global\Access_EC" across the handshake — that's the same mutex the ACPI driver
/// uses, so we don't race with Windows' own EC traffic.
/// </summary>
internal sealed class AcerEc : IDisposable {
    private const byte EC_DATA = 0x62;
    private const byte EC_SC = 0x66;

    private const byte RD_EC = 0x80;
    private const byte WR_EC = 0x81;

    private const byte SC_OBF = 0x01; // output buffer full -> a byte is ready to read
    private const byte SC_IBF = 0x02; // input buffer full  -> EC hasn't consumed our byte yet

    private const int PollIterations = 1000;

    private readonly PawnIOSession _pio;
    private readonly Mutex _ecMutex;

    public AcerEc( PawnIOSession pio ) {
        _pio = pio;
        // Same name the ACPI driver uses; "Global\" so we share across sessions.
        _ecMutex = new Mutex( initiallyOwned: false, name: @"Global\Access_EC" );
    }

    public byte ReadByte( byte register ) {
        AcquireMutex();
        try {
            WaitIbfClear();
            PortWrite( EC_SC, RD_EC );
            WaitIbfClear();
            PortWrite( EC_DATA, register );
            WaitObfSet();
            return PortRead( EC_DATA );
        } finally {
            _ecMutex.ReleaseMutex();
        }
    }

    public ushort ReadWord( byte register ) {
        // The NBFC config has ReadWriteWords=true, which means RPM-style sensors are
        // exposed as two consecutive byte registers, little-endian.
        var lo = ReadByte( register );
        var hi = ReadByte( (byte)(register + 1) );
        return (ushort)(lo | (hi << 8));
    }

    public void WriteByte( byte register, byte value ) {
        AcquireMutex();
        try {
            WaitIbfClear();
            PortWrite( EC_SC, WR_EC );
            WaitIbfClear();
            PortWrite( EC_DATA, register );
            WaitIbfClear();
            PortWrite( EC_DATA, value );
        } finally {
            _ecMutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Take the EC mutex or throw — never silently proceed without it, otherwise
    /// we race the ACPI driver on the EC and corrupt each other's transactions.
    /// AbandonedMutexException means a previous owner crashed mid-handshake;
    /// the OS still grants us ownership, so we treat it as success.
    /// </summary>
    private void AcquireMutex() {
        bool acquired;
        try {
            acquired = _ecMutex.WaitOne( TimeSpan.FromMilliseconds( 500 ) );
        } catch ( AbandonedMutexException ) {
            return;
        }

        if ( !acquired ) {
            throw new TimeoutException( "Could not acquire the EC mutex within 500ms" );
        }
    }

    private void WaitIbfClear() {
        for ( var i = 0; i < PollIterations; i++ ) {
            if ( (PortRead( EC_SC ) & SC_IBF) == 0 ) {
                return;
            }
        }
        throw new TimeoutException( "EC input buffer never drained (IBF stuck high)" );
    }

    private void WaitObfSet() {
        for ( var i = 0; i < PollIterations; i++ ) {
            if ( (PortRead( EC_SC ) & SC_OBF) != 0 ) {
                return;
            }
        }
        throw new TimeoutException( "EC output buffer never filled (OBF stuck low)" );
    }

    private byte PortRead( byte port ) {
        var value = _pio.ExecuteSingle( "ioctl_pio_read", [port] );
        return (byte)value;
    }

    private void PortWrite( byte port, byte value ) => _pio.Execute( "ioctl_pio_write", [port, value] );

    public void Dispose() => _ecMutex.Dispose();
}
