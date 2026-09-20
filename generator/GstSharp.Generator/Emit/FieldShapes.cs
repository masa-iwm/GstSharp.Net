using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>
/// The shape of a gir field, as the ledgers of the skip report name it.
/// </summary>
/// <remarks>
/// A record and a class hold their fields the same way, and both ledgers
/// measure the same thing: what the C structure carries and the generated
/// surface does not. The rule lives here so that the two read one definition of
/// a pointer, an embedded structure and a callback slot rather than two that
/// drift apart.
/// </remarks>
internal sealed class FieldShapes
{
    /// <summary>
    /// The reason a field carries when nothing more precise is known about it.
    /// It is the one the record ledger refines, because it is the only one that
    /// names no shape and therefore measures nothing.
    /// </summary>
    internal const string OtherReason = "Other";

    private readonly Repository _repository;
    private readonly TypeMap _types;

    /// <summary>Initializes a new instance of the <see cref="FieldShapes"/> class.</summary>
    /// <param name="repository">The loaded gir repository.</param>
    /// <param name="types">The type map.</param>
    internal FieldShapes(Repository repository, TypeMap types)
    {
        _repository = repository;
        _types = types;
    }

    /// <summary>Names one field the way the overlays address it.</summary>
    /// <param name="owner">The declaring record or class.</param>
    /// <param name="field">The field to name.</param>
    /// <returns>The <c>c:type</c> of the owner and the gir name of the field.</returns>
    /// <remarks>
    /// A field of a reserved ABI union is addressed by the structure it grows
    /// and the field itself, with the union and its structure transparent, which
    /// is the same shape the accessor of one is named after.
    /// </remarks>
    internal static string SkipKey(GirTypeDeclaration owner, GirField field) =>
        (owner.CType is { Length: > 0 } cType ? cType : owner.Name) + "." + field.Name;

    /// <summary>
    /// Adds the version a field arrived in to the shape that kept it off the
    /// generated surface, for the fields that arrived after the support floor.
    /// </summary>
    /// <param name="field">The field being reported.</param>
    /// <param name="reason">What the ledger reports it under.</param>
    /// <returns>The reason, with the version when there is one to state.</returns>
    /// <remarks>
    /// The shape alone would read as a gap the binding could close, and this
    /// one it cannot: an accessor of a field the library on an older machine
    /// does not have reads past the end of the structure. The line says which
    /// version put it there, so a reader of the ledger can tell the two apart.
    /// </remarks>
    internal static string WithSince(GirField field, string reason) =>
        Availability.SinceVersion(field) is { } version ? reason + ", since " + version : reason;

    /// <summary>
    /// Names the shape that kept a field out of the generated surface.
    /// </summary>
    /// <param name="ns">The gir namespace of the declaring structure.</param>
    /// <param name="field">The field to classify.</param>
    /// <param name="bound">The names of the fields the structure binds.</param>
    /// <returns>
    /// The reason, or <see langword="null"/> when the field is bound after all.
    /// </returns>
    internal string? Reason(GirNamespace ns, GirField field, IReadOnlySet<string> bound)
    {
        if (bound.Contains(field.Name))
        {
            return null;
        }

        if (field.Callback is not null)
        {
            return "Callback";
        }

        if (field.Type is not { } type)
        {
            return OtherReason;
        }

        if (type is GirArrayRef { FixedSize: not null } array)
        {
            if (array.ElementType is not { } element)
            {
                return OtherReason;
            }

            if (element.IsPointer || _types.Map(element, ns).Kind == MarshalKind.Pointer)
            {
                return "InlineArray(pointer element)";
            }

            return IsValueElement(ns, type) ? OtherReason : "InlineArray(struct element)";
        }

        // An array without a fixed size decays to a pointer in the C structure,
        // which is what it is reported as: the length is nowhere in the layout.
        if (type.IsPointer || type is GirArrayRef)
        {
            return "Pointer";
        }

        if (_repository.Resolve(type, ns) is { } reference)
        {
            // A field the gir spells without a star and that names a type is
            // either a function pointer slot or a structure laid into the
            // declaring one; a class only ever appears by value as the
            // instance structure of the base type.
            switch (_repository.ResolveAlias(reference))
            {
                case { Kind: GirSymbolKind.Callback }:
                    return "Callback";

                case { Kind: GirSymbolKind.Record or GirSymbolKind.Class or GirSymbolKind.Interface }:
                    return "EmbeddedStruct";

                default:
                    break;
            }
        }

        if (_types.Map(type, ns).Kind == MarshalKind.Pointer)
        {
            return "Pointer";
        }

        return OtherReason;
    }

    /// <summary>
    /// Tests whether a field is laid into its structure as the instance
    /// structure of a base class, which is the inheritance chain rather than a
    /// member the binding left out.
    /// </summary>
    /// <param name="ns">The gir namespace of the declaring class.</param>
    /// <param name="field">The field to test.</param>
    /// <returns><see langword="true"/> when the field is a base instance.</returns>
    /// <remarks>
    /// A class only ever appears by value as the head of the structure that
    /// derives from it, so the shape says what the field is and neither its
    /// name nor its position has to be trusted: a gir spells the same thing
    /// <c>parent</c>, <c>object</c>, <c>element</c> and <c>parent_instance</c>.
    /// </remarks>
    internal bool IsBaseInstance(GirNamespace ns, GirField field) =>
        field.Type is { IsPointer: false } type
        && _repository.Resolve(type, ns) is { } reference
        && _repository.ResolveAlias(reference) is { Kind: GirSymbolKind.Class };

    /// <summary>
    /// Tests whether a field lays a union into its structure by value.
    /// </summary>
    /// <param name="ns">The gir namespace of the declaring structure.</param>
    /// <param name="field">The field to test.</param>
    /// <returns><see langword="true"/> when the field is an embedded union.</returns>
    /// <remarks>
    /// It is asked of the fields no shape accounted for, so that a union laid
    /// into an instance reads as the structure it is rather than as a value
    /// nothing has an account of. A lock is the case that occurs: GLib spells
    /// <c>GMutex</c> a union and <c>GRecMutex</c> a record, and the two sit in
    /// an instance the same way.
    /// </remarks>
    internal bool IsEmbeddedUnion(GirNamespace ns, GirField field) =>
        field.Type is { IsPointer: false } type
        && _repository.Resolve(type, ns) is { } reference
        && _repository.ResolveAlias(reference) is { Kind: GirSymbolKind.Union };

    /// <summary>
    /// Tests whether a field holds a value a reader could be handed on its own:
    /// a scalar, a <c>GType</c>, a quark or an enumeration.
    /// </summary>
    /// <param name="ns">The gir namespace of the declaring structure.</param>
    /// <param name="field">The field to test.</param>
    /// <returns><see langword="true"/> when the field is a plain value.</returns>
    /// <remarks>
    /// It is asked of the fields no shape accounted for, which is where a plain
    /// <c>gint</c>, a <c>gboolean</c> and an enumeration all land. Saying so
    /// keeps the catch all for the fields this rule really has no account of.
    /// </remarks>
    internal bool IsScalar(GirNamespace ns, GirField field) =>
        field.Type is { } type
        && _types.Map(type, ns).Kind
            is MarshalKind.Blittable
            or MarshalKind.Boolean
            or MarshalKind.GType
            or MarshalKind.Quark
            or MarshalKind.Enum
            or MarshalKind.Flags;

    /// <summary>
    /// Tests whether the elements of a fixed size field are values a wrapper
    /// can hand out, that is scalars or an enumeration this module generates.
    /// </summary>
    /// <param name="ns">The gir namespace of the declaring structure.</param>
    /// <param name="type">The type of the field.</param>
    /// <returns><see langword="true"/> when the storage carries API.</returns>
    /// <remarks>
    /// An array of pointers or of embedded structures is left out: the
    /// elements would be bare addresses or mirrors that only the interop layer
    /// can read, which is the same line the scalar accessors draw. The three
    /// fields that fall here are <c>GstVideoFormatInfo.tile_info</c> and the
    /// <c>data</c> and <c>map</c> of <c>GstVideoFrame</c>, whose wrapper is
    /// hand written and answers them already.
    /// </remarks>
    internal bool IsValueElement(GirNamespace ns, GirTypeRef? type)
    {
        if (type is not GirArrayRef { ElementType: { } element } || element.IsPointer)
        {
            return false;
        }

        MappedType mapped = _types.Map(element, ns);
        return mapped.Kind switch
        {
            MarshalKind.Blittable or MarshalKind.Boolean or MarshalKind.GType or MarshalKind.Quark => true,
            MarshalKind.Enum or MarshalKind.Flags => mapped.Symbol is { } symbol
                && string.Equals(symbol.Namespace.Name, ns.Name, StringComparison.Ordinal),
            _ => false,
        };
    }
}
