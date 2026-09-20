using System.Runtime.CompilerServices;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// Where the fields a class declares itself begin inside one of its instances.
/// </summary>
/// <remarks>
/// <para>
/// A C instance structure embeds the instance structure of its base type by
/// value and first, and a type registers <c>sizeof</c> of that structure as its
/// instance size, so the first field a class declares itself starts at the
/// instance size of its parent type. That size is read from the running library
/// rather than mirrored: it is the one term of the arithmetic that differs
/// between ABIs — <c>GstPad</c> is 512 bytes where a C <c>long</c> is 32 bits
/// wide and 520 where it is 64 — and no literal in this repository could be
/// right on both.
/// </para>
/// <para>
/// The other term is the offset of the field inside the generated
/// <c>&lt;Class&gt;OwnFieldsRaw</c> mirror, measured by
/// <see cref="OffsetOf{TStruct, TField}"/> the way a class struct slot is
/// measured by <see cref="ClassSlot.OffsetOf{TClass}"/>, so that no accessor
/// can drift from the mirror it reads through.
/// </para>
/// </remarks>
internal static class InstanceLayout
{
    /// <summary>
    /// Returns the instance size the running library registered for a type.
    /// </summary>
    /// <param name="type">The <c>GType</c> to ask about.</param>
    /// <returns>The instance size in bytes.</returns>
    /// <exception cref="InvalidOperationException">
    /// The type is not registered, which <c>g_type_query</c> answers by zeroing
    /// the whole structure.
    /// </exception>
    internal static int SizeOf(nuint type)
    {
        GObjectNative.TypeQuery(type, out GTypeQuery query);
        if (query.InstanceSize == 0)
        {
            throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"g_type_query reported no instance size for the type 0x{type:x}; the type is not registered."));
        }

        return (int)query.InstanceSize;
    }

    /// <summary>Returns the byte offset of one field within a mirror.</summary>
    /// <typeparam name="TStruct">The mirror.</typeparam>
    /// <typeparam name="TField">The type of the field.</typeparam>
    /// <param name="origin">The start of the mirror.</param>
    /// <param name="field">The field to measure.</param>
    /// <returns>The offset in bytes.</returns>
    internal static int OffsetOf<TStruct, TField>(ref TStruct origin, ref TField field)
        where TStruct : struct =>
        (int)Unsafe.ByteOffset(
            ref Unsafe.As<TStruct, byte>(ref origin),
            ref Unsafe.As<TField, byte>(ref field));
}

/// <summary>One instance field the generator exposed, as the ABI probes read it.</summary>
/// <param name="Name">The name the gir gives the field, for example <c>segment</c>.</param>
/// <param name="Offset">The offset the mirror measured for it, from the first own field.</param>
internal readonly record struct InstanceFieldProbe(string Name, int Offset);

/// <summary>
/// One class whose own instance fields are mirrored, as the ABI probes read it.
/// </summary>
/// <remarks>
/// The generated mirrors describe themselves here so that the probes are
/// written once rather than once per class: a field that joins the
/// <c>instanceFields</c> allowlist joins the probes with it, which is what keeps
/// the two from drifting apart.
/// </remarks>
internal readonly unsafe struct InstanceMirrorProbe
{
    /// <summary>Initialises a new row.</summary>
    /// <param name="cName">The C name of the instance structure.</param>
    /// <param name="getGType">The <c>get_type</c> function of the class.</param>
    /// <param name="parentGetGType">The <c>get_type</c> function of its parent class.</param>
    /// <param name="ownSize">The size of the mirror of the fields the class declares itself.</param>
    /// <param name="fields">The fields of the class that carry an accessor.</param>
    internal InstanceMirrorProbe(
        string cName,
        delegate*<nuint> getGType,
        delegate*<nuint> parentGetGType,
        int ownSize,
        InstanceFieldProbe[] fields)
    {
        CName = cName;
        GetGType = getGType;
        ParentGetGType = parentGetGType;
        OwnSize = ownSize;
        Fields = fields;
    }

    /// <summary>Gets the C name of the instance structure, for example <c>GstBaseSink</c>.</summary>
    internal string CName { get; }

    /// <summary>Gets the <c>get_type</c> function of the class.</summary>
    internal delegate*<nuint> GetGType { get; }

    /// <summary>Gets the <c>get_type</c> function of the parent class.</summary>
    internal delegate*<nuint> ParentGetGType { get; }

    /// <summary>
    /// Gets the size of the mirror of the own fields, which the instance size of
    /// the parent plus this has to equal the instance size of the class.
    /// </summary>
    internal int OwnSize { get; }

    /// <summary>Gets the fields of the class that carry a generated accessor.</summary>
    internal InstanceFieldProbe[] Fields { get; }
}
