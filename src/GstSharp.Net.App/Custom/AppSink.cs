using System.Runtime.InteropServices;

namespace Gst.App;

public unsafe partial class AppSink
{
    /// <summary>
    /// Installs a set of callbacks on this sink, taking the set over.
    /// </summary>
    /// <param name="callbacks">
    /// The callbacks to install. The call consumes them:
    /// <paramref name="callbacks"/> is disposed when this method returns, and
    /// using it afterwards throws <see cref="ObjectDisposedException"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is the managed face of <c>gst_app_sink_set_simple_callbacks</c>,
    /// whose <c>cb</c> parameter is <c>transfer-ownership="full"</c>. The
    /// generator does not emit calls that take a wrapper over, because handing
    /// the only reference of a wrapper to the library would let both of them
    /// release it, so this method is written by hand along the lines of
    /// <see cref="Gst.App.AppSrc.PushBuffer"/>: it hands the call a reference of
    /// its own and then disposes the wrapper, which leaves the native reference
    /// count exactly where the C call leaves it.
    /// </para>
    /// <para>
    /// Consuming the wrapper is what the C API does, and here it buys something
    /// else as well. GStreamer seals a set of callbacks the moment it is
    /// installed — "once <c>cb</c> is set on a <c>GstAppSink</c> it is not
    /// possible anymore to change any of the callbacks inside it" — and a
    /// consumed wrapper cannot be used to try: every setter of
    /// <see cref="Gst.App.AppSinkSimpleCallbacks"/> reads its handle and throws
    /// <see cref="ObjectDisposedException"/>. The rule that C states in its
    /// documentation and enforces with a runtime warning is a type-level rule
    /// here.
    /// </para>
    /// <para>
    /// Installing callbacks turns the signals off. While a set is installed the
    /// sink emits none of <see cref="NewSample"/>, <see cref="NewPreroll"/>,
    /// <see cref="Eos"/>, <see cref="NewSerializedEvent"/> or
    /// <see cref="ProposeAllocation"/>, whatever <see cref="EmitSignals"/> says,
    /// which is the point of the callbacks: they cost less per frame than a
    /// signal emission. The two paths are alternatives, not layers.
    /// <see cref="ClearSimpleCallbacks"/> puts the signal path back.
    /// <c>gst_app_sink_set_callbacks</c>, the struct of function pointers this
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
    /// thread that installed it, and several of them may run at once on a sink
    /// that is fed by more than one thread. What they do has to be safe there:
    /// pulling and releasing a sample is, touching user-interface state is not.
    /// An exception that leaves a callback does not cross the native frame — the
    /// trampoline of the delegate catches it, reports it through
    /// <see cref="Gst.Interop.ExceptionTrap"/> and answers native code with
    /// <see cref="Gst.FlowReturn.Error"/> for the sample and preroll callbacks
    /// and with <see langword="false"/> for the event and allocation ones, which
    /// stops the pipeline with an error rather than the process.
    /// </para>
    /// <para>
    /// A callback that captures state keeps that state alive for as long as the
    /// native sink holds the callbacks: the reference chain runs from the
    /// <c>GCHandle</c> the setter allocated through the closure and into
    /// whatever it captured, and if the closure captured this wrapper it runs
    /// back into the native sink as well. The collector cannot break a cycle
    /// that leaves the managed heap. It is broken from the other end, when the
    /// sink is torn down or is handed a different set of callbacks: GStreamer
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
    public void SetSimpleCallbacks(Gst.App.AppSinkSimpleCallbacks callbacks)
    {
        ArgumentNullException.ThrowIfNull(callbacks);

        // Both handles are read before anything is referenced, so that a
        // disposed wrapper throws without leaking the reference of the other.
        nint sink = Handle;
        nint owned = callbacks.Handle;

        // The reference the call consumes. Without it the wrapper and the
        // library would both own the one reference the wrapper holds.
        GstAppSinkSimpleCallbacksRef(owned);

        GstAppSinkSetSimpleCallbacks(sink, owned);

        // The handles were read before the call, so nothing keeps this wrapper
        // alive across it on its own.
        GC.KeepAlive(this);

        // And the reference of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing, and what seals
        // the set of callbacks against later changes.
        callbacks.Dispose();
    }

    /// <summary>
    /// Installs the given callbacks on this sink, building the set of callbacks
    /// they go into.
    /// </summary>
    /// <param name="onNewSample">
    /// Called when a sample is ready, on a streaming thread. The sample is
    /// fetched with <see cref="PullSample"/> or
    /// <see cref="TryPullSample(Gst.ClockTime)"/>.
    /// </param>
    /// <param name="onNewPreroll">
    /// Called when a preroll sample is ready, on a streaming thread. The sample
    /// is fetched with <see cref="PullPreroll"/> or
    /// <see cref="TryPullPreroll(Gst.ClockTime)"/>.
    /// </param>
    /// <param name="onEos">Called when the stream is over, on a streaming thread.</param>
    /// <param name="onNewEvent">
    /// Called when a serialized event arrives, on a streaming thread. It
    /// answers whether the event was handled.
    /// </param>
    /// <param name="onProposeAllocation">
    /// Called for an allocation query, on a streaming thread. It answers whether
    /// the query was handled.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is the short form of
    /// <see cref="SetSimpleCallbacks(Gst.App.AppSinkSimpleCallbacks)"/> for the
    /// usual case, where the callbacks are written at the point of the install:
    /// it creates a <see cref="Gst.App.AppSinkSimpleCallbacks"/>, fills in the
    /// slots that were given and installs it. Every remark of that overload
    /// applies unchanged — the signals stay silent, the callbacks run on
    /// streaming threads, exceptions are trapped, and captured state lives until
    /// the sink lets go of the callbacks.
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
        Gst.App.AppSinkNewSampleCallback? onNewSample = null,
        Gst.App.AppSinkNewPrerollCallback? onNewPreroll = null,
        Gst.App.AppSinkEosCallback? onEos = null,
        Gst.App.AppSinkNewEventCallback? onNewEvent = null,
        Gst.App.AppSinkProposeAllocationCallback? onProposeAllocation = null)
    {
        if (onNewSample is null &&
            onNewPreroll is null &&
            onEos is null &&
            onNewEvent is null &&
            onProposeAllocation is null)
        {
            throw new ArgumentException(
                "At least one callback is required. Use ClearSimpleCallbacks() to remove the callbacks that are installed.");
        }

        Gst.App.AppSinkSimpleCallbacks callbacks = Gst.App.AppSinkSimpleCallbacks.New();

        try
        {
            if (onNewSample is not null)
            {
                callbacks.SetNewSample(onNewSample);
            }

            if (onNewPreroll is not null)
            {
                callbacks.SetNewPreroll(onNewPreroll);
            }

            if (onEos is not null)
            {
                callbacks.SetEos(onEos);
            }

            if (onNewEvent is not null)
            {
                callbacks.SetNewEvent(onNewEvent);
            }

            if (onProposeAllocation is not null)
            {
                callbacks.SetProposeAllocation(onProposeAllocation);
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
    /// Takes the set of callbacks off this sink, so that it emits its signals
    /// again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <c>gst_app_sink_set_simple_callbacks</c> with a null set. What
    /// the library does with it is what the integration tests observe rather
    /// than what a header promises: the set that was installed is released, the
    /// destroy notification of every slot in it runs, and what those closures
    /// captured becomes collectible — the same release that installing a second
    /// set over the first performs. From then on the sink emits
    /// <see cref="NewSample"/> and its neighbours again, subject to
    /// <see cref="EmitSignals"/>, and nothing that was connected before the
    /// install has to be connected again.
    /// </para>
    /// <para>
    /// Clearing a sink that has no callbacks installed does nothing.
    /// </para>
    /// <para>
    /// This is the counterpart of
    /// <see cref="SetSimpleCallbacks(Gst.App.AppSinkSimpleCallbacks)"/> the way
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
        GstAppSinkSetSimpleCallbacks(Handle, nint.Zero);
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Blocks until a sample or a serialized event becomes available, or until
    /// this sink is set to the READY or the NULL state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The answer is a <see cref="Gst.Sample"/> for a buffer or a buffer list
    /// and a <see cref="Gst.Event"/> for every serialized event except the end
    /// of the stream: stream-start, caps, segment, tag, gap, a custom event.
    /// Which of the two it is, is decided at run time, so the caller matches on
    /// the type it is handed.
    /// </para>
    /// <para>
    /// The answer is <see langword="null"/> when the sink is not started, that
    /// is when it sits in the READY or the NULL state, and when the stream has
    /// ended. The end of the stream is never queued as an event, so
    /// <see cref="IsEos"/> is what tells the two apart.
    /// </para>
    /// <para>
    /// The queue read here is the one <see cref="PullSample"/> and
    /// <see cref="TryPullSample(Gst.ClockTime)"/> read, and those two discard
    /// every event they are handed, so a consumer picks one of the two families
    /// and stays with it.
    /// </para>
    /// <para>The caller owns the answer: dispose it.</para>
    /// </remarks>
    /// <returns>
    /// The sample or the event that was queued, or <see langword="null"/> when
    /// the sink is stopped or the stream is over.
    /// </returns>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The object that was pulled is of a type this binding does not know.
    /// </exception>
    public Gst.MiniObject? PullObject()
    {
        nint nativeResult = GstAppSinkPullObject(Handle);
        GC.KeepAlive(this);
        return WrapPulledObject(nativeResult);
    }

    /// <summary>
    /// Blocks until a sample or a serialized event becomes available, until
    /// this sink is set to the READY or the NULL state, or until the timeout
    /// expires.
    /// </summary>
    /// <param name="timeout">
    /// The longest time to wait for an object. A timeout of zero answers with
    /// whatever is queued without waiting at all.
    /// </param>
    /// <remarks>
    /// <para>
    /// The answer is a <see cref="Gst.Sample"/> for a buffer or a buffer list
    /// and a <see cref="Gst.Event"/> for every serialized event except the end
    /// of the stream: stream-start, caps, segment, tag, gap, a custom event.
    /// Which of the two it is, is decided at run time, so the caller matches on
    /// the type it is handed.
    /// </para>
    /// <para>
    /// The answer is <see langword="null"/> when the sink is not started, that
    /// is when it sits in the READY or the NULL state, when the stream has
    /// ended, and when the timeout expired. The end of the stream is never
    /// queued as an event, so <see cref="IsEos"/> is what tells those apart.
    /// </para>
    /// <para>
    /// The queue read here is the one <see cref="PullSample"/> and
    /// <see cref="TryPullSample(Gst.ClockTime)"/> read, and those two discard
    /// every event they are handed, so a consumer picks one of the two families
    /// and stays with it.
    /// </para>
    /// <para>The caller owns the answer: dispose it.</para>
    /// </remarks>
    /// <returns>
    /// The sample or the event that was queued, or <see langword="null"/> when
    /// the sink is stopped, the stream is over or the timeout expired.
    /// </returns>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The object that was pulled is of a type this binding does not know.
    /// </exception>
    public Gst.MiniObject? TryPullObject(Gst.ClockTime timeout)
    {
        nint nativeResult = GstAppSinkTryPullObject(Handle, timeout.Nanoseconds);
        GC.KeepAlive(this);
        return WrapPulledObject(nativeResult);
    }

    /// <summary>
    /// Creates the wrapper of an object the sink handed over, whatever its
    /// type is.
    /// </summary>
    /// <param name="handle">
    /// The mini object the sink answered with, owned by the caller, or
    /// <see cref="nint.Zero"/>.
    /// </param>
    /// <returns>The wrapper of that object, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The first word of a mini object is its <c>GType</c>, so the type
    /// registry can be asked for the factory of the concrete type and build the
    /// <see cref="Gst.Sample"/> or the <see cref="Gst.Event"/> the sink meant.
    /// <c>Gst.GObject.TypeRegistry.TryCreateWrapper</c> is asked rather than
    /// its mini object variant, because the variant answers the same
    /// <see langword="false"/> whether no factory was found or the factory
    /// built something that is not a mini object, and those two are released
    /// differently: in the first case nothing took the reference over and it is
    /// this method that has to drop it, in the second the wrapper that was
    /// built already owns it and dropping it here as well would be a double
    /// free. A factory that answers null is counted with the first case, which
    /// is safe: every registered mini object factory constructs its wrapper and
    /// none of them answers null.
    /// </remarks>
    private static Gst.MiniObject? WrapPulledObject(nint handle)
    {
        if (handle == nint.Zero)
        {
            return null;
        }

        Gst.GObject.GType type = Gst.GObject.Value.MiniObjectTypeOf(handle);

        if (!Gst.GObject.TypeRegistry.TryCreateWrapper(type, handle, Gst.Interop.Transfer.Full, out object? wrapper))
        {
            // Nothing adopted the reference the sink handed over, so it is
            // released here rather than leaked.
            Gst.GstNative.MiniObjectUnref(handle);
            throw new InvalidOperationException(
                $"gst_app_sink_pull_object answered an object of type {type.Name}, which this binding does not wrap.");
        }

        if (wrapper is Gst.MiniObject miniObject)
        {
            return miniObject;
        }

        // The wrapper that was built owns the reference now, so it is what
        // releases it; the handle must not be unreffed a second time.
        (wrapper as IDisposable)?.Dispose();
        throw new InvalidOperationException(
            $"gst_app_sink_pull_object answered an object of type {type.Name}, which this binding wraps as something that is not a mini object.");
    }

    /// <summary>The <c>gst_app_sink_pull_object</c> entry point.</summary>
    [LibraryImport("GstApp", EntryPoint = "gst_app_sink_pull_object")]
    private static partial nint GstAppSinkPullObject(nint appsink);

    /// <summary>The <c>gst_app_sink_try_pull_object</c> entry point.</summary>
    [LibraryImport("GstApp", EntryPoint = "gst_app_sink_try_pull_object")]
    private static partial nint GstAppSinkTryPullObject(nint appsink, ulong timeout);

    /// <summary>The <c>gst_app_sink_set_simple_callbacks</c> entry point.</summary>
    [LibraryImport("GstApp", EntryPoint = "gst_app_sink_set_simple_callbacks")]
    private static partial void GstAppSinkSetSimpleCallbacks(nint appsink, nint cb);

    /// <summary>The <c>gst_app_sink_simple_callbacks_ref</c> entry point.</summary>
    [LibraryImport("GstApp", EntryPoint = "gst_app_sink_simple_callbacks_ref")]
    private static partial nint GstAppSinkSimpleCallbacksRef(nint cb);
}
