namespace GstSharp.Generator.GirParsing.Model;

/// <summary>
/// A <c>&lt;type&gt;</c> reference, or a <c>&lt;varargs/&gt;</c> marker.
/// </summary>
internal class GirTypeRef
{
    /// <summary>The gir type reference used for an untyped <c>gpointer</c>.</summary>
    internal const string GPointerName = "gpointer";

    /// <summary>Gets the verbatim <c>name</c> attribute (<c>guint64</c>, <c>Gst.Buffer</c>, ...).</summary>
    internal string? Name { get; init; }

    /// <summary>Gets the verbatim <c>c:type</c> attribute.</summary>
    internal string? CType { get; init; }

    /// <summary>Gets the nested <c>&lt;type&gt;</c> children of a container type.</summary>
    internal IReadOnlyList<GirTypeRef> InnerTypes { get; init; } = [];

    /// <summary>Gets a value indicating whether this stands for <c>&lt;varargs/&gt;</c>.</summary>
    internal bool IsVarArgs { get; init; }

    /// <summary>Gets a value indicating whether the C type is a pointer.</summary>
    internal bool IsPointer => CType is not null && CType.EndsWith('*');
}

/// <summary>
/// An <c>&lt;array&gt;</c> reference.
/// </summary>
internal sealed class GirArrayRef : GirTypeRef
{
    /// <summary>Gets the index of the parameter holding the length, if any.</summary>
    internal int? LengthParameterIndex { get; init; }

    /// <summary>Gets a value indicating whether the array is <c>zero-terminated</c>.</summary>
    internal bool IsZeroTerminated { get; init; }

    /// <summary>Gets the <c>fixed-size</c> attribute, if any.</summary>
    internal int? FixedSize { get; init; }

    /// <summary>Gets the element type, if the gir declared one.</summary>
    internal GirTypeRef? ElementType => InnerTypes.Count > 0 ? InnerTypes[0] : null;
}
