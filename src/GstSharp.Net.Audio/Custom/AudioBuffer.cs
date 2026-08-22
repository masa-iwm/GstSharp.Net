using Gst.Interop;

namespace Gst.Audio;

/// <content>
/// The mapping of a buffer as an audio buffer, which C performs into a
/// structure the caller declares.
/// </content>
/// <remarks>
/// <c>gst_audio_buffer_map</c> fills a <c>GstAudioBuffer</c> that the caller
/// owns and that <c>gst_audio_buffer_unmap</c> releases again, which is a scope
/// and not an out parameter: the plane pointers it hands out are only valid
/// until the release. Both entry points are on the skip list of
/// <c>girs/overlays/fixups.json</c> for that reason and are bound here.
/// </remarks>
public sealed partial class AudioBuffer
{
    /// <summary>Maps a buffer as an audio buffer.</summary>
    /// <param name="info">The audio info the buffer holds.</param>
    /// <param name="buffer">The buffer to map.</param>
    /// <param name="flags">
    /// The access the caller needs. <see cref="Gst.MapFlags.Write"/> requires
    /// the buffer to be writable.
    /// </param>
    /// <returns>
    /// The mapping, which has to be disposed to release the planes again.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_audio_buffer_map</c>. An interleaved buffer maps to a
    /// single plane holding every channel; a planar one maps to one plane per
    /// channel, described by the <c>GstAudioMeta</c> the buffer has to carry.
    /// </para>
    /// <para>
    /// <b>The mapping takes no reference of the buffer.</b> Unlike a video
    /// frame, an audio buffer holds the pointer and nothing else, so the buffer
    /// staying alive is the caller's responsibility for as long as the mapping
    /// lives. The scope keeps the wrapper reachable, which is what makes that
    /// hold here, and it is a correctness requirement rather than a
    /// convenience: without it the collector could finalize a wrapper whose
    /// last use was this call and free the memory the planes point into.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="info"/> or <paramref name="buffer"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// <paramref name="info"/> or <paramref name="buffer"/> was disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The buffer could not be mapped.
    /// </exception>
    public static MapScope Map(AudioInfo info, Gst.Buffer buffer, Gst.MapFlags flags) =>
        new(info, buffer, flags);

    /// <summary>
    /// A buffer that is mapped as an audio buffer, and the planes it was mapped
    /// to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scope owns the mapping and carries the <c>GstAudioBuffer</c> itself,
    /// so it must not be copied: the copy would release the same mapping a
    /// second time. It is a <c>ref struct</c>, which is what keeps it on the
    /// stack, out of a field, out of a lambda and out of an async state
    /// machine — exactly the lifetime the plane pointers have.
    /// </para>
    /// <para>
    /// It also holds on to the buffer wrapper, which the mapping itself does
    /// not: see <see cref="AudioBuffer.Map"/>.
    /// </para>
    /// <para>
    /// A mapping belongs to the thread that created it. Nothing here is
    /// synchronised.
    /// </para>
    /// </remarks>
    public unsafe ref struct MapScope : IDisposable
    {
        private readonly Gst.Buffer _owner;
        private readonly bool _inline;
        private AudioBufferRaw _audio;
        private bool _mapped;

        /// <summary>Maps a buffer as an audio buffer.</summary>
        /// <param name="info">The audio info the buffer holds.</param>
        /// <param name="buffer">The buffer to map.</param>
        /// <param name="flags">The access the caller needs.</param>
        /// <exception cref="InvalidOperationException">
        /// The buffer could not be mapped.
        /// </exception>
        internal MapScope(AudioInfo info, Gst.Buffer buffer, Gst.MapFlags flags)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(buffer);

            nint infoHandle = info.Handle;
            nint bufferHandle = buffer.Handle;

            _audio = default;
            _owner = buffer;

            int mapped;
            bool inline;
            fixed (AudioBufferRaw* audio = &_audio)
            {
                mapped = AudioBufferNative.Map((nint)audio, infoHandle, bufferHandle, (int)flags);

                // Which of the two array layouts the mapping chose can only be
                // read while the structure is still where the call saw it.
                inline = _audio.Planes == (nint)(&audio->PrivPlanesArr);
            }

            GC.KeepAlive(info);
            GC.KeepAlive(buffer);

            // The C function releases whatever it had mapped before it reports
            // a failure, so there is no partial state to hand back and no
            // GError to report either. That makes this an
            // InvalidOperationException, exactly as Gst.Buffer.Map is.
            if (mapped == 0)
            {
                throw new InvalidOperationException(
                    $"The buffer could not be mapped as an audio buffer with {flags}. " +
                    "The audio info has to describe the buffer, a planar layout needs a matching " +
                    "audio meta on the buffer, and a writable mapping needs the only reference to it.");
            }

            _inline = inline;
            _mapped = true;
        }

        /// <summary>
        /// Gets the buffer the mapping was taken from.
        /// </summary>
        public readonly Gst.Buffer Buffer => _owner;

        /// <summary>
        /// Gets the <c>buffer</c> field of the mapped structure, as the library
        /// wrote it, and whether the mapping pointed its two array fields at
        /// the inline arrays of the structure itself.
        /// </summary>
        /// <remarks>
        /// The offsets of <see cref="AudioBufferRaw"/> are stated by the ABI
        /// probe tests and measured against the installed library by the caller
        /// allocated storage tests, which read these back after a live mapping.
        /// The inline flag is the stronger of the two: it only holds when the
        /// library wrote the address of <c>priv_planes_arr</c> into
        /// <c>planes</c>, so it measures both offsets at once. Nothing on the
        /// public surface needs either, and a released scope answers what the
        /// mapping left behind rather than throwing.
        /// </remarks>
        internal readonly (nint Buffer, bool PlanesAreInline) RawFields => (_audio.Buffer, _inline);

        /// <summary>
        /// Gets the number of samples per channel the mapping holds.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// The mapping was released.
        /// </exception>
        public readonly nuint NSamples
        {
            get
            {
                ObjectDisposedException.ThrowIf(!_mapped, typeof(MapScope));
                return _audio.NSamples;
            }
        }

        /// <summary>
        /// Gets the number of planes the mapping holds: one for an interleaved
        /// buffer, one per channel for a planar one.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// The mapping was released.
        /// </exception>
        public readonly int NPlanes
        {
            get
            {
                ObjectDisposedException.ThrowIf(!_mapped, typeof(MapScope));
                return _audio.NPlanes;
            }
        }

        /// <summary>
        /// Gets a copy of the audio info the mapping settled on, which is the
        /// info of the <c>GstAudioMeta</c> of the buffer where there was one.
        /// </summary>
        /// <remarks>
        /// The copy is the caller's own and has to be disposed. The info the
        /// mapping carries belongs to the mapping and would dangle once the
        /// scope is disposed, which is why this is a copy and not a view.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// The mapping was released.
        /// </exception>
        public AudioInfo Info
        {
            get
            {
                ObjectDisposedException.ThrowIf(!_mapped, typeof(MapScope));

                // The info sits at the front of a GstAudioBuffer, so the
                // address of the structure is the address of the info.
                fixed (AudioBufferRaw* audio = &_audio)
                {
                    return AudioInfo.FromNative((nint)audio, Transfer.None)
                        ?? throw new InvalidOperationException("The mapped buffer carries no audio info.");
                }
            }
        }

        /// <summary>
        /// Gets the mapped memory of one plane.
        /// </summary>
        /// <param name="index">The plane to read, below <see cref="NPlanes"/>.</param>
        /// <returns>The memory of the plane.</returns>
        /// <remarks>
        /// The span starts where the plane starts and ends where the mapping
        /// that carries it ends, which for an interleaved buffer is the whole
        /// of it. How many samples of how many bytes that is, is what
        /// <see cref="NSamples"/> and <see cref="Info"/> describe.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// The mapping was released.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The mapping has no plane at <paramref name="index"/>.
        /// </exception>
        public Span<byte> Plane(uint index)
        {
            ObjectDisposedException.ThrowIf(!_mapped, typeof(MapScope));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, (uint)_audio.NPlanes);

            nint data;
            Gst.MapInfo mapping;
            fixed (AudioBufferRaw* audio = &_audio)
            {
                Rebind(audio);
                data = ((nint*)audio->Planes)[index];
                mapping = ((Gst.MapInfo*)audio->MapInfos)[index];
            }

            if (data == 0)
            {
                return default;
            }

            long length = (long)mapping.Size - ((long)data - mapping.DataPtr);
            return length <= 0
                ? default
                : new Span<byte>((void*)data, checked((int)length));
        }

        /// <summary>
        /// Releases the mapping. Releasing it twice does nothing the second
        /// time, and every accessor throws from then on.
        /// </summary>
        public void Dispose()
        {
            if (!_mapped)
            {
                return;
            }

            _mapped = false;

            fixed (AudioBufferRaw* audio = &_audio)
            {
                Rebind(audio);
                AudioBufferNative.Unmap((nint)audio);
            }

            // The mapping never referenced the buffer, so the wrapper is the
            // only thing that kept the memory the planes point into alive.
            GC.KeepAlive(_owner);
        }

        /// <summary>
        /// Points the two array fields of the structure back at the structure
        /// itself, when that is where the mapping put them.
        /// </summary>
        /// <param name="audio">The pinned structure.</param>
        /// <remarks>
        /// For eight planes or fewer the mapping does not allocate: it points
        /// <c>planes</c> and <c>map_infos</c> at the inline arrays of the
        /// structure it was handed. A scope that was returned by value carries
        /// those pointers to where the structure used to be, which reads freed
        /// stack on the way in and — worse — makes
        /// <c>gst_audio_buffer_unmap</c> take them for heap arrays and call
        /// <c>g_free</c> on the middle of a stack frame. Repairing them here is
        /// what makes the scope safe to move.
        /// </remarks>
        private readonly void Rebind(AudioBufferRaw* audio)
        {
            if (_inline)
            {
                audio->Planes = (nint)(&audio->PrivPlanesArr);
                audio->MapInfos = (nint)(&audio->PrivMapInfosArr);
            }
        }
    }
}
