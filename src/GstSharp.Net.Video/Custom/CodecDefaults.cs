using System.Runtime.InteropServices;

namespace Gst.Video;

/// <summary>
/// What <c>GstVideoDecoder</c> does for the <c>getcaps</c> slot its own class
/// leaves NULL, reached from the chain-up of a managed decoder.
/// </summary>
/// <remarks>
/// gstvideodecoder.c:2093-2096 falls back to
/// <c>gst_video_decoder_proxy_getcaps (decoder, NULL, filter)</c>, which the
/// binding also exposes as <c>VideoDecoder.ProxyGetcaps</c>. The chain-up
/// reaches it through a pointer rather than through the wrapper, because a
/// chain-up hands handles on without building one.
/// </remarks>
internal static partial class VideoDecoderDefaults
{
    /// <summary>Answers the caps the sink pad of the decoder can accept.</summary>
    /// <param name="decoder">The native <c>GstVideoDecoder</c>.</param>
    /// <param name="filter">The filter of the caps query, or <c>0</c>.</param>
    /// <returns>The caps, owned by the caller.</returns>
    internal static nint ProxyGetcaps(nint decoder, nint filter) => Proxy(decoder, nint.Zero, filter);

    /// <summary>The <c>gst_video_decoder_proxy_getcaps</c> entry point.</summary>
    /// <param name="decoder">The native <c>GstVideoDecoder</c>.</param>
    /// <param name="caps">The caps to proxy, or <c>0</c> for the template caps.</param>
    /// <param name="filter">The filter of the caps query, or <c>0</c>.</param>
    /// <returns>The caps, owned by the caller.</returns>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_decoder_proxy_getcaps")]
    private static partial nint Proxy(nint decoder, nint caps, nint filter);
}

/// <summary>
/// What <c>GstVideoEncoder</c> does for the <c>getcaps</c> slot its own class
/// leaves NULL, reached from the chain-up of a managed encoder.
/// </summary>
/// <remarks>
/// gstvideoencoder.c:833-836 falls back to
/// <c>gst_video_encoder_proxy_getcaps (enc, NULL, filter)</c>.
/// </remarks>
internal static partial class VideoEncoderDefaults
{
    /// <summary>Answers the caps the sink pad of the encoder can accept.</summary>
    /// <param name="enc">The native <c>GstVideoEncoder</c>.</param>
    /// <param name="filter">The filter of the caps query, or <c>0</c>.</param>
    /// <returns>The caps, owned by the caller.</returns>
    internal static nint ProxyGetcaps(nint enc, nint filter) => Proxy(enc, nint.Zero, filter);

    /// <summary>The <c>gst_video_encoder_proxy_getcaps</c> entry point.</summary>
    /// <param name="enc">The native <c>GstVideoEncoder</c>.</param>
    /// <param name="caps">The caps to proxy, or <c>0</c> for the template caps.</param>
    /// <param name="filter">The filter of the caps query, or <c>0</c>.</param>
    /// <returns>The caps, owned by the caller.</returns>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_encoder_proxy_getcaps")]
    private static partial nint Proxy(nint enc, nint caps, nint filter);
}
