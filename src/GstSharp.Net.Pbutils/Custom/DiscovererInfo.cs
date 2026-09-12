using System.Runtime.InteropServices;
using Gst.GLib;

namespace Gst.Pbutils;

/// <content>
/// The serialisation pair of a discovery result, the only two calls of the
/// whole GStreamer API that carry a <c>GVariant</c>.
/// </content>
/// <remarks>
/// Both are written by hand because <c>Gst.GLib.Variant</c> is a hand written
/// runtime type — GLib is not a generated module — and because each of them
/// answers a precondition of the C that the gir does not carry. See the
/// <c>skip</c> list of <c>girs/overlays/fixups.json</c>.
/// </remarks>
public partial class DiscovererInfo
{
    /// <summary>
    /// Serialises the result into a value that <see cref="FromVariant"/> reads
    /// back.
    /// </summary>
    /// <param name="flags">What of the result is written.</param>
    /// <returns>The value, which the caller has to dispose.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_discoverer_info_to_variant</c>. The value it produces
    /// has the type <c>"v"</c>, a wrapper around the pair of the information
    /// and the stream tree, which is what makes it readable back from bytes
    /// alone.
    /// </para>
    /// <para>
    /// The C answers a value only for a discovery that produced one:
    /// <see cref="DiscovererResult.Ok"/> or
    /// <see cref="DiscovererResult.MissingPlugins"/>. Anything else is a
    /// critical and a null in C, so the member refuses it here instead.
    /// </para>
    /// <para>
    /// A result the C admits but cannot serialise is a discovery that produced
    /// no stream tree at all: <c>gst_discoverer_info_to_variant</c> reads the
    /// tree with a call that answers null for one
    /// (gstdiscoverer-types.c:1043-1049) and hands that null straight to a
    /// serialiser that dereferences it (gstdiscoverer.c:2141, :2220-2221). A
    /// discovery that found a missing plugin before any topology was posted is
    /// exactly that case, so the member asks for the tree first and refuses
    /// the call when there is none.
    /// </para>
    /// <para>
    /// <b>The value the C hands back is floating</b> although its annotation
    /// says it is transferred, and the wrapper claims it rather than adding a
    /// reference to it. See <c>docs/ownership.md</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The discovery did not produce a result that can be serialised, or
    /// produced no stream tree to serialise.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Variant ToVariant(DiscovererSerializeFlags flags)
    {
        DiscovererResult result = GetResult();
        if (result is not (DiscovererResult.Ok or DiscovererResult.MissingPlugins))
        {
            throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"A discovery whose result is {result} cannot be serialised."));
        }

        // Only the presence of the tree is asked for, and it is asked with the
        // raw call rather than with GetStreamInfo(): the wrapper of a GObject
        // is interned, so a wrapper taken here and disposed again would dispose
        // the one a caller may be holding of that very object. What the call
        // hands over is a reference, and that is what is released instead.
        nint tree = GstDiscovererInfoGetStreamInfo(Handle);
        if (tree == nint.Zero)
        {
            throw new InvalidOperationException(
                "A discovery that produced no stream tree cannot be serialised.");
        }

        Gst.Interop.GObjectNative.ObjectUnref(tree);

        nint variant = GstDiscovererInfoToVariant(Handle, (int)flags);
        Variant value = variant == nint.Zero
            ? throw new InvalidOperationException("gst_discoverer_info_to_variant returned no value.")
            : new Variant(variant, Gst.Interop.Transfer.Full);

        GC.KeepAlive(this);
        return value;
    }

    /// <summary>
    /// Reads a result back out of a value <see cref="ToVariant"/> produced.
    /// </summary>
    /// <param name="variant">The value to read.</param>
    /// <returns>
    /// The result, or <see langword="null"/> when the library refused the
    /// value.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_discoverer_info_from_variant</c>, and
    /// <b>only the output of <see cref="ToVariant"/> may be handed to it</b>.
    /// The outer type is checked here, before the call, because a value of the
    /// wrong type is not a parse failure the C reports: 1.24 dereferences a
    /// null over it and later versions raise a critical and answer nothing. A
    /// value of the right outer type whose contents are something else is
    /// still a run of GLib criticals, which no managed check can prevent.
    /// </para>
    /// <para>
    /// The value is only read; the caller keeps it and still disposes it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="variant"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="variant"/> is not a value of type <c>"v"</c> and
    /// therefore not the output of <see cref="ToVariant"/>.
    /// </exception>
    public static DiscovererInfo? FromVariant(Variant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);

        if (!variant.IsOfType("v"))
        {
            throw new ArgumentException(
                "Only the output of ToVariant can be read back, which is a variant of type \"v\".",
                nameof(variant));
        }

        nint handle = GstDiscovererInfoFromVariant(variant.Handle);
        DiscovererInfo? info = Gst.GObject.Object.FromNative<DiscovererInfo>(
            handle,
            Gst.Interop.Transfer.Full);

        GC.KeepAlive(variant);
        return info;
    }

    /// <summary>The <c>gst_discoverer_info_to_variant</c> entry point.</summary>
    /// <param name="info">The result to serialise.</param>
    /// <param name="flags">What of it is written.</param>
    /// <returns>The value, with a floating reference despite the annotation.</returns>
    [LibraryImport("GstPbutils", EntryPoint = "gst_discoverer_info_to_variant")]
    private static partial nint GstDiscovererInfoToVariant(nint info, int flags);

    /// <summary>The <c>gst_discoverer_info_from_variant</c> entry point.</summary>
    /// <param name="variant">The value to read, which is only borrowed.</param>
    /// <returns>The result, which the caller owns, or <c>0</c>.</returns>
    [LibraryImport("GstPbutils", EntryPoint = "gst_discoverer_info_from_variant")]
    private static partial nint GstDiscovererInfoFromVariant(nint variant);
}
