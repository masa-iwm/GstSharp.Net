using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst.Video;

/// <summary>
/// The three public fields of a <c>GstColorBalanceChannel</c>, which the
/// generator does not emit: field accessors are a record concern, and a
/// GObject derived class never reaches that path, plus the construction of a
/// channel of one's own. See
/// <c>$comment-step32-color-balance-channel-fields</c> in
/// <c>girs/overlays/fixups.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// A channel has two provenances, and they behave differently. One listed by
/// an element through <see cref="ColorBalanceExtensions.ListChannels"/> is
/// borrowed: the element wrote the three fields while constructing it, nothing
/// in GStreamer writes them again, and it is not the caller's to dispose or to
/// write to. One made by <see cref="New(string, int, int)"/> is the caller's,
/// and the three setters here are how its fields are filled in after the fact.
/// </para>
/// <para>
/// The three setters enforce that difference rather than document it:
/// <see cref="New(string, int, int)"/> marks the instance it makes, and a
/// channel without that mark — every channel an element listed — answers a
/// write with an <see cref="InvalidOperationException"/>. Writing one would
/// break the element that owns it: elements find their own channel by the
/// content of its label, <c>playsink</c> asserts that the search found one
/// (<c>g_assert (channel)</c>, <c>gstplaysink.c:1720</c> on the video path and
/// <c>:5548</c> on the audio one) and aborts the process when it did not, and
/// the write frees the previous string while the element may be reading it on
/// another thread.
/// </para>
/// <para>
/// There is no lock on either side: the fields are plain struct fields, and the
/// channel derives from <c>GObject</c> rather than from <c>GstObject</c>, so it
/// carries no object lock. Write a channel of one's own before handing it to
/// anything else, the way every implementer in GStreamer does.
/// </para>
/// </remarks>
public unsafe partial class ColorBalanceChannel
{
    /// <summary>
    /// The quark of the mark a channel this binding made carries, or zero while
    /// it has not been resolved yet.
    /// </summary>
    private static uint _ownChannelQuark;

    /// <summary>Creates a color balance channel of one's own.</summary>
    /// <param name="label">
    /// The descriptive name of the channel. See the remarks: a channel meant
    /// for <c>playsink</c> needs a label it recognizes.
    /// </param>
    /// <param name="minValue">The minimum valid value for the channel.</param>
    /// <param name="maxValue">The maximum valid value for the channel.</param>
    /// <returns>The channel, which is the caller's.</returns>
    /// <remarks>
    /// <para>
    /// <c>GstColorBalanceChannel</c> is a plain <c>GObject</c> subtype with no
    /// constructor function and no properties of its own: the three fields are
    /// written directly, which is what every implementer in GStreamer does. The
    /// instance is not floating, so what comes back here holds the only
    /// reference and the wrapper owns it.
    /// </para>
    /// <para>
    /// <strong>The label is not decoration.</strong> An element that proxies
    /// the channels of another one looks its own channel up by substring:
    /// <c>playsink</c> walks the real channel list, keeps the first channel
    /// whose <c>label</c> contains the proxy's label, and then asserts that it
    /// found one (<c>g_assert (channel)</c>, <c>gstplaysink.c:1720</c> on the
    /// video path and <c>:5548</c> on the audio one). A channel whose label is
    /// <see langword="null"/>, or which lacks the substring that side expects,
    /// aborts the process there. Nothing in this binding can guard against
    /// that, so a channel handed to <c>playsink</c> has to carry a label that
    /// contains the name it looks for.
    /// </para>
    /// <para>
    /// The instance is marked as one of this binding's own, which is what the
    /// three setters check: they refuse a channel that carries no mark, since
    /// writing a channel an element listed breaks that element's own lookup by
    /// label, races its readers and aborts under <c>playsink</c>. The mark goes
    /// on the object rather than on the wrapper, because a GObject wrapper is
    /// replaced whenever the last one was disposed and the object is wrapped
    /// again.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="label"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="label"/> contains a null character, which native code
    /// would only see the part in front of.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minValue"/> is greater than <paramref name="maxValue"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">GObject returned no instance.</exception>
    public static ColorBalanceChannel New(string label, int minValue, int maxValue)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);

        // The label is copied before the instance exists, so the one label the
        // copy refuses — a string with a null character in it — cannot leave an
        // instance behind with nothing to release it. The copy is the channel's
        // own: the C dispose frees the field with g_free, whoever wrote it.
        nint labelCopy = StrDupNative(label);

        nint handle = Gst.Interop.GObjectNative.ObjectNewWithProperties(GetGType(), 0, null, null);
        if (handle == nint.Zero)
        {
            Gst.Interop.GLibNative.Free(labelCopy);
            throw new InvalidOperationException("g_object_new_with_properties returned no color balance channel.");
        }

        // The fields are written before the wrapper exists, so nothing can
        // observe the channel half filled in.
        ColorBalanceChannelRaw* raw = (ColorBalanceChannelRaw*)handle;
        raw->Label = labelCopy;
        raw->MinValue = minValue;
        raw->MaxValue = maxValue;

        // The mark is written before the wrapper exists too, so no channel of
        // one's own is ever observed unmarked, and only this factory writes it:
        // the absence of the mark is what every other provenance answers.
        Gst.Interop.GObjectNative.ObjectSetQdata(handle, OwnChannelQuark(), 1);

        return Gst.GObject.Object.FromNative<ColorBalanceChannel>(handle, Gst.Interop.Transfer.Full)
            ?? throw new InvalidOperationException("g_object_new_with_properties returned no color balance channel.");
    }

    /// <summary>Copies a string into memory the GLib allocator made.</summary>
    private static nint StrDupNative(string text)
    {
        System.Span<byte> buffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
        using Gst.Interop.Utf8Scope scope = Gst.Interop.GMarshal.StackUtf8(text, buffer);
        return Gst.Interop.GLibNative.StrDup(scope.Pointer);
    }

    /// <summary>
    /// Answers the quark of the mark a channel of one's own carries, resolving
    /// it once.
    /// </summary>
    /// <returns>The quark of <c>gstsharp-color-balance-channel-of-ones-own</c>.</returns>
    /// <remarks>
    /// Resolving it twice answers the same quark, which is why the race between
    /// two threads that both find the field unset is not worth a lock.
    /// </remarks>
    private static uint OwnChannelQuark()
    {
        uint quark = _ownChannelQuark;
        if (quark != 0)
        {
            return quark;
        }

        quark = Gst.GLib.Quark.FromString("gstsharp-color-balance-channel-of-ones-own").Value;
        _ownChannelQuark = quark;
        return quark;
    }

    /// <summary>
    /// Refuses a write to a channel that was not made by
    /// <see cref="New(string, int, int)"/>.
    /// </summary>
    /// <param name="member">The member being written, which the message names.</param>
    /// <exception cref="InvalidOperationException">
    /// The channel was listed by an element rather than made here.
    /// </exception>
    /// <exception cref="System.ObjectDisposedException">The wrapper was disposed.</exception>
    /// <remarks>
    /// The C refuses nothing: the fields are public and unguarded. What makes a
    /// write to a borrowed channel worse than a wrong value is that the element
    /// that owns the channel finds it again by the content of its label, so a
    /// rewritten label silently disables the element's own <c>set_value</c> and
    /// <c>get_value</c>, aborts the process under <c>playsink</c>
    /// (<c>g_assert (channel)</c>, <c>gstplaysink.c:1720</c> and <c>:5548</c>),
    /// and frees the previous string under a reader on another thread, there
    /// being no lock on either side.
    /// </remarks>
    private void ThrowIfNotOnesOwn(string member)
    {
        nint handle = Handle;
        bool marked = Gst.Interop.GObjectNative.ObjectGetQdata(handle, OwnChannelQuark()) != nint.Zero;

        // Reading Handle is the last use of this wrapper, so without this the
        // collector may finalize it while the lookup is still running.
        System.GC.KeepAlive(this);

        if (!marked)
        {
            throw new InvalidOperationException(
                $"{member} can only be written on a channel from ColorBalanceChannel.New. This channel was " +
                "listed by an element, which finds it again by the content of its label and reads it without " +
                "a lock.");
        }
    }

    /// <summary>A string containing a descriptive name for this channel.</summary>
    /// <remarks>
    /// <para>
    /// The string is copied out of the instance on every read. The storage
    /// belongs to the channel and is released with it, so what comes back here
    /// is the caller's and outlives it.
    /// </para>
    /// <para>
    /// The three fields are plain struct fields behind no lock, and the channel
    /// itself carries no object lock either — it derives from <c>GObject</c>
    /// rather than from <c>GstObject</c>. Nothing needs one: every implementer
    /// in GStreamer writes the three fields once while constructing the
    /// channel, before the channel is reachable from
    /// <see cref="ColorBalanceExtensions.ListChannels"/>. A channel obtained
    /// from that list is an interned wrapper that keeps a reference of its own,
    /// so the three fields stay readable for as long as the wrapper lives; the
    /// channel only means something to the element that listed it, and it is
    /// neither the caller's to dispose nor the caller's to write to. The setter
    /// is for a channel of one's own, from <see cref="New(string, int, int)"/>,
    /// and refuses every other channel: the element that listed one finds it
    /// again by the content of this very field, so a rewritten label disables
    /// the element's own lookup, aborts under <c>playsink</c> and frees the
    /// string the element may be reading on another thread.
    /// </para>
    /// <para>
    /// Writing the label frees whatever was there — the C dispose frees the
    /// field with <c>g_free</c>, so nothing else ever releases the previous
    /// string — and stores a copy of the new one. What the getter returned
    /// before is unaffected: it was already a managed copy.
    /// </para>
    /// <para>
    /// <strong>The label is not decoration.</strong> <c>playsink</c> matches
    /// its proxy channels against the real ones by substring and then asserts
    /// that it found one (<c>g_assert (channel)</c>,
    /// <c>gstplaysink.c:1720</c> on the video path and <c>:5548</c> on the
    /// audio one), so a channel whose label is <see langword="null"/>, or which
    /// lacks the substring that side expects, aborts the process there.
    /// </para>
    /// <para>
    /// The gir carries no nullable annotation on the field, which the generator
    /// reads as non-nullable and would answer with a <c>string</c> that throws
    /// on a null pointer. This reads it as <see langword="null"/> instead: the
    /// C instance init leaves the field at <c>NULL</c> and nothing forces an
    /// implementer to set one. The setter, being hand-written, refuses
    /// <see langword="null"/> rather than writing one back, and carries
    /// <c>DisallowNull</c> so that the refusal is a compile-time one wherever
    /// the compiler can see it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value written is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The value written contains a null character, which native code would
    /// only see the part in front of.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The channel was listed by an element rather than made by
    /// <see cref="New(string, int, int)"/>.
    /// </exception>
    /// <exception cref="System.ObjectDisposedException">The wrapper was disposed.</exception>
    [System.Diagnostics.CodeAnalysis.DisallowNull]
    public string? Label
    {
        get
        {
            // Deliberate deviation from the un-annotated-field convention
            // (string plus a throw): channel_init leaves label NULL.
            string? value = Gst.Interop.GMarshal.PtrToStringUtf8(((ColorBalanceChannelRaw*)Handle)->Label);
            System.GC.KeepAlive(this);
            return value;
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ThrowIfNotOnesOwn("Label");

            // The provenance is checked first, so a disposed wrapper and a
            // borrowed channel both throw before anything is allocated, and the
            // copy is made before the write, so the field holds the old, still
            // valid string until it succeeds.
            ColorBalanceChannelRaw* raw = (ColorBalanceChannelRaw*)Handle;
            nint copy = StrDupNative(value);
            nint previous = raw->Label;
            raw->Label = copy;
            Gst.Interop.GLibNative.Free(previous);
            System.GC.KeepAlive(this);
        }
    }

    /// <summary>The minimum valid value for this channel.</summary>
    /// <remarks>
    /// Every implementer in GStreamer writes the field once while constructing
    /// the channel, before the channel is reachable from
    /// <see cref="ColorBalanceExtensions.ListChannels"/>, and a channel from
    /// that list is an interned wrapper that keeps a reference of its own, so
    /// the field stays readable for as long as the wrapper lives; the channel
    /// only means something to the element that listed it, and it is neither
    /// the caller's to dispose nor the caller's to write to. The setter is for
    /// a channel of one's own, from <see cref="New(string, int, int)"/>, and
    /// refuses a channel an element listed, whose fields that element reads
    /// without a lock.
    /// <para>
    /// The two bounds are not checked against each other on the way in, only in
    /// <see cref="New(string, int, int)"/>: a channel starts out at 0/0, so a
    /// pair written one field at a time would have to be written in the order
    /// the checker happens to accept. A range the wrong way round is the
    /// caller's to avoid.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The channel was listed by an element rather than made by
    /// <see cref="New(string, int, int)"/>.
    /// </exception>
    /// <exception cref="System.ObjectDisposedException">The wrapper was disposed.</exception>
    public int MinValue
    {
        get
        {
            int value = ((ColorBalanceChannelRaw*)Handle)->MinValue;
            System.GC.KeepAlive(this);
            return value;
        }

        set
        {
            ThrowIfNotOnesOwn("MinValue");
            ((ColorBalanceChannelRaw*)Handle)->MinValue = value;
            System.GC.KeepAlive(this);
        }
    }

    /// <summary>The maximum valid value for this channel.</summary>
    /// <remarks>
    /// Every implementer in GStreamer writes the field once while constructing
    /// the channel, before the channel is reachable from
    /// <see cref="ColorBalanceExtensions.ListChannels"/>, and a channel from
    /// that list is an interned wrapper that keeps a reference of its own, so
    /// the field stays readable for as long as the wrapper lives; the channel
    /// only means something to the element that listed it, and it is neither
    /// the caller's to dispose nor the caller's to write to. The setter is for
    /// a channel of one's own, from <see cref="New(string, int, int)"/>, refuses
    /// a channel an element listed the way the other two setters do, and checks
    /// the bound against <see cref="MinValue"/> no more than that one does.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The channel was listed by an element rather than made by
    /// <see cref="New(string, int, int)"/>.
    /// </exception>
    /// <exception cref="System.ObjectDisposedException">The wrapper was disposed.</exception>
    public int MaxValue
    {
        get
        {
            int value = ((ColorBalanceChannelRaw*)Handle)->MaxValue;
            System.GC.KeepAlive(this);
            return value;
        }

        set
        {
            ThrowIfNotOnesOwn("MaxValue");
            ((ColorBalanceChannelRaw*)Handle)->MaxValue = value;
            System.GC.KeepAlive(this);
        }
    }
}

/// <summary>
/// The instance layout of a <c>GstColorBalanceChannel</c>, which is a bare
/// <c>GObject</c> head followed by the three public fields and the reserved
/// tail.
/// </summary>
/// <remarks>
/// <para>
/// The layout is the one of the 1.28 headers, unchanged since 1.24, and the
/// runtime lays this out the way a C compiler lays out the same field list.
/// Written as offsets, for a 64 bit platform where a pointer is 8 bytes and a
/// <c>gint</c> is 4: <c>GObject</c> <c>g_class</c> 0, <c>ref_count</c> 8,
/// <c>qdata</c> 16, so 24 bytes; <c>label</c> 24, <c>min_value</c> 32,
/// <c>max_value</c> 36, <c>_gst_reserved</c> 40, for 72 bytes in total. That is
/// what <c>ColorBalanceChannelFieldTests</c> asserts, against both the headers
/// and the instance size the running library reports.
/// </para>
/// <para>
/// The mirror is only ever used through a pointer into memory that GStreamer
/// owns; it is never allocated as a managed value or copied. It is written
/// through for a channel this binding made itself
/// (<see cref="ColorBalanceChannel.New(string, int, int)"/> and the three
/// setters beside it), and only read for a channel an element listed.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct ColorBalanceChannelRaw
{
    /// <summary>The <c>GObject</c> the instance starts with.</summary>
    internal Gst.GObject.GObjectInstanceRaw Parent;

    /// <summary>The <c>label</c> field.</summary>
    internal nint Label;

    /// <summary>The <c>min_value</c> field.</summary>
    internal int MinValue;

    /// <summary>The <c>max_value</c> field.</summary>
    internal int MaxValue;

    /// <summary>The <c>_gst_reserved</c> field.</summary>
    internal GstReservedArray GstReserved;

    /// <summary>Inline storage of the 4 elements the reserved tail of <c>GstColorBalanceChannel</c> is made of.</summary>
    [InlineArray(4)]
    internal struct GstReservedArray
    {
        private nint _element0;
    }
}
