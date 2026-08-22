using Gst.Interop;

namespace Gst.Video;

/// <content>
/// The mapping of a buffer as a video frame, which C performs into a structure
/// the caller declares.
/// </content>
/// <remarks>
/// <c>gst_video_frame_map</c> fills a <c>GstVideoFrame</c> that the caller owns
/// and that <c>gst_video_frame_unmap</c> releases again, which is a scope and
/// not an out parameter: the plane pointers it hands out are only valid until
/// the release. The three entry points are on the skip list of
/// <c>girs/overlays/fixups.json</c> for that reason and are bound here.
/// </remarks>
public sealed partial class VideoFrame
{
    /// <summary>Maps a buffer as a video frame.</summary>
    /// <param name="info">The video info the buffer holds.</param>
    /// <param name="buffer">The buffer to map.</param>
    /// <param name="flags">
    /// The access the caller needs. <see cref="Gst.MapFlags.Write"/> requires
    /// the buffer to be writable, and <see cref="VideoFrameMapFlags.NoRef"/>
    /// may be combined into it, see the remarks.
    /// </param>
    /// <returns>
    /// The mapping, which has to be disposed to release the planes again.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_video_frame_map</c>, which maps the buffer through the
    /// <c>GstVideoMeta</c> it carries when there is one, and through the plane
    /// offsets of <paramref name="info"/> when there is not.
    /// </para>
    /// <para>
    /// The frame takes a reference of the buffer unless
    /// <see cref="VideoFrameMapFlags.NoRef"/> is set, which is what that flag
    /// is for: without the extra reference the buffer stays writable while the
    /// frame is mapped, and keeping it alive becomes the caller's job. The
    /// scope holds on to the wrapper either way, so the buffer outlives the
    /// mapping whichever flag was used.
    /// </para>
    /// <para>
    /// C spells the two flag sets into one argument, and so does this. The
    /// frame flags continue where the map flags end, so combining them is a
    /// cast and nothing else:
    /// <c>VideoFrame.Map(info, buffer, MapFlags.Read | (MapFlags)VideoFrameMapFlags.NoRef)</c>.
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
    public static MapScope Map(VideoInfo info, Gst.Buffer buffer, Gst.MapFlags flags) =>
        new(info, buffer, id: -1, byId: false, flags);

    /// <summary>Maps one identified frame of a buffer.</summary>
    /// <param name="info">The video info the buffer holds.</param>
    /// <param name="buffer">The buffer to map.</param>
    /// <param name="id">
    /// The frame identifier of the <c>GstVideoMeta</c> to map, or <c>-1</c> for
    /// the first meta of the buffer.
    /// </param>
    /// <param name="flags">The access the caller needs.</param>
    /// <returns>
    /// The mapping, which has to be disposed to release the planes again.
    /// </returns>
    /// <remarks>
    /// This is <c>gst_video_frame_map_id</c>. Unlike
    /// <see cref="Map(VideoInfo, Gst.Buffer, Gst.MapFlags)"/> it fails when the
    /// buffer carries no meta with that identifier, because there is nothing to
    /// fall back on: an identifier only means anything to a meta.
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
    public static MapScope MapId(VideoInfo info, Gst.Buffer buffer, int id, Gst.MapFlags flags) =>
        new(info, buffer, id, byId: true, flags);

    /// <summary>
    /// A buffer that is mapped as a video frame, and the planes it was mapped
    /// to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scope owns the mapping and carries the <c>GstVideoFrame</c> itself,
    /// so it must not be copied: the copy would release the same mapping a
    /// second time. It is a <c>ref struct</c>, which is what keeps it on the
    /// stack, out of a field, out of a lambda and out of an async state
    /// machine — exactly the lifetime the plane pointers have.
    /// </para>
    /// <para>
    /// It also holds on to the buffer wrapper. The planes point into memory the
    /// buffer owns, so the buffer has to outlive the mapping, and that is a
    /// correctness requirement rather than a convenience: with
    /// <see cref="VideoFrameMapFlags.NoRef"/> the frame takes no reference of
    /// its own and nothing else would keep the buffer alive.
    /// </para>
    /// <para>
    /// A mapping belongs to the thread that created it. Nothing here is
    /// synchronised.
    /// </para>
    /// </remarks>
    public unsafe ref struct MapScope : IDisposable
    {
        private readonly Gst.Buffer _owner;
        private VideoFrameRaw _frame;
        private bool _mapped;

        /// <summary>Maps a buffer as a video frame.</summary>
        /// <param name="info">The video info the buffer holds.</param>
        /// <param name="buffer">The buffer to map.</param>
        /// <param name="id">The frame identifier, when <paramref name="byId"/> is set.</param>
        /// <param name="byId">Whether the identified overload was called.</param>
        /// <param name="flags">The access the caller needs.</param>
        /// <exception cref="InvalidOperationException">
        /// The buffer could not be mapped.
        /// </exception>
        internal MapScope(VideoInfo info, Gst.Buffer buffer, int id, bool byId, Gst.MapFlags flags)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(buffer);

            nint infoHandle = info.Handle;
            nint bufferHandle = buffer.Handle;

            _frame = default;
            _owner = buffer;

            int mapped;
            fixed (VideoFrameRaw* frame = &_frame)
            {
                mapped = byId
                    ? VideoFrameNative.MapId((nint)frame, infoHandle, bufferHandle, id, (int)flags)
                    : VideoFrameNative.Map((nint)frame, infoHandle, bufferHandle, (int)flags);
            }

            GC.KeepAlive(info);
            GC.KeepAlive(buffer);

            // Every failure path of the C function leaves nothing mapped: it
            // either never took a mapping, or released the ones it had taken
            // and zeroed the frame. There is no partial state to hand back and
            // no GError to report either, which is why this is an
            // InvalidOperationException, exactly as Gst.Buffer.Map is.
            if (mapped == 0)
            {
                throw new InvalidOperationException(
                    $"The buffer could not be mapped as a video frame with {flags}. " +
                    "The video info has to describe the buffer, a writable mapping needs the only " +
                    "reference to the buffer, and mapping by identifier needs a matching video meta.");
            }

            _mapped = true;
        }

        /// <summary>
        /// Gets the buffer the mapping was taken from.
        /// </summary>
        public readonly Gst.Buffer Buffer => _owner;

        /// <summary>
        /// Gets the <c>buffer</c>, <c>meta</c> and <c>id</c> fields of the
        /// mapped frame, as the library wrote them.
        /// </summary>
        /// <remarks>
        /// The offsets of <see cref="VideoFrameRaw"/> are stated by the ABI
        /// probe tests and measured against the installed library by the caller
        /// allocated storage tests, which read these three back after a live
        /// mapping: they are the fields the plane spans do not reach. Nothing
        /// on the public surface needs them, and the values are copies rather
        /// than storage, so a released scope answers what the mapping left
        /// behind rather than throwing.
        /// </remarks>
        internal readonly (nint Buffer, nint Meta, int Id) RawFields =>
            (_frame.BufferPtr, _frame.MetaPtr, _frame.Id);

        /// <summary>
        /// Gets the frame flags the mapping settled on, which combine the flags
        /// of the video info with those of the buffer.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// The mapping was released.
        /// </exception>
        public readonly VideoFrameFlags Flags
        {
            get
            {
                ObjectDisposedException.ThrowIf(!_mapped, typeof(MapScope));
                return (VideoFrameFlags)_frame.Flags;
            }
        }

        /// <summary>
        /// Gets a copy of the video info the frame was mapped with, corrected
        /// by the <c>GstVideoMeta</c> of the buffer where there was one.
        /// </summary>
        /// <remarks>
        /// The copy is the caller's own and has to be disposed. The info the
        /// frame carries belongs to the mapping and would dangle once the scope
        /// is disposed, which is why this is a copy and not a view.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// The mapping was released.
        /// </exception>
        public VideoInfo Info
        {
            get
            {
                ObjectDisposedException.ThrowIf(!_mapped, typeof(MapScope));

                // The info sits at the front of a GstVideoFrame, so the address
                // of the frame is the address of the info.
                fixed (VideoFrameRaw* frame = &_frame)
                {
                    return VideoInfo.FromNative((nint)frame, Transfer.None)
                        ?? throw new InvalidOperationException("The mapped frame carries no video info.");
                }
            }
        }

        /// <summary>
        /// Gets a view of the frame that the members of
        /// <see cref="VideoFrame"/> take, such as
        /// <see cref="VideoFrame.Copy(VideoFrame)"/>.
        /// </summary>
        /// <remarks>
        /// The view does not own the frame: the scope does, and the view is
        /// only usable while the scope lives. Nothing on it releases the
        /// mapping, because <c>gst_video_frame_unmap</c> is bound by
        /// <see cref="Dispose"/> alone.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// The mapping was released.
        /// </exception>
        public VideoFrame Frame
        {
            get
            {
                ObjectDisposedException.ThrowIf(!_mapped, typeof(MapScope));
                fixed (VideoFrameRaw* frame = &_frame)
                {
                    return new VideoFrame((nint)frame);
                }
            }
        }

        /// <summary>
        /// Gets the mapped memory of one plane.
        /// </summary>
        /// <param name="index">
        /// The plane to read, below the number of planes the format of the
        /// frame has.
        /// </param>
        /// <returns>The memory of the plane.</returns>
        /// <remarks>
        /// The span starts where the plane starts and ends where the mapping
        /// that carries it ends. A frame that was mapped through a
        /// <c>GstVideoMeta</c> has one mapping per plane, so the span is that
        /// plane and nothing else; a frame that was mapped without one has a
        /// single mapping over the whole buffer, so the span of a plane runs to
        /// the end of the buffer and holds the planes after it as well. How
        /// many rows of how many bytes a plane really has is what
        /// <see cref="Info"/> describes, in both cases.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// The mapping was released.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The frame has no plane at <paramref name="index"/>.
        /// </exception>
        public Span<byte> Plane(uint index)
        {
            ObjectDisposedException.ThrowIf(!_mapped, typeof(MapScope));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, (uint)VideoFrameRaw.MaxPlanes);

            nint data = _frame.Data[(int)index];
            if (data == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    "The mapped frame has no plane at that index.");
            }

            // A plane that was mapped on its own carries a GstMapInfo of its
            // own; one that came out of a single mapping of the whole buffer
            // points into the mapping of plane zero.
            Gst.MapInfo mapping = _frame.Map[(int)index];
            if (mapping.DataPtr == 0)
            {
                mapping = _frame.Map[0];
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

            // gst_video_frame_unmap reads the buffer, the meta and the plane
            // mappings back out of the frame, and drops the reference the
            // mapping took unless it was asked not to take one. It is not
            // idempotent, which is what the flag above is for.
            fixed (VideoFrameRaw* frame = &_frame)
            {
                VideoFrameNative.Unmap((nint)frame);
            }

            // The wrapper had to stay alive until the planes were given back.
            GC.KeepAlive(_owner);
        }
    }
}
