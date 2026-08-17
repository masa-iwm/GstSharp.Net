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
