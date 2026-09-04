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

/// <summary>One slot of a mirrored class struct, as the ABI probes read it.</summary>
/// <param name="Name">The name the gir gives the slot, for example <c>change_state</c>.</param>
/// <param name="Offset">The byte offset the mirror measured for it.</param>
internal readonly record struct ClassSlotProbe(string Name, int Offset);

/// <summary>
/// One mirrored class struct, as the ABI probes read it.
/// </summary>
/// <remarks>
/// The generated mirrors describe themselves here so that the probe of the
/// integration tests is written once rather than once per class: a class that
/// joins the allowlist joins the probe with it, which is what keeps the two
/// from drifting apart.
/// </remarks>
internal readonly unsafe struct ClassStructProbe
{
    /// <summary>Initialises a new row.</summary>
    /// <param name="cName">The C name of the class struct.</param>
    /// <param name="getGType">The <c>get_type</c> function of the class it belongs to.</param>
    /// <param name="size">The size the mirror occupies.</param>
    /// <param name="slots">The overridable slots of the class itself.</param>
    internal ClassStructProbe(string cName, delegate*<nuint> getGType, int size, ClassSlotProbe[] slots)
    {
        CName = cName;
        GetGType = getGType;
        Size = size;
        Slots = slots;
    }

    /// <summary>Gets the C name of the class struct, for example <c>GstElementClass</c>.</summary>
    internal string CName { get; }

    /// <summary>Gets the <c>get_type</c> function of the class the struct belongs to.</summary>
    internal delegate*<nuint> GetGType { get; }

    /// <summary>Gets the size of the mirror, which <c>g_type_query</c> has to agree with.</summary>
    internal int Size { get; }

    /// <summary>Gets the overridable slots the class declares itself.</summary>
    internal ClassSlotProbe[] Slots { get; }
}
