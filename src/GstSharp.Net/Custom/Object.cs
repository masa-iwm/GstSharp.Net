namespace Gst;

public abstract unsafe partial class Object
{
    /// <summary>
    /// Gets the flags of the object, that is <c>GST_OBJECT_FLAGS</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bits are handed out raw rather than as one enumeration, because the
    /// field is shared: <see cref="ObjectFlags"/> claims the low ones, and
    /// <see cref="ElementFlags"/>, <see cref="BinFlags"/> and
    /// <see cref="PadFlags"/> each claim their own above them, so no single
    /// enumeration names what a given object has set. Compare against the one
    /// that belongs to the class of the object —
    /// <c>IsFlagSet((uint)ElementFlags.Source)</c> — and read
    /// <see cref="ObjectFlags"/> out of any object.
    /// </para>
    /// <para>
    /// The read is unlocked, exactly as the C macro is: the field is a plain
    /// <c>guint32</c> in the object and neither the macro nor this takes the
    /// object lock for it, so what comes back is a snapshot of a value another
    /// thread may be changing.
    /// </para>
    /// <para>
    /// The field is read at its offset in <c>struct _GstObject</c>, because
    /// GStreamer exposes it through the macro only: the <c>GObject</c> takes 24
    /// bytes, the <c>GMutex</c> 8, and <c>name</c> and <c>parent</c> 8 each,
    /// which puts <c>flags</c> at 48.
    /// </para>
    /// </remarks>
    public uint Flags
    {
        get
        {
            uint flags = *(uint*)((byte*)Handle + 48);
            GC.KeepAlive(this);
            return flags;
        }
    }

    /// <summary>
    /// Tests whether every bit of <paramref name="flag"/> is set, that is
    /// <c>GST_OBJECT_FLAG_IS_SET</c>.
    /// </summary>
    /// <param name="flag">
    /// The bits to test for, cast out of the enumeration that names them:
    /// <c>(uint)ElementFlags.Sink</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every bit of <paramref name="flag"/> is set
    /// in <see cref="Flags"/>. A <paramref name="flag"/> of <c>0</c> is
    /// trivially set, as it is in C.
    /// </returns>
    public bool IsFlagSet(uint flag) => (Flags & flag) == flag;
}
