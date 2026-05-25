using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace FanControl.AcerPredatorPH315.PawnIO;

/// <summary>
/// The PawnIO installer drops PawnIOLib.dll into %ProgramFiles%\PawnIO\ but does
/// NOT add that directory to PATH. So a naïve DllImport("PawnIOLib") throws
/// DllNotFoundException when the plugin loads inside FanControl. We work around
/// that by pre-loading the DLL from its real install location before any
/// PawnIOLib P/Invoke runs.
/// </summary>
internal static partial class PawnIOLibLoader {
    private static bool _loaded;
#if NET9_0_OR_GREATER
    private static readonly System.Threading.Lock _gate = new();
#else
    private static readonly object _gate = new();
#endif

    public static void EnsureLoaded() {
        if ( _loaded ) {
            return;
        }

        lock ( _gate ) {
            if ( _loaded ) {
                return;
            }

            var path = FindPawnIOLib()
                ?? throw new InvalidOperationException(
                    "PawnIOLib.dll not found. Install PawnIO from https://pawnio.eu/ first." );

            if ( LoadLibraryW( path ) == IntPtr.Zero ) {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"LoadLibrary failed for '{path}' (Win32 error {err})" );
            }
            _loaded = true;
        }
    }

    private static string? FindPawnIOLib() {
        foreach ( var candidate in CandidatePaths() ) {
            if ( !string.IsNullOrWhiteSpace( candidate ) && File.Exists( candidate ) ) {
                return candidate;
            }
        }
        return null;
    }

    private static IEnumerable<string> CandidatePaths() {
        // 1. PawnIO registers its install location in the Uninstall key.
        var regPath = TryReadRegistry(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO",
            "InstallLocation" );
        if ( !string.IsNullOrWhiteSpace( regPath ) ) {
            yield return Path.Combine( regPath, "PawnIOLib.dll" );
        }

        // 2. Standard install dir.
        var pf = Environment.GetFolderPath( Environment.SpecialFolder.ProgramFiles );
        yield return Path.Combine( pf, "PawnIO", "PawnIOLib.dll" );

        // 3. 32-bit Program Files (just in case).
        var pf86 = Environment.GetFolderPath( Environment.SpecialFolder.ProgramFilesX86 );
        yield return Path.Combine( pf86, "PawnIO", "PawnIOLib.dll" );
    }

    private static string? TryReadRegistry( string subkey, string valueName ) {
        try {
            using var key = RegistryKey
                .OpenBaseKey( RegistryHive.LocalMachine, RegistryView.Registry64 )
                .OpenSubKey( subkey );
            return key?.GetValue( valueName ) as string;
        } catch {
            return null;
        }
    }

#if NET7_0_OR_GREATER
    [LibraryImport( "kernel32", EntryPoint = "LoadLibraryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16 )]
    private static partial IntPtr LoadLibraryW( string lpLibFileName );
#else
    [DllImport( "kernel32", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LoadLibraryW" )]
    private static extern IntPtr LoadLibraryW( [MarshalAs( UnmanagedType.LPWStr )] string lpLibFileName );
#endif
}
