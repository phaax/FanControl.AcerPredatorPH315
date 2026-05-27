using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;

namespace FanControl.AcerPredatorPH315.Probe;

/// <summary>
/// Standalone console probe for the Acer Predator PH315-53 gaming WMI surface.
/// No EC / PawnIO dependency — pure WMI. Built so we can iterate on probes
/// without restarting FanControl between every test.
///
/// Manifest requests administrator at launch; the gaming WMI Set* methods need it.
/// </summary>
internal static class Program {
    private const string TargetGuid = "{7A4DDFE7-5B5D-40B4-8595-4408E0CC7F56}";
    private const string WmiNamespace = @"root\WMI";

    private static int Main( string[] args ) {
        if ( args.Length == 0 ) {
            PrintUsage();
            return 1;
        }
        var directive = args[0];

        try {
            switch ( directive.ToLowerInvariant() ) {
                case "enum":
                    RunEnum();
                    break;
                case "getall":
                    RunGetAll();
                    break;
                case "behavior-hunt":
                    RunBehaviorHunt();
                    break;
                case "speed-cpu":
                    RunSpeedSweep( fanId: 0x00, label: "CPU" );
                    break;
                case "speed-gpu":
                    RunSpeedSweep( fanId: 0x01, label: "GPU" );
                    break;
                case "fan-auto":
                    RunFanBehaviorInvoke( 0x0055000F, "fan-auto" );
                    break;
                case "fan-turbo":
                    RunFanBehaviorInvoke( 0x00AA000F, "fan-turbo" );
                    break;
                case "fan-experiment":
                    RunFanExperiment();
                    break;
                case "profile-hunt":
                    RunProfileHunt();
                    break;
                case "watch":
                    var intervalMs = args.Length > 1 && int.TryParse( args[1], out var v ) ? v : 500;
                    RunWatch( intervalMs );
                    break;
                case "raw":
                    if ( args.Length < 3 ) { Console.Error.WriteLine( "usage: raw <methodName> <hexArg>" ); return 1; }
                    var arg = Convert.ToUInt64( args[2], 16 );
                    RunRawInvoke( args[1], arg );
                    break;
                default:
                    Console.Error.WriteLine( $"unknown directive: {directive}" );
                    PrintUsage();
                    return 1;
            }
            return 0;
        } catch ( Exception ex ) {
            Console.Error.WriteLine( $"!! probe threw: {ex}" );
            return 2;
        }
    }

    private static void PrintUsage() {
        Console.WriteLine( "AcerProbe <directive>" );
        Console.WriteLine( "  enum            - dump Acer gaming WMI class methods/properties" );
        Console.WriteLine( "  getall          - call every Get* method with selectors 0..4" );
        Console.WriteLine( "  behavior-hunt   - try multiple SetGamingFanBehavior modes" );
        Console.WriteLine( "  speed-cpu       - sweep SetGamingFanSpeed CPU through 0..100 with verify" );
        Console.WriteLine( "  speed-gpu       - sweep SetGamingFanSpeed GPU through 0..100 with verify" );
        Console.WriteLine( "  fan-auto        - SetGamingFanBehavior(0x0055000F) (Linux 'auto' value)" );
        Console.WriteLine( "  fan-turbo       - SetGamingFanBehavior(0x00AA000F) (Linux 'turbo' value)" );
        Console.WriteLine( "  fan-experiment  - SetGamingFanBehavior with experimental values" );
        Console.WriteLine( "  profile-hunt    - SetGamingProfile sweep to find a mode that unlocks SetGamingFanSpeed" );
        Console.WriteLine( "  watch [intervalMs] - poll all Get* methods, log diffs (Ctrl-C to stop)" );
        Console.WriteLine( "  raw <name> <hexarg> - call any method with a UInt64 hex arg, e.g. raw SetGamingFanSpeed 0x6400" );
    }

    // ---- Directives ----

    private static void RunEnum() {
        Log( $"=== enum @ {DateTime.Now:HH:mm:ss.fff} ===" );
        var (className, _) = FindGamingClass( logMethodsToo: true );
        if ( className is null ) {
            Log( "no class found" );
        } else {
            Log( $"gaming class: {className}" );
        }
    }

    private static void RunGetAll() {
        Log( $"=== getall @ {DateTime.Now:HH:mm:ss.fff} ===" );
        var (className, _) = FindGamingClass( logMethodsToo: false );
        if ( className is null ) { Log( "no class found" ); return; }
        Log( $"target class: {className}" );

        var scope = new ManagementScope( WmiNamespace );
        scope.Connect();
        var path = new ManagementPath { ClassName = className, NamespacePath = WmiNamespace };
        using var cls = new ManagementClass( scope, path, null );
        var instance = GetSingleInstance( scope, className );
        if ( instance is null ) { Log( "no instance" ); return; }

        using ( instance ) {
            foreach ( var m in cls.Methods ) {
                if ( !m.Name.StartsWith( "Get", StringComparison.Ordinal ) ) {
                    continue;
                }

                CallGet( cls, instance, m );
            }
        }
    }

    private static void CallGet( ManagementClass cls, ManagementObject instance, MethodData method ) {
        ManagementBaseObject? inParams = null;
        try { inParams = cls.GetMethodParameters( method.Name ); } catch ( Exception ex ) { Log( $"  {method.Name}: GetMethodParameters threw {ex.Message}" ); }

        if ( inParams is null || !inParams.Properties.Cast<PropertyData>().Any() ) {
            InvokeAndLog( instance, method.Name, inParams, "<no input>" );
            return;
        }

        // Log the parameter shape so we can see what we're feeding.
        var paramDesc = string.Join( ", ", inParams.Properties.Cast<PropertyData>()
            .Select( p => $"{p.Type}{(p.IsArray ? "[]" : "")} {p.Name}" ) );
        Log( $"  {method.Name}: input params = ({paramDesc})" );

        foreach ( var sel in new uint[] { 0, 1, 2, 3, 4 } ) {
            try {
                var first = inParams.Properties.Cast<PropertyData>().First();
                // Match the property's expected type. Array params get a 1-element array
                // of the right CIM type; scalars get the value cast appropriately.
                first.Value = CoerceSelector( first, sel );
                InvokeAndLog( instance, method.Name, inParams, $"input=0x{sel:X8}" );
            } catch ( Exception ex ) {
                Log( $"  {method.Name} input=0x{sel:X8} -> SETUP THREW {ex.GetType().Name}: {ex.Message}" );
            }
        }
    }

    private static object CoerceSelector( PropertyData prop, uint sel ) {
        if ( prop.IsArray ) {
            // Single-element array of the right element type.
            return prop.Type switch {
                CimType.UInt8 => new byte[] { (byte)sel },
                CimType.UInt16 => new ushort[] { (ushort)sel },
                CimType.UInt32 => new uint[] { sel },
                CimType.UInt64 => new ulong[] { sel },
                _ => new uint[] { sel },
            };
        }
        return prop.Type switch {
            CimType.UInt8 => (byte)sel,
            CimType.UInt16 => (ushort)sel,
            CimType.UInt32 => sel,
            CimType.UInt64 => (ulong)sel,
            _ => sel,
        };
    }

    private static void RunSpeedSweep( byte fanId, string label ) {
        var sweep = new byte[] { 0, 10, 20, 30, 40, 50, 60, 80, 100 };
        Log( $"=== speed-sweep ({label} fanId=0x{fanId:X2}) @ {DateTime.Now:HH:mm:ss.fff} ===" );
        var (className, _) = FindGamingClass( logMethodsToo: false );
        if ( className is null ) { Log( "no class" ); return; }

        LogFanState( className, "baseline" );
        foreach ( var speed in sweep ) {
            var packed = ((ulong)speed << 8) | fanId;
            Log( $"SetGamingFanSpeed(0x{packed:X16})  // speed={speed} fanId={fanId}" );
            var ret = InvokeMethod( className, "SetGamingFanSpeed", packed );
            Log( $"  return={ret}" );
            Thread.Sleep( 1500 );
            LogFanState( className, $"after speed={speed}" );
        }
        InvokeMethod( className, "SetGamingFanSpeed", (50UL << 8) | fanId );
        Log( "restored speed=50" );
    }

    private static void RunBehaviorHunt() {
        Log( $"=== behavior-hunt @ {DateTime.Now:HH:mm:ss.fff} ===" );
        var (className, _) = FindGamingClass( logMethodsToo: false );
        if ( className is null ) { Log( "no class" ); return; }

        var originalBytes = ReadBytes( className, "GetGamingFanBehavior", 1u );
        var originalByte = originalBytes is null ? (byte)0x03 : originalBytes[1];
        Log( $"baseline CPU behavior byte = 0x{originalByte:X2}" );

        byte[] modes = [0x00, 0x01, 0x02, 0x04, 0x05, 0x55, 0xAA, 0xFF];
        byte fanId = 0x00;

        foreach ( var mode in modes ) {
            var packed = ((ulong)mode << 8) | fanId;
            Log( $"-- mode 0x{mode:X2} --" );
            Log( $"SetGamingFanBehavior(0x{packed:X16})" );
            var ret = InvokeMethod( className, "SetGamingFanBehavior", packed );
            Log( $"  return={ret}" );

            var after = ReadBytes( className, "GetGamingFanBehavior", 1u );
            Log( $"  Get(1) bytes = [{FormatBytes( after )}]" );

            if ( after is null || after[1] != mode ) {
                Log( "  mode NOT applied; skip speed test" );
                continue;
            }

            // Mode took. Try speed=100, verify.
            var speedPacked = (100UL << 8) | fanId;
            Log( $"  SetGamingFanSpeed(0x{speedPacked:X16})  // speed=100" );
            var ret2 = InvokeMethod( className, "SetGamingFanSpeed", speedPacked );
            Log( $"    return={ret2}" );
            var speedAfter = ReadBytes( className, "GetGamingFanSpeed", 1u );
            Log( $"    Get(1) bytes = [{FormatBytes( speedAfter )}]" );
            if ( speedAfter is not null && speedAfter[1] == 100 ) {
                Log( "    *** SPEED WRITE ACCEPTED ***" );
                Thread.Sleep( 4000 );
                Log( $"    Get(1) after 4s = [{FormatBytes( ReadBytes( className, "GetGamingFanSpeed", 1u ) )}]" );
                // Try the goal value: speed=0
                InvokeMethod( className, "SetGamingFanSpeed", fanId );
                Thread.Sleep( 4000 );
                Log( $"    after speed=0 + 4s: Get(1) = [{FormatBytes( ReadBytes( className, "GetGamingFanSpeed", 1u ) )}]" );
                InvokeMethod( className, "SetGamingFanSpeed", (50UL << 8) | fanId );
            } else {
                Log( "    speed rejected in this mode" );
            }
        }

        Log( $"restoring SetGamingFanBehavior to original mode 0x{originalByte:X2}" );
        InvokeMethod( className, "SetGamingFanBehavior", ((ulong)originalByte << 8) | fanId );
        InvokeMethod( className, "SetGamingFanSpeed", (50UL << 8) | fanId );
    }

    private static void RunFanBehaviorInvoke( uint argument, string label ) {
        Log( $"=== {label} (arg=0x{argument:X8}) @ {DateTime.Now:HH:mm:ss.fff} ===" );
        var (className, _) = FindGamingClass( logMethodsToo: false );
        if ( className is null ) { Log( "no class" ); return; }
        var ret = InvokeMethod( className, "SetGamingFanBehavior", argument );
        Log( $"return={ret}" );
        Thread.Sleep( 2000 );
        LogFanState( className, "after-2s" );
    }

    private static void RunFanExperiment() {
        Log( $"=== fan-experiment @ {DateTime.Now:HH:mm:ss.fff} ===" );
        uint[] vals = [0x0000000F, 0x00FF000F, 0x0055000C, 0x00000000];
        foreach ( var v in vals ) {
            RunFanBehaviorInvoke( v, $"experiment 0x{v:X8}" );
            Thread.Sleep( 2000 );
        }
        RunFanBehaviorInvoke( 0x0055000F, "restore-auto" );
    }

    /// <summary>
    /// Sweep SetGamingProfile through plausible byte[2] (fan-behavior) values.
    /// The profile blob shape we observed is [00 00 BB 00 00 00 FF 00] where BB
    /// is the fan-behavior byte. After each Set we read profile and try a
    /// SetGamingFanSpeed(100) to detect which mode unlocks speed writes.
    /// </summary>
    private static void RunProfileHunt() {
        Log( $"=== profile-hunt @ {DateTime.Now:HH:mm:ss.fff} ===" );
        var (className, _) = FindGamingClass( logMethodsToo: false );
        if ( className is null ) { Log( "no class" ); return; }

        // Baseline profile and current behavior byte (the byte that historically
        // controls fan policy).
        var originalProfileBytes = ReadBytes( className, "GetGamingProfile", 0u );
        Log( $"baseline profile = [{FormatBytes( originalProfileBytes )}]" );

        // Two encodings worth trying:
        //   A) Selector index in low byte:  0x0000_0000_0000_00XX
        //   B) Full profile blob shape:     0x00FF_0000_00BB_0000  (mirror of Get)
        byte[] candidates = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x0F, 0x10, 0x55, 0xAA, 0xFF];

        foreach ( var bb in candidates ) {
            // Encoding A: simple selector
            ulong simple = bb;
            Log( $"-- candidate 0x{bb:X2} (simple selector) --" );
            Log( $"  SetGamingProfile(0x{simple:X16}) -> {InvokeMethod( className, "SetGamingProfile", simple )}" );
            ProbeAfterProfileWrite( className, bb );

            // Encoding B: full blob shape with bb in byte[2]
            var blob = ((ulong)0x00 << 56) | ((ulong)0xFF << 48) | ((ulong)bb << 16);
            // That gives bytes [00 00 BB 00 00 00 FF 00] in little-endian
            Log( $"  SetGamingProfile(0x{blob:X16}) [blob shape] -> {InvokeMethod( className, "SetGamingProfile", blob )}" );
            ProbeAfterProfileWrite( className, bb );
        }

        Log( "restoring original profile" );
        if ( originalProfileBytes is not null ) {
            var restored = BitConverter.ToUInt64( originalProfileBytes, 0 );
            Log( $"  SetGamingProfile(0x{restored:X16}) -> {InvokeMethod( className, "SetGamingProfile", restored )}" );
        }
        // Restore safe fan speed
        InvokeMethod( className, "SetGamingFanSpeed", 50UL << 8 );
    }

    private static void ProbeAfterProfileWrite( string className, byte candidate ) {
        var profileAfter = ReadBytes( className, "GetGamingProfile", 0u );
        Log( $"    GetGamingProfile(0) = [{FormatBytes( profileAfter )}]" );

        var behaviorAfter = ReadBytes( className, "GetGamingFanBehavior", 1u );
        Log( $"    GetGamingFanBehavior(1) = [{FormatBytes( behaviorAfter )}]" );

        // Try setting fan speed to 100 — if this mode unlocks it, return code is 0.
        Log( $"    SetGamingFanSpeed(0x6400) -> {InvokeMethod( className, "SetGamingFanSpeed", 0x6400UL )}" );
        var speedAfter = ReadBytes( className, "GetGamingFanSpeed", 1u );
        Log( $"    GetGamingFanSpeed(1) = [{FormatBytes( speedAfter )}]" );

        if ( speedAfter is not null && speedAfter[1] == 100 ) {
            Log( $"    *** UNLOCK: profile candidate 0x{candidate:X2} permits speed writes ***" );
            // Restore safe speed before moving on
            InvokeMethod( className, "SetGamingFanSpeed", 50UL << 8 );
        }
    }

    /// <summary>
    /// Poll all interesting Get* methods on a tight interval and log only the
    /// fields that change. Use this with PredatorSense (or any other tool) to
    /// see which methods + argument shapes the firmware actually accepts.
    /// Runs until Ctrl-C.
    /// </summary>
    private static void RunWatch( int intervalMs ) {
        Log( $"=== watch (every {intervalMs}ms) @ {DateTime.Now:HH:mm:ss.fff} — Ctrl-C to stop ===" );
        var (className, _) = FindGamingClass( logMethodsToo: false );
        if ( className is null ) { Log( "no class" ); return; }

        // Probes to poll. (label, methodName, selector-or-null-if-no-input)
        var probes = new List<(string label, string method, uint? selector)> {
            ("profile[0]",         "GetGamingProfile",         0u),
            ("behavior[1]",        "GetGamingFanBehavior",     1u),
            ("behavior[2]",        "GetGamingFanBehavior",     2u),
            ("speed[1]",           "GetGamingFanSpeed",        1u),
            ("speed[2]",           "GetGamingFanSpeed",        2u),
            ("fanTable",           "GetGamingFanTable",        null),
            ("miscSetting[0]",     "GetGamingMiscSetting",     0u),
            ("miscSetting[1]",     "GetGamingMiscSetting",     1u),
            ("miscSetting[2]",     "GetGamingMiscSetting",     2u),
            ("miscSetting[3]",     "GetGamingMiscSetting",     3u),
            ("profileSetting[0]",  "GetGamingProfileSetting",  0u),
            ("profileSetting[1]",  "GetGamingProfileSetting",  1u),
            ("profileSetting[2]",  "GetGamingProfileSetting",  2u),
            ("profileSetting[3]",  "GetGamingProfileSetting",  3u),
            ("profileSetting[4]",  "GetGamingProfileSetting",  4u),
        };

        // Capture baseline.
        var prev = new Dictionary<string, string>();
        foreach ( var (label, method, sel) in probes ) {
            var bytes = sel.HasValue
                ? ReadBytes( className, method, sel.Value )
                : ReadBytesNoInput( className, method );
            prev[label] = FormatBytes( bytes );
            Log( $"  baseline {label,-20} = [{prev[label]}]" );
        }
        Log( "--- watching ---" );

        // Graceful Ctrl-C.
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += ( _, e ) => { e.Cancel = true; cts.Cancel(); };

        while ( !cts.IsCancellationRequested ) {
            foreach ( var (label, method, sel) in probes ) {
                var bytes = sel.HasValue
                    ? ReadBytes( className, method, sel.Value )
                    : ReadBytesNoInput( className, method );
                var now = FormatBytes( bytes );
                if ( now != prev[label] ) {
                    Log( $"  {label,-20}  [{prev[label]}] -> [{now}]" );
                    prev[label] = now;
                }
            }
            try { Thread.Sleep( intervalMs ); } catch { break; }
        }

        Log( "=== watch stopped ===" );
    }

    private static byte[]? ReadBytesNoInput( string className, string methodName ) {
        try {
            var scope = new ManagementScope( WmiNamespace );
            scope.Connect();
            var instance = GetSingleInstance( scope, className );
            if ( instance is null ) {
                return null;
            }

            using ( instance ) {
                var outParams = instance.InvokeMethod( methodName, null, null );
                var raw = outParams?.Properties.Cast<PropertyData>().FirstOrDefault()?.Value;
                if ( raw is ulong u ) {
                    return BitConverter.GetBytes( u );
                }

                if ( raw is uint u32 ) {
                    return BitConverter.GetBytes( (ulong)u32 );
                }

                if ( raw is byte b ) {
                    return [b];
                }

                if ( raw is byte[] arr ) {
                    return arr;
                }
            }
        } catch { }
        return null;
    }

    private static void RunRawInvoke( string methodName, ulong arg ) {
        Log( $"=== raw {methodName}(0x{arg:X16}) @ {DateTime.Now:HH:mm:ss.fff} ===" );
        var (className, _) = FindGamingClass( logMethodsToo: false );
        if ( className is null ) { Log( "no class" ); return; }
        var ret = InvokeMethod( className, methodName, arg );
        Log( $"return={ret}" );
    }

    // ---- Plumbing ----

    private static (string? className, int methodCount) FindGamingClass( bool logMethodsToo ) {
        try {
            var scope = new ManagementScope( WmiNamespace );
            scope.Connect();
            var query = new ManagementObjectSearcher( scope, new SelectQuery( "meta_class" ) );
            foreach ( var raw in query.Get() ) {
                var cls = (ManagementClass)raw;
                string? guid = null;
                try { guid = cls.Qualifiers["guid"]?.Value?.ToString(); } catch { }
                if ( guid is null ) {
                    continue;
                }

                if ( !string.Equals( guid, TargetGuid, StringComparison.OrdinalIgnoreCase ) ) {
                    continue;
                }

                if ( logMethodsToo ) {
                    LogClassSurface( cls );
                }

                return (cls.ClassPath.ClassName, cls.Methods.Count);
            }
        } catch ( Exception ex ) {
            Log( $"WMI enumeration failed: {ex.Message}" );
        }
        return (null, 0);
    }

    private static void LogClassSurface( ManagementClass cls ) {
        var sb = new StringBuilder();
        sb.AppendLine( $"class {cls.ClassPath.ClassName}" );
        sb.AppendLine( "  --- qualifiers ---" );
        foreach ( var q in cls.Qualifiers ) {
            sb.AppendLine( $"    {q.Name} = {Format( q.Value )}" );
        }

        sb.AppendLine( "  --- methods ---" );
        foreach ( var m in cls.Methods ) {
            sb.Append( $"    {m.Name}(" );
            if ( m.InParameters?.Properties != null ) {
                var parts = m.InParameters.Properties.Cast<PropertyData>().Select( p => $"{p.Type} {p.Name}" );
                sb.Append( string.Join( ", ", parts ) );
            }
            sb.Append( ") -> " );
            if ( m.OutParameters?.Properties != null ) {
                var parts = m.OutParameters.Properties.Cast<PropertyData>().Select( p => $"{p.Type} {p.Name}" );
                sb.Append( string.Join( ", ", parts ) );
            }
            sb.AppendLine();
            foreach ( var q in m.Qualifiers ) {
                sb.AppendLine( $"      [{q.Name}={Format( q.Value )}]" );
            }
        }
        Log( sb.ToString() );
    }

    private static void InvokeAndLog( ManagementObject instance, string methodName, ManagementBaseObject? inParams, string inputLabel ) {
        try {
            var outParams = instance.InvokeMethod( methodName, inParams, null );
            if ( outParams is null ) { Log( $"  {methodName} {inputLabel} -> <no output>" ); return; }
            var sb = new StringBuilder( $"  {methodName} {inputLabel} -> " );
            foreach ( var p in outParams.Properties ) {
                sb.Append( $"{p.Name}=" );
                if ( p.Value is ulong u64 ) {
                    sb.Append( $"0x{u64:X16} ({u64}) bytes=[{string.Join( " ", BitConverter.GetBytes( u64 ).Select( b => b.ToString( "X2" ) ) )}]  " );
                } else if ( p.Value is uint u32 ) {
                    sb.Append( $"0x{u32:X8} ({u32})  " );
                } else if ( p.Value is byte u8 ) {
                    sb.Append( $"0x{u8:X2} ({u8})  " );
                } else {
                    sb.Append( $"{Format( p.Value )}  " );
                }
            }
            Log( sb.ToString() );
        } catch ( Exception ex ) {
            Log( $"  {methodName} {inputLabel} -> THREW {ex.GetType().Name}: {ex.Message}" );
        }
    }

    private static string InvokeMethod( string className, string methodName, ulong argument ) {
        try {
            var scope = new ManagementScope( WmiNamespace );
            scope.Connect();
            var path = new ManagementPath { ClassName = className, NamespacePath = WmiNamespace };
            using var cls = new ManagementClass( scope, path, null );
            ManagementBaseObject? inParams = null;
            try { inParams = cls.GetMethodParameters( methodName ); } catch { }

            // Methods with no input parameters return null/empty from GetMethodParameters.
            // We must NOT try to set Properties[0] in that case.
            if ( inParams is not null && inParams.Properties.Cast<PropertyData>().Any() ) {
                var first = inParams.Properties.Cast<PropertyData>().First();
                first.Value = CoerceSelector( first, (uint)argument ) is var coerced && coerced is uint
                    ? argument   // scalar UInt32/64 — pass full argument
                    : coerced;   // array or smaller type — use coerced
                // Above is a bit hacky; for UInt64 inputs we want the full 64 bits.
                if ( first.Type == CimType.UInt64 ) {
                    first.Value = argument;
                } else if ( first.Type == CimType.UInt32 ) {
                    first.Value = (uint)argument;
                } else if ( first.Type == CimType.UInt16 ) {
                    first.Value = (ushort)argument;
                } else if ( first.Type == CimType.UInt8 ) {
                    first.Value = (byte)argument;
                }
            }

            var instance = GetSingleInstance( scope, className );
            if ( instance is null ) {
                return "<no instance>";
            }

            using ( instance ) {
                var outParams = instance.InvokeMethod( methodName, inParams, null );
                if ( outParams is null ) {
                    return "<no output>";
                }
                // Format all output properties, not just the first — some methods
                // (e.g. GetGamingKBBacklight) return multiple.
                var parts = outParams.Properties.Cast<PropertyData>()
                    .Select( p => $"{p.Name}={FormatValue( p.Value )}" );
                return string.Join( "  ", parts );
            }
        } catch ( Exception ex ) {
            return $"THREW {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string FormatValue( object? v ) {
        return v is null
            ? "<null>"
            : v is ulong u64
            ? $"0x{u64:X16} ({u64}) bytes=[{string.Join( " ", BitConverter.GetBytes( u64 ).Select( b => b.ToString( "X2" ) ) )}]"
            : v is uint u32 ? $"0x{u32:X8} ({u32})" : v is byte u8 ? $"0x{u8:X2} ({u8})" : Format( v );
    }

    private static byte[]? ReadBytes( string className, string methodName, uint selector ) {
        try {
            var scope = new ManagementScope( WmiNamespace );
            scope.Connect();
            var path = new ManagementPath { ClassName = className, NamespacePath = WmiNamespace };
            using var cls = new ManagementClass( scope, path, null );
            var inParams = cls.GetMethodParameters( methodName );
            inParams.Properties.Cast<PropertyData>().First().Value = selector;
            var instance = GetSingleInstance( scope, className );
            if ( instance is null ) {
                return null;
            }

            using ( instance ) {
                var outParams = instance.InvokeMethod( methodName, inParams, null );
                var raw = outParams?.Properties.Cast<PropertyData>().FirstOrDefault()?.Value;
                if ( raw is ulong u ) {
                    return BitConverter.GetBytes( u );
                }

                if ( raw is uint u32 ) {
                    return BitConverter.GetBytes( (ulong)u32 );
                }
            }
        } catch { }
        return null;
    }

    private static ManagementObject? GetSingleInstance( ManagementScope scope, string className ) {
        using var search = new ManagementObjectSearcher( scope, new SelectQuery( className ) );
        foreach ( var raw in search.Get() ) {
            return (ManagementObject)raw;
        }

        return null;
    }

    private static void LogFanState( string className, string label ) {
        for ( uint sel = 1; sel <= 2; sel++ ) {
            var sp = ReadBytes( className, "GetGamingFanSpeed", sel );
            var be = ReadBytes( className, "GetGamingFanBehavior", sel );
            Log( $"  state[{label}] sel={sel}  speed=[{FormatBytes( sp )}]  behavior=[{FormatBytes( be )}]" );
        }
    }

    private static string FormatBytes( byte[]? bytes )
        => bytes is null ? "?" : string.Join( " ", bytes.Select( b => b.ToString( "X2" ) ) );

    private static string Format( object? value ) {
        if ( value is null ) {
            return "<null>";
        }

        if ( value is Array arr ) {
            var parts = new List<string>();
            foreach ( var item in arr ) {
                parts.Add( item?.ToString() ?? "<null>" );
            }

            return "[" + string.Join( ", ", parts ) + "]";
        }
        return value.ToString() ?? "<null>";
    }

    private static void Log( string text ) => Console.WriteLine( $"[{DateTime.Now:HH:mm:ss.fff}] {text}" );
}
