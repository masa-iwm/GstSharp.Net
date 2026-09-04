using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Planning;

/// <summary>
/// How the trampoline of a virtual method projects one argument it is handed,
/// and how the chain-up helper hands it back to the parent slot.
/// </summary>
/// <remarks>
/// The buckets are the ones <c>docs/subclassing.md</c> §4.3 names, decided
/// once by the planner so that the emitter never re-derives them from the gir.
/// </remarks>
internal enum VfuncBucket
{
    /// <summary>A blittable value, an enumeration or a string: a cast either way.</summary>
    Cast,

    /// <summary>A GObject the call lends, wrapped with <c>Transfer.None</c>.</summary>
    BorrowGObject,

    /// <summary>
    /// A mini object the call lends, wrapped by a true borrow that takes no
    /// reference (<c>Gst.Interop.Borrowed</c>), because an override may have to
    /// write to what it was given.
    /// </summary>
    BorrowMiniObject,

    /// <summary>A boxed or opaque record the call lends, wrapped with <c>Transfer.None</c>.</summary>
    BorrowWrapper,

    /// <summary>
    /// A handle the call gives up: the trampoline adopts it with
    /// <c>Transfer.Full</c> and the chain-up hands the parent slot a value
    /// minted for it.
    /// </summary>
    Adopt,

    /// <summary>A blittable value, an enumeration or a wrapper the call produces.</summary>
    OutScalar,

    /// <summary>A handle the call produces, whose reference the caller takes over.</summary>
    OutHandle,

    /// <summary>
    /// A handle the call is given and may replace. The value on entry is
    /// borrowed and the value on exit is written back.
    /// </summary>
    InOutHandle,
}

/// <summary>
/// What the trampoline does with the value the managed override returned.
/// </summary>
internal enum VfuncReturnBucket
{
    /// <summary>Nothing; the slot is <c>void</c>.</summary>
    Void,

    /// <summary>A blittable value, an enumeration or a string: a cast.</summary>
    Cast,

    /// <summary>A GObject the caller takes over, which is referenced once more on the way out.</summary>
    OwnedGObject,

    /// <summary>A mini object the caller takes over, which is referenced once more on the way out.</summary>
    OwnedMiniObject,

    /// <summary>
    /// A handle the caller only borrows, whose reference stays with the
    /// wrapper the override answered.
    /// </summary>
    BorrowedHandle,
}

/// <summary>One argument of a virtual method.</summary>
/// <param name="Argument">The marshalling of the argument.</param>
/// <param name="Bucket">What the trampoline and the chain-up do with it.</param>
/// <param name="IsIdentity">
/// Whether the caller of the slot compares the handle it gets back with one it
/// already holds and only releases the input when the two differ, which makes a
/// reference taken on an unchanged answer a leak. Named by the overlay key
/// <c>vfuncIdentityBuffers</c>.
/// </param>
/// <param name="IdentityReference">
/// The raw parameter whose handle an identity preserving <em>out</em> argument
/// is compared with, or <see langword="null"/> when the comparison is against
/// the value the pointer held on entry, which is what an <em>inout</em>
/// argument compares with.
/// </param>
internal sealed record VfuncArgument(
    ArgumentPlan Argument,
    VfuncBucket Bucket,
    bool IsIdentity = false,
    string? IdentityReference = null);

/// <summary>
/// Everything the emitter needs to write one <c>&lt;virtual-method&gt;</c>: the
/// managed <c>OnX</c> member, the chain-up helpers and the trampoline the class
/// struct slot is set to.
/// </summary>
/// <remarks>
/// A virtual method crosses the boundary in both directions. Native code calls
/// into managed code through the trampoline, which is the direction a signal
/// handler is planned in; and the chain-up calls back out through the slot of
/// the parent class, which is the direction a method is planned in. The plan
/// therefore carries one bucket per argument that both renderings read.
/// </remarks>
internal sealed class VirtualMethodPlan
{
    /// <summary>Gets the gir declaration.</summary>
    internal required GirVirtualMethod Method { get; init; }

    /// <summary>Gets the key the overlays address the slot by, as <c>Gst.Element::change_state</c>.</summary>
    internal required string OverlayKey { get; init; }

    /// <summary>Gets the C# name of the slot, as <c>ChangeState</c>.</summary>
    internal required string Name { get; init; }

    /// <summary>Gets the name of the mirror field the slot lives in.</summary>
    internal required string SlotMember { get; init; }

    /// <summary>Gets the C# name the raw instance pointer carries in the generated helpers.</summary>
    internal required string InstanceName { get; init; }

    /// <summary>Gets the arguments, without the instance.</summary>
    internal required IReadOnlyList<VfuncArgument> Arguments { get; init; }

    /// <summary>Gets the value the slot answers.</summary>
    internal required ReturnPlan Return { get; init; }

    /// <summary>Gets what the trampoline does with the answer.</summary>
    internal required VfuncReturnBucket ReturnBucket { get; init; }

    /// <summary>
    /// Gets the C# expression a chain-up answers when the parent class leaves
    /// the slot null, or <see langword="null"/> when there is no documented
    /// default and the chain-up throws instead.
    /// </summary>
    internal string? NullSlotDefault { get; init; }
}
