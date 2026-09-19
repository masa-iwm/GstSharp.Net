namespace Gst.Rtp;

/// <summary>
/// The half of the header extension surface that a payloader and a depayloader
/// share: reading the <c>extensions</c> property, and the check the
/// <c>add-extension</c> signal makes in C by refusing to do anything.
/// </summary>
/// <remarks>
/// The two C files are the same code with other names
/// (<c>gstrtpbasepayload.c</c> and <c>gstrtpbasedepayload.c</c>), so the managed
/// side of both is written once here and reached from the partial of each class.
/// </remarks>
internal static class RTPHeaderExtensionArray
{
    /// <summary>The name of the property both classes carry.</summary>
    private const string PropertyName = "extensions";

    /// <summary>Reads the <c>extensions</c> property of one of the two classes.</summary>
    /// <param name="owner">The payloader or depayloader to read.</param>
    /// <returns>
    /// A snapshot of the extensions, empty when there are none and never
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// The C builds the array under the object lock and stores each entry with
    /// <c>g_value_set_object</c>, which takes a reference the array owns
    /// (<c>gstrtpbasepayload.c:1647-1666</c>). Reading an element out of it with
    /// <see cref="Gst.GObject.Value.GetObject()"/> hands out the interned
    /// wrapper of the extension, which takes a reference of its own, and every
    /// value read along the way is disposed here, so the array releases
    /// everything it held when this returns.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The property answered something other than a header extension.
    /// </exception>
    internal static System.Collections.Generic.IReadOnlyList<Gst.Rtp.RTPHeaderExtension> Read(
        Gst.GObject.Object owner)
    {
        using Gst.GObject.Value value = owner.GetProperty(PropertyName);

        uint size = Gst.ValueArray.GetSize(value);
        if (size == 0)
        {
            return [];
        }

        List<Gst.Rtp.RTPHeaderExtension> extensions = new((int)size);

        for (uint index = 0; index < size; index++)
        {
            using Gst.GObject.Value element = Gst.ValueArray.GetValue(value, index);

            extensions.Add(element.GetObject() switch
            {
                Gst.Rtp.RTPHeaderExtension extension => extension,
                { } other => throw new InvalidOperationException(
                    $"The {PropertyName} property answered a {other.GetType().Name}, and every member of it is "
                    + "a header extension."),
                null => throw new InvalidOperationException(
                    $"The {PropertyName} property answered a member holding no object, and every member of it "
                    + "is a header extension."),
            });
        }

        return extensions;
    }

    /// <summary>
    /// Refuses an extension the C would only complain about, before the signal
    /// that would carry it is emitted.
    /// </summary>
    /// <param name="extension">The extension the caller wants added.</param>
    /// <remarks>
    /// <para>
    /// Both handlers open with
    /// <c>g_return_if_fail (GST_IS_RTP_HEADER_EXTENSION (ext))</c> and
    /// <c>g_return_if_fail (gst_rtp_header_extension_get_id (ext) &gt; 0)</c>,
    /// which log a critical and return: an extension that was never added, and
    /// no way for the caller to tell. The managed members raise instead.
    /// </para>
    /// <para>
    /// The check here is the wider one, because the check of the C is not
    /// enough. An id is valid between 1 and 255:
    /// <c>gst_rtp_header_extension_set_id</c> refuses anything from
    /// <c>MAX_RTP_EXT_ID</c> up and leaves the id it had
    /// (<c>gstrtphdrext.c:44</c>, <c>:389</c>), and an extension that was never
    /// given an id carries <c>G_MAXUINT32</c> rather than 0
    /// (<c>gstrtphdrext.c:200</c>), which the check of the C lets through: the
    /// extension is then added, written into no packet and reported by no
    /// <c>extmap-</c> field, and every later use of it logs a critical. That is
    /// the same "the C only complains and the caller cannot tell" case the id
    /// of 0 is refused for, so an extension built from its URI is refused here
    /// until <see cref="Gst.Rtp.RTPHeaderExtension.SetId(uint)"/> has given it
    /// a usable id.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="extension"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The id of <paramref name="extension"/> is not between 1 and 255: 0, or
    /// never set.
    /// </exception>
    internal static void CheckAddable(Gst.Rtp.RTPHeaderExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        if (extension.GetId() is 0 or > 255)
        {
            throw new ArgumentException(
                "A header extension is added under its id, and this one has no usable one: set an id between "
                + "1 and 255 with SetId before adding it.",
                nameof(extension));
        }
    }
}
