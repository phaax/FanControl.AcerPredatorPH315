using System;
using System.IO;
using System.Reflection;

namespace FanControl.AcerPredatorPH315.PawnIO;

/// <summary>
/// RAII wrapper around a PawnIO device handle with a loaded module. Throws on any
/// non-zero HRESULT from PawnIOLib so the plugin can surface a clean error.
/// </summary>
internal sealed class PawnIOSession : IDisposable {
    private IntPtr _handle;

    private PawnIOSession( IntPtr handle ) {
        _handle = handle;
    }

    public static PawnIOSession LoadEmbeddedModule( string resourceName ) {
        PawnIOLibLoader.EnsureLoaded();
        var blob = ReadEmbeddedResource( resourceName );

        Check( PawnIONative.pawnio_open( out var handle ), nameof( PawnIONative.pawnio_open ) );
        var session = new PawnIOSession( handle );

        try {
            Check( PawnIONative.pawnio_load( handle, blob, (UIntPtr)blob.LongLength ),
                  nameof( PawnIONative.pawnio_load ) );
        } catch {
            session.Dispose();
            throw;
        }

        return session;
    }

    public void Execute( string functionName, ulong[] inputs, ulong[]? outputs = null ) {
        outputs ??= [];
        var hr = PawnIONative.pawnio_execute(
            _handle,
            functionName,
            inputs,
            (UIntPtr)inputs.LongLength,
            outputs,
            (UIntPtr)outputs.LongLength,
            out _ );
        Check( hr, $"pawnio_execute({functionName})" );
    }

    public ulong ExecuteSingle( string functionName, ulong[] inputs ) {
        var outputs = new ulong[1];
        Execute( functionName, inputs, outputs );
        return outputs[0];
    }

    public void Dispose() {
        if ( _handle != IntPtr.Zero ) {
            _ = PawnIONative.pawnio_close( _handle );
            _handle = IntPtr.Zero;
        }
    }

    private static byte[] ReadEmbeddedResource( string name ) {
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream( name )
            ?? throw new InvalidOperationException(
                $"Embedded resource '{name}' not found. Did you drop the .bin into modules/ before building?" );
        using var ms = new MemoryStream();
        s.CopyTo( ms );
        return ms.ToArray();
    }

    private static void Check( int hr, string what ) {
        if ( hr < 0 ) {
            throw new InvalidOperationException( $"{what} failed with HRESULT 0x{hr:X8}" );
        }
    }
}
