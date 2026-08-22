using System.Runtime.InteropServices;

namespace Gst.Audio;

/// <summary>
/// Raw entry points of <c>libgstaudio-1.0</c> that the hand written audio
/// buffer mapping needs.
/// </summary>
/// <remarks>
/// The audio buffer is passed as a bare address rather than as a typed pointer,
/// so that nothing about the layout of <c>AudioBufferRaw</c> — the generated
/// mirror of <c>GstAudioBuffer</c>, which is the storage
/// <see cref="AudioBuffer.MapScope"/> declares — has to cross an assembly
/// boundary in an interop signature. Both entry points are on the skip list of
/// <c>girs/overlays/fixups.json</c>: the mapping belongs to the scope and the
/// release is one way.
/// </remarks>
internal static partial class AudioBufferNative
{
    /// <summary>Fills an audio buffer with the planes of a buffer.</summary>
    /// <param name="buffer">The storage to fill.</param>
    /// <param name="info">The audio info the buffer holds.</param>
    /// <param name="gstbuffer">The buffer to map.</param>
    /// <param name="flags">The access the caller needs.</param>
    /// <returns>Non zero when the buffer was mapped.</returns>
    [LibraryImport("GstAudio", EntryPoint = "gst_audio_buffer_map")]
    internal static partial int Map(nint buffer, nint info, nint gstbuffer, int flags);

    /// <summary>Releases the planes an audio buffer was mapped to.</summary>
    /// <param name="buffer">The audio buffer to release.</param>
    [LibraryImport("GstAudio", EntryPoint = "gst_audio_buffer_unmap")]
    internal static partial void Unmap(nint buffer);
}
