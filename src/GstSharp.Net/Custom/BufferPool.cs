using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

public unsafe partial class BufferPool
{
    /// <summary>
    /// Applies a configuration to the pool, taking the configuration over.
    /// </summary>
    /// <param name="config">
    /// The configuration to apply, normally the one
    /// <see cref="GetConfig"/> handed out with the parameters filled in by
    /// <see cref="ConfigSetParams"/>, <see cref="ConfigSetAllocator"/> and
    /// <see cref="ConfigAddOption"/>. The call consumes it:
    /// <paramref name="config"/> is disposed when this method returns, whatever
    /// the answer is, and using it afterwards throws
    /// <see cref="ObjectDisposedException"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the pool took the configuration, which
    /// includes the case of a configuration it is already running with.
    /// <see langword="false"/> when it refused: the pool is active, buffers it
    /// handed out are still outstanding, or the parameters are ones it cannot
    /// honour.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_buffer_pool_set_config</c>, whose <c>config</c> parameter
    /// is <c>transfer-ownership="full"</c>. The generator does not emit calls
    /// that take a wrapper over, because handing the only value of a wrapper to
    /// the library would let both of them release it, so this method is written
    /// by hand along the lines of
    /// <c>Gst.WebRTC.WebRTCSessionDescription.New</c>: it hands the call a value
    /// of its own and then disposes the wrapper, which leaves the caller with
    /// exactly what the C call leaves it with. A
    /// <see cref="Gst.GObject.Boxed"/> value has no reference count to raise, so
    /// the copy that <c>g_boxed_copy</c> makes is what a reference is there.
    /// Without it the whole builder surface of the pool would be unreachable:
    /// a configuration could be read and filled in, and never applied.
    /// </para>
    /// <para>
    /// <b>A refusal consumes the configuration too.</b> The C function takes
    /// ownership on every path it can reach — it frees the structure when it
    /// declines, and stores it when the pool's own <c>set_config</c> declines,
    /// deliberately, so that the caller can read back how far it got. A
    /// <see langword="false"/> answer therefore means "not applied as asked",
    /// never "nothing happened": call <see cref="GetConfig"/> to see the state
    /// the pool is in now, refine that structure, and set it again.
    /// </para>
    /// <para>
    /// The configuration the caller passes is never modified; what the pool
    /// keeps and may rewrite is the copy. This is also why the round trip is
    /// clean: <see cref="GetConfig"/> is <c>transfer-ownership="full"</c> and
    /// hands out a copy of its own, so nothing is aliased in either direction.
    /// </para>
    /// <para>
    /// The default pool needs the mandatory parameters before it accepts
    /// anything, which is what <see cref="ConfigSetParams"/> writes; a
    /// configuration without them is refused.
    /// <see cref="Gst.GObject.Boxed.Dispose()"/> is idempotent, so a
    /// <c>using</c> declaration around the configuration stays correct.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">
    /// <paramref name="config"/> was disposed, or this wrapper was.
    /// </exception>
    public bool SetConfig(Gst.Structure config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Both handles are read before anything is copied, so that a disposed
        // wrapper throws before a copy exists that nobody would free.
        nint pool = Handle;
        nint owned = config.Handle;
        nuint boxedType = config.BoxedType.Value;

        // The value the call consumes.
        nint copy = GObjectNative.BoxedCopy(boxedType, owned);

        int result = GstBufferPoolSetConfig(pool, copy);

        // The handle was read before the call, so nothing keeps this wrapper
        // alive across it on its own.
        GC.KeepAlive(this);

        // And the structure of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing. It happens on a
        // refusal as well, because the C function consumes the structure either
        // way.
        config.Dispose();

        return result != 0;
    }

    /// <summary>The <c>gst_buffer_pool_set_config</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_pool_set_config")]
    private static partial int GstBufferPoolSetConfig(nint pool, nint config);
}
