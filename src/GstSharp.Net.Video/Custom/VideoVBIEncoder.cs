using System.Runtime.InteropServices;

namespace Gst.Video;

/// <summary>
/// The writing side of the VBI encoder, which the generator does not emit:
/// <c>gst_video_vbi_encoder_write_line</c> takes a bare line pointer with no
/// length beside it, and the number of bytes it writes follows from the format
/// and the pixel width the encoder was created with. The encoder is opaque and
/// hands neither of them back, so the geometry is captured here instead. See
/// <c>$comment-step9-video-vbi-lines</c> in <c>girs/overlays/fixups.json</c>.
/// </summary>
public sealed unsafe partial class VideoVBIEncoder
{
    /// <summary>The pixel width the encoder was created with, or <c>0</c> when it is unknown.</summary>
    private uint _pixelWidth;

    /// <summary>The length of one line in bytes, or <c>0</c> when it is unknown.</summary>
    private int _lineStride;

    /// <summary>Create a new #GstVideoVBIEncoder for the specified @format and @pixel_width.</summary>
    /// <param name="format">a #GstVideoFormat</param>
    /// <param name="pixelWidth">The width in pixel to use</param>
    /// <returns>
    /// The new #GstVideoVBIEncoder or %NULL if the @format and/or @pixel_width
    /// is not supported.
    /// </returns>
    public static Gst.Video.VideoVBIEncoder? New(Gst.Video.VideoFormat format, uint pixelWidth)
    {
        nint nativeResult = GstVideoVbiEncoderNew((int)format, pixelWidth);
        Gst.Video.VideoVBIEncoder? result = Gst.Video.VideoVBIEncoder.FromNative(nativeResult, Gst.Interop.Transfer.Full);

        if (result is not null)
        {
            // The geometry is recorded for WriteLine, which is the only place
            // the length of a line can come from.
            result._pixelWidth = pixelWidth;
            result._lineStride = Gst.Video.VideoVBILine.Measure(format, pixelWidth);
        }

        return result;
    }

    /// <summary>The <c>gst_video_vbi_encoder_copy</c> function.</summary>
    /// <returns>The result of <c>gst_video_vbi_encoder_copy</c>.</returns>
    public Gst.Video.VideoVBIEncoder Copy()
    {
        nint nativeResult = GstVideoVbiEncoderCopy(Handle);
        Gst.Video.VideoVBIEncoder result = Gst.Video.VideoVBIEncoder.FromNative(nativeResult, Gst.Interop.Transfer.Full)
            ?? throw new InvalidOperationException("gst_video_vbi_encoder_copy returned no value.");

        // A copy encodes the very same geometry, so it inherits the line size
        // of the encoder it was taken from, unknown geometry included.
        result._pixelWidth = _pixelWidth;
        result._lineStride = _lineStride;

        System.GC.KeepAlive(this);
        return result;
    }

    /// <summary>
    /// Writes the ancillary data that was added since the last call into
    /// <paramref name="data"/>, which has to hold one whole video line.
    /// </summary>
    /// <param name="data">
    /// One line of the format and the pixel width the encoder was created
    /// with. It has to be at least as long as the stride of that geometry,
    /// which is the size the C function documents for it: the library is
    /// handed a bare pointer and writes up to that many bytes without ever
    /// being told how long the line is.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_video_vbi_encoder_write_line</c>. It writes nothing at
    /// all when no ancillary packet was added since the last call, and it
    /// clears the pending packets once it has written them.
    /// </para>
    /// <para>
    /// The memory belongs to the caller for the whole call and nothing is
    /// retained: the span is pinned for the duration of the write.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The encoder carries no line size, because it was not created by
    /// <see cref="New"/> or <see cref="Copy"/>, or because it was created with
    /// a pixel width below six, where the unsigned loop bound of the library
    /// wraps and the write runs off the line.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> is shorter than one line.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void WriteLine(System.Span<byte> data)
    {
        // The handle is read before anything else, so that a disposed wrapper
        // throws before the arguments are looked at.
        nint encoder = Handle;

        if (_lineStride <= 0)
        {
            throw new InvalidOperationException(
                "The line size of this GstVideoVBIEncoder is unknown, because it did not come from VideoVBIEncoder.New or VideoVBIEncoder.Copy.");
        }

        if (_pixelWidth < Gst.Video.VideoVBILine.MinimumPixelWidth)
        {
            throw new InvalidOperationException(
                "The line size is undefined for a pixel width below six: the loop bound of the conversion routine wraps and the library writes past the end of the line.");
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
            GstVideoVbiEncoderWriteLine(encoder, dataPointer);
        }

        // The handle was read before the call, so nothing keeps this wrapper
        // alive across it on its own.
        System.GC.KeepAlive(this);
    }

    /// <summary>The <c>gst_video_vbi_encoder_new</c> entry point.</summary>
    /// <param name="format">The format of the lines the encoder writes.</param>
    /// <param name="pixelWidth">The width of those lines, in pixels.</param>
    /// <returns>The new encoder, or <c>0</c> for a geometry it does not support.</returns>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_vbi_encoder_new")]
    private static partial nint GstVideoVbiEncoderNew(int format, uint pixelWidth);

    /// <summary>The <c>gst_video_vbi_encoder_copy</c> entry point.</summary>
    /// <param name="encoder">The encoder to copy.</param>
    /// <returns>The copy.</returns>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_vbi_encoder_copy")]
    private static partial nint GstVideoVbiEncoderCopy(nint encoder);

    /// <summary>The <c>gst_video_vbi_encoder_write_line</c> entry point.</summary>
    /// <param name="encoder">The encoder holding the pending ancillary data.</param>
    /// <param name="data">The line the data is written into.</param>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_vbi_encoder_write_line")]
    private static partial void GstVideoVbiEncoderWriteLine(nint encoder, byte* data);
}
