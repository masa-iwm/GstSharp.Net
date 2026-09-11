namespace Gst.Base;

public unsafe partial class Adapter
{
    /// <summary>
    /// Maps the first <paramref name="size"/> bytes of the adapter into a span
    /// that lives for as long as the returned scope does.
    /// </summary>
    /// <param name="size">
    /// The number of bytes to map. It has to be a number that
    /// <see cref="Available"/> reports as available, and it has to be greater
    /// than zero.
    /// </param>
    /// <returns>
    /// The mapping, which has to be disposed to release the memory again.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_adapter_map</c>. The memory belongs to the adapter and is
    /// only borrowed: the adapter merges as many of the buffers it holds as it
    /// takes to hand out one contiguous block, which may cost an allocation and
    /// a copy, and it keeps that block until the mapping is released.
    /// </para>
    /// <para>
    /// <b>The span is only valid until the next call on this adapter.</b>
    /// Pushing a buffer into it, flushing it, taking bytes out of it or clearing
    /// it all invalidate the memory, so the scope has to be disposed before any
    /// of them — the usual shape is to map, read, dispose, and only then flush
    /// what was consumed.
    /// </para>
    /// <para>
    /// <b>Mappings do not nest.</b> An adapter holds a single mapping, and
    /// <c>gst_adapter_unmap</c> releases whichever one is current, so a second
    /// scope taken while a first one is alive releases the memory of both. Map
    /// once, and dispose that scope before mapping again.
    /// </para>
    /// <para>
    /// A <c>GstAdapter</c> is not thread safe at all: every call on it, this one
    /// included, has to be serialised by the caller, and the span must not be
    /// handed to another thread while the scope is alive. The scope is a
    /// <c>ref struct</c>, so it cannot outlive the stack frame that created it
    /// and cannot be captured by a lambda, a field or an async state machine,
    /// which is exactly the lifetime of the mapping itself.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The adapter holds fewer than <paramref name="size"/> bytes, or
    /// <paramref name="size"/> is zero.
    /// </exception>
    public MapScope Map(nuint size) => new(this, size);

    /// <summary>
    /// Copies <paramref name="size"/> bytes of the adapter into a block of
    /// their own.
    /// </summary>
    /// <param name="offset">Where in the adapter the copy starts.</param>
    /// <param name="size">The number of bytes to copy.</param>
    /// <returns>
    /// The copy, which the caller owns and disposes. It is independent of the
    /// adapter: flushing, clearing or pushing into the adapter afterwards does
    /// not touch it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_adapter_copy_bytes</c>, the copying counterpart of
    /// <see cref="Map(nuint)"/>: the mapping borrows the memory of the adapter
    /// and has to be released before the next call on it, while this allocates
    /// and copies and has no such rule. Copying is also what makes an offset
    /// possible at all — a mapping always starts at the front.
    /// </para>
    /// <para>
    /// <b>The range is checked here rather than by the library.</b> The C
    /// raises a critical for a size of zero and another one for a range the
    /// adapter does not hold, and neither stops it: the first answers an empty
    /// block and the second hands out the block it allocated without ever
    /// copying into it, which is uninitialised memory of the requested length.
    /// A size of zero is answered with an empty block of our own, and a range
    /// the adapter does not hold with
    /// <see cref="ArgumentOutOfRangeException"/>. The sum is compared without
    /// forming it, so that an offset near <see cref="nuint.MaxValue"/> is
    /// refused rather than wrapping around into a range that looks valid.
    /// </para>
    /// <para>
    /// A <c>GstAdapter</c> is not thread safe, so reading
    /// <see cref="Available"/> and then copying is only as racy as the C call
    /// itself: every call on one adapter has to be serialised by the caller,
    /// and nothing may push into it or flush it between the two.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The adapter does not hold <paramref name="size"/> bytes from
    /// <paramref name="offset"/> on.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.GLib.Bytes Copy(nuint offset, nuint size)
    {
        // The handle is read first, so that a disposed wrapper throws before
        // anything is allocated.
        nint adapter = Handle;

        if (size == 0)
        {
            // See the remarks: the C answers an empty block and a critical.
            return Gst.GLib.Bytes.New(ReadOnlySpan<byte>.Empty);
        }

        nuint available = Available();
        if (size > available)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                $"The adapter holds {available} bytes.");
        }

        if (offset > available - size)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                offset,
                $"The adapter holds {available} bytes, and {size} of them are asked for.");
        }

        nint nativeResult = AdapterNative.CopyBytes(adapter, offset, size);

        // The handle was read before the call, so nothing keeps this wrapper
        // alive across it on its own.
        GC.KeepAlive(this);

        return Gst.GLib.Bytes.FromNative(nativeResult, Gst.Interop.Transfer.Full)
            ?? throw new InvalidOperationException("gst_adapter_copy_bytes returned no value.");
    }

    /// <summary>
    /// An adapter whose bytes are mapped into one contiguous block, and the span
    /// over that block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// See <see cref="Adapter.Map(nuint)"/> for how long the mapping lives,
    /// which calls invalidate it and which thread owns it. The scope owns the
    /// mapping, so it must not be copied: the copy would release the same
    /// mapping a second time.
    /// </para>
    /// <para>
    /// It also holds on to the wrapper it was created from. The memory belongs
    /// to the adapter, so the adapter has to outlive the mapping, and without
    /// that reference nothing would stop the collector from finalizing a wrapper
    /// whose last use was the call to <see cref="Adapter.Map(nuint)"/>.
    /// </para>
    /// <para>
    /// The span is read only, unlike the one of <c>Gst.Buffer.MapScope</c>:
    /// <c>gst_adapter_map</c> returns a <c>const guint8 *</c>, and the bytes
    /// behind it may be the memory of a buffer that the adapter shares with
    /// whoever pushed it.
    /// </para>
    /// </remarks>
    public unsafe ref struct MapScope : IDisposable
    {
        private readonly Adapter _owner;
        private readonly nint _data;
        private readonly nuint _size;
        private nint _adapter;

        /// <summary>
        /// Maps an adapter.
        /// </summary>
        /// <param name="owner">The adapter to map.</param>
        /// <param name="size">The number of bytes to map.</param>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="owner"/> was disposed.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The adapter could not be mapped.
        /// </exception>
        internal MapScope(Adapter owner, nuint size)
        {
            nint adapter = owner.Handle;

            // gst_adapter_map reports a plain failure, without a GError: the
            // adapter holds fewer bytes than were asked for, or the size is
            // zero, which the C function rejects outright. Both are states of
            // the caller's own object rather than failures of an external
            // resource, which is why this is an InvalidOperationException and
            // not a GException.
            nint data = AdapterNative.Map(adapter, size);

            if (data == nint.Zero)
            {
                throw new InvalidOperationException(
                    $"The adapter could not be mapped over {size} bytes: it holds fewer than that, or the " +
                    "size is zero. Ask Available() how many bytes there are before mapping.");
            }

            _owner = owner;
            _data = data;
            _size = size;
            _adapter = adapter;
        }

        /// <summary>
        /// Gets the number of bytes in the mapping, which is the size that was
        /// asked for.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// The mapping was released.
        /// </exception>
        public readonly nuint Size
        {
            get
            {
                ObjectDisposedException.ThrowIf(_adapter == nint.Zero, typeof(MapScope));
                return _size;
            }
        }

        /// <summary>
        /// Gets the mapped memory, which is read only.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// The mapping was released.
        /// </exception>
        /// <exception cref="OverflowException">
        /// The mapping is larger than <see cref="int.MaxValue"/> bytes and does
        /// not fit into a single span.
        /// </exception>
        public readonly ReadOnlySpan<byte> Span
        {
            get
            {
                ObjectDisposedException.ThrowIf(_adapter == nint.Zero, typeof(MapScope));
                return new ReadOnlySpan<byte>((void*)_data, checked((int)_size));
            }
        }

        /// <summary>
        /// Releases the mapping. Releasing it twice does nothing the second
        /// time, and every accessor throws from then on.
        /// </summary>
        public void Dispose()
        {
            nint adapter = _adapter;
            if (adapter == nint.Zero)
            {
                return;
            }

            _adapter = nint.Zero;

            // gst_adapter_unmap takes the adapter alone: the adapter itself
            // remembers which block it handed out.
            AdapterNative.Unmap(adapter);

            // The wrapper had to stay alive until the memory was given back.
            GC.KeepAlive(_owner);
        }
    }
}
