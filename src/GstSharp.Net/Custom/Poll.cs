using System.Runtime.InteropServices;

namespace Gst;

public sealed unsafe partial class Poll
{
    /// <summary>Creates a new file descriptor set.</summary>
    /// <param name="controllable">
    /// <see langword="true"/> to make a wait on the set restartable and
    /// flushable, which is what <see cref="Restart"/> and
    /// <see cref="SetFlushing"/> need.
    /// </param>
    /// <returns>The new set.</returns>
    /// <exception cref="InvalidOperationException">
    /// The C call answered <see langword="null"/>, which it does when the
    /// allocation or the control socket pair failed (gstpoll.c:662-727).
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is <c>gst_poll_new</c>, which the gir declares
    /// <c>introspectable="0"</c> because the C marks it <c>(skip)</c>
    /// (gstpoll.c:663), so it is written by hand.
    /// </para>
    /// <para>
    /// <b>The caller owns the set and releases it with <see cref="Free"/>.</b>
    /// A set is not reference counted: <c>gst_poll_free</c> takes the one
    /// ownership there is, and the instance must not be used again afterwards.
    /// Nothing frees it on the caller's behalf. There is no finalizer and the
    /// type is not <see cref="IDisposable"/>, on purpose: <c>gst_poll_free</c>
    /// must not run while another thread waits on the set, and neither a
    /// finalizer nor a <c>using</c> block can promise that. Either may be added
    /// in a later release without breaking this shape.
    /// </para>
    /// </remarks>
    public static Poll New(bool controllable)
    {
        nint handle = GstPollNew(controllable ? 1 : 0);
        if (handle == 0)
        {
            throw new InvalidOperationException("gst_poll_new answered NULL; the file descriptor set was not created.");
        }

        return new Poll(handle);
    }

    /// <summary>
    /// Creates a new set that schedules cancellable timeouts rather than
    /// watching descriptors.
    /// </summary>
    /// <returns>The new timer set.</returns>
    /// <exception cref="InvalidOperationException">
    /// The C call answered <see langword="null"/>, which it does when the
    /// allocation or the control socket pair failed (gstpoll.c:732-768).
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is <c>gst_poll_new_timer</c>, which the gir declares
    /// <c>introspectable="0"</c> because the C marks it <c>(skip)</c>
    /// (gstpoll.c:732), so it is written by hand. The timeout is taken by
    /// <see cref="Wait"/>, and <see cref="WriteControl"/> cancels one from
    /// another thread.
    /// </para>
    /// <para>
    /// <b>The caller owns the set and releases it with <see cref="Free"/></b>,
    /// under exactly the rule <see cref="New"/> states: no finalizer, no
    /// <see cref="IDisposable"/>, and no use of the instance after
    /// <see cref="Free"/>.
    /// </para>
    /// </remarks>
    public static Poll NewTimer()
    {
        nint handle = GstPollNewTimer();
        if (handle == 0)
        {
            throw new InvalidOperationException(
                "gst_poll_new_timer answered NULL; the file descriptor set was not created.");
        }

        return new Poll(handle);
    }

    /// <summary>
    /// Reads the descriptor of the reading half of the control socket of the
    /// set.
    /// </summary>
    /// <returns>
    /// The descriptor, watched for <see cref="Gst.GLib.IOCondition.In"/>,
    /// <see cref="Gst.GLib.IOCondition.Hup"/> and
    /// <see cref="Gst.GLib.IOCondition.Err"/>, with nothing reported yet.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_poll_get_read_gpollfd</c>, which integrates a
    /// <see cref="Poll"/> into an event loop that is not its own: the
    /// descriptor is signalled when <see cref="WriteControl"/> raised the
    /// wakeup of the set, and <see cref="ReadControl"/> releases it again
    /// (gstpoll.c:808-825).
    /// </para>
    /// <para>
    /// The C fills all three fields and reads none of them, so nothing of the
    /// answer comes from the caller. The gir does not say so — it spells the
    /// parameter as a plain pointer with no direction — which is why the
    /// member is written by hand.
    /// </para>
    /// <para>
    /// <b>Never read from, write to or close the descriptor.</b> It belongs to
    /// the set and is released by <see cref="Free"/>.
    /// </para>
    /// </remarks>
    public Gst.GLib.PollFD GetReadGpollfd()
    {
        // Allocated as longs so that the block is aligned for the gint64 the
        // C writes into it on 64 bit Windows.
        long* block = stackalloc long[Gst.GLib.PollFD.RawSize / sizeof(long)];
        byte* raw = (byte*)block;

        // The block is cleared first: on a platform whose GPollFD is eight
        // bytes wide the call writes only those eight, and the rest of the
        // block would otherwise be whatever the stack held.
        Gst.GLib.PollFD.PrepareOut(raw);

        GstPollGetReadGpollfd(Handle, raw);

        Gst.GLib.PollFD fd = Gst.GLib.PollFD.FromNative(raw);
        GC.KeepAlive(this);
        return fd;
    }

    /// <summary>The <c>gst_poll_new</c> entry point.</summary>
    /// <param name="controllable">Whether a wait on the set can be controlled.</param>
    /// <returns>The new set, or zero when the call failed.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_poll_new")]
    private static partial nint GstPollNew(int controllable);

    /// <summary>The <c>gst_poll_new_timer</c> entry point.</summary>
    /// <returns>The new timer set, or zero when the call failed.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_poll_new_timer")]
    private static partial nint GstPollNewTimer();

    /// <summary>The <c>gst_poll_get_read_gpollfd</c> entry point.</summary>
    /// <param name="set">The set to read.</param>
    /// <param name="fd">
    /// A block of <see cref="Gst.GLib.PollFD.RawSize"/> bytes the call fills.
    /// </param>
    [LibraryImport("Gst", EntryPoint = "gst_poll_get_read_gpollfd")]
    private static partial void GstPollGetReadGpollfd(nint set, byte* fd);
}
