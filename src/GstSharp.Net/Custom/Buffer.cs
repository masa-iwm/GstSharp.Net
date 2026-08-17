namespace Gst;

public sealed partial class Buffer
{
    /// <summary>
    /// Wraps a buffer that GStreamer keeps owning, for the length of one call.
    /// </summary>
    /// <param name="handle">The buffer that is lent to managed code.</param>
    /// <remarks>
    /// This is how a vfunc override receives a <c>transfer none</c> buffer. The
    /// wrapper takes no reference of its own, so the buffer stays as writable
    /// as GStreamer handed it over and an in place override can map it for
    /// writing; disposing the wrapper only detaches it. See
    /// <see cref="Gst.Interop.Borrowed"/>.
    /// </remarks>
    internal static Buffer Borrow(nint handle) => new(new Gst.Interop.Borrowed(handle));

    private Buffer(Gst.Interop.Borrowed borrowed)
        : base(borrowed)
    {
    }

    /// <summary>
    /// Sets the presentation timestamp of the buffer.
    /// </summary>
    /// <param name="pts">
    /// The time the media should be presented at, or
    /// <see cref="ClockTime.None"/> when it is not known or not relevant.
    /// </param>
    /// <remarks>See <see cref="SetOffsetEnd"/> for the writability rule.</remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">The buffer is not writable.</exception>
    public unsafe void SetPts(ClockTime pts)
    {
        nint handle = WritableHandle();
        ((BufferRaw*)handle)->Pts = pts.Nanoseconds;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets the decoding timestamp of the buffer.
    /// </summary>
    /// <param name="dts">
    /// The time the media should be processed at, or
    /// <see cref="ClockTime.None"/> when it is not known or not relevant.
    /// </param>
    /// <remarks>See <see cref="SetOffsetEnd"/> for the writability rule.</remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">The buffer is not writable.</exception>
    public unsafe void SetDts(ClockTime dts)
    {
        nint handle = WritableHandle();
        ((BufferRaw*)handle)->Dts = dts.Nanoseconds;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets how long the data of the buffer lasts.
    /// </summary>
    /// <param name="duration">
    /// The duration, or <see cref="ClockTime.None"/> when it is not known or
    /// not relevant.
    /// </param>
    /// <remarks>See <see cref="SetOffsetEnd"/> for the writability rule.</remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">The buffer is not writable.</exception>
    public unsafe void SetDuration(ClockTime duration)
    {
        nint handle = WritableHandle();
        ((BufferRaw*)handle)->Duration = duration.Nanoseconds;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets the media specific offset of the buffer: the frame number for
    /// video, the index of the first sample for audio, the byte offset for
    /// plain data.
    /// </summary>
    /// <param name="offset">
    /// The offset, or <c>GST_BUFFER_OFFSET_NONE</c> (<see cref="ulong.MaxValue"/>)
    /// when it is not known.
    /// </param>
    /// <remarks>See <see cref="SetOffsetEnd"/> for the writability rule.</remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">The buffer is not writable.</exception>
    public unsafe void SetOffset(ulong offset)
    {
        nint handle = WritableHandle();
        ((BufferRaw*)handle)->Offset = offset;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets the last offset the buffer contains, in the same unit as
    /// <see cref="SetOffset"/>.
    /// </summary>
    /// <param name="offsetEnd">
    /// The offset, or <c>GST_BUFFER_OFFSET_NONE</c> (<see cref="ulong.MaxValue"/>)
    /// when it is not known.
    /// </param>
    /// <remarks>
    /// <para>
    /// The five timestamp and offset fields of <c>GstBuffer</c> are written
    /// directly, which is what the <c>GST_BUFFER_PTS</c> family of macros does
    /// in C. They are methods rather than setters on the generated properties
    /// because the properties are emitted read only today; a later generator
    /// change may add setters next to them, and these methods stay valid when
    /// it does.
    /// </para>
    /// <para>
    /// Writing a field of a buffer that somebody else also holds a reference to
    /// corrupts what that other holder sees, so this refuses to write unless
    /// the buffer is writable, exactly as <c>gst_buffer_is_writable</c> defines
    /// it. A buffer that was just allocated, or one that came back from a
    /// make-writable call, is the one to write to.
    /// </para>
    /// <para>
    /// The check and the write are two steps, and nothing can make them one:
    /// another thread that takes a reference in between makes the buffer shared
    /// after the check said it was not. That is the same rule the C API
    /// imposes — a buffer belongs to one owner until it is handed on — and it
    /// is why this is a guard against a mistake rather than a lock.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">The buffer is not writable.</exception>
    public unsafe void SetOffsetEnd(ulong offsetEnd)
    {
        nint handle = WritableHandle();
        ((BufferRaw*)handle)->OffsetEnd = offsetEnd;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Makes the buffer writable, copying it when somebody else holds a
    /// reference to it, and returns the wrapper to keep using.
    /// </summary>
    /// <returns>
    /// This wrapper. The return value exists so that the call can be chained;
    /// it is never a second wrapper.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_buffer_make_writable</c>. That function consumes the
    /// reference it is given and returns one that is either the same buffer,
    /// when the caller held the only reference, or a fresh copy of it. The
    /// wrapper adopts whatever comes back, so the buffer this wrapper stands
    /// for can change identity across the call, and it is the same wrapper
    /// either way — the C idiom <c>buf = gst_buffer_make_writable (buf)</c>
    /// becomes a plain <c>buffer.MakeWritable()</c>.
    /// </para>
    /// <para>
    /// <b>Any handle read before the call is stale afterwards.</b>
    /// <see cref="MiniObject.Handle"/> has to be read again, and a native
    /// pointer, a mapping or a raw field address that was taken from the old
    /// buffer must not be used any more.
    /// </para>
    /// <para>
    /// This is single owner surgery. It is only correct while no other wrapper
    /// and no other thread uses this wrapper, which is the rule the C API
    /// imposes on <c>gst_buffer_make_writable</c> as well: a buffer belongs to
    /// one owner until it is handed on. Nothing here locks, and a reference
    /// another thread takes while the call runs is simply lost work.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.Buffer MakeWritable()
    {
        _ = MakeWritableHandle();
        return this;
    }

    /// <summary>
    /// Creates a copy of the buffer that carries the same flags, timestamps,
    /// offsets, metadata and memory.
    /// </summary>
    /// <returns>
    /// The copy, which the caller owns and has to dispose, or
    /// <see langword="null"/> when the buffer could not be copied.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_buffer_copy</c>, which is
    /// <see cref="CopyRegion(Gst.BufferCopyFlags, nuint, nuint)"/> over the
    /// whole buffer with <see cref="BufferCopy.All"/>. It is hand written
    /// because the introspection data marks the function as not
    /// introspectable; see <c>BufferNative</c> for why it is importable
    /// nonetheless.
    /// </para>
    /// <para>
    /// <b>The bytes are shared, not duplicated.</b> Only the memory blocks are
    /// referenced a second time, so this is cheap no matter how large the
    /// buffer is, and it is the way to give a buffer its own timestamps without
    /// touching the ones the original carries. It also means that the copy is
    /// writable as a mini object — it holds the only reference to itself, so
    /// <see cref="SetPts"/> and the other field setters work on it — while a
    /// <see cref="MapFlags.Write"/> mapping of it can still fail: the memory
    /// underneath belongs to both buffers, and neither may write it while the
    /// other lives. Use
    /// <see cref="CopyRegion(Gst.BufferCopyFlags, nuint, nuint)"/> with
    /// <see cref="BufferCopyFlags.Deep"/> when the bytes have to be a copy too.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.Buffer? Copy()
    {
        nint nativeResult = BufferNative.Copy(Handle);

        // The buffer has to outlive the call that reads it: reading Handle is
        // the last use of this wrapper, and a finalizer that runs in between
        // would release the buffer being copied.
        GC.KeepAlive(this);
        return Gst.Buffer.FromNative(nativeResult, Gst.Interop.Transfer.Full);
    }

    /// <summary>
    /// Returns the handle of the buffer, provided that its fields may be
    /// written.
    /// </summary>
    /// <returns>The buffer to write to.</returns>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">The buffer is not writable.</exception>
    private nint WritableHandle()
    {
        nint handle = Handle;

        if (GstNative.MiniObjectIsWritable(handle) == 0)
        {
            throw new InvalidOperationException(
                "The buffer is not writable: somebody else holds a reference to it, and writing a field " +
                "would change what they see. Make it writable first.");
        }

        return handle;
    }

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
