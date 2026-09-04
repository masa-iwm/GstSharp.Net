using System.Runtime.InteropServices;

namespace Gst.Audio;

/// <summary>
/// What <c>GstAudioDecoder</c> does for the <c>getcaps</c> slot its own class
/// leaves NULL, reached from the chain-up of a managed decoder.
/// </summary>
/// <remarks>
/// gstaudiodecoder.c:2826-2829 falls back to
/// <c>gst_audio_decoder_proxy_getcaps (decoder, NULL, filter)</c>, which the
/// binding also exposes as <c>AudioDecoder.ProxyGetcaps</c>. The chain-up
/// reaches it through a pointer rather than through the wrapper, because a
/// chain-up hands handles on without building one.
/// </remarks>
internal static partial class AudioDecoderDefaults
{
    /// <summary>Answers the caps the sink pad of the decoder can accept.</summary>
    /// <param name="dec">The native <c>GstAudioDecoder</c>.</param>
    /// <param name="filter">The filter of the caps query, or <c>0</c>.</param>
    /// <returns>The caps, owned by the caller.</returns>
    internal static nint ProxyGetcaps(nint dec, nint filter) => Proxy(dec, nint.Zero, filter);

    /// <summary>The <c>gst_audio_decoder_proxy_getcaps</c> entry point.</summary>
    /// <param name="dec">The native <c>GstAudioDecoder</c>.</param>
    /// <param name="caps">The caps to proxy, or <c>0</c> for the template caps.</param>
    /// <param name="filter">The filter of the caps query, or <c>0</c>.</param>
    /// <returns>The caps, owned by the caller.</returns>
    [LibraryImport("GstAudio", EntryPoint = "gst_audio_decoder_proxy_getcaps")]
    private static partial nint Proxy(nint dec, nint caps, nint filter);
}

/// <summary>
/// What <c>GstAudioEncoder</c> installs as its own <c>getcaps</c>, reached from
/// the chain-up of a managed encoder.
/// </summary>
/// <remarks>
/// gstaudioencoder.c falls back to
/// <c>gst_audio_encoder_proxy_getcaps (enc, NULL, filter)</c>, and its caller
/// unrefs the answer without checking it for NULL.
/// </remarks>
internal static partial class AudioEncoderDefaults
{
    /// <summary>Answers the caps the sink pad of the encoder can accept.</summary>
    /// <param name="enc">The native <c>GstAudioEncoder</c>.</param>
    /// <param name="filter">The filter of the caps query, or <c>0</c>.</param>
    /// <returns>The caps, owned by the caller.</returns>
    internal static nint ProxyGetcaps(nint enc, nint filter) => Proxy(enc, nint.Zero, filter);

    /// <summary>The <c>gst_audio_encoder_proxy_getcaps</c> entry point.</summary>
    /// <param name="enc">The native <c>GstAudioEncoder</c>.</param>
    /// <param name="caps">The caps to proxy, or <c>0</c> for the template caps.</param>
    /// <param name="filter">The filter of the caps query, or <c>0</c>.</param>
    /// <returns>The caps, owned by the caller.</returns>
    [LibraryImport("GstAudio", EntryPoint = "gst_audio_encoder_proxy_getcaps")]
    private static partial nint Proxy(nint enc, nint caps, nint filter);
}
