using System.Runtime.InteropServices;

namespace Gst.Audio;

/// <summary>
/// Raw entry points of <c>libgstaudio-1.0</c> that the hand written audio
/// metadata glue needs.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_buffer_add_audio_meta</c> sizes its <c>offsets</c> array by
/// <c>info-&gt;channels</c>, a field of another parameter, which no array
/// annotation of the gir can express and which <c>arrayOverrides</c> cannot
/// redirect to either — it only names another parameter. The entry point is on
/// the skip list of <c>girs/overlays/fixups.json</c> for it and is imported
/// here for <see cref="AudioGlobal.BufferAddAudioMeta(Gst.Buffer, Gst.Audio.AudioInfo, nuint, System.ReadOnlySpan{nuint})"/>.
/// </para>
/// <para>
/// <c>gst_buffer_add_audio_downmix_meta</c> is not introspectable at all: its
/// <c>matrix</c> is a <c>const gfloat**</c> row table the gir spells as an
/// array of <c>gpointer</c> with no length, so nothing describes either
/// dimension of it. It is imported here for
/// <see cref="AudioGlobal.BufferAddAudioDownmixMeta"/>, which builds the row
/// table from one row major span.
/// </para>
/// </remarks>
internal static unsafe partial class AudioGlobalNative
{
    /// <summary>Attaches audio metadata to a buffer.</summary>
    /// <param name="buffer">The buffer, which must be writable.</param>
    /// <param name="info">The format of the audio the buffer holds.</param>
    /// <param name="samples">How many samples per channel the buffer holds.</param>
    /// <param name="offsets">
    /// Where each channel plane starts, in bytes, with as many entries as
    /// <c>info-&gt;channels</c>, or <c>0</c> to have them computed.
    /// </param>
    /// <returns>
    /// The metadata item, which the buffer owns, or <c>0</c> when the buffer
    /// refused it.
    /// </returns>
    [LibraryImport("GstAudio", EntryPoint = "gst_buffer_add_audio_meta")]
    internal static partial nint BufferAddAudioMeta(nint buffer, nint info, nuint samples, nuint* offsets);

    /// <summary>Attaches a downmix matrix to a buffer.</summary>
    /// <param name="buffer">The buffer, which must be writable.</param>
    /// <param name="fromPosition">The channel positions of the source.</param>
    /// <param name="fromChannels">How many entries <paramref name="fromPosition"/> has.</param>
    /// <param name="toPosition">The channel positions of the destination.</param>
    /// <param name="toChannels">How many entries <paramref name="toPosition"/> has.</param>
    /// <param name="matrix">
    /// A table of <paramref name="toChannels"/> row pointers, each addressing
    /// <paramref name="fromChannels"/> coefficients.
    /// </param>
    /// <returns>The metadata item, which the buffer owns.</returns>
    [LibraryImport("GstAudio", EntryPoint = "gst_buffer_add_audio_downmix_meta")]
    internal static partial nint BufferAddAudioDownmixMeta(
        nint buffer,
        Gst.Audio.AudioChannelPosition* fromPosition,
        int fromChannels,
        Gst.Audio.AudioChannelPosition* toPosition,
        int toChannels,
        float** matrix);
}
