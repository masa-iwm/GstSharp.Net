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
    /// <c>struct _GstSegment</c> of <c>gstsegment.h</c>:
    /// <c>GstSegmentFlags flags</c> at 0 with 4 bytes of padding behind it,
    /// <c>gdouble rate</c> at 8, <c>gdouble applied_rate</c> at 16,
    /// <c>GstFormat format</c> at 24 with 4 more bytes of padding, the seven
    /// <c>guint64</c> values <c>base</c>, <c>offset</c>, <c>start</c>,
    /// <c>stop</c>, <c>time</c>, <c>position</c> and <c>duration</c> at 32 to
    /// 80, and <c>gpointer _gst_reserved[GST_PADDING]</c> at 88, for 120 bytes
    /// in total.
    /// </summary>
    [Fact]
    public unsafe void SegmentRawMatchesTheHeaderLayout()
    {
        SegmentRaw raw = default;

        _output.WriteLine(Format("SegmentRaw", Unsafe.SizeOf<SegmentRaw>()));
        Assert.Equal(120, Unsafe.SizeOf<SegmentRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Flags));
        Assert.Equal(8L, Offset(&raw, &raw.Rate));
        Assert.Equal(16L, Offset(&raw, &raw.AppliedRate));
        Assert.Equal(24L, Offset(&raw, &raw.Format));
        Assert.Equal(32L, Offset(&raw, &raw.Base));
        Assert.Equal(40L, Offset(&raw, &raw.Offset));
        Assert.Equal(48L, Offset(&raw, &raw.Start));
        Assert.Equal(56L, Offset(&raw, &raw.Stop));
        Assert.Equal(64L, Offset(&raw, &raw.Time));
        Assert.Equal(72L, Offset(&raw, &raw.Position));
        Assert.Equal(80L, Offset(&raw, &raw.Duration));
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

        Assert.Equal(0L, Offset(&info, &info.MemoryPtr));
        Assert.Equal(8L, Offset(&info, &info.Flags));
        Assert.Equal(16L, Offset(&info, &info.DataPtr));
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
    /// <c>struct _GstBinClass</c> of <c>gstbin.h</c>: the 488 bytes of
    /// <c>GstElementClass</c>, the data field <c>pool</c> at 488, the eight
    /// slots <c>element_added</c> … <c>deep_element_removed</c> at 496 to 552,
    /// and <c>_gst_reserved[GST_PADDING - 2]</c> at 560, for 576 bytes in
    /// total. Two of the padding pointers were spent on the two
    /// <c>deep_</c> slots.
    /// </summary>
    [Fact]
    public unsafe void BinClassRawMatchesTheHeaderLayout()
    {
        BinClassRaw raw = default;

        _output.WriteLine(Format("BinClassRaw", Unsafe.SizeOf<BinClassRaw>()));
        Assert.Equal(576, Unsafe.SizeOf<BinClassRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.ParentClass));
        Assert.Equal(488L, Offset(&raw, &raw.Pool));
        Assert.Equal(496L, Offset(&raw, &raw.ElementAdded));
        Assert.Equal(504L, Offset(&raw, &raw.ElementRemoved));
        Assert.Equal(512L, Offset(&raw, &raw.AddElement));
        Assert.Equal(520L, Offset(&raw, &raw.RemoveElement));
        Assert.Equal(528L, Offset(&raw, &raw.HandleMessage));
        Assert.Equal(536L, Offset(&raw, &raw.DoLatency));
        Assert.Equal(544L, Offset(&raw, &raw.DeepElementAdded));
        Assert.Equal(552L, Offset(&raw, &raw.DeepElementRemoved));

        Assert.Equal(528, BinClassRaw.HandleMessageOffset);
    }

    /// <summary>
    /// <c>struct _GstBaseSrcClass</c> and <c>struct _GstPushSrcClass</c> of
    /// <c>gstbasesrc.h</c> and <c>gstpushsrc.h</c>: 488 bytes of
    /// <c>GstElementClass</c>, nineteen slots from <c>get_caps</c> to
    /// <c>fill</c> at 488 to 632 and a full <c>GST_PADDING_LARGE</c> at 640,
    /// for 800; the push source adds its three slots at 800 and a plain
    /// <c>GST_PADDING</c>, for 856.
    /// </summary>
    [Fact]
    public unsafe void TheSourceClassStructsMatchTheHeaderLayout()
    {
        Gst.Base.BaseSrcClassRaw source = default;
        Gst.Base.PushSrcClassRaw push = default;

        _output.WriteLine(Format("BaseSrcClassRaw", Unsafe.SizeOf<Gst.Base.BaseSrcClassRaw>()));
        _output.WriteLine(Format("PushSrcClassRaw", Unsafe.SizeOf<Gst.Base.PushSrcClassRaw>()));

        Assert.Equal(800, Unsafe.SizeOf<Gst.Base.BaseSrcClassRaw>());
        Assert.Equal(856, Unsafe.SizeOf<Gst.Base.PushSrcClassRaw>());

        Assert.Equal(488L, Offset(&source, &source.GetCaps));
        Assert.Equal(512L, Offset(&source, &source.SetCaps));
        Assert.Equal(528L, Offset(&source, &source.Start));
        Assert.Equal(536L, Offset(&source, &source.Stop));
        Assert.Equal(560L, Offset(&source, &source.IsSeekable));
        Assert.Equal(616L, Offset(&source, &source.Create));
        Assert.Equal(632L, Offset(&source, &source.Fill));

        Assert.Equal(800L, Offset(&push, &push.Create));
        Assert.Equal(808L, Offset(&push, &push.Alloc));
        Assert.Equal(816L, Offset(&push, &push.Fill));

        // The offsets the subclass surface declares its overrides with are
        // measured from the mirrors, so they cannot drift from the fields.
        Assert.Equal(512, Gst.Base.BaseSrcClassRaw.SetCapsOffset);
        Assert.Equal(528, Gst.Base.BaseSrcClassRaw.StartOffset);
        Assert.Equal(536, Gst.Base.BaseSrcClassRaw.StopOffset);
        Assert.Equal(560, Gst.Base.BaseSrcClassRaw.IsSeekableOffset);
        Assert.Equal(800, Gst.Base.PushSrcClassRaw.CreateOffset);
    }

    /// <summary>
    /// <c>struct _GstBaseSinkClass</c> of <c>gstbasesink.h</c>: 488 bytes of
    /// <c>GstElementClass</c>, eighteen slots from <c>get_caps</c> to
    /// <c>render_list</c> at 488 to 624 and a full <c>GST_PADDING_LARGE</c>,
    /// for 792.
    /// </summary>
    [Fact]
    public unsafe void TheSinkClassStructMatchesTheHeaderLayout()
    {
        Gst.Base.BaseSinkClassRaw raw = default;

        _output.WriteLine(Format("BaseSinkClassRaw", Unsafe.SizeOf<Gst.Base.BaseSinkClassRaw>()));
        Assert.Equal(792, Unsafe.SizeOf<Gst.Base.BaseSinkClassRaw>());

        Assert.Equal(488L, Offset(&raw, &raw.GetCaps));
        Assert.Equal(496L, Offset(&raw, &raw.SetCaps));
        Assert.Equal(536L, Offset(&raw, &raw.Start));
        Assert.Equal(544L, Offset(&raw, &raw.Stop));
        Assert.Equal(608L, Offset(&raw, &raw.Preroll));
        Assert.Equal(616L, Offset(&raw, &raw.Render));
        Assert.Equal(624L, Offset(&raw, &raw.RenderList));

        Assert.Equal(496, Gst.Base.BaseSinkClassRaw.SetCapsOffset);
        Assert.Equal(536, Gst.Base.BaseSinkClassRaw.StartOffset);
        Assert.Equal(544, Gst.Base.BaseSinkClassRaw.StopOffset);
        Assert.Equal(608, Gst.Base.BaseSinkClassRaw.PrerollOffset);
        Assert.Equal(616, Gst.Base.BaseSinkClassRaw.RenderOffset);
    }

    /// <summary>
    /// <c>struct _GstBaseTransformClass</c> of <c>gstbasetransform.h</c>: 488
    /// bytes of <c>GstElementClass</c>, then <b>two <c>gboolean</c> data
    /// fields</b> at 488 and 492 — which is why a class struct is not a vfunc
    /// table — then twenty two slots from <c>transform_caps</c> at 496 to
    /// <c>generate_output</c> at 664, and <c>GST_PADDING_LARGE - 2</c> at 672,
    /// for 816.
    /// </summary>
    [Fact]
    public unsafe void TheTransformClassStructMatchesTheHeaderLayout()
    {
        Gst.Base.BaseTransformClassRaw raw = default;

        _output.WriteLine(Format("BaseTransformClassRaw", Unsafe.SizeOf<Gst.Base.BaseTransformClassRaw>()));
        Assert.Equal(816, Unsafe.SizeOf<Gst.Base.BaseTransformClassRaw>());

        Assert.Equal(488L, Offset(&raw, &raw.PassthroughOnSameCaps));
        Assert.Equal(492L, Offset(&raw, &raw.TransformIpOnPassthrough));
        Assert.Equal(496L, Offset(&raw, &raw.TransformCaps));
        Assert.Equal(520L, Offset(&raw, &raw.SetCaps));
        Assert.Equal(576L, Offset(&raw, &raw.Start));
        Assert.Equal(584L, Offset(&raw, &raw.Stop));
        Assert.Equal(640L, Offset(&raw, &raw.Transform));
        Assert.Equal(648L, Offset(&raw, &raw.TransformIp));
        Assert.Equal(664L, Offset(&raw, &raw.GenerateOutput));

        Assert.Equal(520, Gst.Base.BaseTransformClassRaw.SetCapsOffset);
        Assert.Equal(576, Gst.Base.BaseTransformClassRaw.StartOffset);
        Assert.Equal(584, Gst.Base.BaseTransformClassRaw.StopOffset);
        Assert.Equal(648, Gst.Base.BaseTransformClassRaw.TransformIpOffset);
    }

    /// <summary>
    /// The library is the ground truth for the total size of every class struct
    /// a managed subclass derives from: <c>g_type_query</c> reports what
    /// <c>g_type_register_static</c> allocates, and each mirror has to agree
    /// with it exactly. A mirror that spelled the two <c>gboolean</c> data
    /// fields of <c>GstBaseTransformClass</c> as pointers would be caught here
    /// and nowhere else.
    /// </summary>
    [Fact]
    public void TheSubclassableClassSizesMatchTheRunningLibrary()
    {
        AssertClassSize("GstBin", Unsafe.SizeOf<BinClassRaw>(), Bin.GetGType());
        AssertClassSize("GstBaseSrc", Unsafe.SizeOf<Gst.Base.BaseSrcClassRaw>(), Gst.Base.BaseSrc.GetGType());
        AssertClassSize("GstPushSrc", Unsafe.SizeOf<Gst.Base.PushSrcClassRaw>(), Gst.Base.PushSrc.GetGType());
        AssertClassSize("GstBaseSink", Unsafe.SizeOf<Gst.Base.BaseSinkClassRaw>(), Gst.Base.BaseSink.GetGType());
        AssertClassSize(
            "GstBaseTransform",
            Unsafe.SizeOf<Gst.Base.BaseTransformClassRaw>(),
            Gst.Base.BaseTransform.GetGType());
    }

    /// <summary>
    /// The <c>handle_message</c> slot is where the mirror says it is, proven
    /// against two classes the library filled in itself: <c>GstPipeline</c>
    /// overrides it and <c>GstBin</c> installs its own, so the two hold
    /// different, non null addresses.
    /// </summary>
    [Fact]
    public unsafe void TheHandleMessageSlotHoldsWhatTheLibraryPutThere()
    {
        nint bin = GObjectNative.TypeClassRef(Bin.GetGType());
        nint pipeline = GObjectNative.TypeClassRef(Pipeline.GetGType());

        try
        {
            nint binSlot = ((BinClassRaw*)bin)->HandleMessage;
            nint pipelineSlot = ((BinClassRaw*)pipeline)->HandleMessage;

            _output.WriteLine(FormattableString.Invariant(
                $"handle_message: GstBin=0x{binSlot:x} GstPipeline=0x{pipelineSlot:x}"));

            Assert.NotEqual(nint.Zero, binSlot);
            Assert.NotEqual(nint.Zero, pipelineSlot);
            Assert.NotEqual(binSlot, pipelineSlot);
        }
        finally
        {
            GObjectNative.TypeClassUnref(pipeline);
            GObjectNative.TypeClassUnref(bin);
        }
    }

    /// <summary>
    /// The lifecycle slots of <c>GstBaseSrcClass</c> are where the mirror says
    /// they are: <c>GstBaseSrc</c> installs none of them, and <c>filesrc</c>
    /// installs all three. A wrong offset reads a slot the base class did fill
    /// in, or a data field, and fails one of the two halves.
    /// </summary>
    [Fact]
    public unsafe void TheBaseSrcLifecycleSlotsHoldWhatTheLibraryPutThere()
    {
        using Element source = ElementFactory.Make("filesrc", null)
            ?? throw new InvalidOperationException("filesrc is part of coreelements and has to exist.");

        Gst.Base.BaseSrcClassRaw* concrete = (Gst.Base.BaseSrcClassRaw*)ClassOf(source);
        nint abstractClass = GObjectNative.TypeClassRef(Gst.Base.BaseSrc.GetGType());

        try
        {
            Gst.Base.BaseSrcClassRaw* baseSrc = (Gst.Base.BaseSrcClassRaw*)abstractClass;

            _output.WriteLine(FormattableString.Invariant(
                $"filesrc: start=0x{concrete->Start:x} stop=0x{concrete->Stop:x} is_seekable=0x{concrete->IsSeekable:x}"));
            _output.WriteLine(FormattableString.Invariant(
                $"GstBaseSrc: create=0x{baseSrc->Create:x} fill=0x{baseSrc->Fill:x}"));

            Assert.NotEqual(nint.Zero, concrete->Start);
            Assert.NotEqual(nint.Zero, concrete->Stop);
            Assert.NotEqual(nint.Zero, concrete->IsSeekable);

            // GstBaseSrc leaves these to its subclasses, which is exactly why
            // the chain-up helpers document a default for a null slot.
            Assert.Equal(nint.Zero, baseSrc->Start);
            Assert.Equal(nint.Zero, baseSrc->Stop);
            Assert.Equal(nint.Zero, baseSrc->IsSeekable);

            // And it does install the one that produces data.
            Assert.NotEqual(nint.Zero, baseSrc->Create);
        }
        finally
        {
            GObjectNative.TypeClassUnref(abstractClass);
        }
    }

    /// <summary>
    /// The <c>create</c> slot of <c>GstPushSrcClass</c> — the one a managed
    /// push source lives in — is where the mirror says it is: <c>GstPushSrc</c>
    /// leaves it null and <c>fdsrc</c> fills it in.
    /// </summary>
    [RequiresElementFact("fdsrc")]
    public unsafe void ThePushSrcCreateSlotHoldsWhatTheLibraryPutThere()
    {
        using Element source = ElementFactory.Make("fdsrc", null)
            ?? throw new InvalidOperationException("The fact is gated on fdsrc being installed.");

        Gst.Base.PushSrcClassRaw* concrete = (Gst.Base.PushSrcClassRaw*)ClassOf(source);
        nint abstractClass = GObjectNative.TypeClassRef(Gst.Base.PushSrc.GetGType());

        try
        {
            Gst.Base.PushSrcClassRaw* pushSrc = (Gst.Base.PushSrcClassRaw*)abstractClass;

            _output.WriteLine(FormattableString.Invariant(
                $"create: fdsrc=0x{concrete->Create:x} GstPushSrc=0x{pushSrc->Create:x}"));

            Assert.NotEqual(nint.Zero, concrete->Create);
            Assert.Equal(nint.Zero, pushSrc->Create);

            // GstPushSrc's own contribution is one level up: it installs the
            // GstBaseSrc create that dispatches to the three slots above.
            Assert.NotEqual(nint.Zero, pushSrc->ParentClass.Create);
            Assert.Equal(pushSrc->ParentClass.Create, concrete->ParentClass.Create);
        }
        finally
        {
            GObjectNative.TypeClassUnref(abstractClass);
        }
    }

    /// <summary>
    /// The two slots a managed sink lives in are where the mirror says they
    /// are: <c>GstBaseSink</c> installs neither <c>preroll</c> nor
    /// <c>render</c>, and <c>fakesink</c> installs both.
    /// </summary>
    [Fact]
    public unsafe void TheBaseSinkRenderSlotsHoldWhatTheLibraryPutThere()
    {
        using Element sink = ElementFactory.Make("fakesink", null)
            ?? throw new InvalidOperationException("fakesink is part of coreelements and has to exist.");

        Gst.Base.BaseSinkClassRaw* concrete = (Gst.Base.BaseSinkClassRaw*)ClassOf(sink);
        nint abstractClass = GObjectNative.TypeClassRef(Gst.Base.BaseSink.GetGType());

        try
        {
            Gst.Base.BaseSinkClassRaw* baseSink = (Gst.Base.BaseSinkClassRaw*)abstractClass;

            _output.WriteLine(FormattableString.Invariant(
                $"fakesink: preroll=0x{concrete->Preroll:x} render=0x{concrete->Render:x}"));

            Assert.NotEqual(nint.Zero, concrete->Preroll);
            Assert.NotEqual(nint.Zero, concrete->Render);
            Assert.Equal(nint.Zero, baseSink->Preroll);
            Assert.Equal(nint.Zero, baseSink->Render);

            // set_caps sits one word behind get_caps and the base class does
            // install it, which pins the front of the struct as well.
            Assert.NotEqual(nint.Zero, baseSink->SetCaps);
        }
        finally
        {
            GObjectNative.TypeClassUnref(abstractClass);
        }
    }

    /// <summary>
    /// The slots and the two data fields of <c>GstBaseTransformClass</c> hold
    /// what the library put there: <c>identity</c> fills in
    /// <c>transform_ip</c>, <c>start</c> and <c>stop</c>, and the two
    /// <c>gboolean</c> fields in front of the slots read back as the defaults
    /// <c>GstBaseTransform</c> sets — <c>FALSE</c> and <c>TRUE</c>. Mirroring
    /// them as anything but two 32 bit integers would read a pointer here and
    /// move every slot behind them.
    /// </summary>
    [Fact]
    public unsafe void TheBaseTransformSlotsAndDataFieldsHoldWhatTheLibraryPutThere()
    {
        using Element filter = ElementFactory.Make("identity", null)
            ?? throw new InvalidOperationException("identity is part of coreelements and has to exist.");

        Gst.Base.BaseTransformClassRaw* concrete = (Gst.Base.BaseTransformClassRaw*)ClassOf(filter);
        nint abstractClass = GObjectNative.TypeClassRef(Gst.Base.BaseTransform.GetGType());

        try
        {
            Gst.Base.BaseTransformClassRaw* baseTransform = (Gst.Base.BaseTransformClassRaw*)abstractClass;

            _output.WriteLine(FormattableString.Invariant(
                $"identity: transform_ip=0x{concrete->TransformIp:x} transform=0x{concrete->Transform:x}"));
            _output.WriteLine(FormattableString.Invariant(
                $"identity: passthrough_on_same_caps={concrete->PassthroughOnSameCaps}"));
            _output.WriteLine(FormattableString.Invariant(
                $"identity: transform_ip_on_passthrough={concrete->TransformIpOnPassthrough}"));

            Assert.NotEqual(nint.Zero, concrete->TransformIp);
            Assert.NotEqual(nint.Zero, concrete->Start);
            Assert.NotEqual(nint.Zero, concrete->Stop);
            Assert.Equal(nint.Zero, baseTransform->TransformIp);

            // The defaults of gst_base_transform_class_init, inherited by
            // identity: it does not force passthrough on equal caps, and it
            // does want transform_ip to run while passing through.
            Assert.Equal(0, concrete->PassthroughOnSameCaps);
            Assert.Equal(1, concrete->TransformIpOnPassthrough);
            Assert.Equal(0, baseTransform->PassthroughOnSameCaps);
            Assert.Equal(1, baseTransform->TransformIpOnPassthrough);
        }
        finally
        {
            GObjectNative.TypeClassUnref(abstractClass);
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
    /// <c>struct _GstAllocationParams</c> of <c>gstallocator.h</c>:
    /// <c>GstMemoryFlags flags</c> at 0 with 4 bytes of padding behind it, the
    /// three <c>gsize</c> values <c>align</c>, <c>prefix</c> and
    /// <c>padding</c> at 8, 16 and 24, and <c>GST_PADDING</c> at 32, for 64
    /// bytes in total.
    /// </summary>
    [Fact]
    public unsafe void AllocationParamsRawMatchesTheHeaderLayout()
    {
        AllocationParamsRaw raw = default;

        _output.WriteLine(Format("AllocationParamsRaw", Unsafe.SizeOf<AllocationParamsRaw>()));
        Assert.Equal(64, Unsafe.SizeOf<AllocationParamsRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Flags));
        Assert.Equal(8L, Offset(&raw, &raw.Align));
        Assert.Equal(16L, Offset(&raw, &raw.Prefix));
        Assert.Equal(24L, Offset(&raw, &raw.Padding));
    }

    /// <summary>
    /// <c>struct _GstStructure</c> of <c>gststructure.h</c>: <c>GType type</c>
    /// at 0 and <c>GQuark name</c> at 8, which the alignment of the first pads
    /// out to 16.
    /// </summary>
    [Fact]
    public unsafe void StructureRawMatchesTheHeaderLayout()
    {
        StructureRaw raw = default;

        _output.WriteLine(Format("StructureRaw", Unsafe.SizeOf<StructureRaw>()));
        Assert.Equal(16, Unsafe.SizeOf<StructureRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Type));
        Assert.Equal(8L, Offset(&raw, &raw.Name));
    }

    /// <summary>
    /// <c>struct _GstIterator</c> of <c>gstiterator.h</c>: the five function
    /// pointers <c>copy</c> … <c>free</c> at 0 to 32, <c>GstIterator *pushed</c>
    /// at 40, <c>GType type</c> at 48, <c>GMutex *lock</c> at 56,
    /// <c>guint32 cookie</c> at 64, <c>guint32 *master_cookie</c> at 72,
    /// <c>guint size</c> at 80 and <c>GST_PADDING</c> at 88, for 120 bytes.
    /// </summary>
    [Fact]
    public unsafe void IteratorRawMatchesTheHeaderLayout()
    {
        IteratorRaw raw = default;

        _output.WriteLine(Format("IteratorRaw", Unsafe.SizeOf<IteratorRaw>()));
        Assert.Equal(120, Unsafe.SizeOf<IteratorRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Copy));
        Assert.Equal(40L, Offset(&raw, &raw.Pushed));
        Assert.Equal(48L, Offset(&raw, &raw.Type));
        Assert.Equal(56L, Offset(&raw, &raw.Lock));
        Assert.Equal(64L, Offset(&raw, &raw.Cookie));
        Assert.Equal(72L, Offset(&raw, &raw.MasterCookie));
        Assert.Equal(80L, Offset(&raw, &raw.Size));
    }

    /// <summary>
    /// <c>struct _GstMetaInfo</c> of <c>gstmeta.h</c>: <c>GType api</c>,
    /// <c>GType type</c> and <c>gsize size</c> at 0, 8 and 16, then the six
    /// function pointers <c>init_func</c> … <c>clear_func</c> at 24 to 64, for
    /// 72 bytes. The structure carries no padding pointers: GStreamer always
    /// allocates it itself, so it is extended in place.
    /// </summary>
    [Fact]
    public unsafe void MetaInfoRawMatchesTheHeaderLayout()
    {
        MetaInfoRaw raw = default;

        _output.WriteLine(Format("MetaInfoRaw", Unsafe.SizeOf<MetaInfoRaw>()));
        Assert.Equal(72, Unsafe.SizeOf<MetaInfoRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Api));
        Assert.Equal(8L, Offset(&raw, &raw.Type));
        Assert.Equal(16L, Offset(&raw, &raw.Size));
        Assert.Equal(24L, Offset(&raw, &raw.InitFunc));
        Assert.Equal(32L, Offset(&raw, &raw.FreeFunc));
        Assert.Equal(40L, Offset(&raw, &raw.TransformFunc));
        Assert.Equal(48L, Offset(&raw, &raw.SerializeFunc));
        Assert.Equal(56L, Offset(&raw, &raw.DeserializeFunc));
        Assert.Equal(64L, Offset(&raw, &raw.ClearFunc));
    }

    /// <summary>
    /// <c>struct _GstMeta</c> of <c>gstmeta.h</c>: <c>GstMetaFlags flags</c> at
    /// 0 and <c>const GstMetaInfo *info</c> at 8, for 16 bytes. This is the
    /// header every <c>*Meta</c> record of the girs embeds by value, so a
    /// wrong size here moves every field of all twenty two of them.
    /// </summary>
    [Fact]
    public unsafe void MetaRawMatchesTheHeaderLayout()
    {
        MetaRaw raw = default;

        _output.WriteLine(Format("MetaRaw", Unsafe.SizeOf<MetaRaw>()));
        Assert.Equal(16, Unsafe.SizeOf<MetaRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Flags));
        Assert.Equal(8L, Offset(&raw, &raw.Info));
    }

    /// <summary>
    /// <c>struct _GstPadProbeInfo</c> of <c>gstpad.h</c>:
    /// <c>GstPadProbeType type</c> at 0, <c>gulong id</c>, <c>gpointer data</c>,
    /// <c>guint64 offset</c>, <c>guint size</c> and the <c>ABI</c> union the
    /// mirror stops in front of. <b>A <c>gulong</c> is four bytes on Windows
    /// and eight everywhere else</b>, which moves everything behind it: the
    /// union sits at 32 on Windows and at 40 on the other platforms, and that
    /// is what the prefix mirror is as large as.
    /// </summary>
    [Fact]
    public unsafe void PadProbeInfoRawMatchesTheHeaderLayout()
    {
        PadProbeInfoRaw raw = default;
        bool windows = OperatingSystem.IsWindows();

        _output.WriteLine(Format("PadProbeInfoRaw", Unsafe.SizeOf<PadProbeInfoRaw>()));
        Assert.Equal(windows ? 32 : 40, Unsafe.SizeOf<PadProbeInfoRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Type));
        Assert.Equal(windows ? 4L : 8L, Offset(&raw, &raw.Id));
        Assert.Equal(windows ? 8L : 16L, Offset(&raw, &raw.Data));
        Assert.Equal(windows ? 16L : 24L, Offset(&raw, &raw.Offset));
        Assert.Equal(windows ? 24L : 32L, Offset(&raw, &raw.Size));
    }

    /// <summary>
    /// <c>struct _GstStaticPadTemplate</c> of <c>gstpadtemplate.h</c>:
    /// <c>const gchar *name_template</c> at 0, <c>GstPadDirection direction</c>
    /// at 8, <c>GstPadPresence presence</c> at 12 and the
    /// <c>GstStaticCaps static_caps</c> it embeds by value at 16. A
    /// <c>GstStaticCaps</c> is a <c>GstCaps *</c>, a <c>const char *</c> and
    /// <c>GST_PADDING</c>, for 48, which makes the template 64.
    /// </summary>
    [Fact]
    public unsafe void StaticPadTemplateRawMatchesTheHeaderLayout()
    {
        StaticPadTemplateRaw raw = default;
        StaticCapsRaw caps = default;

        _output.WriteLine(Format("StaticPadTemplateRaw", Unsafe.SizeOf<StaticPadTemplateRaw>()));
        _output.WriteLine(Format("StaticCapsRaw", Unsafe.SizeOf<StaticCapsRaw>()));

        Assert.Equal(64, Unsafe.SizeOf<StaticPadTemplateRaw>());
        Assert.Equal(48, Unsafe.SizeOf<StaticCapsRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.NameTemplate));
        Assert.Equal(8L, Offset(&raw, &raw.Direction));
        Assert.Equal(12L, Offset(&raw, &raw.Presence));
        Assert.Equal(16L, Offset(&raw, &raw.StaticCaps));

        Assert.Equal(0L, Offset(&caps, &caps.Caps));
        Assert.Equal(8L, Offset(&caps, &caps.String));
    }

    /// <summary>
    /// <c>GstReferenceTimestampMeta</c> of <c>gstbuffer.h</c>: the 16 byte
    /// <c>GstMeta</c> header, <c>GstCaps *reference</c> at 16, the two
    /// <c>GstClockTime</c> values <c>timestamp</c> and <c>duration</c> at 24
    /// and 32 and <c>GstStructure *info</c> at 40, for 48 bytes.
    /// </summary>
    [Fact]
    public unsafe void ReferenceTimestampMetaRawMatchesTheHeaderLayout()
    {
        ReferenceTimestampMetaRaw raw = default;

        _output.WriteLine(Format("ReferenceTimestampMetaRaw", Unsafe.SizeOf<ReferenceTimestampMetaRaw>()));
        Assert.Equal(48, Unsafe.SizeOf<ReferenceTimestampMetaRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Parent));
        Assert.Equal(16L, Offset(&raw, &raw.Reference));
        Assert.Equal(24L, Offset(&raw, &raw.Timestamp));
        Assert.Equal(32L, Offset(&raw, &raw.Duration));
        Assert.Equal(40L, Offset(&raw, &raw.Info));
    }

    /// <summary>
    /// <c>GstVideoCropMeta</c> of <c>gstvideometa.h</c>: the 16 byte
    /// <c>GstMeta</c> header and the four <c>guint</c> values <c>x</c>,
    /// <c>y</c>, <c>width</c> and <c>height</c> at 16, 20, 24 and 28, for 32
    /// bytes.
    /// </summary>
    [Fact]
    public unsafe void VideoCropMetaRawMatchesTheHeaderLayout()
    {
        Gst.Video.VideoCropMetaRaw raw = default;

        _output.WriteLine(Format("VideoCropMetaRaw", Unsafe.SizeOf<Gst.Video.VideoCropMetaRaw>()));
        Assert.Equal(32, Unsafe.SizeOf<Gst.Video.VideoCropMetaRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Meta));
        Assert.Equal(16L, Offset(&raw, &raw.X));
        Assert.Equal(20L, Offset(&raw, &raw.Y));
        Assert.Equal(24L, Offset(&raw, &raw.Width));
        Assert.Equal(28L, Offset(&raw, &raw.Height));
    }

    /// <summary>
    /// <c>GstVideoRegionOfInterestMeta</c> of <c>gstvideometa.h</c>: the 16
    /// byte header, <c>GQuark roi_type</c> at 16, <c>gint id</c> and
    /// <c>gint parent_id</c> at 20 and 24, the four <c>guint</c> values
    /// <c>x</c>, <c>y</c>, <c>w</c> and <c>h</c> at 28 to 40, and
    /// <c>GList *params</c> at 48, for 56 bytes.
    /// </summary>
    [Fact]
    public unsafe void VideoRegionOfInterestMetaRawMatchesTheHeaderLayout()
    {
        Gst.Video.VideoRegionOfInterestMetaRaw raw = default;

        _output.WriteLine(Format(
            "VideoRegionOfInterestMetaRaw",
            Unsafe.SizeOf<Gst.Video.VideoRegionOfInterestMetaRaw>()));
        Assert.Equal(56, Unsafe.SizeOf<Gst.Video.VideoRegionOfInterestMetaRaw>());

        Assert.Equal(16L, Offset(&raw, &raw.RoiType));
        Assert.Equal(20L, Offset(&raw, &raw.Id));
        Assert.Equal(24L, Offset(&raw, &raw.ParentId));
        Assert.Equal(28L, Offset(&raw, &raw.X));
        Assert.Equal(32L, Offset(&raw, &raw.Y));
        Assert.Equal(36L, Offset(&raw, &raw.W));
        Assert.Equal(40L, Offset(&raw, &raw.H));
        Assert.Equal(48L, Offset(&raw, &raw.Params));
    }

    /// <summary>
    /// <c>GstVideoMeta</c> of <c>gstvideometa.h</c>: the 16 byte header,
    /// <c>GstBuffer *buffer</c> at 16, <c>flags</c> and <c>format</c> at 24 and
    /// 28, <c>gint id</c> at 32, <c>width</c>, <c>height</c> and
    /// <c>n_planes</c> at 36, 40 and 44, <c>gsize offset[4]</c> at 48,
    /// <c>gint stride[4]</c> at 80, the <c>map</c> and <c>unmap</c> slots at 96
    /// and 104 and the 32 byte <c>GstVideoAlignment alignment</c> at 112, for
    /// 144 bytes.
    /// </summary>
    [Fact]
    public unsafe void VideoMetaRawMatchesTheHeaderLayout()
    {
        Gst.Video.VideoMetaRaw raw = default;

        _output.WriteLine(Format("VideoMetaRaw", Unsafe.SizeOf<Gst.Video.VideoMetaRaw>()));
        Assert.Equal(144, Unsafe.SizeOf<Gst.Video.VideoMetaRaw>());
        Assert.Equal(32, Unsafe.SizeOf<Gst.Video.VideoAlignment>());

        Assert.Equal(16L, Offset(&raw, &raw.Buffer));
        Assert.Equal(24L, Offset(&raw, &raw.Flags));
        Assert.Equal(28L, Offset(&raw, &raw.Format));
        Assert.Equal(32L, Offset(&raw, &raw.Id));
        Assert.Equal(36L, Offset(&raw, &raw.Width));
        Assert.Equal(40L, Offset(&raw, &raw.Height));
        Assert.Equal(44L, Offset(&raw, &raw.NPlanes));
        Assert.Equal(48L, Offset(&raw, &raw.Offset));
        Assert.Equal(80L, Offset(&raw, &raw.Stride));
        Assert.Equal(96L, Offset(&raw, &raw.Map));
        Assert.Equal(104L, Offset(&raw, &raw.Unmap));
        Assert.Equal(112L, Offset(&raw, &raw.Alignment));
    }

    /// <summary>
    /// <c>GstAudioClippingMeta</c> of <c>gstaudiometa.h</c>: the 16 byte
    /// header, <c>GstFormat format</c> at 16 with 4 bytes of padding behind it
    /// and the two <c>guint64</c> values <c>start</c> and <c>end</c> at 24 and
    /// 32, for 40 bytes.
    /// </summary>
    [Fact]
    public unsafe void AudioClippingMetaRawMatchesTheHeaderLayout()
    {
        Gst.Audio.AudioClippingMetaRaw raw = default;

        _output.WriteLine(Format("AudioClippingMetaRaw", Unsafe.SizeOf<Gst.Audio.AudioClippingMetaRaw>()));
        Assert.Equal(40, Unsafe.SizeOf<Gst.Audio.AudioClippingMetaRaw>());

        Assert.Equal(16L, Offset(&raw, &raw.Format));
        Assert.Equal(24L, Offset(&raw, &raw.Start));
        Assert.Equal(32L, Offset(&raw, &raw.End));
    }

    /// <summary>
    /// <c>GstAudioDownmixMeta</c> of <c>gstaudiometa.h</c>: the 16 byte header,
    /// the two position pointers at 16 and 24, <c>gint from_channels</c> and
    /// <c>gint to_channels</c> at 32 and 36 and <c>gfloat **matrix</c> at 40,
    /// for 48 bytes.
    /// </summary>
    [Fact]
    public unsafe void AudioDownmixMetaRawMatchesTheHeaderLayout()
    {
        Gst.Audio.AudioDownmixMetaRaw raw = default;

        _output.WriteLine(Format("AudioDownmixMetaRaw", Unsafe.SizeOf<Gst.Audio.AudioDownmixMetaRaw>()));
        Assert.Equal(48, Unsafe.SizeOf<Gst.Audio.AudioDownmixMetaRaw>());

        Assert.Equal(16L, Offset(&raw, &raw.FromPosition));
        Assert.Equal(24L, Offset(&raw, &raw.ToPosition));
        Assert.Equal(32L, Offset(&raw, &raw.FromChannels));
        Assert.Equal(36L, Offset(&raw, &raw.ToChannels));
        Assert.Equal(40L, Offset(&raw, &raw.Matrix));
    }

    /// <summary>
    /// <c>struct _GstVideoCodecFrame</c> of <c>gstvideoutils.h</c>:
    /// <c>ref_count</c> at 0, the four frame numbers at 4 to 16, the three
    /// <c>GstClockTime</c> values <c>dts</c>, <c>pts</c> and <c>duration</c> at
    /// 24, 32 and 40, <c>distance_from_sync</c> at 48, the two buffers at 56
    /// and 64, <c>deadline</c> at 72, and the private <c>events</c>,
    /// <c>user_data</c> and <c>user_data_destroy_notify</c> at 80, 88 and 96.
    /// The <c>abidata</c> union sits at 104, which is where the prefix mirror
    /// stops.
    /// </summary>
    [Fact]
    public unsafe void VideoCodecFrameRawMatchesTheHeaderLayout()
    {
        Gst.Video.VideoCodecFrameRaw raw = default;

        _output.WriteLine(Format("VideoCodecFrameRaw", Unsafe.SizeOf<Gst.Video.VideoCodecFrameRaw>()));
        Assert.Equal(104, Unsafe.SizeOf<Gst.Video.VideoCodecFrameRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.RefCount));
        Assert.Equal(4L, Offset(&raw, &raw.Flags));
        Assert.Equal(8L, Offset(&raw, &raw.SystemFrameNumber));
        Assert.Equal(12L, Offset(&raw, &raw.DecodeFrameNumber));
        Assert.Equal(16L, Offset(&raw, &raw.PresentationFrameNumber));
        Assert.Equal(24L, Offset(&raw, &raw.Dts));
        Assert.Equal(32L, Offset(&raw, &raw.Pts));
        Assert.Equal(40L, Offset(&raw, &raw.Duration));
        Assert.Equal(48L, Offset(&raw, &raw.DistanceFromSync));
        Assert.Equal(56L, Offset(&raw, &raw.InputBuffer));
        Assert.Equal(64L, Offset(&raw, &raw.OutputBuffer));
        Assert.Equal(72L, Offset(&raw, &raw.Deadline));
    }

    /// <summary>
    /// <c>struct _GstVideoInfo</c> of <c>video-info.h</c>:
    /// <c>const GstVideoFormatInfo *finfo</c> at 0, <c>interlace_mode</c> and
    /// <c>flags</c> at 8 and 12, <c>gint width</c> and <c>gint height</c> at 16
    /// and 20 and <c>gsize size</c> at 24. Behind those, <c>views</c> and
    /// <c>chroma_site</c> at 32 and 36, the 16 byte <c>colorimetry</c> at 40,
    /// the pixel aspect ratio and the framerate at 56 to 68,
    /// <c>gsize offset[4]</c> at 72 and <c>gint stride[4]</c> at 104. The
    /// <c>ABI</c> union sits at 120, which is where the prefix mirror stops —
    /// the whole C structure is 152 bytes and the mirror is deliberately not.
    /// </summary>
    [Fact]
    public unsafe void VideoInfoRawMatchesTheHeaderLayoutUpToTheAbiUnion()
    {
        Gst.Video.VideoInfoRaw raw = default;

        _output.WriteLine(Format("VideoInfoRaw", Unsafe.SizeOf<Gst.Video.VideoInfoRaw>()));
        Assert.Equal(120, Unsafe.SizeOf<Gst.Video.VideoInfoRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Finfo));
        Assert.Equal(8L, Offset(&raw, &raw.InterlaceMode));
        Assert.Equal(12L, Offset(&raw, &raw.Flags));
        Assert.Equal(16L, Offset(&raw, &raw.Width));
        Assert.Equal(20L, Offset(&raw, &raw.Height));
        Assert.Equal(24L, Offset(&raw, &raw.Size));
        Assert.Equal(32L, Offset(&raw, &raw.Views));
        Assert.Equal(36L, Offset(&raw, &raw.ChromaSite));
        Assert.Equal(40L, Offset(&raw, &raw.Colorimetry));
        Assert.Equal(56L, Offset(&raw, &raw.ParN));
        Assert.Equal(60L, Offset(&raw, &raw.ParD));
        Assert.Equal(64L, Offset(&raw, &raw.FpsN));
        Assert.Equal(68L, Offset(&raw, &raw.FpsD));
        Assert.Equal(72L, Offset(&raw, &raw.Offset));
        Assert.Equal(104L, Offset(&raw, &raw.Stride));
    }

    /// <summary>
    /// <c>struct _GstAudioInfo</c> of <c>audio-info.h</c>:
    /// <c>const GstAudioFormatInfo *finfo</c> at 0, <c>flags</c> and
    /// <c>layout</c> at 8 and 12, <c>rate</c>, <c>channels</c> and <c>bpf</c>
    /// at 16, 20 and 24, <c>GstAudioChannelPosition position[64]</c> at 28 and
    /// <c>GST_PADDING</c> at 288, for 320 bytes.
    /// </summary>
    [Fact]
    public unsafe void AudioInfoRawMatchesTheHeaderLayout()
    {
        Gst.Audio.AudioInfoRaw raw = default;

        _output.WriteLine(Format("AudioInfoRaw", Unsafe.SizeOf<Gst.Audio.AudioInfoRaw>()));
        Assert.Equal(320, Unsafe.SizeOf<Gst.Audio.AudioInfoRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Finfo));
        Assert.Equal(8L, Offset(&raw, &raw.Flags));
        Assert.Equal(12L, Offset(&raw, &raw.Layout));
        Assert.Equal(16L, Offset(&raw, &raw.Rate));
        Assert.Equal(20L, Offset(&raw, &raw.Channels));
        Assert.Equal(24L, Offset(&raw, &raw.Bpf));
        Assert.Equal(28L, Offset(&raw, &raw.Position));
    }

    /// <summary>
    /// <c>struct _GstDsdInfo</c> of <c>gstdsd.h</c>: <c>format</c>,
    /// <c>rate</c>, <c>channels</c> and <c>layout</c> at 0 to 12,
    /// <c>gboolean reversed_bytes</c> at 16,
    /// <c>GstAudioChannelPosition positions[64]</c> at 20, <c>flags</c> at 276
    /// and <c>GST_PADDING</c> at 280, for 312 bytes.
    /// </summary>
    /// <remarks>
    /// <c>GstDsdInfo</c> arrived in 1.24, and nothing here calls into the
    /// library: the assertion is about the mirror the generator built from the
    /// 1.28 gir, so it runs everywhere the other probes do.
    /// </remarks>
    [Fact]
    public unsafe void DsdInfoRawMatchesTheHeaderLayout()
    {
        Gst.Audio.DsdInfoRaw raw = default;

        _output.WriteLine(Format("DsdInfoRaw", Unsafe.SizeOf<Gst.Audio.DsdInfoRaw>()));
        Assert.Equal(312, Unsafe.SizeOf<Gst.Audio.DsdInfoRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Format));
        Assert.Equal(4L, Offset(&raw, &raw.Rate));
        Assert.Equal(8L, Offset(&raw, &raw.Channels));
        Assert.Equal(12L, Offset(&raw, &raw.Layout));
        Assert.Equal(16L, Offset(&raw, &raw.ReversedBytes));
        Assert.Equal(20L, Offset(&raw, &raw.Positions));
        Assert.Equal(276L, Offset(&raw, &raw.Flags));
    }

    /// <summary>
    /// <c>struct _GstVideoTimeCode</c> of <c>gstvideotimecode.h</c>: the 24
    /// byte <c>GstVideoTimeCodeConfig</c> it embeds by value — two
    /// <c>guint</c>, a flags word and a <c>GDateTime *</c> at 16 — and the five
    /// <c>guint</c> values <c>hours</c> … <c>field_count</c> at 24 to 40, for
    /// 48 bytes. The interval is four <c>guint</c>, for 16.
    /// </summary>
    [Fact]
    public unsafe void VideoTimeCodeRawMatchesTheHeaderLayout()
    {
        Gst.Video.VideoTimeCodeRaw raw = default;
        Gst.Video.VideoTimeCodeConfigRaw config = default;
        Gst.Video.VideoTimeCodeIntervalRaw interval = default;

        _output.WriteLine(Format("VideoTimeCodeRaw", Unsafe.SizeOf<Gst.Video.VideoTimeCodeRaw>()));

        Assert.Equal(48, Unsafe.SizeOf<Gst.Video.VideoTimeCodeRaw>());
        Assert.Equal(24, Unsafe.SizeOf<Gst.Video.VideoTimeCodeConfigRaw>());
        Assert.Equal(16, Unsafe.SizeOf<Gst.Video.VideoTimeCodeIntervalRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Config));
        Assert.Equal(24L, Offset(&raw, &raw.Hours));
        Assert.Equal(28L, Offset(&raw, &raw.Minutes));
        Assert.Equal(32L, Offset(&raw, &raw.Seconds));
        Assert.Equal(36L, Offset(&raw, &raw.Frames));
        Assert.Equal(40L, Offset(&raw, &raw.FieldCount));

        Assert.Equal(0L, Offset(&config, &config.FpsN));
        Assert.Equal(4L, Offset(&config, &config.FpsD));
        Assert.Equal(8L, Offset(&config, &config.Flags));
        Assert.Equal(16L, Offset(&config, &config.LatestDailyJam));

        Assert.Equal(0L, Offset(&interval, &interval.Hours));
        Assert.Equal(12L, Offset(&interval, &interval.Frames));
    }

    /// <summary>
    /// <c>struct _GstRTSPTransport</c> of <c>gstrtsptransport.h</c>: the three
    /// enumerations at 0, 4 and 8, <c>destination</c> and <c>source</c> at 16
    /// and 24, <c>layers</c> at 32, the three <c>gboolean</c> at 36, 40 and 44,
    /// the eight byte <c>interleaved</c> range at 48, <c>ttl</c> at 56, the
    /// three port ranges at 60, 68 and 76, <c>ssrc</c> at 84 and
    /// <c>GST_PADDING</c> at 88, for 120 bytes.
    /// </summary>
    [Fact]
    public unsafe void RtspTransportRawMatchesTheHeaderLayout()
    {
        Gst.Rtsp.RTSPTransportRaw raw = default;

        _output.WriteLine(Format("RTSPTransportRaw", Unsafe.SizeOf<Gst.Rtsp.RTSPTransportRaw>()));
        Assert.Equal(120, Unsafe.SizeOf<Gst.Rtsp.RTSPTransportRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Trans));
        Assert.Equal(4L, Offset(&raw, &raw.Profile));
        Assert.Equal(8L, Offset(&raw, &raw.LowerTransport));
        Assert.Equal(16L, Offset(&raw, &raw.Destination));
        Assert.Equal(24L, Offset(&raw, &raw.Source));
        Assert.Equal(32L, Offset(&raw, &raw.Layers));
        Assert.Equal(36L, Offset(&raw, &raw.ModePlay));
        Assert.Equal(40L, Offset(&raw, &raw.ModeRecord));
        Assert.Equal(44L, Offset(&raw, &raw.Append));
        Assert.Equal(48L, Offset(&raw, &raw.Interleaved));
        Assert.Equal(56L, Offset(&raw, &raw.Ttl));
        Assert.Equal(60L, Offset(&raw, &raw.Port));
        Assert.Equal(68L, Offset(&raw, &raw.ClientPort));
        Assert.Equal(76L, Offset(&raw, &raw.ServerPort));
        Assert.Equal(84L, Offset(&raw, &raw.Ssrc));
    }

    /// <summary>
    /// <c>struct _GstNetTimePacket</c> of <c>gstnettimepacket.h</c>: the two
    /// <c>GstClockTime</c> values <c>local_time</c> and <c>remote_time</c> at 0
    /// and 8, for 16 bytes.
    /// </summary>
    [Fact]
    public unsafe void NetTimePacketRawMatchesTheHeaderLayout()
    {
        Gst.Net.NetTimePacketRaw raw = default;

        _output.WriteLine(Format("NetTimePacketRaw", Unsafe.SizeOf<Gst.Net.NetTimePacketRaw>()));
        Assert.Equal(16, Unsafe.SizeOf<Gst.Net.NetTimePacketRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.LocalTime));
        Assert.Equal(8L, Offset(&raw, &raw.RemoteTime));
    }

    /// <summary>
    /// <c>struct _GstWebRTCSessionDescription</c> of
    /// <c>rtcsessiondescription.h</c>: <c>GstWebRTCSDPType type</c> at 0 and
    /// <c>GstSDPMessage *sdp</c> at 8, for 16 bytes.
    /// </summary>
    [Fact]
    public unsafe void WebRtcSessionDescriptionRawMatchesTheHeaderLayout()
    {
        Gst.WebRTC.WebRTCSessionDescriptionRaw raw = default;

        _output.WriteLine(Format(
            "WebRTCSessionDescriptionRaw",
            Unsafe.SizeOf<Gst.WebRTC.WebRTCSessionDescriptionRaw>()));
        Assert.Equal(16, Unsafe.SizeOf<Gst.WebRTC.WebRTCSessionDescriptionRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Type));
        Assert.Equal(8L, Offset(&raw, &raw.Sdp));
    }

    /// <summary>
    /// <c>struct _GstVideoFrame</c> of <c>video-frame.h</c>: the 152 byte
    /// <c>GstVideoInfo info</c> at 0, <c>GstVideoFrameFlags flags</c> at 152,
    /// four bytes of padding, <c>GstBuffer *buffer</c> at 160,
    /// <c>gpointer meta</c> at 168, <c>gint id</c> at 176, four bytes of
    /// padding, <c>gpointer data[4]</c> at 184, <c>GstMapInfo map[4]</c> at 216
    /// and the four reserved pointers of <c>GST_PADDING</c> at 632, for 664
    /// bytes in total.
    /// </summary>
    /// <remarks>
    /// This is the mirror <c>Gst.Video.VideoFrame.MapScope</c> hands to
    /// <c>gst_video_frame_map</c> as its own storage, so it is the one mirror
    /// whose size the library writes through rather than reads: a mirror that
    /// is too small is a stack frame the library writes past. The header is
    /// what this probe states; that the installed library agrees is what
    /// <c>CallerAllocatedStorageTests</c> measures, by reading the fields back
    /// out of a live mapping.
    /// </remarks>
    [Fact]
    public unsafe void VideoFrameRawMatchesTheHeaderLayout()
    {
        Gst.Video.VideoFrameRaw raw = default;

        _output.WriteLine(Format("VideoFrameRaw", Unsafe.SizeOf<Gst.Video.VideoFrameRaw>()));
        Assert.Equal(664, Unsafe.SizeOf<Gst.Video.VideoFrameRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Info));
        Assert.Equal(152L, Offset(&raw, &raw.Flags));
        Assert.Equal(160L, Offset(&raw, &raw.BufferPtr));
        Assert.Equal(168L, Offset(&raw, &raw.MetaPtr));
        Assert.Equal(176L, Offset(&raw, &raw.Id));
        Assert.Equal(184L, Offset(&raw, &raw.Data));
        Assert.Equal(216L, Offset(&raw, &raw.Map));
    }

    /// <summary>
    /// <c>struct _GstAudioBuffer</c> of <c>audio-buffer.h</c>: the 320 byte
    /// <c>GstAudioInfo</c> it embeds by value at 0, <c>gsize n_samples</c> at
    /// 320, <c>gint n_planes</c> at 328, <c>gpointer *planes</c> at 336,
    /// <c>GstBuffer *buffer</c> at 344 and the private <c>map_infos</c> at 352,
    /// followed by <c>gpointer priv_planes_arr[8]</c> at 360,
    /// <c>GstMapInfo priv_map_infos_arr[8]</c> at 424 and
    /// <c>GST_PADDING</c>, for 1288 bytes.
    /// </summary>
    /// <remarks>
    /// The two accessors of the wrapper sit behind the embedded info, so this
    /// is what says the embed has the size the C structure gives it. The two
    /// private arrays are part of the mirror as well, because a live mapping
    /// points at them: for eight planes or fewer <c>planes</c> and
    /// <c>map_infos</c> address the structure itself, which is what
    /// <c>Gst.Audio.AudioBuffer.MapScope</c> has to repair after the scope is
    /// moved. That the installed library really writes those two addresses is
    /// what <c>CallerAllocatedStorageTests</c> measures, on a live mapping.
    /// </remarks>
    [Fact]
    public unsafe void AudioBufferRawMatchesTheHeaderLayout()
    {
        Gst.Audio.AudioBufferRaw raw = default;

        _output.WriteLine(Format("AudioBufferRaw", Unsafe.SizeOf<Gst.Audio.AudioBufferRaw>()));
        Assert.Equal(1288, Unsafe.SizeOf<Gst.Audio.AudioBufferRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Info));
        Assert.Equal(320L, Offset(&raw, &raw.NSamples));
        Assert.Equal(328L, Offset(&raw, &raw.NPlanes));
        Assert.Equal(336L, Offset(&raw, &raw.Planes));
        Assert.Equal(344L, Offset(&raw, &raw.Buffer));
        Assert.Equal(352L, Offset(&raw, &raw.MapInfos));
        Assert.Equal(360L, Offset(&raw, &raw.PrivPlanesArr));
        Assert.Equal(424L, Offset(&raw, &raw.PrivMapInfosArr));
    }

    /// <summary>
    /// <c>GstAudioMeta</c> of <c>gstaudiometa.h</c>: the 16 byte header, the
    /// 320 byte <c>GstAudioInfo</c> at 16, <c>gsize samples</c> at 336,
    /// <c>gsize *offsets</c> at 344, <c>gsize priv_offsets_arr[8]</c> at 352
    /// and <c>GST_PADDING</c>, for 448 bytes.
    /// </summary>
    /// <remarks>
    /// The one accessor of the wrapper sits behind two embedded records, which
    /// is the deepest the layout of a mirror goes.
    /// </remarks>
    [Fact]
    public unsafe void AudioMetaRawMatchesTheHeaderLayout()
    {
        Gst.Audio.AudioMetaRaw raw = default;

        _output.WriteLine(Format("AudioMetaRaw", Unsafe.SizeOf<Gst.Audio.AudioMetaRaw>()));
        Assert.Equal(448, Unsafe.SizeOf<Gst.Audio.AudioMetaRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Meta));
        Assert.Equal(16L, Offset(&raw, &raw.Info));
        Assert.Equal(336L, Offset(&raw, &raw.Samples));
        Assert.Equal(344L, Offset(&raw, &raw.Offsets));
        Assert.Equal(352L, Offset(&raw, &raw.PrivOffsetsArr));
    }

    /// <summary>
    /// <c>struct _GstAudioRingBufferSpec</c> of <c>gstaudioringbuffer.h</c>:
    /// <c>GstCaps *caps</c> at 0, <c>GstAudioRingBufferFormatType type</c> at 8
    /// with four bytes of padding behind it, the 320 byte <c>GstAudioInfo</c>
    /// at 16, the two <c>guint64</c> values <c>latency_time</c> and
    /// <c>buffer_time</c> at 336 and 344 and the three <c>gint</c> values
    /// <c>segsize</c>, <c>segtotal</c> and <c>seglatency</c> at 352, 356 and
    /// 360. The <c>ABI</c> union sits at 368, which is where the prefix mirror
    /// stops — the whole C structure is 400 bytes and the mirror is
    /// deliberately not.
    /// </summary>
    /// <remarks>
    /// This is the record where a four byte enumeration precedes the eight byte
    /// aligned embed, so it is the one that says the padding of the embed is
    /// where C puts it.
    /// </remarks>
    [Fact]
    public unsafe void AudioRingBufferSpecRawMatchesTheHeaderLayoutUpToTheAbiUnion()
    {
        Gst.Audio.AudioRingBufferSpecRaw raw = default;

        _output.WriteLine(Format("AudioRingBufferSpecRaw", Unsafe.SizeOf<Gst.Audio.AudioRingBufferSpecRaw>()));
        Assert.Equal(368, Unsafe.SizeOf<Gst.Audio.AudioRingBufferSpecRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Caps));
        Assert.Equal(8L, Offset(&raw, &raw.Type));
        Assert.Equal(16L, Offset(&raw, &raw.Info));
        Assert.Equal(336L, Offset(&raw, &raw.LatencyTime));
        Assert.Equal(344L, Offset(&raw, &raw.BufferTime));
        Assert.Equal(352L, Offset(&raw, &raw.Segsize));
        Assert.Equal(356L, Offset(&raw, &raw.Segtotal));
        Assert.Equal(360L, Offset(&raw, &raw.Seglatency));
    }

    /// <summary>
    /// <c>struct _GstMIKEYPayloadSP</c> of <c>gstmikey.h</c>: the 72 byte
    /// <c>GstMIKEYPayload</c> it embeds by value at 0 — a 64 byte
    /// <c>GstMiniObject</c> and two <c>guint</c> — <c>guint policy</c> at 72,
    /// <c>GstMIKEYSecProto proto</c> at 76 and <c>GArray *params</c> at 80, for
    /// 88 bytes.
    /// </summary>
    /// <remarks>
    /// The six MIKEY payload records embed the same header, so the one probe
    /// says where the accessors of all of them sit.
    /// </remarks>
    [Fact]
    public unsafe void MikeyPayloadSpRawMatchesTheHeaderLayout()
    {
        Gst.Sdp.MIKEYPayloadSPRaw raw = default;

        _output.WriteLine(Format("MIKEYPayloadSPRaw", Unsafe.SizeOf<Gst.Sdp.MIKEYPayloadSPRaw>()));
        Assert.Equal(88, Unsafe.SizeOf<Gst.Sdp.MIKEYPayloadSPRaw>());

        Assert.Equal(0L, Offset(&raw, &raw.Pt));
        Assert.Equal(72L, Offset(&raw, &raw.Policy));
        Assert.Equal(76L, Offset(&raw, &raw.Proto));
        Assert.Equal(80L, Offset(&raw, &raw.Params));
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

    /// <summary>
    /// Reads the class of an instance, which is the first word of every
    /// <c>GTypeInstance</c>.
    /// </summary>
    /// <param name="instance">The object to read.</param>
    /// <returns>The class of the instance.</returns>
    private static unsafe nint ClassOf(Gst.GObject.Object instance) => *(nint*)instance.Handle;

    /// <summary>
    /// Asserts that a mirror is exactly as large as the class the library
    /// allocates.
    /// </summary>
    /// <param name="name">The name of the class, for the output.</param>
    /// <param name="mirror">The size of the mirror.</param>
    /// <param name="type">The type to query.</param>
    private void AssertClassSize(string name, int mirror, nuint type)
    {
        GObjectNative.TypeQuery(type, out GTypeQuery query);

        _output.WriteLine(FormattableString.Invariant(
            $"g_type_query({name}): class_size={query.ClassSize} instance_size={query.InstanceSize}, mirror={mirror}"));

        Assert.Equal((uint)mirror, query.ClassSize);
    }

    private static unsafe long Offset(void* start, void* field) => (byte*)field - (byte*)start;

    private static string Format(string name, int size) =>
        string.Create(CultureInfo.InvariantCulture, $"sizeof({name}) = {size}");
}
