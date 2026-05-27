using FanControl.AcerPredatorPH315.Diagnostics;
using FanControl.AcerPredatorPH315.Ec;
using FanControl.AcerPredatorPH315.PawnIO;
using FanControl.AcerPredatorPH315.Sensors;
using FanControl.Plugins;
using System;

namespace FanControl.AcerPredatorPH315;

public sealed class AcerPredatorPlugin : IPlugin2 {
    private const string LpcModuleResource = "FanControl.AcerPredatorPH315.LpcACPIEC.bin";

    private const string CpuRpmId = "acer.ph315-53.cpu.rpm";
    private const string GpuRpmId = "acer.ph315-53.gpu.rpm";
    private const string CpuCtlId = "acer.ph315-53.cpu.ctl";
    private const string GpuCtlId = "acer.ph315-53.gpu.ctl";

    private readonly IPluginLogger? _logger;

    private PawnIOSession? _lpcSession;
    private AcerEc? _ec;

    private PredatorFanSpeedSensor? _cpuRpm;
    private PredatorFanSpeedSensor? _gpuRpm;
    private PredatorFanControlSensor? _cpuCtl;
    private PredatorFanControlSensor? _gpuCtl;

    public AcerPredatorPlugin() { }
    public AcerPredatorPlugin( IPluginLogger logger ) { _logger = logger; }

    public string Name => "Acer Predator PH315-53";

    public void Initialize() {
        try {
            PluginLog.Info( "Initialize: starting" );
            _lpcSession = PawnIOSession.LoadEmbeddedModule( LpcModuleResource );
            _ec = new AcerEc( _lpcSession );

            // Take control away from the EC's automatic fan curve and silence CoolBoost
            // so duty writes actually stick.
            _ec.WriteByte( PredatorRegisters.CpuManualReg, PredatorRegisters.CpuManualOn );
            _ec.WriteByte( PredatorRegisters.GpuManualReg, PredatorRegisters.GpuManualOn );
            _ec.WriteByte( PredatorRegisters.CoolBoostReg, PredatorRegisters.CoolBoostOff );
            PluginLog.Info( "Initialize: EC ready (manual mode)" );
        } catch ( Exception ex ) {
            PluginLog.Error( "Initialize failed", ex );
            Close();
            throw;
        }
    }

    public void Load( IPluginSensorsContainer container ) {
        if ( _ec is null ) {
            throw new InvalidOperationException( "Initialize() was not called" );
        }

        _cpuRpm = new PredatorFanSpeedSensor( CpuRpmId, "CPU fan speed", _ec, PredatorRegisters.CpuFanReadLo );
        _gpuRpm = new PredatorFanSpeedSensor( GpuRpmId, "GPU fan speed", _ec, PredatorRegisters.GpuFanReadLo );
        _cpuCtl = new PredatorFanControlSensor( CpuCtlId, "CPU fan control", CpuRpmId, _ec, PredatorRegisters.CpuFanWrite );
        _gpuCtl = new PredatorFanControlSensor( GpuCtlId, "GPU fan control", GpuRpmId, _ec, PredatorRegisters.GpuFanWrite );

        // Prime each sensor with an initial read so Value is non-null by the
        // time FanControl builds its sensor picker.
        _cpuRpm.Update();
        _gpuRpm.Update();

        container.FanSensors.Add( _cpuRpm );
        container.FanSensors.Add( _gpuRpm );
        container.ControlSensors.Add( _cpuCtl );
        container.ControlSensors.Add( _gpuCtl );

        PluginLog.Info( $"Load: registered {container.FanSensors.Count} fan, " +
                        $"{container.ControlSensors.Count} control sensors" );
    }

    public void Update() {
        _cpuRpm?.Update();
        _gpuRpm?.Update();
    }

    public void Close() {
        try {
            // Hand the EC back to the BIOS so the laptop keeps cooling itself if
            // FanControl exits.
            if ( _ec is not null ) {
                TrySafeWrite( PredatorRegisters.CpuManualReg, PredatorRegisters.CpuManualOff );
                TrySafeWrite( PredatorRegisters.GpuManualReg, PredatorRegisters.GpuManualOff );
                TrySafeWrite( PredatorRegisters.CoolBoostReg, PredatorRegisters.CoolBoostOn );
            }
        } finally {
            _ec?.Dispose();
            _ec = null;
            _lpcSession?.Dispose();
            _lpcSession = null;
        }
    }

    private void TrySafeWrite( byte reg, byte val ) {
        try {
            _ec!.WriteByte( reg, val );
        } catch ( Exception ex ) {
            _logger?.Log( $"[Acer Predator PH315-53] reset write reg=0x{reg:X2} failed: {ex.Message}" );
        }
    }
}
