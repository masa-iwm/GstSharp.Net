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
    /// The name is the name of the C function: <c>gst_app_src_push_buffer</c>
    /// becomes <c>PushBuffer</c>, and nothing on the generated class competes
    /// for it. GStreamer also publishes the call as the <c>push-buffer</c>
    /// action signal, but an action signal is a call dressed as a signal for
    /// bindings that have nothing better, so the generator emits no event for
    /// it and the name stays free for the method that does the work.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper or <paramref name="buffer"/> was disposed.
    /// </exception>
    public Gst.FlowReturn PushBuffer(Gst.Buffer buffer)
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

    /// <summary>
    /// Installs a set of callbacks on this source, taking the set over.
    /// </summary>
    /// <param name="callbacks">
    /// The callbacks to install. The call consumes them:
    /// <paramref name="callbacks"/> is disposed when this method returns, and
    /// using it afterwards throws <see cref="ObjectDisposedException"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is the managed face of <c>gst_app_src_set_simple_callbacks</c>,
    /// whose <c>cb</c> parameter is <c>transfer-ownership="full"</c>. The
    /// generator does not emit calls that take a wrapper over, because handing
    /// the only reference of a wrapper to the library would let both of them
    /// release it, so this method is written by hand along the lines of
    /// <see cref="PushBuffer"/>: it hands the call a reference of its own and
    /// then disposes the wrapper, which leaves the native reference count
    /// exactly where the C call leaves it.
    /// </para>
    /// <para>
    /// Consuming the wrapper is what the C API does, and here it buys something
    /// else as well. GStreamer seals a set of callbacks the moment it is
    /// installed — "once <c>cb</c> is set on a <c>GstAppSrc</c> it is not
    /// possible anymore to change any of the callbacks inside it" — and a
    /// consumed wrapper cannot be used to try: every setter of
    /// <see cref="Gst.App.AppSrcSimpleCallbacks"/> reads its handle and throws
    /// <see cref="ObjectDisposedException"/>. The rule that C states in its
    /// documentation and enforces with a runtime warning is a type-level rule
    /// here.
    /// </para>
    /// <para>
    /// Installing callbacks turns the signals off. While a set is installed the
    /// source emits none of <see cref="NeedData"/>, <see cref="EnoughData"/> or
    /// <see cref="SeekData"/>, whatever <see cref="EmitSignals"/> says, which is
    /// the point of the callbacks: they cost less per buffer than a signal
    /// emission. The two paths are alternatives, not layers.
    /// <see cref="ClearSimpleCallbacks"/> puts the signal path back.
    /// <c>gst_app_src_set_callbacks</c>, the struct of function pointers this
    /// binding does not expose, is mutually exclusive with this call in the same
    /// way.
    /// </para>
    /// <para>
    /// <see cref="ClearSimpleCallbacks"/> is the call that removes a set, and it
    /// is separate for the reason <see cref="Gst.Bus.ClearSyncHandler"/> is:
    /// a set of callbacks is not an optional argument of an install — removing
    /// one is a different intention — and this class has a second
    /// <c>SetSimpleCallbacks</c> whose parameters are all optional, so a bare
    /// <c>SetSimpleCallbacks(null)</c> is a null that two overloads could take.
    /// Which one wins is a tie-break of the language rather than a decision of
    /// this binding, so the null is refused here and the intention is spelled
    /// out instead.
    /// </para>
    /// <para>
    /// Every callback runs on a streaming thread of the pipeline, never on the
    /// thread that installed it. What they do has to be safe there: pushing a
    /// buffer is, touching user-interface state is not. An exception that leaves
    /// a callback does not cross the native frame — the trampoline of the
    /// delegate catches it and reports it through
    /// <see cref="Gst.Interop.ExceptionTrap"/>. The two callbacks that answer
    /// nothing then simply return; <see cref="Gst.App.AppSrcSeekDataCallback"/>
    /// answers <see langword="false"/>, which tells the source that the seek
    /// failed. A callback that throws therefore stalls the stream rather than
    /// the process, and the report is how it is noticed.
    /// </para>
    /// <para>
    /// A callback that captures state keeps that state alive for as long as the
    /// native source holds the callbacks: the reference chain runs from the
    /// <c>GCHandle</c> the setter allocated through the closure and into
    /// whatever it captured, and if the closure captured this wrapper it runs
    /// back into the native source as well. The collector cannot break a cycle
    /// that leaves the managed heap. It is broken from the other end, when the
    /// source is torn down or is handed a different set of callbacks: GStreamer
    /// then releases the set, the destroy notification of every slot runs and
    /// the <c>GCHandle</c>s go away. This is the doctrine of the binding for
    /// signal handlers as well, and it is the reason a pipeline is disposed
    /// rather than dropped.
    /// </para>
    /// <para>
    /// The call needs GStreamer 1.28 or newer, which is the version this binding
    /// targets. On an older library the entry point does not exist and the first
    /// call throws <see cref="EntryPointNotFoundException"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="callbacks"/> is <see langword="null"/>. Use
    /// <see cref="ClearSimpleCallbacks"/> to remove the set that is installed.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper or <paramref name="callbacks"/> was disposed.
    /// </exception>
    public void SetSimpleCallbacks(Gst.App.AppSrcSimpleCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(callbacks);

        // Both handles are read before anything is referenced, so that a
        // disposed wrapper throws without leaking the reference of the other.
        nint source = Handle;
        nint owned = callbacks.Handle;

        // The reference the call consumes. Without it the wrapper and the
        // library would both own the one reference the wrapper holds.
        GstAppSrcSimpleCallbacksRef(owned);

        GstAppSrcSetSimpleCallbacks(source, owned);

        // The handles were read before the call, so nothing keeps this wrapper
        // alive across it on its own.
        GC.KeepAlive(this);

        // And the reference of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing, and what seals
        // the set of callbacks against later changes.
        callbacks.Dispose();
    }

    /// <summary>
    /// Installs the given callbacks on this source, building the set of
    /// callbacks they go into.
    /// </summary>
    /// <param name="onNeedData">
    /// Called when the source wants data, on a streaming thread. Buffers are
    /// handed over with <see cref="PushBuffer"/> or <see cref="PushSample"/>,
    /// and the end of the stream with <see cref="EndOfStream"/>.
    /// </param>
    /// <param name="onEnoughData">
    /// Called when the source has enough data queued, on a streaming thread.
    /// </param>
    /// <param name="onSeekData">
    /// Called when the stream should continue from another offset, on a
    /// streaming thread. It answers whether the seek was performed.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is the short form of
    /// <see cref="SetSimpleCallbacks(Gst.App.AppSrcSimpleCallbacks)"/> for the
    /// usual case, where the callbacks are written at the point of the install:
    /// it creates a <see cref="Gst.App.AppSrcSimpleCallbacks"/>, fills in the
    /// slots that were given and installs it. Every remark of that overload
    /// applies unchanged — the signals stay silent, the callbacks run on
    /// streaming threads, exceptions are trapped, and captured state lives until
    /// the source lets go of the callbacks.
    /// </para>
    /// <para>
    /// A set with no callback in it would install nothing and silence the
    /// signals, which is never what a caller means, so it is rejected. Removing
    /// the callbacks that are installed is
    /// <see cref="ClearSimpleCallbacks"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Every callback is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public void SetSimpleCallbacks(
        Gst.App.AppSrcNeedDataCallback? onNeedData = null,
        Gst.App.AppSrcEnoughDataCallback? onEnoughData = null,
        Gst.App.AppSrcSeekDataCallback? onSeekData = null)
    {
        if (onNeedData is null && onEnoughData is null && onSeekData is null)
        {
            throw new ArgumentException(
                "At least one callback is required. Use ClearSimpleCallbacks() to remove the callbacks that are installed.");
        }

        Gst.App.AppSrcSimpleCallbacks callbacks = Gst.App.AppSrcSimpleCallbacks.New();

        try
        {
            if (onNeedData is not null)
            {
                callbacks.SetNeedData(onNeedData);
            }

            if (onEnoughData is not null)
            {
                callbacks.SetEnoughData(onEnoughData);
            }

            if (onSeekData is not null)
            {
                callbacks.SetSeekData(onSeekData);
            }
        }
        catch
        {
            // Nothing took the set over, and the slots that were filled release
            // their managed state with it.
            callbacks.Dispose();
            throw;
        }

        SetSimpleCallbacks(callbacks);
    }

    /// <summary>
    /// Takes the set of callbacks off this source, so that it emits its signals
    /// again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <c>gst_app_src_set_simple_callbacks</c> with a null set. What the
    /// library does with it is what the integration tests observe rather than
    /// what a header promises: the set that was installed is released, the
    /// destroy notification of every slot in it runs, and what those closures
    /// captured becomes collectible — the same release that installing a second
    /// set over the first performs. From then on the source emits
    /// <see cref="NeedData"/> and its neighbours again, subject to
    /// <see cref="EmitSignals"/>, and nothing that was connected before the
    /// install has to be connected again.
    /// </para>
    /// <para>
    /// Clearing a source that has no callbacks installed does nothing.
    /// </para>
    /// <para>
    /// This is the counterpart of
    /// <see cref="SetSimpleCallbacks(Gst.App.AppSrcSimpleCallbacks)"/> the way
    /// <see cref="Gst.Bus.ClearSyncHandler"/> is the counterpart of
    /// <see cref="Gst.Bus.SetSyncHandler(Gst.BusSyncHandler)"/>, and it exists
    /// for the same two reasons: removing a set is a different intention from
    /// installing one, and <c>SetSimpleCallbacks(null)</c> would be a null that
    /// two overloads of this class could take, decided by a tie-break of the
    /// language.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public void ClearSimpleCallbacks()
    {
        GstAppSrcSetSimpleCallbacks(Handle, nint.Zero);
        GC.KeepAlive(this);
    }

    /// <summary>The <c>gst_app_src_push_buffer</c> entry point.</summary>
    [LibraryImport("GstApp", EntryPoint = "gst_app_src_push_buffer")]
    private static partial int GstAppSrcPushBuffer(nint appsrc, nint buffer);

    /// <summary>The <c>gst_app_src_set_simple_callbacks</c> entry point.</summary>
    [LibraryImport("GstApp", EntryPoint = "gst_app_src_set_simple_callbacks")]
    private static partial void GstAppSrcSetSimpleCallbacks(nint appsrc, nint cb);

    /// <summary>The <c>gst_app_src_simple_callbacks_ref</c> entry point.</summary>
    [LibraryImport("GstApp", EntryPoint = "gst_app_src_simple_callbacks_ref")]
    private static partial nint GstAppSrcSimpleCallbacksRef(nint cb);
}
