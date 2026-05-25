using System;
using System.IO;
using System.Reflection;

namespace FanControl.AcerPredatorPH315.Diagnostics;

/// <summary>
/// FanControl swallows plugin Initialize() exceptions and just reports "No
/// sensors from plugin detected." Mirror everything important to a file next to
/// the plugin DLL so we have something to look at when it goes wrong.
/// </summary>
internal static class PluginLog {
#if NET9_0_OR_GREATER
    private static readonly System.Threading.Lock _gate = new();
#else
    private static readonly object _gate = new();
#endif
    private static readonly string LogPath = ResolveLogPath();

    public static void Info( string message ) => Write( "INFO", message );
    public static void Error( string message, Exception? ex = null )
        => Write( "ERROR", ex is null ? message : $"{message}: {ex}" );

    private static void Write( string level, string message ) {
        try {
            lock ( _gate ) {
                File.AppendAllText( LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}" );
            }
        } catch { /* logging must never throw */ }
    }

    private static string ResolveLogPath() {
        try {
            var dir = Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location )
                         ?? Path.GetTempPath();
            return Path.Combine( dir, "FanControl.AcerPredatorPH315.log" );
        } catch {
            return Path.Combine( Path.GetTempPath(), "FanControl.AcerPredatorPH315.log" );
        }
    }
}
