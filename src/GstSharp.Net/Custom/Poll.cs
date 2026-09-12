using System.Runtime.InteropServices;

namespace Gst;

public sealed unsafe partial class Poll
{
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

    /// <summary>The <c>gst_poll_get_read_gpollfd</c> entry point.</summary>
    /// <param name="set">The set to read.</param>
    /// <param name="fd">
    /// A block of <see cref="Gst.GLib.PollFD.RawSize"/> bytes the call fills.
    /// </param>
    [LibraryImport("Gst", EntryPoint = "gst_poll_get_read_gpollfd")]
    private static partial void GstPollGetReadGpollfd(nint set, byte* fd);
}
