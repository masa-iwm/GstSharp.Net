namespace Gst.Tag;

/// <summary>
/// The entry point of the <c>GstTag</c> binding: it initialises GstSharp.Net
/// and makes sure that the types of this assembly are in the type registry.
/// </summary>
/// <remarks>
/// <para>
/// Every binding assembly registers its types from a module initialiser, and
/// the runtime runs a module initialiser before the first <em>call</em> into
/// that assembly, not before the first type of it is merely named. What this
/// module hands to the registry is the two abstract element bases of the
/// library — the tag demuxer and the tag muxer that plugins derive from.
/// Everything else the module binds is either a static function of
/// <see cref="TagGlobal"/>, which is a call into this assembly by
/// construction, or the <see cref="ITagXmpWriter"/> interface, which GObject
/// knows a type for but which no wrapper of this repository is built from.
/// </para>
/// <para>
/// An application that only names a type of this assembly and leaves every
/// call to another binding assembly therefore never executes a line of this
/// one: the registry has no entry to build the wrappers of a
/// <c>GstTagDemux</c> or a <c>GstTagMux</c> subclass from, and what arrives is
/// the closest type it does know — the failure described under
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#the-gtype-registry">The GType registry</see>.
/// </para>
/// <para>
/// Calling <see cref="Initialize"/> instead of <c>GstSharp.Initialize</c> is a
/// call into this assembly and closes that hole. The registry is rebuilt on the
/// next lookup after a module is added, so the order of the two does not
/// matter; what matters is that the module initialiser runs at all.
/// </para>
/// <para>
/// <c>GstSharp.Initialize</c> also sweeps the assemblies that are loaded and
/// runs their module initialisers, and it keeps doing so for assemblies that
/// are loaded later, so an application that never names this class is covered
/// as well. Calling this one is the deterministic way to say it.
/// </para>
/// <para>
/// Five contracts of the module are worth stating once, because the C
/// declarations do not carry them and the generated documentation therefore
/// cannot.
/// </para>
/// <para>
/// <strong>The four EXIF conversions answer <see langword="null"/> on a
/// perfectly ordinary input.</strong>
/// <see cref="TagGlobal.TagListToExifBuffer"/> and
/// <see cref="TagGlobal.TagListToExifBufferWithTiffHeader"/> hand out nothing
/// when the tag list carries no tag the EXIF IFD has a slot for — an empty tag
/// list, or one of tags EXIF knows nothing about, is that case — and
/// <see cref="TagGlobal.TagListFromExifBuffer"/> and
/// <see cref="TagGlobal.TagListFromExifBufferWithTiffHeader"/> hand out nothing
/// when the buffer does not parse as an EXIF IFD or as a TIFF header. None of
/// the four is an error path, and the gir of the library marks none of them
/// nullable; the binding corrects the annotation so that a miss is a
/// <see langword="null"/> and not an exception.
/// <see cref="TagXmpWriterExtensions.TagListToXmpBuffer"/> is nullable for the
/// same kind of reason: a writer whose schemas were all removed serialises
/// nothing.
/// </para>
/// <para>
/// <strong>The byte order of the two plain EXIF conversions is the raw GLib
/// number.</strong> <see cref="TagGlobal.TagListToExifBuffer"/> and
/// <see cref="TagGlobal.TagListFromExifBuffer"/> take 1234 for little endian
/// (<c>G_LITTLE_ENDIAN</c>) or 4321 for big endian (<c>G_BIG_ENDIAN</c>), and
/// the binding hands the number to C untranslated. Any other value is a
/// programming error the two sides report differently: the writer asserts and
/// aborts the process — the assertion is ahead of the test for tags this IFD
/// can hold, so even an empty tag list aborts — while the reader logs a
/// critical and answers <see langword="null"/>. In a GStreamer built with the
/// GLib assertions compiled out (<c>-Dglib_assert=false</c>) the writer does
/// not abort at all: it warns and falls back to the byte order of the host, so
/// the same call answers a buffer rather than ending the process. The binding
/// does not guard the value, because a guard would have to replace the
/// generated call and the C behaviour is what the rest of the surface
/// reports. The two with-tiff-header conversions read the byte order from the
/// header, or write it into one, and take no such argument.
/// </para>
/// <para>
/// <strong><see cref="TagGlobal.TagListNewFromId3v1"/> needs exactly 128
/// bytes.</strong> The C function takes a pointer and reads the full ID3v1
/// record behind it without being told a length, so a shorter span would be an
/// over-read. The binding measures the span and throws
/// <see cref="System.ArgumentException"/> rather than letting that happen.
/// </para>
/// <para>
/// <strong>The vendor string of a Vorbis comment is
/// <see langword="null"/> exactly when the parse failed.</strong>
/// <see cref="TagGlobal.TagListFromVorbiscomment"/> and
/// <see cref="TagGlobal.TagListFromVorbiscommentBuffer"/> write the vendor
/// string before they read a single tag, so whenever they answer a tag list
/// they answer a vendor string with it; the two are <see langword="null"/>
/// together. The identification data is optional: pass an empty span for a
/// stream that carries none, which is what the C function documents as a null
/// pointer with a length of zero.
/// </para>
/// <para>
/// <strong><see cref="ITagXmpWriter"/> is bound for surface parity and is not
/// reachable yet.</strong> The interface is implemented by plugin elements —
/// <c>jifmux</c> and the <c>qtmux</c> family are the ones in tree — and the
/// binding has no way to view an element wrapper as a bound GObject interface
/// it was not generated with, so an element built by
/// <see cref="Gst.ElementFactory"/> never casts to it. An
/// interface cast helper is on the backlog; until it lands the extension
/// methods here are callable only from a wrapper that a module of its own
/// declares as an <see cref="ITagXmpWriter"/>.
/// </para>
/// </remarks>
public static class GstTag
{
    /// <summary>
    /// Loads the native libraries, initialises GStreamer and puts the types of
    /// this assembly into the type registry.
    /// </summary>
    /// <param name="options">
    /// Where the native libraries are and how GStreamer should be initialised,
    /// or <see langword="null"/> for the defaults.
    /// </param>
    /// <remarks>
    /// This forwards to <c>GstSharp.Initialize</c> and is idempotent in the
    /// same way: after the first call, a call with <see langword="null"/>
    /// options does nothing but register this module, and options that
    /// contradict the first call are refused.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The options conflict with the ones of the first call.
    /// </exception>
    /// <exception cref="Gst.Interop.GstNativeLoadException">
    /// The native libraries could not be found.
    /// </exception>
    /// <exception cref="Gst.GLib.GException">GStreamer refused to initialise.</exception>
    public static void Initialize(GstSharpOptions? options = null) =>
        global::GstSharp.Initialize(options);
}
