namespace Gst.Video;

/// <summary>
/// The line geometry the VBI encoder and the VBI parser share: both are
/// constructed from a <see cref="Gst.Video.VideoFormat"/> and a pixel width,
/// and the length of the line they read or write follows from those two alone.
/// </summary>
/// <remarks>
/// Neither <c>GstVideoVBIEncoder</c> nor <c>GstVideoVBIParser</c> is readable:
/// both are opaque and declare no getter for the format or the width they were
/// created with, so the only place the line size can be measured is the call
/// that creates the object. See <c>$comment-step9-video-vbi-lines</c> in
/// <c>girs/overlays/fixups.json</c>.
/// </remarks>
internal static class VideoVBILine
{
    /// <summary>
    /// The smallest pixel width the conversion routines of the library are
    /// defined for.
    /// </summary>
    /// <remarks>
    /// The loops of <c>convert_line_to_uyvy</c> and its three siblings run
    /// <c>for (i = 0; i &lt; width - 3; i += 4)</c> and
    /// <c>for (i = 0; i &lt; width - 5; i += 6)</c> with an unsigned counter
    /// against a signed width, so a width below six wraps the bound and the
    /// library walks off the end of the line. Nothing in the library refuses
    /// such a width: <c>gst_video_vbi_encoder_new</c> only asks for a width
    /// above zero.
    /// </remarks>
    internal const uint MinimumPixelWidth = 6;

    /// <summary>
    /// Measures one line of the given geometry, in bytes.
    /// </summary>
    /// <param name="format">The format the encoder or the parser was created with.</param>
    /// <param name="pixelWidth">The width in pixels it was created with.</param>
    /// <returns>
    /// The stride of the first plane of a one pixel high frame of that
    /// geometry, which is the size the library documents for the line, or
    /// <c>0</c> when the geometry has none.
    /// </returns>
    internal static int Measure(Gst.Video.VideoFormat format, uint pixelWidth)
    {
        using Gst.Video.VideoInfo info = Gst.Video.VideoInfo.New();

        if (!info.SetFormat(format, pixelWidth, 1))
        {
            return 0;
        }

        int stride = info.Stride[0];
        return stride > 0 ? stride : 0;
    }
}
