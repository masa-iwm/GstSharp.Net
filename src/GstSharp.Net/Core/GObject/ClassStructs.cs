using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst.GObject;

/// <summary>The native layout of <c>GTypeClass</c>.</summary>
/// <remarks>
/// Every class struct starts with this, which is why
/// <c>TypeRegistry.GetInstanceType</c> can read the type of an instance out of
/// the first word of its class.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GTypeClassRaw
{
    /// <summary>The <c>g_type</c> field.</summary>
    internal nuint GType;
}

/// <summary>The native layout of <c>GObjectClass</c>.</summary>
/// <remarks>
/// The callback slots are <see cref="nint"/> rather than typed function
/// pointers: the runtime only reads the ones it chains up through, and it casts
/// those at the point of use. See <c>docs/subclassing.md</c> §6.1.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GObjectClassRaw
{
    /// <summary>The <c>g_type_class</c> field.</summary>
    internal GTypeClassRaw TypeClass;

    /// <summary>The private <c>construct_properties</c> field, a <c>GSList*</c>.</summary>
    internal nint ConstructProperties;

    /// <summary>The <c>constructor</c> slot.</summary>
    internal nint Constructor;

    /// <summary>The <c>set_property</c> slot.</summary>
    internal nint SetProperty;

    /// <summary>The <c>get_property</c> slot.</summary>
    internal nint GetProperty;

    /// <summary>The <c>dispose</c> slot. Overriding it is a non-goal, see §1.</summary>
    internal nint Dispose;

    /// <summary>The <c>finalize</c> slot. Overriding it is a non-goal, see §1.</summary>
    internal nint Finalize;

    /// <summary>The <c>dispatch_properties_changed</c> slot.</summary>
    internal nint DispatchPropertiesChanged;

    /// <summary>The <c>notify</c> slot.</summary>
    internal nint Notify;

    /// <summary>The <c>constructed</c> slot.</summary>
    internal nint Constructed;

    /// <summary>The private <c>flags</c> field, a <c>gsize</c>.</summary>
    internal nuint Flags;

    /// <summary>The private <c>n_construct_properties</c> field, a <c>gsize</c>.</summary>
    internal nuint ConstructPropertyCount;

    /// <summary>The private <c>pspecs</c> field.</summary>
    internal nint ParamSpecs;

    /// <summary>The private <c>n_pspecs</c> field, a <c>gsize</c>.</summary>
    internal nuint ParamSpecCount;

    /// <summary>The private <c>pdummy</c> field.</summary>
    private PDummyArray _pdummy;

    /// <summary>Inline storage of the 3 elements of the <c>pdummy</c> field of <c>GObjectClass</c>.</summary>
    [InlineArray(3)]
    private struct PDummyArray
    {
        private nint _element0;
    }
}

/// <summary>The native layout of <c>GstObjectClass</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GstObjectClassRaw
{
    /// <summary>
    /// The <c>parent_class</c> field. <c>GInitiallyUnownedClass</c> is a
    /// typedef of <c>GObjectClass</c> and has the same layout.
    /// </summary>
    internal GObjectClassRaw ParentClass;

    /// <summary>The <c>path_string_separator</c> field.</summary>
    internal nint PathStringSeparator;

    /// <summary>The <c>deep_notify</c> slot.</summary>
    internal nint DeepNotify;

    /// <summary>The <c>_gst_reserved</c> field.</summary>
    private GstReservedArray _gstReserved;

    /// <summary>Inline storage of the 4 elements of the <c>_gst_reserved</c> field of <c>GstObjectClass</c>.</summary>
    [InlineArray(4)]
    private struct GstReservedArray
    {
        private nint _element0;
    }
}

/// <summary>The native layout of <c>GstElementClass</c>.</summary>
/// <remarks>
/// <para>
/// A class struct is not a vfunc table: <c>GstElementClass</c> carries five
/// data fields between its parent class and its first slot, and its reserved
/// tail is <c>GST_PADDING_LARGE - 2</c> rather than <c>GST_PADDING_LARGE</c>,
/// because <c>post_message</c> and <c>set_context</c> were carved out of the
/// padding in past minor releases. The mirror has to spell all of that: the
/// only thing the runtime does with a class struct is write a function pointer
/// at a computed offset.
/// </para>
/// <para>
/// The layout is asserted against the running library by
/// <c>AbiProbeTests</c>, both as constants derived from the headers and against
/// the <c>class_size</c> that <c>g_type_query</c> reports.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct ElementClassRaw
{
    /// <summary>The <c>parent_class</c> field.</summary>
    internal GstObjectClassRaw ParentClass;

    /// <summary>The <c>metadata</c> field.</summary>
    internal nint Metadata;

    /// <summary>The <c>elementfactory</c> field.</summary>
    internal nint ElementFactory;

    /// <summary>The <c>padtemplates</c> field, a <c>GList*</c>.</summary>
    internal nint PadTemplates;

    /// <summary>The <c>numpadtemplates</c> field.</summary>
    internal int PadTemplateCount;

    /// <summary>The <c>pad_templ_cookie</c> field.</summary>
    internal uint PadTemplateCookie;

    /// <summary>The <c>pad_added</c> slot.</summary>
    internal nint PadAdded;

    /// <summary>The <c>pad_removed</c> slot.</summary>
    internal nint PadRemoved;

    /// <summary>The <c>no_more_pads</c> slot.</summary>
    internal nint NoMorePads;

    /// <summary>The <c>request_new_pad</c> slot.</summary>
    internal nint RequestNewPad;

    /// <summary>The <c>release_pad</c> slot.</summary>
    internal nint ReleasePad;

    /// <summary>The <c>get_state</c> slot.</summary>
    internal nint GetState;

    /// <summary>The <c>set_state</c> slot.</summary>
    internal nint SetState;

    /// <summary>The <c>change_state</c> slot.</summary>
    internal nint ChangeState;

    /// <summary>The <c>state_changed</c> slot.</summary>
    internal nint StateChanged;

    /// <summary>The <c>set_bus</c> slot.</summary>
    internal nint SetBus;

    /// <summary>The <c>provide_clock</c> slot.</summary>
    internal nint ProvideClock;

    /// <summary>The <c>set_clock</c> slot.</summary>
    internal nint SetClock;

    /// <summary>The <c>send_event</c> slot.</summary>
    internal nint SendEvent;

    /// <summary>The <c>query</c> slot.</summary>
    internal nint Query;

    /// <summary>The <c>post_message</c> slot.</summary>
    internal nint PostMessage;

    /// <summary>The <c>set_context</c> slot.</summary>
    internal nint SetContext;

    /// <summary>The <c>_gst_reserved</c> field.</summary>
    private GstReservedArray _gstReserved;

    /// <summary>
    /// Gets the byte offset of <see cref="ChangeState"/> within the class
    /// struct, which is what a subclass declares the slot with.
    /// </summary>
    /// <remarks>
    /// Measured from the mirror rather than written out as a literal, so that a
    /// slot declaration can never drift from the mirror; the mirror itself is
    /// what the ABI probes pin to the C headers.
    /// </remarks>
    internal static int ChangeStateOffset { get; } = MeasureChangeStateOffset();

    private static unsafe int MeasureChangeStateOffset()
    {
        ElementClassRaw probe = default;
        return (int)((byte*)&probe.ChangeState - (byte*)&probe);
    }

    /// <summary>Inline storage of the 18 elements of the <c>_gst_reserved</c> field of <c>GstElementClass</c>.</summary>
    [InlineArray(18)]
    private struct GstReservedArray
    {
        private nint _element0;
    }
}

/// <summary>The native layout of <c>GstBinClass</c>.</summary>
/// <remarks>
/// <c>pool</c> is a data field, not a slot: it is the <c>GThreadPool</c> the
/// bin was going to use for asynchronous state changes and is unused today.
/// The reserved tail is <c>GST_PADDING - 2</c>, because
/// <c>deep_element_added</c> and <c>deep_element_removed</c> were carved out of
/// it in 1.10.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct BinClassRaw
{
    /// <summary>The <c>parent_class</c> field.</summary>
    internal ElementClassRaw ParentClass;

    /// <summary>The <c>pool</c> field, a <c>GThreadPool*</c>.</summary>
    internal nint Pool;

    /// <summary>The <c>element_added</c> slot.</summary>
    internal nint ElementAdded;

    /// <summary>The <c>element_removed</c> slot.</summary>
    internal nint ElementRemoved;

    /// <summary>The <c>add_element</c> slot.</summary>
    internal nint AddElement;

    /// <summary>The <c>remove_element</c> slot.</summary>
    internal nint RemoveElement;

    /// <summary>The <c>handle_message</c> slot.</summary>
    internal nint HandleMessage;

    /// <summary>The <c>do_latency</c> slot.</summary>
    internal nint DoLatency;

    /// <summary>The <c>deep_element_added</c> slot.</summary>
    internal nint DeepElementAdded;

    /// <summary>The <c>deep_element_removed</c> slot.</summary>
    internal nint DeepElementRemoved;

    /// <summary>The <c>_gst_reserved</c> field.</summary>
    private GstReservedArray _gstReserved;

    /// <summary>
    /// Gets the byte offset of <see cref="HandleMessage"/> within the class
    /// struct, which is what a subclass declares the slot with.
    /// </summary>
    internal static int HandleMessageOffset { get; } = MeasureHandleMessageOffset();

    private static unsafe int MeasureHandleMessageOffset()
    {
        BinClassRaw probe = default;
        return (int)((byte*)&probe.HandleMessage - (byte*)&probe);
    }

    /// <summary>Inline storage of the 2 elements of the <c>_gst_reserved</c> field of <c>GstBinClass</c>.</summary>
    [InlineArray(2)]
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
