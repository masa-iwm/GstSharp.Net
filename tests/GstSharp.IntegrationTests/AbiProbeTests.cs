extern alias gstsharp;

using System.Globalization;
using System.Runtime.CompilerServices;
using Gst;
using Gst.GObject;
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
    /// <c>struct _GTypeInfo</c> of <c>gtype.h</c>: <c>guint16 class_size</c> at
    /// 0 with 6 bytes of padding, the four function pointers <c>base_init</c>,
    /// <c>base_finalize</c>, <c>class_init</c> and <c>class_finalize</c> at 8,
    /// 16, 24 and 32, <c>gconstpointer class_data</c> at 40, the three
    /// <c>guint16</c> fields <c>instance_size</c> and <c>n_preallocs</c> at 48
    /// and 50 with 4 bytes of padding, <c>instance_init</c> at 56 and
    /// <c>value_table</c> at 64, for 72 bytes in total.
    /// <c>struct _GTypeQuery</c> is <c>GType</c>, <c>const gchar *</c> and two
    /// <c>guint</c>, for 24.
    /// </summary>
    [Fact]
    public unsafe void TheRegistrationStructsMatchTheHeaderLayout()
    {
        GTypeInfo info = default;
        GTypeQuery query = default;

        _output.WriteLine(Format("GTypeInfo", Unsafe.SizeOf<GTypeInfo>()));
        _output.WriteLine(Format("GTypeQuery", Unsafe.SizeOf<GTypeQuery>()));

        Assert.Equal(72, Unsafe.SizeOf<GTypeInfo>());
        Assert.Equal(0L, Offset(&info, &info.ClassSize));
        Assert.Equal(8L, Offset(&info, &info.BaseInit));
        Assert.Equal(16L, Offset(&info, &info.BaseFinalize));
        Assert.Equal(24L, Offset(&info, &info.ClassInit));
        Assert.Equal(32L, Offset(&info, &info.ClassFinalize));
        Assert.Equal(40L, Offset(&info, &info.ClassData));
        Assert.Equal(48L, Offset(&info, &info.InstanceSize));
        Assert.Equal(50L, Offset(&info, &info.NPreallocs));
        Assert.Equal(56L, Offset(&info, &info.InstanceInit));
        Assert.Equal(64L, Offset(&info, &info.ValueTable));

        Assert.Equal(24, Unsafe.SizeOf<GTypeQuery>());
        Assert.Equal(0L, Offset(&query, &query.Type));
        Assert.Equal(8L, Offset(&query, &query.TypeName));
        Assert.Equal(16L, Offset(&query, &query.ClassSize));
        Assert.Equal(20L, Offset(&query, &query.InstanceSize));
    }

    /// <summary>
    /// The class struct chain of <c>GstElement</c>:
    /// <c>GTypeClass</c> is one <c>GType</c>, so 8 bytes; <c>GObjectClass</c>
    /// adds <c>construct_properties</c>, eight slots, <c>flags</c>,
    /// <c>n_construct_properties</c>, <c>pspecs</c>, <c>n_pspecs</c> and
    /// <c>pdummy[3]</c>, for 136; <c>GstObjectClass</c> adds
    /// <c>path_string_separator</c>, <c>deep_notify</c> and
    /// <c>_gst_reserved[4]</c>, for 184; <c>GstElementClass</c> adds five data
    /// fields (the two <c>guint</c> sized ones pack into one word), sixteen
    /// slots and <c>_gst_reserved[18]</c>, for 488.
    /// </summary>
    [Fact]
    public unsafe void ElementClassRawMatchesTheHeaderLayout()
    {
        ElementClassRaw raw = default;

        _output.WriteLine(Format("GTypeClassRaw", Unsafe.SizeOf<GTypeClassRaw>()));
        _output.WriteLine(Format("GObjectClassRaw", Unsafe.SizeOf<GObjectClassRaw>()));
        _output.WriteLine(Format("GstObjectClassRaw", Unsafe.SizeOf<GstObjectClassRaw>()));
        _output.WriteLine(Format("ElementClassRaw", Unsafe.SizeOf<ElementClassRaw>()));

        Assert.Equal(8, Unsafe.SizeOf<GTypeClassRaw>());
        Assert.Equal(136, Unsafe.SizeOf<GObjectClassRaw>());
        Assert.Equal(184, Unsafe.SizeOf<GstObjectClassRaw>());
        Assert.Equal(488, Unsafe.SizeOf<ElementClassRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.ParentClass));
        Assert.Equal(184L, Offset(&raw, &raw.Metadata));
        Assert.Equal(192L, Offset(&raw, &raw.ElementFactory));
        Assert.Equal(200L, Offset(&raw, &raw.PadTemplates));
        Assert.Equal(208L, Offset(&raw, &raw.PadTemplateCount));
        Assert.Equal(212L, Offset(&raw, &raw.PadTemplateCookie));
        Assert.Equal(216L, Offset(&raw, &raw.PadAdded));
        Assert.Equal(224L, Offset(&raw, &raw.PadRemoved));
        Assert.Equal(232L, Offset(&raw, &raw.NoMorePads));
        Assert.Equal(240L, Offset(&raw, &raw.RequestNewPad));
        Assert.Equal(248L, Offset(&raw, &raw.ReleasePad));
        Assert.Equal(256L, Offset(&raw, &raw.GetState));
        Assert.Equal(264L, Offset(&raw, &raw.SetState));
        Assert.Equal(272L, Offset(&raw, &raw.ChangeState));
        Assert.Equal(280L, Offset(&raw, &raw.StateChanged));
        Assert.Equal(288L, Offset(&raw, &raw.SetBus));
        Assert.Equal(296L, Offset(&raw, &raw.ProvideClock));
        Assert.Equal(304L, Offset(&raw, &raw.SetClock));
        Assert.Equal(312L, Offset(&raw, &raw.SendEvent));
        Assert.Equal(320L, Offset(&raw, &raw.Query));
        Assert.Equal(328L, Offset(&raw, &raw.PostMessage));
        Assert.Equal(336L, Offset(&raw, &raw.SetContext));

        // The offset a subclass declares its override with is measured from the
        // mirror, so it can never drift from the fields asserted above.
        Assert.Equal(272, ElementClassRaw.ChangeStateOffset);
    }

    /// <summary>
    /// The library is the ground truth for the total size of a class struct:
    /// <c>g_type_query</c> reports what <c>g_type_register_static</c> would
    /// allocate, and the mirror has to agree with it exactly.
    /// </summary>
    [Fact]
    public void ElementClassSizeMatchesTheRunningLibrary()
    {
        GObjectNative.TypeQuery(Element.GetGType(), out GTypeQuery query);

        _output.WriteLine(FormattableString.Invariant(
            $"g_type_query(GstElement): class_size={query.ClassSize} instance_size={query.InstanceSize}"));

        Assert.Equal((uint)Unsafe.SizeOf<ElementClassRaw>(), query.ClassSize);
    }

    /// <summary>
    /// The <c>change_state</c> slot is where the mirror says it is, proven
    /// against a class the library filled in itself: <c>GstBin</c> overrides
    /// <c>change_state</c>, so its slot holds an address of its own, different
    /// from the one <c>GstElement</c> installs. A wrong offset reads a null or
    /// an unrelated field and fails one of the two.
    /// </summary>
    [Fact]
    public unsafe void TheChangeStateSlotHoldsWhatTheLibraryPutThere()
    {
        nint element = GObjectNative.TypeClassRef(Element.GetGType());
        nint bin = GObjectNative.TypeClassRef(Bin.GetGType());

        try
        {
            Assert.NotEqual(nint.Zero, element);
            Assert.NotEqual(nint.Zero, bin);

            nint elementSlot = ((ElementClassRaw*)element)->ChangeState;
            nint binSlot = ((ElementClassRaw*)bin)->ChangeState;

            _output.WriteLine(FormattableString.Invariant(
                $"change_state: GstElement=0x{elementSlot:x} GstBin=0x{binSlot:x}"));

            Assert.NotEqual(nint.Zero, elementSlot);
            Assert.NotEqual(nint.Zero, binSlot);
            Assert.NotEqual(elementSlot, binSlot);
        }
        finally
        {
            GObjectNative.TypeClassUnref(bin);
            GObjectNative.TypeClassUnref(element);
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
