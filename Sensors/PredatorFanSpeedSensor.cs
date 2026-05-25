using FanControl.AcerPredatorPH315.Ec;
using FanControl.Plugins;

namespace FanControl.AcerPredatorPH315.Sensors;

/// <summary>
/// Read-only fan speed sensor. Returns the raw EC value (0..~6122) which the
/// NBFC config calibrates as the maximum reported speed. FanControl labels fan
/// sensors as RPM in the UI; the raw value is close enough to an RPM reading
/// to be useful and matches what NBFC users are used to.
/// </summary>
internal sealed class PredatorFanSpeedSensor : IPluginSensor {
    private readonly AcerEc _ec;
    private readonly byte _register;

    public string Id { get; }
    public string Name { get; }
    public float? Value { get; private set; }

    public PredatorFanSpeedSensor( string id, string name, AcerEc ec, byte register ) {
        Id = id;
        Name = name;
        _ec = ec;
        _register = register;
    }

    public void Update() {
        try {
            var raw = _ec.ReadWord( _register );
            Value = raw;
        } catch {
            // ACPI driver likely grabbed the EC; leave the previous value so the
            // graph doesn't flap to zero.
        }
    }
}
