extern alias gstsharp;

using System.Globalization;
using System.Runtime.CompilerServices;
using Gst;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Compares the raw struct mirrors of the binding with the library that is
/// installed on this machine.
/// </summary>
/// <remarks>
/// <para>
/// The expected sizes and offsets are derived from the C headers of GStreamer
/// on a 64 bit platform, where a pointer, a <c>gsize</c> and a <c>GType</c> are
/// 8 bytes and an <c>int</c>, a <c>guint</c> and an enumeration are 4 bytes.
/// They are written out as constants on purpose: the C ABI is the ground truth,
/// so a mirror that drifts has to fail here rather than quietly agree with
/// itself.
/// </para>
/// <para>
/// The offsets that matter are also probed dynamically, against values that the
/// library itself wrote: the reference count that <c>gst_mini_object_ref</c>
/// moves, and the unset timestamps and offsets of a fresh buffer.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AbiProbeTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public AbiProbeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// <c>struct _GstMiniObject</c> of <c>gstminiobject.h</c>: <c>GType type</c>
    /// at 0 (8 bytes), <c>gint refcount</c> at 8, <c>gint lockstate</c> at 12,
    /// <c>guint flags</c> at 16, 4 bytes of padding, the three function
    /// pointers <c>copy</c>, <c>dispose</c> and <c>free</c> at 24, 32 and 40,
    /// <c>guint priv_uint</c> at 48, 4 bytes of padding and
    /// <c>gpointer priv_pointer</c> at 56, for 64 bytes in total.
    /// </summary>
    [Fact]
    public unsafe void MiniObjectRawMatchesTheHeaderLayout()
    {
        MiniObjectRaw raw = default;

        _output.WriteLine(Format("MiniObjectRaw", Unsafe.SizeOf<MiniObjectRaw>()));
        Assert.Equal(64, Unsafe.SizeOf<MiniObjectRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Type));
        Assert.Equal(8L, Offset(&raw, &raw.Refcount));
        Assert.Equal(12L, Offset(&raw, &raw.Lockstate));
        Assert.Equal(16L, Offset(&raw, &raw.Flags));
        Assert.Equal(24L, Offset(&raw, &raw.Copy));
        Assert.Equal(32L, Offset(&raw, &raw.Dispose));
        Assert.Equal(40L, Offset(&raw, &raw.Free));
        Assert.Equal(48L, Offset(&raw, &raw.PrivUint));
        Assert.Equal(56L, Offset(&raw, &raw.PrivPointer));
    }

    /// <summary>
    /// <c>struct _GstBuffer</c> of <c>gstbuffer.h</c>: the
    /// <c>GstMiniObject</c> header of 64 bytes, <c>GstBufferPool *pool</c> at
    /// 64 and the five 64 bit values <c>pts</c>, <c>dts</c>, <c>duration</c>,
    /// <c>offset</c> and <c>offset_end</c> at 72, 80, 88, 96 and 104, for 112
    /// bytes in total.
    /// </summary>
    [Fact]
    public unsafe void BufferRawMatchesTheHeaderLayout()
    {
        BufferRaw raw = default;

        _output.WriteLine(Format("BufferRaw", Unsafe.SizeOf<BufferRaw>()));
        Assert.Equal(112, Unsafe.SizeOf<BufferRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.MiniObject));
        Assert.Equal(64L, Offset(&raw, &raw.Pool));
        Assert.Equal(72L, Offset(&raw, &raw.Pts));
        Assert.Equal(80L, Offset(&raw, &raw.Dts));
        Assert.Equal(88L, Offset(&raw, &raw.Duration));
        Assert.Equal(96L, Offset(&raw, &raw.Offset));
        Assert.Equal(104L, Offset(&raw, &raw.OffsetEnd));
    }

    /// <summary>
    /// <c>struct _GstMapInfo</c> of <c>gstmemory.h</c>: <c>GstMemory *memory</c>
    /// at 0, <c>GstMapFlags flags</c> at 8, 4 bytes of padding,
    /// <c>guint8 *data</c> at 16, <c>gsize size</c> at 24, <c>gsize maxsize</c>
    /// at 32, <c>gpointer user_data[4]</c> at 40 and the four reserved pointers
    /// of <c>GST_PADDING</c> at 72, for 104 bytes in total.
    /// </summary>
    [Fact]
    public unsafe void MapInfoMatchesTheHeaderLayout()
    {
        MapInfo info = default;

        _output.WriteLine(Format("MapInfo", Unsafe.SizeOf<MapInfo>()));
        Assert.Equal(104, Unsafe.SizeOf<MapInfo>());

        Assert.Equal(0L, Offset(&info, &info.Memory));
        Assert.Equal(8L, Offset(&info, &info.Flags));
        Assert.Equal(16L, Offset(&info, &info.Data));
        Assert.Equal(24L, Offset(&info, &info.Size));
        Assert.Equal(32L, Offset(&info, &info.Maxsize));
        Assert.Equal(40L, Offset(&info, &info.UserData));
    }

    /// <summary>
    /// A fresh buffer has no timing and no offsets: the accessors have to read
    /// <c>GST_CLOCK_TIME_NONE</c> and <c>GST_BUFFER_OFFSET_NONE</c> back, both
    /// of which are the largest 64 bit value. Fields that sat anywhere else
    /// would read the null pool or the mini object header instead.
    /// </summary>
    [Fact]
    public void FreshBufferReadsAsUnsetThroughTheRawAccessors()
    {
        using Buffer buffer = NewBuffer();

        _output.WriteLine(FormattableString.Invariant(
            $"gst_buffer_new: pts={buffer.Pts.Nanoseconds:X} dts={buffer.Dts.Nanoseconds:X} duration={buffer.Duration.Nanoseconds:X}"));
        _output.WriteLine(FormattableString.Invariant(
            $"gst_buffer_new: offset={buffer.Offset:X} offsetEnd={buffer.OffsetEnd:X}"));

        Assert.Equal(ClockTime.None, buffer.Pts);
        Assert.Equal(ClockTime.None, buffer.Dts);
        Assert.Equal(ClockTime.None, buffer.Duration);
        Assert.Equal(ulong.MaxValue, buffer.Offset);
        Assert.Equal(ulong.MaxValue, buffer.OffsetEnd);
    }

    /// <summary>
    /// The reference count of the mini object header is where the mirror says
    /// it is: the library moves it, and the mirror reads the movement back.
    /// </summary>
    [Fact]
    public unsafe void MiniObjectRefcountFollowsTheNativeCalls()
    {
        nint handle = TestNatives.BufferNew();
        Assert.NotEqual(nint.Zero, handle);

        try
        {
            int fresh = ((MiniObjectRaw*)handle)->Refcount;
            GstNative.MiniObjectRef(handle);
            int referenced = ((MiniObjectRaw*)handle)->Refcount;
            GstNative.MiniObjectUnref(handle);
            int released = ((MiniObjectRaw*)handle)->Refcount;

            _output.WriteLine(FormattableString.Invariant(
                $"refcount: new={fresh}, after ref={referenced}, after unref={released}"));

            Assert.Equal(1, fresh);
            Assert.Equal(2, referenced);
            Assert.Equal(1, released);
        }
        finally
        {
            GstNative.MiniObjectUnref(handle);
        }
    }

    /// <summary>
    /// A wrapper owns exactly one reference: it takes its own when the handle
    /// is not transferred, and gives it back when it is disposed.
    /// </summary>
    [Fact]
    public unsafe void WrapperOwnsExactlyOneReference()
    {
        nint handle = TestNatives.BufferNew();
        Assert.NotEqual(nint.Zero, handle);

        try
        {
            using (Buffer buffer = Buffer.FromNative(handle, Transfer.None)!)
            {
                Assert.Equal(2, ((MiniObjectRaw*)handle)->Refcount);
                Assert.False(buffer.IsDisposed);
            }

            Assert.Equal(1, ((MiniObjectRaw*)handle)->Refcount);
        }
        finally
        {
            GstNative.MiniObjectUnref(handle);
        }
    }

    /// <summary>
    /// The binding is built against GStreamer 1.28, and the layouts these
    /// probes mirror have been stable since 1.24, which is the floor they are
    /// meaningful on.
    /// </summary>
    [Fact]
    public void NativeVersionIsSupported()
    {
        Gst.Version version = gstsharp::GstSharp.NativeVersion;

        _output.WriteLine(FormattableString.Invariant(
            $"native version: {version.Description} ({version.Major}.{version.Minor}.{version.Micro}.{version.Nano})"));

        Assert.Equal(1u, version.Major);
        Assert.True(
            version.Minor >= 24,
            FormattableString.Invariant($"GStreamer 1.24 or newer is required, but {version} is installed."));
    }

    /// <summary>
    /// Wraps a fresh buffer, failing the test rather than returning null.
    /// </summary>
    /// <returns>The wrapper of a new, empty buffer.</returns>
    internal static Buffer NewBuffer()
    {
        nint handle = TestNatives.BufferNew();
        Assert.NotEqual(nint.Zero, handle);

        Buffer? buffer = Buffer.FromNative(handle, Transfer.Full);
        Assert.NotNull(buffer);
        return buffer;
    }

    private static unsafe long Offset(void* start, void* field) => (byte*)field - (byte*)start;

    private static string Format(string name, int size) =>
        string.Create(CultureInfo.InvariantCulture, $"sizeof({name}) = {size}");
}
