using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// Handles a message in the thread that posted it, before the message reaches
/// the queue of the bus.
/// </summary>
/// <param name="bus">The bus the message was posted on.</param>
/// <param name="message">
/// The message. The wrapper is disposed when the handler returns, whatever the
/// handler answers, so nothing may keep it: read out of it what is needed, or
/// take a copy with <see cref="Gst.Message.Copy"/>.
/// </param>
/// <returns>What the bus is to do with the message.</returns>
/// <remarks>
/// <para>
/// The handler runs on the thread of the poster, which for a running pipeline
/// is a streaming thread, and it runs while that thread is blocked in the post.
/// It has to be quick and it has to be safe there.
/// </para>
/// <para>
/// <b>Answering <see cref="Gst.BusSyncReply.Drop"/> does not leak the message.</b>
/// The C contract makes the handler responsible for releasing the reference of
/// the poster when it drops a message, which is not something managed code can
/// do — the wrapper it is handed owns a reference of its own and releases only
/// that one. The binding therefore drops the reference of the poster for you,
/// in the trampoline, once the handler has answered
/// <see cref="Gst.BusSyncReply.Drop"/>. A handler that copied what it needs is
/// unaffected: a copy is an object of its own.
/// </para>
/// <para>
/// An exception that leaves the handler does not cross the native frame. It is
/// reported through <see cref="Gst.Interop.ExceptionTrap"/> and the bus is
/// answered <see cref="Gst.BusSyncReply.Pass"/>, so the message still reaches
/// the queue: a handler that failed has decided nothing, and swallowing an
/// error or an end-of-stream message would hang the application that waits for
/// it. <see cref="Gst.Bus.SubscribeSyncDrop"/> is the one place that answers
/// <see cref="Gst.BusSyncReply.Drop"/> instead, and its documentation says why.
/// </para>
/// <para>
/// Both arguments are values the C contract promises, so neither is nullable
/// and neither has to be checked. Should native code pass none all the same,
/// the trampoline reports that through
/// <see cref="Gst.Interop.ExceptionTrap"/> and answers
/// <see cref="Gst.BusSyncReply.Pass"/> without entering the handler, rather
/// than handing over a null the signature excludes.
/// </para>
/// </remarks>
public delegate Gst.BusSyncReply BusSyncHandler(Gst.Bus bus, Gst.Message message);

public unsafe partial class Bus
{
    /// <summary>
    /// The subscription of <see cref="SubscribeSyncDrop"/> that has not been
    /// disposed, or <see langword="null"/> when the bus carries none.
    /// </summary>
    /// <remarks>
    /// One field per bus is the right scope, because a <c>GObject</c> wrapper
    /// is interned: every lookup of the same bus hands out this instance, so
    /// two parts of an application that both subscribe meet here even when
    /// neither knows about the other. What it does not cover is a wrapper that
    /// was disposed in between, which the next lookup replaces by a new one
    /// that knows of no subscription — the same caveat that event handlers
    /// carry.
    /// </remarks>
    private SyncSubscription? _syncSubscription;

    /// <summary>
    /// Creates a bus, choosing whether it delivers messages asynchronously.
    /// </summary>
    /// <param name="enableAsync">
    /// <see langword="true"/> for the ordinary bus, which queues what is posted
    /// on it; <see langword="false"/> for one that serves synchronous handlers
    /// only.
    /// </param>
    /// <returns>The new bus.</returns>
    /// <remarks>
    /// <para>
    /// <c>enable-async</c> is construct only and write only, so it is neither a
    /// property of the wrapper nor an argument of <c>gst_bus_new</c>: the only
    /// moment it can be given a value is while the bus is being constructed,
    /// which is what this overload does. <see cref="New()"/> is the same call
    /// with the property left at its default, which is
    /// <see langword="true"/>.
    /// </para>
    /// <para>
    /// A bus built with <see langword="false"/> has no asynchronous delivery:
    /// the queue exists, since <c>gst_bus_init</c> creates it whatever the
    /// property says, but the <c>GstPoll</c> that signals it is never created,
    /// and <c>gst_bus_post</c> keys on that. It still runs the synchronous
    /// handler and still emits <c>sync-message</c>, and then <b>drops the
    /// message</b> instead of queueing it, so nothing is ever pushed and
    /// nothing is ever popped. That is the point of the option: an element
    /// that only needs its own synchronous handler pays for no delivery
    /// machinery.
    /// </para>
    /// <para>
    /// What the rest of the surface then does is worth spelling out per
    /// member, because <b>the binding cannot guard any of it</b>:
    /// <c>enable-async</c> is write only, so a wrapper cannot ask a bus it was
    /// handed how it was built, and every member that misbehaves below is a
    /// generated one that no hand written override intercepts today. What
    /// works is everything that never reaches for the <c>GstPoll</c> that was
    /// not created: <see cref="Pop"/>, <see cref="PopFiltered"/>,
    /// <see cref="Peek"/>, <see cref="HavePending"/> and
    /// <see cref="SetFlushing"/>, which read the queue that exists and is
    /// simply always empty; <see cref="SetSyncHandler(Gst.BusSyncHandler)"/>,
    /// <see cref="ClearSyncHandler"/>, <see cref="SubscribeSyncDrop"/> and
    /// <see cref="EnableSyncMessageEmission"/> with the
    /// <see cref="SyncMessage"/> event it enables, which are the half of the
    /// bus this option keeps and behave exactly as they do anywhere else; and
    /// disposal.
    /// </para>
    /// <para>
    /// <see cref="AddWatch"/>, <see cref="AddSignalWatch"/> and
    /// <see cref="AddSignalWatchFull"/> install a source that never delivers
    /// anything, and none of the three says so: the two signal watches answer
    /// nothing at all, and <see cref="AddWatch"/> hands back the ordinary
    /// non-zero source id. Of the entry points this binding reaches, none
    /// carries the <c>bus-&gt;priv-&gt;poll != NULL</c> guard.
    /// <c>gst_bus_create_watch</c> and <c>gst_bus_get_pollfd</c> do carry it,
    /// and neither is bound; <c>gst_bus_add_watch_full</c> and
    /// <c>gst_bus_add_signal_watch_full</c> reach
    /// <c>gst_bus_create_watch_unlocked</c> instead, which guards only against
    /// a second <c>GSource</c> and not against the missing poll, and which
    /// hands the source the <c>GPollFD</c> of the bus, a structure
    /// <c>gst_bus_constructed</c> fills only when the <c>GstPoll</c> is built.
    /// The source therefore watches a <c>GPollFD</c> that was never filled,
    /// over a queue nothing is ever pushed onto, so it has nothing to dispatch
    /// and the <see cref="Message"/> event never fires.
    /// </para>
    /// <para>
    /// <see cref="Poll"/> is that signal watch plus a nested main loop, so it
    /// sees nothing either: given a timeout it answers <see langword="null"/>
    /// when the timeout expires, and given <see cref="Gst.ClockTime.None"/> it
    /// never returns at all. <see cref="TimedPop"/> and
    /// <see cref="TimedPopFiltered"/> answer <see langword="null"/> with a
    /// GLib critical for every timeout but zero, since
    /// <c>gst_bus_timed_pop_filtered</c> guards
    /// <c>timeout == 0 || bus-&gt;priv-&gt;poll != NULL</c>; a timeout of zero
    /// is the non-blocking pop and works.
    /// </para>
    /// <para>
    /// A bus built with <see langword="false"/> is therefore a bus for a
    /// synchronous handler and the non-blocking pops, and for nothing else.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">GObject returned no instance.</exception>
    public static Gst.Bus New(bool enableAsync)
    {
        // gst_bus_new is g_object_new plus a sink of the floating reference,
        // and there is no _new_with_properties beside it, so the construct
        // only property has to be given to GObject directly. The floating
        // reference the constructor answers is what the wrapper sinks, which
        // is the ownership gst_bus_new hands over as well.
        System.Span<byte> buffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
        using Gst.Interop.Utf8Scope name = Gst.Interop.GMarshal.StackUtf8("enable-async", buffer);

        Gst.GObject.Value value = Gst.GObject.Value.New(Gst.GObject.GType.Boolean);
        try
        {
            value.SetBoolean(enableAsync);

            byte* names = name.Pointer;
            nint handle = Gst.Interop.GObjectNative.ObjectNewWithProperties(
                GetGType(),
                1,
                &names,
                &value.NativeValue);

            return Gst.GObject.Object.FromNative<Gst.Bus>(handle, Gst.Interop.Transfer.Full)
                ?? throw new InvalidOperationException("g_object_new_with_properties returned no bus.");
        }
        finally
        {
            value.Dispose();
        }
    }

    /// <summary>
    /// Installs the handler that sees every message in the thread that posts
    /// it, replacing the handler that is installed.
    /// </summary>
    /// <param name="func">The handler to install.</param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_bus_set_sync_handler</c> with a handler. Since 1.16.3 the
    /// call replaces whatever is installed without complaining and without a
    /// race: it swaps the handler under the lock of the bus and releases the
    /// old one afterwards, so a handler that is running on another thread at
    /// that moment finishes on the old handler and every later post reaches the
    /// new one. There is one sync handler per bus, and the last install wins.
    /// </para>
    /// <para>
    /// A <see cref="Gst.Pipeline"/> hands out a bus that has no sync handler,
    /// so the first install on it is free. Installing one on a bus that
    /// GStreamer itself drives is not: <c>gst_bus_sync_signal_handler</c>, the
    /// handler that <see cref="EnableSyncMessageEmission"/> relies on, is
    /// installed the same way and would be replaced by this call.
    /// </para>
    /// <para>
    /// <see cref="ClearSyncHandler"/> takes the handler off again, which is the
    /// call to make at teardown. It is the null form of the same C function,
    /// and it is separate here because a handler is not an optional argument of
    /// an install — removing one is a different intention, and spelling it as
    /// <c>SetSyncHandler(null)</c> would let a mislaid null silence a bus by
    /// accident.
    /// </para>
    /// <para>
    /// What the handler captures stays alive for as long as the bus holds the
    /// handler: the reference chain runs from the <c>GCHandle</c> this call
    /// allocates into the closure. GStreamer releases it when the handler is
    /// replaced or cleared and when the bus itself is finalized, and the
    /// destroy notification then frees the <c>GCHandle</c>. A handler that
    /// captured the bus is a cycle that the collector cannot break, which is
    /// the reason to clear it rather than to drop the bus and hope.
    /// </para>
    /// <para>
    /// <b>A handler that throws is answered <see cref="Gst.BusSyncReply.Pass"/>.</b>
    /// The exception is reported through
    /// <see cref="Gst.Interop.ExceptionTrap"/> and the message takes the
    /// ordinary route onto the queue, because a handler that failed has decided
    /// nothing and whoever reads the queue still has to see the message —
    /// swallowing an error or an end-of-stream message would hang the
    /// application that waits for it. <see cref="SubscribeSyncDrop"/> answers
    /// <see cref="Gst.BusSyncReply.Drop"/> in the same situation, and the
    /// difference is deliberate: a subscription is by construction the only
    /// consumer of the bus, so there is nobody behind it to see a passed
    /// message and it would sit in the queue for the lifetime of the pipeline.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="func"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public void SetSyncHandler(Gst.BusSyncHandler func)
    {
        ArgumentNullException.ThrowIfNull(func);

        // The handle is read before the state is allocated, so that a disposed
        // wrapper throws without leaving a GCHandle behind that nothing frees.
        nint bus = Handle;

        Gst.Interop.CallbackHandle state = Gst.Interop.CallbackHandle.Alloc(func);
        BusNative.SetSyncHandler(
            bus,
            BusSyncHandlerTrampoline.Pointer,
            state.UserData,
            (nint)Gst.Interop.CallbackHandle.DestroyNotify);

        GC.KeepAlive(this);
    }

    /// <summary>
    /// Takes the sync handler off the bus, so that every message goes to the
    /// queue again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <c>gst_bus_set_sync_handler</c> with a null handler, which
    /// GStreamer has performed under the lock of the bus since 1.16.3 and which
    /// is therefore safe to call while messages are being posted. The handler
    /// that was installed is released, its destroy notification runs and what
    /// its closure captured becomes collectible.
    /// </para>
    /// <para>
    /// Clearing a bus that has no handler does nothing.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public void ClearSyncHandler()
    {
        BusNative.SetSyncHandler(Handle, nint.Zero, nint.Zero, nint.Zero);
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Delivers every message to a handler in the thread that posts it and
    /// drops it afterwards, so that the queue of the bus never grows.
    /// </summary>
    /// <param name="handler">
    /// The handler, which receives the bus and the message. The wrapper of the
    /// message is disposed when the handler returns, whatever it does, so
    /// nothing may keep it: read out of it what is needed, or take a copy with
    /// <see cref="Gst.Message.Copy"/>.
    /// </param>
    /// <returns>
    /// The subscription. Disposing it takes the handler off the bus again.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the shape an application with no main loop needs. Messages a bus
    /// nobody reads accumulates in its queue for the lifetime of the pipeline,
    /// and a poll of the bus is the usual answer; an application that would
    /// rather be told than ask installs a sync handler instead, and then has to
    /// answer <see cref="Gst.BusSyncReply.Drop"/> from it or gain nothing —
    /// passing puts the message on the queue after all. That reply is the one
    /// with an ownership rule attached, and this call is that rule made
    /// unnecessary: the handler here answers nothing, the binding drops for it,
    /// and the queue stays empty.
    /// </para>
    /// <code>
    /// using IDisposable subscription = pipeline.GetBus().SubscribeSyncDrop(
    ///     (_, message) => Console.WriteLine(message.Type));
    /// </code>
    /// <para>
    /// The handler is an <see cref="Action{T1, T2}"/> rather than a
    /// <see cref="Gst.BusSyncHandler"/> on purpose: a handler whose reply this
    /// call would discard is a handler whose reply reads as if it decided
    /// something. When the reply matters — a handler that passes some messages
    /// and drops others, or one that answers
    /// <see cref="Gst.BusSyncReply.Async"/> — use
    /// <see cref="SetSyncHandler(Gst.BusSyncHandler)"/>, which this is built
    /// on and which changes nothing about how such a handler behaves.
    /// </para>
    /// <para>
    /// <b>There is one sync handler per bus, and this takes it.</b> While a
    /// subscription of this bus has not been disposed, a second call throws
    /// <see cref="InvalidOperationException"/> rather than silencing the first
    /// one: two subscribers of one bus is a mistake that used to be silent, and
    /// only the survivor of it saw any message at all. Dispose the subscription
    /// that is live and subscribe again, or fan out to several consumers from
    /// inside the one handler.
    /// </para>
    /// <para>
    /// <b>What this does not guard is the raw install.</b>
    /// <see cref="SetSyncHandler(Gst.BusSyncHandler)"/> and
    /// <see cref="ClearSyncHandler"/> mirror <c>gst_bus_set_sync_handler</c>,
    /// whose contract is a swap and stays one, and
    /// <see cref="EnableSyncMessageEmission"/> installs a handler the same way.
    /// Calling any of them while a subscription is live replaces the handler on
    /// the bus, so the subscriber stops seeing messages — and the subscription
    /// object still holds the one slot, so subscribing again keeps throwing
    /// until it is disposed. Disposing it then clears whatever is installed at
    /// that moment, which is the raw handler rather than its own: C offers no
    /// exchange, only a swap, and putting back a handler this binding may not
    /// have installed would be a guess.
    /// </para>
    /// <para>
    /// Disposing the subscription more than once is allowed and clears the bus
    /// once. A subscription whose bus wrapper was disposed in the meantime
    /// clears nothing: that wrapper has given up its part in the lifetime of
    /// the bus already, and the handler goes away with the bus itself. Either
    /// way the slot is given back, so a later subscription is accepted.
    /// </para>
    /// <para>
    /// The handler runs on the thread of the poster, which for a running
    /// pipeline is a streaming thread, and it runs while that thread is blocked
    /// in the post. It has to be quick and it has to be safe there.
    /// </para>
    /// <para>
    /// <b>A handler that throws is trapped and its message is dropped all the
    /// same.</b> The exception is reported through
    /// <see cref="Gst.Interop.ExceptionTrap"/> and the reply is still
    /// <see cref="Gst.BusSyncReply.Drop"/>, so the reference of the poster is
    /// released and nothing is left behind. This is the one place that departs
    /// from <see cref="SetSyncHandler(Gst.BusSyncHandler)"/>, which answers
    /// <see cref="Gst.BusSyncReply.Pass"/> when a handler fails, and the reason
    /// is what a subscription is: a passed message goes onto the queue that
    /// this subscription is the reason nobody reads, so it is not deferred to
    /// another consumer, it stays there for the lifetime of the pipeline. A
    /// handler that fails on every message would leak one message per message.
    /// A subscriber that wants to decide for itself should catch inside the
    /// handler, and one that needs a message kept should copy it with
    /// <see cref="Gst.Message.Copy"/> before doing anything that can throw.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// This bus already carries a subscription that has not been disposed.
    /// </exception>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public IDisposable SubscribeSyncDrop(Action<Gst.Bus, Gst.Message> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        SyncSubscription subscription = new(this);

        // The slot is claimed before the handler is installed, so that two
        // threads subscribing at once cannot both install and leave one of the
        // two subscribers silently unreachable.
        if (Interlocked.CompareExchange(ref _syncSubscription, subscription, null) is not null)
        {
            throw new InvalidOperationException(
                "This bus already has a sync drop subscription. Dispose that subscription before "
                + "subscribing again: there is one sync handler per bus, and a second subscription "
                + "would silence the first.");
        }

        try
        {
            // The ordinary install, with the reply supplied here. Everything
            // that makes a dropping handler correct — the wrapper of the
            // message and the release of the poster's reference that Drop makes
            // the handler's job — is the trampoline's, unchanged.
            SetSyncHandler((bus, message) =>
            {
                try
                {
                    handler(bus, message);
                }
                catch (Exception exception)
                {
                    // See the remarks: this is the deliberate difference from
                    // SetSyncHandler. Passing here would put the message on a
                    // queue that this subscription is the reason nobody reads,
                    // and it would stay there for the lifetime of the pipeline.
                    // Dropping releases the reference of the poster as always.
                    Gst.Interop.ExceptionTrap.Report(exception);
                }

                return Gst.BusSyncReply.Drop;
            });
        }
        catch
        {
            // Nothing was installed, so the slot was not taken after all. A
            // wrapper that was disposed is the case that gets here.
            Interlocked.CompareExchange(ref _syncSubscription, null, subscription);
            throw;
        }

        return subscription;
    }

    /// <summary>
    /// The subscription of <see cref="SubscribeSyncDrop"/>: it holds the bus
    /// and clears its sync handler once.
    /// </summary>
    /// <remarks>
    /// Holding the wrapper strongly is deliberate. A subscription is the thing
    /// an application keeps, and a bus whose only holder let go of it would
    /// leave a subscription that clears an object it no longer knows.
    /// </remarks>
    private sealed class SyncSubscription : IDisposable
    {
        private Gst.Bus? _bus;

        /// <summary>Initialises the subscription of one bus.</summary>
        /// <param name="bus">The bus whose handler was installed.</param>
        internal SyncSubscription(Gst.Bus bus) => _bus = bus;

        /// <summary>Takes the sync handler off the bus, at most once.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _bus, null) is not { } bus)
            {
                return;
            }

            try
            {
                // A disposed wrapper has given up its part in the lifetime of
                // the bus, and its Handle throws. The handler goes away with
                // the bus.
                if (!bus.IsDisposed)
                {
                    bus.ClearSyncHandler();
                }
            }
            finally
            {
                // The slot goes back once the handler is off, and whatever
                // state the wrapper is in, so that a bus this subscription can
                // no longer reach can be subscribed to again.
                //
                // The order is the whole point. Releasing the slot first would
                // leave a window in which another thread claims it and installs
                // its handler, and the clear above would then wipe that handler
                // and leave a subscription that holds the slot and never sees a
                // message — the silent silencing this guard exists to stop.
                // Clearing first costs a subscriber that races a disposal a
                // visible InvalidOperationException it can retry instead.
                //
                // The comparison is what stops a disposal from releasing a slot
                // that another subscription holds by then.
                Interlocked.CompareExchange(ref bus._syncSubscription, null, this);
            }
        }
    }

    /// <summary>The native entry point of <see cref="Gst.BusSyncHandler"/>.</summary>
    /// <remarks>
    /// <para>
    /// This trampoline is written by hand because of one line of
    /// <c>gst_bus_post</c>: when the handler answers <c>GST_BUS_DROP</c> the
    /// poster's reference is not released by the bus, and dropping it is the
    /// handler's job. A generated trampoline hands the managed handler a
    /// borrowed wrapper and releases only what that wrapper took, so every
    /// dropped message would leak. The reply is inspected here instead and the
    /// reference is dropped where the C contract asks for it.
    /// </para>
    /// <para>
    /// The order matters. The wrapper of the message is disposed first, which
    /// gives back the reference it took for the handler, and the reference of
    /// the poster goes afterwards. On a message that nothing else holds the
    /// second release is the one that frees it, which is what dropping a
    /// message means.
    /// </para>
    /// <para>
    /// Only <c>GST_BUS_DROP</c> is the handler's to release.
    /// <c>GST_BUS_PASS</c> hands the reference to the queue and
    /// <c>GST_BUS_ASYNC</c> leaves it with <c>gst_bus_post</c>, which releases
    /// it once the message has been delivered.
    /// </para>
    /// </remarks>
    internal static class BusSyncHandlerTrampoline
    {
        /// <summary>Gets the address that is handed to native code.</summary>
        internal static nint Pointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int>)&Invoke;

        /// <summary>Runs the managed handler of a sync handler installation.</summary>
        /// <param name="bus">The bus the message was posted on.</param>
        /// <param name="message">The message that was posted.</param>
        /// <param name="userData">The <c>GCHandle</c> of the managed handler.</param>
        /// <returns>The reply of the handler, as a <c>GstBusSyncReply</c>.</returns>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Invoke(nint bus, nint message, nint userData)
        {
            Gst.BusSyncReply reply;

            try
            {
                if (Gst.Interop.CallbackHandle.GetState<Gst.BusSyncHandler>(userData) is not { } callback)
                {
                    // The state is gone, so nothing decided anything. Passing
                    // leaves the message to the queue and to gst_bus_post,
                    // which is the answer that loses nothing.
                    return (int)Gst.BusSyncReply.Pass;
                }

                // The bus is a GObject and its wrapper is interned, so it is
                // not disposed here; the message is a mini object and this
                // wrapper owns the reference it took.
                //
                // Neither is nullable on the delegate, because the C contract
                // promises both. A conversion that produced nothing all the
                // same is a broken promise rather than a case the handler has
                // to answer, so it is thrown here, inside the try: the trap
                // reports it, the bus is answered Pass, and the handler is not
                // entered with a null its signature excludes.
                Gst.Bus busValue = Gst.GObject.Object.FromNative<Gst.Bus>(bus, Gst.Interop.Transfer.None)
                    ?? throw new InvalidOperationException("GstBusSyncHandler passed no bus.");
                using Gst.Message messageValue = Gst.Message.FromNative(message, Gst.Interop.Transfer.None)
                    ?? throw new InvalidOperationException("GstBusSyncHandler passed no message.");

                reply = callback(busValue, messageValue);
            }
            catch (Exception exception)
            {
                Gst.Interop.ExceptionTrap.Report(exception);

                // A handler that threw has not dropped anything, so the
                // reference of the poster is left where it is and the message
                // takes the ordinary route. Answering Drop here — which is what
                // the zero of the enumeration would do — would swallow the
                // error and end-of-stream messages an application waits for.
                return (int)Gst.BusSyncReply.Pass;
            }

            if (reply == Gst.BusSyncReply.Drop)
            {
                // gst_bus_post does not release the message it was given when
                // the handler drops it. Managed code cannot hold the reference
                // of the poster, so the binding releases it here.
                GstNative.MiniObjectUnref(message);
            }

            return (int)reply;
        }
    }
}
