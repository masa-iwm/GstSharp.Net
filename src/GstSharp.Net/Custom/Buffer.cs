namespace Gst;

public sealed partial class Buffer
{
    /// <summary>
    /// Maps the memory of the buffer into the address space of the process for
    /// as long as the returned scope lives.
    /// </summary>
    /// <param name="flags">
    /// The access the caller needs. <see cref="MapFlags.Write"/> requires the
    /// buffer to be writable, which it only is while this wrapper holds the
    /// single reference to it.
    /// </param>
    /// <returns>
    /// The mapping, which has to be disposed to release the memory again.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The scope is a <c>ref struct</c>, so it cannot outlive the stack frame
    /// that created it and cannot be captured by a lambda, a field or an async
    /// state machine. That is exactly the lifetime of the mapping itself: the
    /// <see cref="System.Span{T}"/> it hands out points into memory that
    /// GStreamer owns and that <see cref="MapScope.Dispose"/> gives back.
    /// </para>
    /// <para>
    /// A mapping belongs to the thread that created it. Nothing here is
    /// synchronised, and neither the span nor the scope may be handed to
    /// another thread while it is alive.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The buffer could not be mapped, typically because
    /// <see cref="MapFlags.Write"/> was asked for while somebody else holds a
    /// reference to the buffer.
    /// </exception>
    public MapScope Map(MapFlags flags) => new(this, flags);

    /// <summary>
    /// A buffer that is mapped into the address space of the process, and the
    /// span over the memory it was mapped to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// See <see cref="Buffer.Map(Gst.MapFlags)"/> for how long the mapping lives and which
    /// thread owns it. The scope owns the mapping, so it must not be copied:
    /// the copy would release the same mapping a second time.
    /// </para>
    /// <para>
    /// It also holds on to the wrapper it was created from. The memory belongs
    /// to the buffer, so the buffer has to outlive the mapping, and without
    /// that reference nothing would stop the collector from finalizing a
    /// wrapper whose last use was the call to <see cref="Buffer.Map(Gst.MapFlags)"/>.
    /// </para>
    /// </remarks>
    public unsafe ref struct MapScope : IDisposable
    {
        private readonly Buffer _owner;
        private MapInfo _info;
        private nint _buffer;

        /// <summary>
        /// Maps a buffer.
        /// </summary>
        /// <param name="owner">The buffer to map.</param>
        /// <param name="flags">The access the caller needs.</param>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="owner"/> was disposed.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The buffer could not be mapped.
        /// </exception>
        internal MapScope(Buffer owner, MapFlags flags)
        {
            nint buffer = owner.Handle;
            MapInfo info = default;

            // gst_buffer_map reports a plain failure, without a GError: the
            // buffer is not writable, or its memory cannot be merged into one
            // block. Both are states of the caller's own object rather than
            // failures of an external resource, which is why this is an
            // InvalidOperationException and not a GException.
            if (BufferNative.Map(buffer, &info, flags) == 0)
            {
                throw new InvalidOperationException(
                    $"The buffer could not be mapped with {flags}. " +
                    "A writable mapping needs the only reference to the buffer.");
            }

            _owner = owner;
            _info = info;
            _buffer = buffer;
        }

        /// <summary>
        /// Gets the number of valid bytes in the mapping.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// The mapping was released.
        /// </exception>
        public readonly nuint Size
        {
            get
            {
                ObjectDisposedException.ThrowIf(_buffer == nint.Zero, typeof(MapScope));
                return _info.Size;
            }
        }

        /// <summary>
        /// Gets the mapped memory.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// The mapping was released.
        /// </exception>
        /// <exception cref="OverflowException">
        /// The mapping is larger than <see cref="int.MaxValue"/> bytes and does
        /// not fit into a single span.
        /// </exception>
        public readonly Span<byte> Span
        {
            get
            {
                ObjectDisposedException.ThrowIf(_buffer == nint.Zero, typeof(MapScope));
                return new Span<byte>((void*)_info.Data, checked((int)_info.Size));
            }
        }

        /// <summary>
        /// Releases the mapping. Releasing it twice does nothing the second
        /// time, and every accessor throws from then on.
        /// </summary>
        public void Dispose()
        {
            nint buffer = _buffer;
            if (buffer == nint.Zero)
            {
                return;
            }

            _buffer = nint.Zero;

            // gst_buffer_unmap reads the memory and the flags back out of the
            // GstMapInfo; it does not care that this is the copy the scope
            // carries rather than the one that was passed to gst_buffer_map.
            fixed (MapInfo* info = &_info)
            {
                BufferNative.Unmap(buffer, info);
            }

            // The wrapper had to stay alive until the memory was given back.
            GC.KeepAlive(_owner);
        }
    }
}
