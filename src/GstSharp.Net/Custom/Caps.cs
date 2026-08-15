namespace Gst;

public sealed partial class Caps
{
    /// <summary>
    /// Makes the caps writable, copying them when somebody else holds a
    /// reference to them, and returns the wrapper to keep using.
    /// </summary>
    /// <returns>
    /// This wrapper. The return value exists so that the call can be chained;
    /// it is never a second wrapper.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_caps_make_writable</c>. That function consumes the
    /// reference it is given and returns one that is either the same caps, when
    /// the caller held the only reference, or a fresh copy of them. The wrapper
    /// adopts whatever comes back, so the caps this wrapper stands for can
    /// change identity across the call, and it is the same wrapper either
    /// way — the C idiom <c>caps = gst_caps_make_writable (caps)</c> becomes a
    /// plain <c>caps.MakeWritable()</c>.
    /// </para>
    /// <para>
    /// Rewriting caps is what this is for: every mutating call on
    /// <see cref="Caps"/>, and every mutating call on a
    /// <see cref="Structure"/> reached through
    /// <see cref="Caps.GetStructure(uint)"/>, needs the caps to be writable
    /// first. Caps that come out of a pad, a sample or a message are shared
    /// with whoever produced them and are not.
    /// </para>
    /// <para>
    /// <b>Any handle read before the call is stale afterwards.</b>
    /// <see cref="MiniObject.Handle"/> has to be read again, and a
    /// <see cref="Structure"/> that was borrowed from the old caps points into
    /// the old caps and must not be used any more.
    /// </para>
    /// <para>
    /// This is single owner surgery. It is only correct while no other wrapper
    /// and no other thread uses this wrapper, which is the rule the C API
    /// imposes on <c>gst_caps_make_writable</c> as well: caps belong to one
    /// owner until they are handed on. Nothing here locks, and a reference
    /// another thread takes while the call runs is simply lost work.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.Caps MakeWritable()
    {
        _ = MakeWritableHandle();
        return this;
    }
}
