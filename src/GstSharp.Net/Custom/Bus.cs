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
/// it.
/// </para>
/// </remarks>
public delegate Gst.BusSyncReply BusSyncHandler(Gst.Bus? bus, Gst.Message? message);

public unsafe partial class Bus
{
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
                Gst.Bus? busValue = Gst.GObject.Object.FromNative<Gst.Bus>(bus, Gst.Interop.Transfer.None);
                using Gst.Message? messageValue = Gst.Message.FromNative(message, Gst.Interop.Transfer.None);

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
