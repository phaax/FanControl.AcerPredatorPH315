using System;
using System.Runtime.InteropServices;

namespace FanControl.AcerPredatorPH315.PawnIO;

/// <summary>
/// P/Invoke surface for PawnIOLib.dll (shipped with the PawnIO setup, installed to
/// %ProgramFiles%\PawnIO\). FanControl V238+ already loads the driver, so we only need
/// the user-mode wrapper.
///
/// API reference: https://github.com/namazso/PawnIO/blob/master/PawnIOLib/PawnIOLib.cpp
/// </summary>
internal static partial class PawnIONative {
    private const string Dll = "PawnIOLib";

#if NET7_0_OR_GREATER
    [LibraryImport( Dll )]
    public static partial int pawnio_open( out IntPtr handle );

    [LibraryImport( Dll )]
    public static partial int pawnio_load( IntPtr handle, byte[] blob, UIntPtr size );

    // Function names are ASCII identifiers, so UTF-8 marshalling is byte-identical to LPStr here.
    [LibraryImport( Dll, StringMarshalling = StringMarshalling.Utf8 )]
    public static partial int pawnio_execute(
        IntPtr handle,
        string functionName,
        ulong[] inArray,
        UIntPtr inCount,
        [Out] ulong[] outArray,
        UIntPtr outCount,
        out UIntPtr returnCount );

    [LibraryImport( Dll )]
    public static partial int pawnio_close( IntPtr handle );
#else
    [DllImport( Dll, ExactSpelling = true )]
    public static extern int pawnio_open( out IntPtr handle );

    [DllImport( Dll, ExactSpelling = true )]
    public static extern int pawnio_load( IntPtr handle, byte[] blob, UIntPtr size );

    [DllImport( Dll, ExactSpelling = true, CharSet = CharSet.Ansi, BestFitMapping = false )]
    public static extern int pawnio_execute(
        IntPtr handle,
        [MarshalAs( UnmanagedType.LPStr )] string functionName,
        ulong[] inArray,
        UIntPtr inCount,
        ulong[] outArray,
        UIntPtr outCount,
        out UIntPtr returnCount );

    [DllImport( Dll, ExactSpelling = true )]
    public static extern int pawnio_close( IntPtr handle );
#endif
}
