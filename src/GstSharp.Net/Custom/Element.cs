using System.Runtime.InteropServices;

namespace Gst;

public abstract partial class Element
{
    /// <summary>
    /// Links this element to the first of <paramref name="elements"/>, that one
    /// to the next, and so on, so that a whole chain is linked in one call.
    /// </summary>
    /// <param name="elements">
    /// The elements to link, in the order in which the data flows. An empty
    /// chain links nothing and succeeds.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every pair was linked. The chain is linked
    /// left to right and stops at the first pair that GStreamer refuses, so a
    /// failure leaves the pairs before it linked; the caller decides whether to
    /// tear the elements down or to try a different chain.
    /// </returns>
    /// <remarks>
    /// Each pair is linked by <see cref="Link(Gst.Element)"/>, which is what
    /// linking two elements calls directly; this overload only exists to spare
    /// the caller the chain of <c>&amp;&amp;</c>. All elements have to live in
    /// the same bin already.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="elements"/>, or one of its entries, is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">A wrapper was disposed.</exception>
    public bool Link(params Element[] elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        Element source = this;
        foreach (Element sink in elements)
        {
            ArgumentNullException.ThrowIfNull(sink, nameof(elements));

            if (!source.Link(sink))
            {
                return false;
            }

            source = sink;
        }

        return true;
    }

    /// <summary>
    /// Unlinks the chain that <see cref="Link(Gst.Element[])"/> established:
    /// this element from the first of <paramref name="elements"/>, that one
    /// from the next, and so on.
    /// </summary>
    /// <param name="elements">The elements of the chain, in the same order.</param>
    /// <remarks>
    /// Like <see cref="Unlink(Gst.Element)"/>, unlinking a pair that is not
    /// linked does nothing, so this reports no result.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="elements"/>, or one of its entries, is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">A wrapper was disposed.</exception>
    public void Unlink(params Element[] elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        Element source = this;
        foreach (Element sink in elements)
        {
            ArgumentNullException.ThrowIfNull(sink, nameof(elements));

            source.Unlink(sink);
            source = sink;
        }
    }

    /// <summary>
    /// Sends an event to this element, taking the event over.
    /// </summary>
    /// <param name="event">
    /// The event to send. The call consumes it: <paramref name="event"/> is
    /// disposed when this method returns, and using it afterwards throws
    /// <see cref="ObjectDisposedException"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the event was handled. An element that has
    /// no handler for it and no pad to pass it on to answers
    /// <see langword="false"/>. The event is consumed in either case.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the managed face of <c>gst_element_send_event</c>, whose
    /// <c>event</c> parameter is <c>transfer-ownership="full"</c>. The
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
    /// <c>using</c> declaration around the event stays correct.
    /// </para>
    /// <para>
    /// An element that implements no event handler passes the event on: a
    /// downstream event goes out of one of its linked sink pads and an upstream
    /// event out of one of its linked source pads. On a <see cref="Gst.Bin"/>,
    /// and therefore on a <see cref="Gst.Pipeline"/>, that is what makes this
    /// the call an application reaches for. Sending
    /// <see cref="Gst.Event.NewEos"/> to a pipeline is how a recording is ended
    /// on purpose: the end of the stream travels to the sources, then down the
    /// whole graph, every muxer writes its trailer and the sink posts
    /// <see cref="Gst.MessageType.Eos"/> on the bus. Tearing the pipeline down
    /// with <see cref="Gst.State.Null"/> instead ends it where it stands, which
    /// for a file that needs a trailer means an unplayable file.
    /// </para>
    /// <para>
    /// The call is thread safe and may be made while the pipeline is running,
    /// which is the only time most events mean anything. Events that trigger a
    /// preroll, such as a flushing seek or a step, are answered asynchronously
    /// with <see cref="Gst.MessageType.AsyncDone"/> on the bus rather than by
    /// the result of this call.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="event"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper or <paramref name="event"/> was disposed.
    /// </exception>
    public bool SendEvent(Gst.Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Both handles are read before anything is referenced, so that a
        // disposed wrapper throws without leaking the reference of the other.
        nint element = Handle;
        nint owned = @event.Handle;

        // The reference the call consumes. Without it the wrapper and the
        // library would both own the one reference the wrapper holds.
        GstNative.MiniObjectRef(owned);

        int result = GstElementSendEvent(element, owned);

        // The handles were read before the call, so nothing keeps either
        // wrapper alive across it on its own.
        GC.KeepAlive(this);

        // And the reference of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing.
        @event.Dispose();

        return result != 0;
    }

    /// <summary>The <c>gst_element_send_event</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_element_send_event")]
    private static partial int GstElementSendEvent(nint element, nint @event);
}
