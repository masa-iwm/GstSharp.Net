namespace Gst;

public sealed partial class Caps
{
    /// <summary>
    /// Wraps caps that GStreamer keeps owning, for the length of one call.
    /// </summary>
    /// <param name="handle">The caps that are lent to managed code.</param>
    /// <remarks>
    /// This is how a vfunc override receives <c>transfer none</c> caps. The
    /// wrapper takes no reference of its own and disposing it only detaches it,
    /// so caps that outlive the call throw instead of releasing a reference
    /// that was never taken. See <see cref="Gst.Interop.Borrowed"/>.
    /// </remarks>
    internal static Caps Borrow(nint handle) => new(new Gst.Interop.Borrowed(handle));

    private Caps(Gst.Interop.Borrowed borrowed)
        : base(borrowed)
    {
    }

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
    /// Rewriting caps is what this is for: the calls that change the caps
    /// themselves — <see cref="Caps.RemoveStructure(uint)"/>,
    /// <see cref="Caps.StealStructure(uint)"/>,
    /// <see cref="Caps.MapInPlace(Gst.CapsMapFunc)"/> and
    /// <see cref="Caps.FilterAndMapInPlace(Gst.CapsFilterMapFunc)"/> — write
    /// into the caps in place, and the library refuses them on caps that
    /// somebody else holds. Caps that come out of a pad, a sample or a message
    /// are shared with whoever produced them and are not writable.
    /// </para>
    /// <para>
    /// A <see cref="Structure"/> taken from
    /// <see cref="Caps.GetStructure(uint)"/> needs none of this and gains
    /// nothing from it. <see cref="Structure"/> is a boxed type, so the binding
    /// hands back a copy that the caller owns and disposes rather than a window
    /// into the caps: changes made to that copy are not written back, whether
    /// the caps are writable or not.
    /// </para>
    /// <para>
    /// <b>Any handle read before the call is stale afterwards.</b>
    /// <see cref="MiniObject.Handle"/> has to be read again. A
    /// <see cref="Structure"/> read earlier is unaffected, because it was a
    /// copy of its own all along and never pointed into the old caps.
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
