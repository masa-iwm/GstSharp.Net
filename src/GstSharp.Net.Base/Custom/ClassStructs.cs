using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.GObject;

namespace Gst.Base;

/// <summary>The native layout of <c>GstBaseSrcClass</c>.</summary>
/// <remarks>
/// <para>
/// The only thing the runtime does with a class struct is write a function
/// pointer at a computed offset, so the mirror has to spell every field that
/// comes before the ones it writes — including the reserved tail, which is a
/// full <c>GST_PADDING_LARGE</c> here because no slot of this class has been
/// carved out of it yet.
/// </para>
/// <para>
/// The layout is asserted against the running library by <c>AbiProbeTests</c>,
/// as constants derived from the headers and against the <c>class_size</c> that
/// <c>g_type_query</c> reports. See <c>docs/subclassing.md</c> §6.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct BaseSrcClassRaw
{
    /// <summary>The <c>parent_class</c> field.</summary>
    internal ElementClassRaw ParentClass;

    /// <summary>The <c>get_caps</c> slot.</summary>
    internal nint GetCaps;

    /// <summary>The <c>negotiate</c> slot.</summary>
    internal nint Negotiate;

    /// <summary>The <c>fixate</c> slot.</summary>
    internal nint Fixate;

    /// <summary>The <c>set_caps</c> slot.</summary>
    internal nint SetCaps;

    /// <summary>The <c>decide_allocation</c> slot.</summary>
    internal nint DecideAllocation;

    /// <summary>The <c>start</c> slot.</summary>
    internal nint Start;

    /// <summary>The <c>stop</c> slot.</summary>
    internal nint Stop;

    /// <summary>The <c>get_times</c> slot.</summary>
    internal nint GetTimes;

    /// <summary>The <c>get_size</c> slot.</summary>
    internal nint GetSize;

    /// <summary>The <c>is_seekable</c> slot.</summary>
    internal nint IsSeekable;

    /// <summary>The <c>prepare_seek_segment</c> slot.</summary>
    internal nint PrepareSeekSegment;

    /// <summary>The <c>do_seek</c> slot.</summary>
    internal nint DoSeek;

    /// <summary>The <c>unlock</c> slot.</summary>
    internal nint Unlock;

    /// <summary>The <c>unlock_stop</c> slot.</summary>
    internal nint UnlockStop;

    /// <summary>The <c>query</c> slot.</summary>
    internal nint Query;

    /// <summary>The <c>event</c> slot.</summary>
    internal nint Event;

    /// <summary>The <c>create</c> slot.</summary>
    internal nint Create;

    /// <summary>The <c>alloc</c> slot.</summary>
    internal nint Alloc;

    /// <summary>The <c>fill</c> slot.</summary>
    internal nint Fill;

    /// <summary>The <c>_gst_reserved</c> field.</summary>
    private GstReservedArray _gstReserved;

    static BaseSrcClassRaw()
    {
        BaseSrcClassRaw probe = default;

        SetCapsOffset = ClassSlot.OffsetOf(ref probe, ref probe.SetCaps);
        StartOffset = ClassSlot.OffsetOf(ref probe, ref probe.Start);
        StopOffset = ClassSlot.OffsetOf(ref probe, ref probe.Stop);
        IsSeekableOffset = ClassSlot.OffsetOf(ref probe, ref probe.IsSeekable);
    }

    /// <summary>Gets the byte offset of <see cref="SetCaps"/> within the class struct.</summary>
    internal static int SetCapsOffset { get; }

    /// <summary>Gets the byte offset of <see cref="Start"/> within the class struct.</summary>
    internal static int StartOffset { get; }

    /// <summary>Gets the byte offset of <see cref="Stop"/> within the class struct.</summary>
    internal static int StopOffset { get; }

    /// <summary>Gets the byte offset of <see cref="IsSeekable"/> within the class struct.</summary>
    internal static int IsSeekableOffset { get; }

    /// <summary>Inline storage of the 20 elements of the <c>_gst_reserved</c> field of <c>GstBaseSrcClass</c>.</summary>
    [InlineArray(20)]
    private struct GstReservedArray
    {
        private nint _element0;
    }
}

/// <summary>The native layout of <c>GstPushSrcClass</c>.</summary>
/// <remarks>
/// The three slots repeat the ones of <c>GstBaseSrcClass</c> without the offset
/// and size parameters, which is the whole point of the class: a push source
/// produces whatever it has rather than what it was asked for. Its reserved
/// tail is a plain <c>GST_PADDING</c>.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct PushSrcClassRaw
{
    /// <summary>The <c>parent_class</c> field.</summary>
    internal BaseSrcClassRaw ParentClass;

    /// <summary>The <c>create</c> slot.</summary>
    internal nint Create;

    /// <summary>The <c>alloc</c> slot.</summary>
    internal nint Alloc;

    /// <summary>The <c>fill</c> slot.</summary>
    internal nint Fill;

    /// <summary>The <c>_gst_reserved</c> field.</summary>
    private GstReservedArray _gstReserved;

    static PushSrcClassRaw()
    {
        PushSrcClassRaw probe = default;

        CreateOffset = ClassSlot.OffsetOf(ref probe, ref probe.Create);
    }

    /// <summary>Gets the byte offset of <see cref="Create"/> within the class struct.</summary>
    internal static int CreateOffset { get; }

    /// <summary>Inline storage of the 4 elements of the <c>_gst_reserved</c> field of <c>GstPushSrcClass</c>.</summary>
    [InlineArray(4)]
    private struct GstReservedArray
    {
        private nint _element0;
    }
}

/// <summary>The native layout of <c>GstBaseSinkClass</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BaseSinkClassRaw
{
    /// <summary>The <c>parent_class</c> field.</summary>
    internal ElementClassRaw ParentClass;

    /// <summary>The <c>get_caps</c> slot.</summary>
    internal nint GetCaps;

    /// <summary>The <c>set_caps</c> slot.</summary>
    internal nint SetCaps;

    /// <summary>The <c>fixate</c> slot.</summary>
    internal nint Fixate;

    /// <summary>The <c>activate_pull</c> slot.</summary>
    internal nint ActivatePull;

    /// <summary>The <c>get_times</c> slot.</summary>
    internal nint GetTimes;

    /// <summary>The <c>propose_allocation</c> slot.</summary>
    internal nint ProposeAllocation;

    /// <summary>The <c>start</c> slot.</summary>
    internal nint Start;

    /// <summary>The <c>stop</c> slot.</summary>
    internal nint Stop;

    /// <summary>The <c>unlock</c> slot.</summary>
    internal nint Unlock;

    /// <summary>The <c>unlock_stop</c> slot.</summary>
    internal nint UnlockStop;

    /// <summary>The <c>query</c> slot.</summary>
    internal nint Query;

    /// <summary>The <c>event</c> slot.</summary>
    internal nint Event;

    /// <summary>The <c>wait_event</c> slot.</summary>
    internal nint WaitEvent;

    /// <summary>The <c>prepare</c> slot.</summary>
    internal nint Prepare;

    /// <summary>The <c>prepare_list</c> slot.</summary>
    internal nint PrepareList;

    /// <summary>The <c>preroll</c> slot.</summary>
    internal nint Preroll;

    /// <summary>The <c>render</c> slot.</summary>
    internal nint Render;

    /// <summary>The <c>render_list</c> slot.</summary>
    internal nint RenderList;

    /// <summary>The <c>_gst_reserved</c> field.</summary>
    private GstReservedArray _gstReserved;

    static BaseSinkClassRaw()
    {
        BaseSinkClassRaw probe = default;

        SetCapsOffset = ClassSlot.OffsetOf(ref probe, ref probe.SetCaps);
        StartOffset = ClassSlot.OffsetOf(ref probe, ref probe.Start);
        StopOffset = ClassSlot.OffsetOf(ref probe, ref probe.Stop);
        PrerollOffset = ClassSlot.OffsetOf(ref probe, ref probe.Preroll);
        RenderOffset = ClassSlot.OffsetOf(ref probe, ref probe.Render);
    }

    /// <summary>Gets the byte offset of <see cref="SetCaps"/> within the class struct.</summary>
    internal static int SetCapsOffset { get; }

    /// <summary>Gets the byte offset of <see cref="Start"/> within the class struct.</summary>
    internal static int StartOffset { get; }

    /// <summary>Gets the byte offset of <see cref="Stop"/> within the class struct.</summary>
    internal static int StopOffset { get; }

    /// <summary>Gets the byte offset of <see cref="Preroll"/> within the class struct.</summary>
    internal static int PrerollOffset { get; }

    /// <summary>Gets the byte offset of <see cref="Render"/> within the class struct.</summary>
    internal static int RenderOffset { get; }

    /// <summary>Inline storage of the 20 elements of the <c>_gst_reserved</c> field of <c>GstBaseSinkClass</c>.</summary>
    [InlineArray(20)]
    private struct GstReservedArray
    {
        private nint _element0;
    }
}

/// <summary>The native layout of <c>GstBaseTransformClass</c>.</summary>
/// <remarks>
/// This is the class struct that shows most clearly that a class is not a vfunc
/// table: it leads with two <c>gboolean</c> <em>data</em> fields, which a
/// subclass sets to say how it wants to be driven, and only then starts the
/// slots. Mirroring them as anything but two 32 bit integers would move every
/// slot behind them.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct BaseTransformClassRaw
{
    /// <summary>The <c>parent_class</c> field.</summary>
    internal ElementClassRaw ParentClass;

    /// <summary>The <c>passthrough_on_same_caps</c> data field.</summary>
    internal int PassthroughOnSameCaps;

    /// <summary>The <c>transform_ip_on_passthrough</c> data field.</summary>
    internal int TransformIpOnPassthrough;

    /// <summary>The <c>transform_caps</c> slot.</summary>
    internal nint TransformCaps;

    /// <summary>The <c>fixate_caps</c> slot.</summary>
    internal nint FixateCaps;

    /// <summary>The <c>accept_caps</c> slot.</summary>
    internal nint AcceptCaps;

    /// <summary>The <c>set_caps</c> slot.</summary>
    internal nint SetCaps;

    /// <summary>The <c>query</c> slot.</summary>
    internal nint Query;

    /// <summary>The <c>decide_allocation</c> slot.</summary>
    internal nint DecideAllocation;

    /// <summary>The <c>filter_meta</c> slot.</summary>
    internal nint FilterMeta;

    /// <summary>The <c>propose_allocation</c> slot.</summary>
    internal nint ProposeAllocation;

    /// <summary>The <c>transform_size</c> slot.</summary>
    internal nint TransformSize;

    /// <summary>The <c>get_unit_size</c> slot.</summary>
    internal nint GetUnitSize;

    /// <summary>The <c>start</c> slot.</summary>
    internal nint Start;

    /// <summary>The <c>stop</c> slot.</summary>
    internal nint Stop;

    /// <summary>The <c>sink_event</c> slot.</summary>
    internal nint SinkEvent;

    /// <summary>The <c>src_event</c> slot.</summary>
    internal nint SrcEvent;

    /// <summary>The <c>prepare_output_buffer</c> slot.</summary>
    internal nint PrepareOutputBuffer;

    /// <summary>The <c>copy_metadata</c> slot.</summary>
    internal nint CopyMetadata;

    /// <summary>The <c>transform_meta</c> slot.</summary>
    internal nint TransformMeta;

    /// <summary>The <c>before_transform</c> slot.</summary>
    internal nint BeforeTransform;

    /// <summary>The <c>transform</c> slot.</summary>
    internal nint Transform;

    /// <summary>The <c>transform_ip</c> slot.</summary>
    internal nint TransformIp;

    /// <summary>The <c>submit_input_buffer</c> slot.</summary>
    internal nint SubmitInputBuffer;

    /// <summary>The <c>generate_output</c> slot.</summary>
    internal nint GenerateOutput;

    /// <summary>The <c>_gst_reserved</c> field.</summary>
    private GstReservedArray _gstReserved;

    static BaseTransformClassRaw()
    {
        BaseTransformClassRaw probe = default;

        SetCapsOffset = ClassSlot.OffsetOf(ref probe, ref probe.SetCaps);
        StartOffset = ClassSlot.OffsetOf(ref probe, ref probe.Start);
        StopOffset = ClassSlot.OffsetOf(ref probe, ref probe.Stop);
        TransformIpOffset = ClassSlot.OffsetOf(ref probe, ref probe.TransformIp);
    }

    /// <summary>Gets the byte offset of <see cref="SetCaps"/> within the class struct.</summary>
    internal static int SetCapsOffset { get; }

    /// <summary>Gets the byte offset of <see cref="Start"/> within the class struct.</summary>
    internal static int StartOffset { get; }

    /// <summary>Gets the byte offset of <see cref="Stop"/> within the class struct.</summary>
    internal static int StopOffset { get; }

    /// <summary>Gets the byte offset of <see cref="TransformIp"/> within the class struct.</summary>
    internal static int TransformIpOffset { get; }

    /// <summary>Inline storage of the 18 elements of the <c>_gst_reserved</c> field of <c>GstBaseTransformClass</c>.</summary>
    [InlineArray(18)]
    private struct GstReservedArray
    {
        private nint _element0;
    }
}

/// <summary>
/// Measures where a slot sits inside a class struct.
/// </summary>
/// <remarks>
/// The offsets a subclass declares its overrides with are measured from the
/// mirrors rather than written out as literals, so that a declaration can never
/// drift from the mirror; the mirrors themselves are what the ABI probes pin to
/// the C headers and to the running library.
/// </remarks>
internal static class ClassSlot
{
    /// <summary>Returns the byte offset of a slot within its class struct.</summary>
    /// <typeparam name="TClass">The class struct.</typeparam>
    /// <param name="origin">The start of the struct.</param>
    /// <param name="slot">The slot to measure.</param>
    /// <returns>The offset in bytes.</returns>
    internal static int OffsetOf<TClass>(ref TClass origin, ref nint slot)
        where TClass : struct =>
        (int)Unsafe.ByteOffset(
            ref Unsafe.As<TClass, byte>(ref origin),
            ref Unsafe.As<nint, byte>(ref slot));
}
