using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst.Video;

/// <summary>
/// The three public fields of a <c>GstColorBalanceChannel</c>, which the
/// generator does not emit: field accessors are a record concern, and a
/// GObject derived class never reaches that path. See
/// <c>$comment-step32-color-balance-channel-fields</c> in
/// <c>girs/overlays/fixups.json</c>.
/// </summary>
public unsafe partial class ColorBalanceChannel
{
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
    /// not the caller's to dispose.
    /// </para>
    /// <para>
    /// The gir carries no nullable annotation on the field, which the generator
    /// reads as non-nullable and would answer with a <c>string</c> that throws
    /// on a null pointer. This reads it as <see langword="null"/> instead: the
    /// C instance init leaves the field at <c>NULL</c> and nothing forces an
    /// implementer to set one.
    /// </para>
    /// </remarks>
    /// <exception cref="System.ObjectDisposedException">The wrapper was disposed.</exception>
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
    }

    /// <summary>The minimum valid value for this channel.</summary>
    /// <remarks>
    /// Every implementer in GStreamer writes the field once while constructing
    /// the channel, before the channel is reachable from
    /// <see cref="ColorBalanceExtensions.ListChannels"/>, and a channel from
    /// that list is an interned wrapper that keeps a reference of its own, so
    /// the field stays readable for as long as the wrapper lives; the channel
    /// only means something to the element that listed it, and it is not the
    /// caller's to dispose.
    /// </remarks>
    /// <exception cref="System.ObjectDisposedException">The wrapper was disposed.</exception>
    public int MinValue
    {
        get
        {
            int value = ((ColorBalanceChannelRaw*)Handle)->MinValue;
            System.GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>The maximum valid value for this channel.</summary>
    /// <remarks>
    /// Every implementer in GStreamer writes the field once while constructing
    /// the channel, before the channel is reachable from
    /// <see cref="ColorBalanceExtensions.ListChannels"/>, and a channel from
    /// that list is an interned wrapper that keeps a reference of its own, so
    /// the field stays readable for as long as the wrapper lives; the channel
    /// only means something to the element that listed it, and it is not the
    /// caller's to dispose.
    /// </remarks>
    /// <exception cref="System.ObjectDisposedException">The wrapper was disposed.</exception>
    public int MaxValue
    {
        get
        {
            int value = ((ColorBalanceChannelRaw*)Handle)->MaxValue;
            System.GC.KeepAlive(this);
            return value;
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
/// The mirror is only ever read through a pointer into memory that GStreamer
/// owns; it is never allocated, assigned or copied.
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
