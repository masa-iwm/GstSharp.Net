namespace Gst.Rtp;

/// <content>
/// The header extensions of a payloader: the <c>extensions</c> property and the
/// <c>add-extension</c> and <c>clear-extensions</c> signals, none of which the
/// generator emits.
/// </content>
/// <remarks>
/// <para>
/// The property is a <c>GST_TYPE_ARRAY</c> of
/// <see cref="Gst.Rtp.RTPHeaderExtension"/> objects
/// (<c>gstrtpbasepayload.c:450-456</c>), a kind the property planner has no
/// projection for, so it is filed as a hand binding and read here through
/// <see cref="Gst.GObject.Object.GetProperty(string)"/> and
/// <see cref="Gst.ValueArray"/>. The two signals are
/// <c>G_SIGNAL_ACTION</c> signals, which the generator never binds because an
/// action signal normally doubles a C function - and here it does not: both
/// handlers are static and no exported function reaches them, so the signal is
/// the only way in and is emitted below.
/// </para>
/// <para>
/// The same three members sit on <see cref="Gst.Rtp.RTPBaseDepayload"/>, whose
/// C is the same code with other names.
/// </para>
/// </remarks>
public abstract unsafe partial class RTPBasePayload
{
    /// <summary>
    /// The header extensions the payloader currently writes, in the order it
    /// holds them.
    /// </summary>
    /// <value>
    /// A snapshot of the extensions, empty when there are none and never
    /// <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// The list is built at the moment of the read and is not a live view: an
    /// extension added afterwards is on the next list, not on this one. The
    /// elements follow the rule of every other GObject a call answers - each is
    /// the interned wrapper of that extension, holding a reference of its own,
    /// so it is the very instance that was handed to
    /// <see cref="AddExtension(Gst.Rtp.RTPHeaderExtension)"/> and the caller
    /// does not dispose it.
    /// </para>
    /// <para>
    /// The payloader also fills the list itself during negotiation, out of the
    /// <c>extmap-</c> fields of the caps it agrees on, so a list read after a
    /// pipeline has started holds extensions nobody added by hand. Available
    /// since GStreamer 1.24.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The property answered something other than a header extension.
    /// </exception>
    public System.Collections.Generic.IReadOnlyList<Gst.Rtp.RTPHeaderExtension> Extensions =>
        RTPHeaderExtensionArray.Read(this);

    /// <summary>Adds a header extension to the ones the payloader writes.</summary>
    /// <param name="extension">
    /// The extension to add. Its id has to be set, and the payloader takes a
    /// reference of its own: the caller keeps its wrapper and disposes it as it
    /// otherwise would.
    /// </param>
    /// <remarks>
    /// <para>
    /// The extension is appended under the object lock and the property is
    /// notified, so <c>notify::extensions</c> reports the change; the source pad
    /// is marked for reconfiguration, which is what makes the extension appear
    /// in the caps at the next negotiation. Available since GStreamer 1.20.
    /// </para>
    /// <para>
    /// The documentation of the C signal calls the argument
    /// <c>(transfer full)</c> and the handler contradicts it: it takes a
    /// reference of its own with <c>gst_object_ref</c>
    /// (<c>gstrtpbasepayload.c:1619-1633</c>) and sinks nothing, so the
    /// effective ownership is transfer none. The wrapper is therefore handed
    /// over to nobody and stays the caller's.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="extension"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The id of <paramref name="extension"/> is 0, which the C refuses with a
    /// critical and a silent no-op.
    /// </exception>
    public void AddExtension(Gst.Rtp.RTPHeaderExtension extension)
    {
        RTPHeaderExtensionArray.CheckAddable(extension);
        EmitSignal("add-extension", extension);
    }

    /// <summary>Removes every header extension the payloader writes.</summary>
    /// <remarks>
    /// The list is emptied under the object lock and the property is notified.
    /// What the payloader negotiates afterwards may fill it again. Available
    /// since GStreamer 1.20.
    /// </remarks>
    public void ClearExtensions() => EmitSignal("clear-extensions");
}
