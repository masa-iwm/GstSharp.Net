using System.Runtime.InteropServices;

namespace Gst.App;

public unsafe partial class AppSrc
{
    /// <summary>
    /// Adds a buffer to the queue of buffers that this source pushes to its
    /// source pad, taking the buffer over.
    /// </summary>
    /// <param name="buffer">
    /// The buffer to push. The call consumes it: <paramref name="buffer"/> is
    /// disposed when this method returns, and using it afterwards throws
    /// <see cref="ObjectDisposedException"/>.
    /// </param>
    /// <returns>
    /// <see cref="Gst.FlowReturn.Ok"/> when the buffer was queued,
    /// <see cref="Gst.FlowReturn.Flushing"/> when the source is not running and
    /// <see cref="Gst.FlowReturn.Eos"/> after the end of the stream. The buffer
    /// is consumed in every one of those cases.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the managed face of <c>gst_app_src_push_buffer</c>, whose
    /// <c>buffer</c> parameter is <c>transfer-ownership="full"</c>. The
    /// generator does not emit calls that take a wrapper over, because handing
    /// the only reference of a wrapper to the library would let both of them
    /// release it, so this method is written by hand: it hands the call a
    /// reference of its own and then disposes the wrapper, which leaves the
    /// native reference count exactly where the C call leaves it.
    /// </para>
    /// <para>
    /// The consuming shape is the one the C API has and the one applications
    /// expect, and it keeps the ownership rule of the binding intact: after
    /// this call the wrapper owns nothing, which is precisely what its disposed
    /// state means. <see cref="Gst.MiniObject.Dispose()"/> is idempotent, so a
    /// <c>using</c> declaration around the buffer stays correct.
    /// </para>
    /// <para>
    /// Use <see cref="PushSample"/> instead to keep the wrapper: the sample it
    /// takes is <c>transfer-ownership="none"</c> and stays usable.
    /// </para>
    /// <para>
    /// The name is <c>Push</c> rather than <c>PushBuffer</c> because the
    /// generated class still carries the <c>push-buffer</c> action signal as an
    /// event of that name, and a type cannot hold both. The renames of
    /// <c>GstApp.AppSrc::push-sample</c> and <c>GstApp.AppSrc::end-of-stream</c>
    /// in <c>girs/overlays/fixups.json</c> exist for exactly that reason;
    /// <c>push-buffer</c> kept the bare name only because the method it collides
    /// with was never emitted. Once the generator stops emitting events for
    /// action signals — an action signal is a call, and calling it through an
    /// event is nobody's idea of an API — the name is free and this method takes
    /// it: rename it to <c>PushBuffer</c>, which is what
    /// <c>gst_app_src_push_buffer</c> is called and what §1 of the acceptance
    /// requirements asks for. It has never been published under either name, so
    /// no compatibility forwarder is needed.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper or <paramref name="buffer"/> was disposed.
    /// </exception>
    public Gst.FlowReturn Push(Gst.Buffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        // Both handles are read before anything is referenced, so that a
        // disposed wrapper throws without leaking the reference of the other.
        nint source = Handle;
        nint owned = buffer.Handle;

        // The reference the call consumes. Without it the wrapper and the
        // library would both own the one reference the wrapper holds.
        Gst.GstNative.MiniObjectRef(owned);

        int result = GstAppSrcPushBuffer(source, owned);

        // The handles were read before the call, so nothing keeps either
        // wrapper alive across it on its own.
        GC.KeepAlive(this);

        // And the reference of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing.
        buffer.Dispose();

        return (Gst.FlowReturn)result;
    }

    /// <summary>The <c>gst_app_src_push_buffer</c> entry point.</summary>
    [LibraryImport("GstApp", EntryPoint = "gst_app_src_push_buffer")]
    private static partial int GstAppSrcPushBuffer(nint appsrc, nint buffer);
}
