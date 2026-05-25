using FanControl.AcerPredatorPH315.Ec;
using FanControl.Plugins;
using System;

namespace FanControl.AcerPredatorPH315.Sensors;

/// <summary>
/// Writable fan-duty control. Implements IPluginControlSensor2 so FanControl can
/// auto-pair it with the matching speed sensor in the UI.
/// </summary>
internal sealed class PredatorFanControlSensor : IPluginControlSensor2 {
    private const byte DefaultDuty = 50;

    private readonly AcerEc _ec;
    private readonly byte _register;

    public string Id { get; }
    public string Name { get; }
    public string PairedFanSensorId { get; }

    /// <summary>Last value we wrote (FanControl reads this back to draw the line).</summary>
    public float? Value { get; private set; }

    public PredatorFanControlSensor( string id, string name, string pairedFanSensorId, AcerEc ec, byte register ) {
        Id = id;
        Name = name;
        PairedFanSensorId = pairedFanSensorId;
        _ec = ec;
        _register = register;
    }

    public void Update() {
        /* no-op: write-only register */
    }

    public void Set( float val ) {
        var duty = ClampToByte( val );
        _ec.WriteByte( _register, duty );
        Value = duty;
    }

    public void Reset() {
        // We've already restored manual-mode regs in the plugin's Close path; while
        // the plugin is alive but a curve is detached, fall back to a sane fixed duty.
        _ec.WriteByte( _register, DefaultDuty );
        Value = DefaultDuty;
    }

    private static byte ClampToByte( float val ) => float.IsNaN( val ) || val < 0f ? (byte)0 : val > 100f ? (byte)100 : (byte)Math.Round( val );
}
