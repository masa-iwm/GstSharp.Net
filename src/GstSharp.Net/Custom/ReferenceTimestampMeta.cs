using System;
using System.Globalization;

namespace Gst;

public sealed unsafe partial class ReferenceTimestampMeta
{
    /// <summary>
    /// Gets the additional information about the timestamp, or
    /// <see langword="null"/> when the item carries none.
    /// </summary>
    /// <value>
    /// A copy of the structure the item holds, or <see langword="null"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// The <c>info</c> field arrived in GStreamer 1.28 and the binding supports
    /// 1.24, which is why no accessor is generated for it: a structure that
    /// grew a field is allocated at the size the library on the machine
    /// registered it with — <c>sizeof (GstReferenceTimestampMeta)</c>,
    /// gstbuffer.c:3004 at 1.28.6 against gstbuffer.c:2925 at 1.24.13 — so on
    /// an older library the storage this field sits in belongs to something
    /// else or does not exist, and a field access has nothing to fail on. This
    /// accessor is the runtime version check that lifts that, and the version
    /// is the source of the size: the field is the last one of the structure
    /// and only 1.28 allocates room for it, so nothing but the version has to
    /// be asked.
    /// </para>
    /// <para>
    /// The exception is the one every member that arrived after the floor
    /// answers with on an older library, where the entry point behind it does
    /// not exist. A field has no entry point of its own, and raising anything
    /// else for the same condition would make "the library predates this" two
    /// different exceptions depending on whether the thing missing is a call
    /// or a field.
    /// </para>
    /// <para>
    /// The C keeps the structure — 1.28 frees it with the item
    /// (gstbuffer.c:2875-2876) — so the read is a <c>transfer none</c> one and
    /// what comes back is a copy, which is what a borrowed boxed value is in
    /// this binding: dispose it when you are done, and note that changes made
    /// to it are not written back. There is no setter, because 1.28 has none
    /// either: <c>gst_buffer_add_reference_timestamp_meta</c> sets the field to
    /// <c>NULL</c> (gstbuffer.c:2783), the library writes it directly, and the
    /// one public path that fills it is <c>gst_meta_deserialize</c>
    /// (gstbuffer.c:2980).
    /// </para>
    /// </remarks>
    /// <exception cref="EntryPointNotFoundException">
    /// The loaded GStreamer is older than 1.28, where the item has no such
    /// field.
    /// </exception>
    public Gst.Structure? Info
    {
        get
        {
            Gst.Version version = global::GstSharp.NativeVersion;
            if (!version.IsAtLeast(1, 28, 0))
            {
                throw new EntryPointNotFoundException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The info field of GstReferenceTimestampMeta arrived in GStreamer 1.28, and {version} is loaded: the item it is read off is allocated without it."));
            }

            Gst.Structure? info = Gst.Structure.FromNative(
                ((ReferenceTimestampMetaRaw*)Handle)->Info,
                Gst.Interop.Transfer.None);
            GC.KeepAlive(this);
            return info;
        }
    }
}
