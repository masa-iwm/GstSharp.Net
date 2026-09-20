using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>One instance field of a class that the allowlist exposes.</summary>
/// <param name="Field">The field, as the gir spells it.</param>
/// <param name="Key">The key the overlays address it by.</param>
/// <param name="Entry">The allowlist entry.</param>
/// <param name="Member">The name of the generated accessor, for example <c>GetSegment</c>.</param>
/// <param name="Mirror">The member of the own fields mirror the field is laid out as.</param>
/// <param name="Overrides">
/// The names of the managed overrides that form the window a read is consistent
/// in, for example <c>OnRender</c>.
/// </param>
internal sealed record InstanceFieldPlan(
    GirField Field,
    string Key,
    InstanceField Entry,
    string Member,
    string Mirror,
    IReadOnlyList<string> Overrides);

/// <summary>
/// Emits the mirror of the fields a class declares itself, and the accessors of
/// the ones the allowlist exposes.
/// </summary>
/// <remarks>
/// <para>
/// A wrapper holds a native instance and mirrors no part of it, so a field of a
/// class is out of reach unless something says where it is. The offset is the sum
/// of two terms, neither of which is a literal in this repository: the instance
/// size the running library registered for the parent type, read through
/// <c>g_type_query</c>, and the offset of the field inside the mirror of the
/// fields the class declares itself, measured from the mirror at run time. The
/// first term is what makes the arithmetic right on an ABI where a C
/// <c>long</c> is 64 bits wide without a mirror of the head; the second is what
/// keeps an accessor from drifting from the layout it reads through.
/// </para>
/// <para>
/// The mirror lays out every field the gir declares after the parent instance,
/// the ones it marks private included, because it is the total size of it that
/// the integration tests compare with the instance size of the class: a field
/// the mirror leaves out or types too narrow moves that total. There is
/// therefore no truncation here. A field the closed table below cannot type is
/// an error.
/// </para>
/// </remarks>
internal sealed class InstanceFieldEmitter
{
    /// <summary>The directory the mirrors are written to, below <c>Generated</c>.</summary>
    internal const string DirectoryName = "InstanceFields";

    /// <summary>The suffix every mirror carries.</summary>
    internal const string MirrorSuffix = "OwnFieldsRaw";

    /// <summary>
    /// The one embedded structure wave 1 of the allowlist admits. Whether the
    /// copy of an embedded value is a flat duplication of its storage is a
    /// decision per type, so the admitted ones are named rather than derived.
    /// </summary>
    private const string AdmittedStruct = "GstSegment";

    private readonly Repository _repository;
    private readonly Overlays _overlays;
    private readonly EmissionCensus _census;
    private readonly DiagnosticBag _diagnostics;
    private readonly FieldShapes _shapes;
    private readonly Dictionary<string, HashSet<string>> _emittedVirtuals;
    private readonly List<RegistryRow> _rows = [];

    /// <summary>Initialises the emitter.</summary>
    /// <param name="repository">The loaded girs, for resolving field types.</param>
    /// <param name="overlays">The overlay configuration.</param>
    /// <param name="census">The census of the run.</param>
    /// <param name="diagnostics">Where a refused entry and an unlayable field are reported.</param>
    /// <param name="shapes">The shape rule the ledgers read, shared with the class emitter.</param>
    /// <param name="emittedVirtuals">
    /// The managed override names every subclassable class emitted, keyed by
    /// qualified gir name, which is what an <c>overrides</c> entry is checked
    /// against.
    /// </param>
    internal InstanceFieldEmitter(
        Repository repository,
        Overlays overlays,
        EmissionCensus census,
        DiagnosticBag diagnostics,
        FieldShapes shapes,
        Dictionary<string, HashSet<string>> emittedVirtuals)
    {
        _repository = repository;
        _overlays = overlays;
        _census = census;
        _diagnostics = diagnostics;
        _shapes = shapes;
        _emittedVirtuals = emittedVirtuals;
    }

    /// <summary>Returns the name of the mirror of the own fields of one class.</summary>
    /// <param name="ns">The gir namespace of the class.</param>
    /// <param name="typeName">The C# name of the wrapper.</param>
    /// <returns>The fully qualified C# type name.</returns>
    internal static string MirrorNameOf(GirNamespace ns, string typeName) =>
        ModuleMap.ClrNamespaceOf(ns.Name) + "." + typeName + MirrorSuffix;

    /// <summary>
    /// Reads the allowlist for one class and says which of its fields carry an
    /// accessor.
    /// </summary>
    /// <param name="ns">The gir namespace of the class.</param>
    /// <param name="declaration">The class being emitted.</param>
    /// <param name="taken">The member names the surface of the class already carries.</param>
    /// <returns>One plan per exposed field, in gir order.</returns>
    /// <remarks>
    /// The allowlist is read in front of every filter the ledger applies, so that
    /// a key naming a field the gir marks private, or the instance structure of
    /// the base class, is refused for what it names rather than going quietly
    /// stale.
    /// </remarks>
    internal IReadOnlyList<InstanceFieldPlan> Plan(
        GirNamespace ns,
        GirClass declaration,
        IReadOnlyCollection<string> taken)
    {
        List<InstanceFieldPlan> plans = [];
        foreach (GirField field in declaration.Fields)
        {
            string key = FieldShapes.SkipKey(declaration, field);
            if (_overlays.GetInstanceField(key) is not { } entry)
            {
                continue;
            }

            // The key matched a field of a class this run emits, so it is not
            // stale whatever the checks below make of it.
            _census.InstanceFieldKey(key);

            if (entry.ShapeFault is { } fault)
            {
                _diagnostics.Error("GEN0060", $"The instance field '{key}' {fault}; the entry says too little to act on.");
                continue;
            }

            if (_shapes.IsBaseInstance(ns, field))
            {
                _diagnostics.Error(
                    "GEN0060",
                    $"The instance field '{key}' names the instance structure of the base class, which is the "
                    + "inheritance chain rather than a field.");
                continue;
            }

            if (_overlays.GetFieldSkip(key) is not null)
            {
                _diagnostics.Error(
                    "GEN0060",
                    $"The instance field '{key}' is also registered under 'fieldSkips', which says another member "
                    + "answers it; the two entries say different things about the same field.");
                continue;
            }

            if (field.IsPrivate || !field.IsReadable || field.Name.StartsWith('_'))
            {
                _diagnostics.Error(
                    "GEN0061",
                    $"The instance field '{key}' is marked private or unreadable in the gir, and such fields are "
                    + "not exposed; a header that documents it as public API needs a key that says so.");
                continue;
            }

            if (Availability.SinceVersion(field) is { } version)
            {
                _diagnostics.Error(
                    "GEN0061",
                    $"The instance field '{key}' arrived in {version}, which is above the support floor; an "
                    + "accessor of it would read past the end of the structure on an older library.");
                continue;
            }

            if (ShapeRefusal(ns, field) is { } refusal)
            {
                _diagnostics.Error("GEN0061", $"{refusal} instance fields are not exposed; '{key}' is one.");
                continue;
            }

            string member = "Get"
                + (entry.Name is { Length: > 0 } renamed ? renamed : NameMapper.ToPascalCase(field.Name));
            if (IsTaken(member, taken))
            {
                _diagnostics.Error(
                    "GEN0063",
                    $"The accessor '{member}' of the instance field '{key}' collides with a member the class "
                    + $"'{ns.Name}.{declaration.Name}' already carries; the member shipped first, so the field "
                    + "takes a 'name'.");
                continue;
            }

            if (Overrides(ns, declaration, key, entry) is not { } overrides)
            {
                continue;
            }

            plans.Add(new InstanceFieldPlan(
                field,
                key,
                entry,
                member,
                NameMapper.ToPascalCase(field.Name),
                overrides));
        }

        return plans;
    }

    /// <summary>Writes the accessors of the exposed fields of one class.</summary>
    /// <param name="writer">The writer of the class.</param>
    /// <param name="module">The module the class belongs to.</param>
    /// <param name="ns">The gir namespace of the class.</param>
    /// <param name="declaration">The class being emitted.</param>
    /// <param name="typeName">The C# name of the wrapper.</param>
    /// <param name="plans">The exposed fields.</param>
    internal void WriteAccessors(
        CodeWriter writer,
        ModuleInfo module,
        GirNamespace ns,
        GirClass declaration,
        string typeName,
        IReadOnlyList<InstanceFieldPlan> plans)
    {
        string mirror = MirrorNameOf(ns, typeName);
        string cName = declaration.CType is { Length: > 0 } declared ? declared : declaration.Name;
        foreach (InstanceFieldPlan plan in plans)
        {
            _census.Emitted(module.GirNamespace, "instance field");
            writer.WriteLine();
            writer.WriteLine(
                "/// <summary>Answers a copy of the <c>" + plan.Field.Name + "</c> field of <c>" + cName
                + "</c>.</summary>");
            writer.WriteLine("/// <remarks>");
            writer.WriteLine("/// <para>");
            writer.WriteLine("/// The structure is embedded in the instance this wrapper points at. What comes");
            writer.WriteLine("/// back is a copy of it that the caller owns and disposes, so it stays good after");
            writer.WriteLine("/// the instance is gone, and writing into it changes nothing native.");
            writer.WriteLine("/// </para>");
            writer.WriteLine("/// <para>");
            writer.WriteLine(
                "/// The library rewrites the field under " + plan.Entry.Lock + ", which managed code cannot");
            writer.WriteLine("/// take, so the copy is only guaranteed consistent when it is read on the");
            writer.WriteLine("/// streaming thread, inside");
            writer.WriteLine("/// " + Links(plan.Overrides) + ".");
            writer.WriteLine("/// A read from any other thread may mix the fields of two segments; it is never");
            writer.WriteLine("/// unsafe, because the structure is flat and owns no pointer.");
            writer.WriteLine("/// </para>");
            writer.WriteLine("/// </remarks>");
            writer.WriteLine("/// <returns>A copy of the <c>" + plan.Field.Name + "</c> field.</returns>");
            writer.WriteLine("/// <exception cref=\"System.ObjectDisposedException\">The wrapper was disposed.</exception>");
            writer.WriteLine("public Gst.Segment " + plan.Member + "()");
            writer.OpenBlock();
            writer.WriteLine(
                "Gst.Segment value = Gst.Segment.FromNative(");
            writer.WriteLine(
                "    Handle + " + mirror + ".OwnOffset + " + mirror + "." + plan.Mirror + "Offset,");
            writer.WriteLine("    Gst.Interop.Transfer.None)");
            writer.WriteLine(
                "    ?? throw new System.InvalidOperationException(\"The '" + plan.Field.Name + "' field of "
                + cName + " is null.\");");
            writer.WriteLine("System.GC.KeepAlive(this);");
            writer.WriteLine("return value;");
            writer.CloseBlock();
        }
    }

    /// <summary>
    /// Emits the mirror of the fields one class declares itself, and records the
    /// row the ABI probes read it through.
    /// </summary>
    /// <param name="module">The module the class belongs to.</param>
    /// <param name="ns">The gir namespace of the class.</param>
    /// <param name="declaration">The class being emitted.</param>
    /// <param name="typeName">The C# name of the wrapper.</param>
    /// <param name="parentType">The C# name of the wrapper of the parent class.</param>
    /// <param name="plans">The exposed fields of the class.</param>
    /// <returns>The generated file.</returns>
    internal GeneratedFile EmitMirror(
        ModuleInfo module,
        GirNamespace ns,
        GirClass declaration,
        string typeName,
        string parentType,
        IReadOnlyList<InstanceFieldPlan> plans)
    {
        string mirror = typeName + MirrorSuffix;
        string cName = declaration.CType is { Length: > 0 } declared ? declared : declaration.Name;

        CodeWriter writer = new();
        writer.WriteLine("// <auto-generated/>");
        writer.WriteLine("// Generated by GstSharp.Generator from " + ns.Name + "-" + ns.Version + ".gir. Do not edit.");
        writer.WriteLine();
        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        writer.WriteLine("using System.Runtime.InteropServices;");
        writer.WriteLine();
        writer.WriteLine("namespace " + module.ClrNamespace + ";");
        writer.WriteLine();
        writer.WriteLine(
            "/// <summary>The native layout of the fields <c>" + cName + "</c> declares itself.</summary>");
        writer.WriteLine("/// <remarks>");
        writer.WriteLine("/// <para>");
        writer.WriteLine("/// The instance structure of the base class is not laid out here: a C instance");
        writer.WriteLine("/// embeds it by value and first, and its size is what the library registered as");
        writer.WriteLine(
            "/// the instance size of <c>" + (declaration.Parent ?? "the parent class")
            + "</c>. <see cref=\"OwnOffset\"/> reads");
        writer.WriteLine("/// that from the running library rather than mirroring it: it is the term of the");
        writer.WriteLine("/// arithmetic that differs between ABIs.");
        writer.WriteLine("/// </para>");
        writer.WriteLine("/// <para>");
        writer.WriteLine("/// Every field the gir declares after the parent instance is laid out, the private");
        writer.WriteLine("/// ones and the reserved tail included, because it is the total size of this that");
        writer.WriteLine("/// the ABI probes compare with the instance size of the class.");
        writer.WriteLine("/// </para>");
        writer.WriteLine("/// </remarks>");
        writer.WriteLine("[StructLayout(LayoutKind.Sequential)]");
        writer.WriteLine("internal struct " + mirror);
        writer.OpenBlock();

        List<(string Name, int Length, string Element)> inlineArrays = [];
        bool first = true;
        foreach (GirField field in declaration.Fields)
        {
            if (_shapes.IsBaseInstance(ns, field))
            {
                continue;
            }

            string name = NameMapper.ToPascalCase(field.Name.TrimStart('_'));
            if (!first)
            {
                writer.WriteLine();
            }

            first = false;

            if (field.Type is GirArrayRef { FixedSize: > 0 } array)
            {
                string arrayType = name + "Array";
                inlineArrays.Add((arrayType, array.FixedSize.Value, ElementOf(array, ns, field)));
                writer.WriteLine("/// <summary>The <c>" + field.Name + "</c> field.</summary>");
                writer.WriteLine("private " + arrayType + " _" + char.ToLowerInvariant(name[0]) + name[1..] + ";");
                continue;
            }

            writer.WriteLine("/// <summary>The <c>" + field.Name + "</c> field.</summary>");
            writer.WriteLine("internal " + TypeOf(ns, field) + " " + name + ";");
        }

        writer.WriteLine();
        writer.WriteLine("/// <summary>Where the own fields begin, or -1 until the library has been asked.</summary>");
        writer.WriteLine("private static int _ownOffset = -1;");

        if (plans.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("static " + mirror + "()");
            writer.OpenBlock();
            writer.WriteLine(mirror + " probe = default;");
            foreach (InstanceFieldPlan plan in plans)
            {
                writer.WriteLine(
                    plan.Mirror + "Offset = Gst.GObject.InstanceLayout.OffsetOf(ref probe, ref probe."
                    + plan.Mirror + ");");
            }

            writer.CloseBlock();

            foreach (InstanceFieldPlan plan in plans)
            {
                writer.WriteLine();
                writer.WriteLine(
                    "/// <summary>Gets the byte offset of <see cref=\"" + plan.Mirror
                    + "\"/> from the first own field of the instance.</summary>");
                writer.WriteLine("internal static int " + plan.Mirror + "Offset { get; }");
            }
        }

        writer.WriteLine();
        writer.WriteLine("/// <summary>");
        writer.WriteLine("/// Gets the offset of the first field the class declares itself, which is the");
        writer.WriteLine("/// instance size the running library registered for the parent type.");
        writer.WriteLine("/// </summary>");
        writer.WriteLine("/// <remarks>");
        writer.WriteLine("/// It is read on first use rather than in the static constructor: an instance of");
        writer.WriteLine("/// the class exists whenever an accessor runs, so the library is initialised and");
        writer.WriteLine("/// the parent type registered, which a type initialiser could not promise. Two");
        writer.WriteLine("/// threads that race here read the same answer out of the library and write it.");
        writer.WriteLine("/// </remarks>");
        writer.WriteLine("internal static int OwnOffset");
        writer.OpenBlock();
        writer.WriteLine("get");
        writer.OpenBlock();
        writer.WriteLine("int offset = _ownOffset;");
        writer.WriteLine("if (offset < 0)");
        writer.OpenBlock();
        writer.WriteLine("offset = Gst.GObject.InstanceLayout.SizeOf(" + parentType + ".GetGType());");
        writer.WriteLine("_ownOffset = offset;");
        writer.CloseBlock();
        writer.WriteLine();
        writer.WriteLine("return offset;");
        writer.CloseBlock();
        writer.CloseBlock();

        foreach ((string arrayType, int length, string element) in inlineArrays)
        {
            writer.WriteLine();
            writer.WriteLine(
                "/// <summary>Inline storage of the " + Count(length) + " elements of the reserved tail of <c>"
                + cName + "</c>.</summary>");
            writer.WriteLine("[System.Runtime.CompilerServices.InlineArray(" + Count(length) + ")]");
            writer.WriteLine("private struct " + arrayType);
            writer.OpenBlock();
            writer.WriteLine("private " + element + " _element0;");
            writer.CloseBlock();
        }

        writer.CloseBlock();

        _rows.Add(new RegistryRow(
            cName,
            module.ClrNamespace + "." + typeName,
            parentType,
            module.ClrNamespace + "." + mirror,
            [.. plans.Select(static plan => (plan.Field.Name, plan.Mirror))]));
        _census.Emitted(module.GirNamespace, "instance field mirror");

        return new GeneratedFile(
            module.ProjectDirectory + "/Generated/" + DirectoryName + "/" + mirror + ".cs",
            writer.ToSource());
    }

    /// <summary>Emits the table the ABI probes of a module read, if it has one.</summary>
    /// <param name="module">The module to emit.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <returns>The generated file, or <see langword="null"/> when no class of the module exposes a field.</returns>
    internal GeneratedFile? EmitRegistry(ModuleInfo module, GirNamespace ns)
    {
        if (_rows.Count == 0)
        {
            return null;
        }

        CodeWriter writer = new();
        writer.WriteLine("// <auto-generated/>");
        writer.WriteLine("// Generated by GstSharp.Generator from " + ns.Name + "-" + ns.Version + ".gir. Do not edit.");
        writer.WriteLine();
        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        writer.WriteLine("using System.Runtime.CompilerServices;");
        writer.WriteLine();
        writer.WriteLine("namespace " + module.ClrNamespace + ";");
        writer.WriteLine();
        writer.WriteLine("/// <summary>The instance fields this module exposes, for the ABI probes.</summary>");
        writer.WriteLine("internal static unsafe class InstanceFieldRegistry");
        writer.OpenBlock();
        writer.WriteLine("/// <summary>Builds the table of the own field mirrors of the module.</summary>");
        writer.WriteLine("/// <returns>One row per class that exposes an instance field.</returns>");
        writer.WriteLine("internal static Gst.GObject.InstanceMirrorProbe[] CreateEntries() =>");
        writer.WriteLine("[");
        foreach (RegistryRow row in _rows)
        {
            writer.WriteLine("    new Gst.GObject.InstanceMirrorProbe(");
            writer.WriteLine("        \"" + row.CName + "\",");
            writer.WriteLine("        &" + row.Wrapper + ".GetGType,");
            writer.WriteLine("        &" + row.Parent + ".GetGType,");
            writer.WriteLine("        Unsafe.SizeOf<" + row.Mirror + ">(),");
            writer.WriteLine("        [");
            foreach ((string field, string member) in row.Fields)
            {
                writer.WriteLine(
                    "            new Gst.GObject.InstanceFieldProbe(\"" + field + "\", " + row.Mirror + "."
                    + member + "Offset),");
            }

            writer.WriteLine("        ]),");
        }

        writer.WriteLine("];");
        writer.CloseBlock();

        _rows.Clear();
        return new GeneratedFile(
            module.ProjectDirectory + "/Generated/" + DirectoryName + "/InstanceFieldRegistry.cs",
            writer.ToSource());
    }

    /// <summary>Names the shape that keeps a field off the allowlist, if any does.</summary>
    /// <param name="ns">The gir namespace of the class.</param>
    /// <param name="field">The field to classify.</param>
    /// <returns>The shape, or <see langword="null"/> when the field may be exposed.</returns>
    /// <remarks>
    /// The first wave of the allowlist admits one shape: an embedded
    /// <c>GstSegment</c>. A pointer would need a transfer and a nullability the
    /// gir does not carry, a callback is useless to managed code, a scalar would
    /// freeze a property whose value only means something under a lock, and
    /// whether the copy of some other embedded structure is a flat duplication of
    /// its storage is a decision per type.
    /// </remarks>
    private string? ShapeRefusal(GirNamespace ns, GirField field)
    {
        string reason = _shapes.Reason(ns, field, new HashSet<string>(StringComparer.Ordinal))
            ?? FieldShapes.OtherReason;
        if (!string.Equals(reason, "EmbeddedStruct", StringComparison.Ordinal))
        {
            return string.Equals(reason, FieldShapes.OtherReason, StringComparison.Ordinal)
                && _shapes.IsScalar(ns, field)
                    ? "Scalar"
                    : reason;
        }

        return string.Equals(CTypeOf(ns, field), AdmittedStruct, StringComparison.Ordinal)
            ? null
            : "Embedded " + (CTypeOf(ns, field) ?? "structure");
    }

    /// <summary>Returns the <c>c:type</c> the type of a field resolves to.</summary>
    /// <param name="ns">The gir namespace of the class.</param>
    /// <param name="field">The field to read.</param>
    /// <returns>The C name of the type, or <see langword="null"/> when there is none.</returns>
    private string? CTypeOf(GirNamespace ns, GirField field) =>
        field.Type?.CType is { Length: > 0 } cType
            ? cType
            : field.Type is { } type
                && _repository.Resolve(type, ns) is { Declaration: GirTypeDeclaration declared }
                    ? declared.CType
                    : null;

    /// <summary>
    /// Resolves the <c>overrides</c> of an entry onto the managed members they
    /// name.
    /// </summary>
    /// <param name="ns">The gir namespace of the class.</param>
    /// <param name="declaration">The class being emitted.</param>
    /// <param name="key">The key of the entry, for the diagnostic.</param>
    /// <param name="entry">The allowlist entry.</param>
    /// <returns>
    /// The member names, or <see langword="null"/> when one of them names no
    /// emitted override.
    /// </returns>
    /// <remarks>
    /// The name is not spelled by hand: it is the one the subclassing surface
    /// emitted, read out of the same table the <c>new</c> rule of an override is
    /// decided from, so a renamed slot cannot leave the remark pointing at a
    /// member that does not exist.
    /// </remarks>
    private IReadOnlyList<string>? Overrides(
        GirNamespace ns,
        GirClass declaration,
        string key,
        InstanceField entry)
    {
        _ = _emittedVirtuals.TryGetValue(ns.Name + "." + declaration.Name, out HashSet<string>? emitted);
        List<string> members = [];
        foreach (string slot in entry.Overrides ?? [])
        {
            GirVirtualMethod? method = null;
            foreach (GirVirtualMethod candidate in declaration.VirtualMethods)
            {
                if (string.Equals(candidate.Name, slot, StringComparison.Ordinal))
                {
                    method = candidate;
                    break;
                }
            }

            if (method is null || emitted is null || !emitted.Contains(NameMapper.ToPascalCase(method.Name)))
            {
                _diagnostics.Error(
                    "GEN0060",
                    $"The instance field '{key}' names the override '{slot}', which the subclassing surface of "
                    + $"'{ns.Name}.{declaration.Name}' does not emit; the remark would point at a member that "
                    + "does not exist.");
                return null;
            }

            members.Add("On" + NameMapper.ToPascalCase(method.Name));
        }

        return members;
    }

    /// <summary>
    /// Tests whether the surface of the class already carries a member of the
    /// name an accessor would take.
    /// </summary>
    /// <param name="member">The name of the accessor.</param>
    /// <param name="taken">The reserved names and the member keys of the class.</param>
    /// <returns><see langword="true"/> when the name is taken.</returns>
    /// <remarks>
    /// The accessor is added beside the planned surface rather than through it,
    /// so it has no signature the <c>new</c> rule could compare: a member of its
    /// name collides whatever shape it has, which is what the key test answers.
    /// </remarks>
    private static bool IsTaken(string member, IReadOnlyCollection<string> taken)
    {
        foreach (string key in taken)
        {
            if (SurfaceBuilder.KeyNames(key, member))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Spells the <c>see cref</c> list of the override window.</summary>
    /// <param name="members">The member names.</param>
    /// <returns>The prose list.</returns>
    private static string Links(IReadOnlyList<string> members)
    {
        List<string> links = [.. members.Select(static member => "<see cref=\"" + member + "\"/>")];
        return links.Count == 1
            ? links[0]
            : string.Join(", ", links.Take(links.Count - 1)) + " or " + links[^1];
    }

    /// <summary>Spells a count the way the invariant culture does.</summary>
    /// <param name="value">The count.</param>
    /// <returns>The decimal digits.</returns>
    private static string Count(int value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Returns the element type of a fixed size field of a mirror.</summary>
    /// <param name="array">The array the field declares.</param>
    /// <param name="ns">The gir namespace of the class.</param>
    /// <param name="field">The field, for the diagnostic.</param>
    /// <returns>The C# type name.</returns>
    private string ElementOf(GirArrayRef array, GirNamespace ns, GirField field) =>
        array.ElementType is { } element ? ScalarOf(ns, field, element) : Unlayable(field);

    /// <summary>Maps the type of one field of a mirror onto the storage it occupies.</summary>
    /// <param name="ns">The gir namespace of the class.</param>
    /// <param name="field">The field to lay out.</param>
    /// <returns>The C# type name.</returns>
    private string TypeOf(GirNamespace ns, GirField field)
    {
        if (field.Callback is not null)
        {
            return "nint";
        }

        return field.Type is { } type ? ScalarOf(ns, field, type) : Unlayable(field);
    }

    /// <summary>
    /// Maps one gir type onto the blittable type that occupies the same storage
    /// in an instance.
    /// </summary>
    /// <param name="ns">The gir namespace of the class.</param>
    /// <param name="field">The field, for the diagnostic.</param>
    /// <param name="type">The type to map.</param>
    /// <returns>The C# type name.</returns>
    /// <remarks>
    /// The table is closed on purpose. A mirror that guessed at a type it does
    /// not know would shift every field behind the one it guessed at, and the
    /// accessors read through it, so a type this does not name is an error rather
    /// than a pointer sized assumption.
    /// </remarks>
    private string ScalarOf(GirNamespace ns, GirField field, GirTypeRef type)
    {
        if (type.CType is { Length: > 0 } pointer && pointer.EndsWith('*'))
        {
            return "nint";
        }

        switch (type.Name)
        {
            case "gpointer" or "gconstpointer" or "utf8" or "filename":
                return "nint";
            case "gboolean" or "gint" or "gint32":
                return "int";
            case "guint" or "guint32":
                return "uint";
            case "gint64":
                return "long";
            case "guint64":
                return "ulong";
            case "gsize":
                return "nuint";
            case "gssize":
                return "nint";
            case "gfloat":
                return "float";
            case "gdouble":
                return "double";
            case "GLib.Mutex":
                return "Gst.GLib.MutexRaw";
            case "GLib.Cond":
                return "Gst.GLib.CondRaw";
            case "GLib.RecMutex":
                return "Gst.GLib.RecMutexRaw";
            case "GLib.RWLock":
                return "Gst.GLib.RWLockRaw";
            case "Gst.ClockID":
                return "nint";
            case "Gst.Segment":
                return "Gst.SegmentRaw";
            default:
                break;
        }

        if (type.Name is { } name && _repository.Resolve(name, ns) is { } symbol)
        {
            switch (_repository.ResolveAlias(symbol))
            {
                case { Kind: GirSymbolKind.Enumeration }:
                    return "int";

                case { Kind: GirSymbolKind.Callback }:
                    return "nint";

                default:
                    break;
            }
        }

        return Unlayable(field);
    }

    /// <summary>Reports a field the closed table cannot type.</summary>
    /// <param name="field">The field.</param>
    /// <returns>A pointer, so that the emitter can finish writing the file the run discards.</returns>
    private string Unlayable(GirField field)
    {
        _diagnostics.Error(
            "GEN0062",
            $"The instance field '{field.Name}' has the type '{field.Type?.Name ?? "(none)"}', which the own "
            + "fields mirror has no storage for; a mirror that guessed at it would move every field behind it.");
        return "nint";
    }

    /// <summary>One row of the table the ABI probes read.</summary>
    /// <param name="CName">The C name of the instance structure.</param>
    /// <param name="Wrapper">The C# name of the wrapper.</param>
    /// <param name="Parent">The C# name of the wrapper of the parent class.</param>
    /// <param name="Mirror">The C# name of the own fields mirror.</param>
    /// <param name="Fields">The exposed fields, as the gir names them and as the mirror does.</param>
    private sealed record RegistryRow(
        string CName,
        string Wrapper,
        string Parent,
        string Mirror,
        IReadOnlyList<(string Field, string Member)> Fields);
}
