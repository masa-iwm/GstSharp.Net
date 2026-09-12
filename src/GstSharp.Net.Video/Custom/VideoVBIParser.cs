using System.Runtime.InteropServices;

namespace Gst.Video;

/// <summary>
/// The reading side of the VBI parser, which the generator does not emit:
/// <c>gst_video_vbi_parser_add_line</c> takes a bare line pointer with no
/// length beside it, and the number of bytes it reads follows from the format
/// and the pixel width the parser was created with. The parser is opaque and
/// hands neither of them back, so the geometry is captured here instead. See
/// <c>$comment-step9-video-vbi-lines</c> in <c>girs/overlays/fixups.json</c>.
/// </summary>
public sealed unsafe partial class VideoVBIParser
{
    /// <summary>The pixel width the parser was created with, or <c>0</c> when it is unknown.</summary>
    private uint _pixelWidth;

    /// <summary>The length of one line in bytes, or <c>0</c> when it is unknown.</summary>
    private int _lineStride;

    /// <summary>Create a new #GstVideoVBIParser for the specified @format and @pixel_width.</summary>
    /// <param name="format">a #GstVideoFormat</param>
    /// <param name="pixelWidth">The width in pixel to use</param>
    /// <returns>
    /// The new #GstVideoVBIParser or %NULL if the @format and/or @pixel_width
    /// is not supported.
    /// </returns>
    public static Gst.Video.VideoVBIParser? New(Gst.Video.VideoFormat format, uint pixelWidth)
    {
        nint nativeResult = GstVideoVbiParserNew((int)format, pixelWidth);
        Gst.Video.VideoVBIParser? result = Gst.Video.VideoVBIParser.FromNative(nativeResult, Gst.Interop.Transfer.Full);

        if (result is not null)
        {
            // The geometry is recorded for AddLine, which is the only place
            // the length of a line can come from.
            result._pixelWidth = pixelWidth;
            result._lineStride = Gst.Video.VideoVBILine.Measure(format, pixelWidth);
        }

        return result;
    }

    /// <summary>The <c>gst_video_vbi_parser_copy</c> function.</summary>
    /// <returns>The result of <c>gst_video_vbi_parser_copy</c>.</returns>
    public Gst.Video.VideoVBIParser Copy()
    {
        nint nativeResult = GstVideoVbiParserCopy(Handle);
        Gst.Video.VideoVBIParser result = Gst.Video.VideoVBIParser.FromNative(nativeResult, Gst.Interop.Transfer.Full)
            ?? throw new InvalidOperationException("gst_video_vbi_parser_copy returned no value.");

        // A copy parses the very same geometry, so it inherits the line size
        // of the parser it was taken from, unknown geometry included.
        result._pixelWidth = _pixelWidth;
        result._lineStride = _lineStride;

        System.GC.KeepAlive(this);
        return result;
    }

    /// <summary>
    /// Hands one whole video line to the parser, whose ancillary packets
    /// <see cref="GetAncillary"/> then reads out one at a time.
    /// </summary>
    /// <param name="data">
    /// One line of the format and the pixel width the parser was created
    /// with. It has to be at least as long as the stride of that geometry,
    /// which is the size the C function documents for it: the library is
    /// handed a bare pointer and reads up to that many bytes without ever
    /// being told how long the line is.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_video_vbi_parser_add_line</c>. Each call discards
    /// whatever the previous line left unread.
    /// </para>
    /// <para>
    /// The memory belongs to the caller for the whole call and nothing is
    /// retained: the line is copied into the workspace of the parser while the
    /// span is pinned.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The parser carries no line size, because it was not created by
    /// <see cref="New"/> or <see cref="Copy"/>, or because it was created with
    /// a pixel width below six, where the unsigned loop bound of the library
    /// wraps and the read runs off the line.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> is shorter than one line.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void AddLine(System.ReadOnlySpan<byte> data)
    {
        // The handle is read before anything else, so that a disposed wrapper
        // throws before the arguments are looked at.
        nint parser = Handle;

        if (_lineStride <= 0)
        {
            throw new InvalidOperationException(
                "The line size of this GstVideoVBIParser is unknown, because it did not come from VideoVBIParser.New or VideoVBIParser.Copy.");
        }

        if (_pixelWidth < Gst.Video.VideoVBILine.MinimumPixelWidth)
        {
            throw new InvalidOperationException(
                "The line size is undefined for a pixel width below six: the loop bound of the conversion routine wraps and the library reads past the end of the line.");
        }

        if (data.Length < _lineStride)
        {
            throw new ArgumentException(
                FormattableString.Invariant(
                    $"One line of this geometry is {_lineStride} bytes long, and the span holds {data.Length}."),
                nameof(data));
        }

        fixed (byte* dataPointer = data)
        {
            GstVideoVbiParserAddLine(parser, dataPointer);
        }

        // The handle was read before the call, so nothing keeps this wrapper
        // alive across it on its own.
        System.GC.KeepAlive(this);
    }

    /// <summary>The <c>gst_video_vbi_parser_new</c> entry point.</summary>
    /// <param name="format">The format of the lines the parser reads.</param>
    /// <param name="pixelWidth">The width of those lines, in pixels.</param>
    /// <returns>The new parser, or <c>0</c> for a geometry it does not support.</returns>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_vbi_parser_new")]
    private static partial nint GstVideoVbiParserNew(int format, uint pixelWidth);

    /// <summary>The <c>gst_video_vbi_parser_copy</c> entry point.</summary>
    /// <param name="parser">The parser to copy.</param>
    /// <returns>The copy.</returns>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_vbi_parser_copy")]
    private static partial nint GstVideoVbiParserCopy(nint parser);

    /// <summary>The <c>gst_video_vbi_parser_add_line</c> entry point.</summary>
    /// <param name="parser">The parser the line is handed to.</param>
    /// <param name="data">The line to parse.</param>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_vbi_parser_add_line")]
    private static partial void GstVideoVbiParserAddLine(nint parser, byte* data);
}
