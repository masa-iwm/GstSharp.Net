using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Semantic;

/// <summary>
/// One member of a class struct, in the order the gir declares it, which is
/// the order the C compiler laid it out.
/// </summary>
/// <param name="Field">The field, as the gir spells it.</param>
/// <param name="Method">
/// The virtual method the field is the slot of, or <see langword="null"/> when
/// the field carries data rather than a function pointer, or carries one the
/// gir declares no virtual method for.
/// </param>
/// <remarks>
/// A class struct is not a table of function pointers: <c>GstElementClass</c>
/// opens with five data fields and <c>GstBaseTransformClass</c> interleaves two
/// <c>gboolean</c>s before its slots, and <c>GstAudioSinkClass</c> ends with a
/// private pointer its own <c>class_init</c> writes. The mirror has to lay all
/// of them out to stay byte compatible, so this pairing decides which members
/// get a managed surface, never which members exist.
/// </remarks>
internal sealed record ClassStructMember(GirField Field, GirVirtualMethod? Method)
{
    /// <summary>Gets a value indicating whether the member is an overridable slot.</summary>
    internal bool IsSlot => Method is not null;
}

/// <summary>
/// The class struct of one class, paired with the virtual methods its slots
/// stand for.
/// </summary>
internal sealed class ClassStructModel
{
    /// <summary>Gets the namespace the class is declared in.</summary>
    internal required GirNamespace Namespace { get; init; }

    /// <summary>Gets the class the struct belongs to.</summary>
    internal required GirClass Owner { get; init; }

    /// <summary>Gets the record the gir declares the layout in.</summary>
    internal required GirRecord TypeStruct { get; init; }

    /// <summary>Gets the members, in gir order, which is the ABI order.</summary>
    internal required IReadOnlyList<ClassStructMember> Members { get; init; }

    /// <summary>
    /// Gets a value indicating whether a managed subclass may derive from the
    /// class, which is what the <c>subclassable</c> overlay states.
    /// </summary>
    /// <remarks>
    /// A model that is not subclassable is on the parent chain of one that is:
    /// its mirror is emitted because the derived mirror embeds it, and it gets
    /// no <c>OnX</c> surface of its own.
    /// </remarks>
    internal required bool IsSubclassable { get; init; }

    /// <summary>Gets the model of the class this one derives from, if any.</summary>
    /// <remarks>
    /// The chain ends where the gir does: <c>GObject.Object</c> and
    /// <c>GObject.InitiallyUnowned</c> are mirrored by hand in the runtime, so
    /// a model whose parent is one of them has no parent here.
    /// </remarks>
    internal ClassStructModel? Parent { get; set; }

    /// <summary>Gets the qualified gir name of the class, for example <c>Gst.Element</c>.</summary>
    internal string QualifiedName => Namespace.Name + "." + Owner.Name;

    /// <summary>Gets the overridable slots, in ABI order.</summary>
    internal IEnumerable<ClassStructMember> Slots => Members.Where(static member => member.IsSlot);

    /// <summary>Builds the key the overlays address one slot of this class by.</summary>
    /// <param name="virtualMethodName">The gir name of the virtual method.</param>
    /// <returns>The key, for example <c>Gst.Element::change_state</c>.</returns>
    internal string KeyOf(string virtualMethodName) => QualifiedName + "::" + virtualMethodName;
}

/// <summary>
/// The class structs the generator mirrors, which is the allowlisted classes
/// and every class they derive from.
/// </summary>
/// <remarks>
/// <para>
/// The set is built once per run, before anything is emitted, because it is
/// what decides three separate things: which records get a mirror, which
/// virtual methods get a managed surface, and which slot of which record an
/// annotation correction addresses. Building it early is also what lets a
/// stale overlay entry be reported rather than silently ignored.
/// </para>
/// <para>
/// Pairing is by name. A <c>&lt;field&gt;</c> whose inline
/// <c>&lt;callback&gt;</c> has the name of a <c>&lt;virtual-method&gt;</c> of
/// the same class is that method's slot; a callback field with no matching
/// virtual method is a private or unannotated slot and stays a data field, as
/// does every field that is not a callback at all. The gir carries the same
/// signature twice for a paired slot, on the virtual method and on the field
/// callback; the virtual method is the carrier this generator reads, so an
/// annotation correction is stated against it.
/// </para>
/// </remarks>
internal sealed class SubclassModel
{
    private readonly Dictionary<string, ClassStructModel> _byQualifiedName;

    private SubclassModel(
        IReadOnlyList<ClassStructModel> classStructs,
        Dictionary<string, ClassStructModel> byQualifiedName)
    {
        ClassStructs = classStructs;
        _byQualifiedName = byQualifiedName;

        HashSet<string> slotKeys = new(StringComparer.Ordinal);
        HashSet<string> parameterKeys = new(StringComparer.Ordinal);
        foreach (ClassStructModel model in classStructs)
        {
            if (!model.IsSubclassable)
            {
                continue;
            }

            foreach (ClassStructMember slot in model.Slots)
            {
                string key = model.KeyOf(slot.Method!.Name);
                _ = slotKeys.Add(key);
                foreach (GirParameter parameter in slot.Method.Parameters)
                {
                    _ = parameterKeys.Add(key + "#" + parameter.Name);
                }
            }
        }

        VirtualMethodKeys = slotKeys;
        VirtualMethodParameterKeys = parameterKeys;
    }

    /// <summary>Gets an empty model, for a run with no allowlist.</summary>
    internal static SubclassModel Empty { get; } = new([], new Dictionary<string, ClassStructModel>(StringComparer.Ordinal));

    /// <summary>
    /// Gets the keys of every slot a subclassable class has, which is what an
    /// overlay entry addressing a virtual method may name.
    /// </summary>
    /// <remarks>
    /// The slots of a class that is only on the parent chain are left out: they
    /// get no managed surface, so an entry naming one states nothing about the
    /// run.
    /// </remarks>
    internal IReadOnlySet<string> VirtualMethodKeys { get; }

    /// <summary>
    /// Gets the keys of every parameter of those slots, in the
    /// <c>Ns.Class::vfunc#parameter</c> spelling.
    /// </summary>
    internal IReadOnlySet<string> VirtualMethodParameterKeys { get; }

    /// <summary>
    /// Gets every mirrored class struct, parents before the classes that embed
    /// them, in the document order of the girs.
    /// </summary>
    internal IReadOnlyList<ClassStructModel> ClassStructs { get; }

    /// <summary>Looks up the model of one class.</summary>
    /// <param name="qualifiedName">The qualified gir name of the class.</param>
    /// <returns>The model, or <see langword="null"/> when the class is not mirrored.</returns>
    internal ClassStructModel? Find(string qualifiedName) =>
        _byQualifiedName.TryGetValue(qualifiedName, out ClassStructModel? model) ? model : null;

    /// <summary>
    /// Builds the model from the allowlist, resolving the parent chain of every
    /// listed class.
    /// </summary>
    /// <param name="repository">The loaded girs.</param>
    /// <param name="overlays">The overlays holding the allowlist.</param>
    /// <param name="diagnostics">Where a listed class that resolves to nothing is reported.</param>
    /// <returns>The model.</returns>
    internal static SubclassModel Build(Repository repository, Overlays overlays, DiagnosticBag diagnostics)
    {
        Dictionary<string, ClassStructModel> byName = new(StringComparer.Ordinal);
        List<ClassStructModel> ordered = [];
        HashSet<string> matched = new(StringComparer.Ordinal);

        foreach (GirNamespace ns in repository.Namespaces)
        {
            foreach (GirClass declaration in ns.Classes)
            {
                string qualifiedName = ns.Name + "." + declaration.Name;
                if (!overlays.IsSubclassable(qualifiedName))
                {
                    continue;
                }

                matched.Add(qualifiedName);
                _ = AddChain(repository, overlays, diagnostics, ns, declaration, byName, ordered);
            }
        }

        foreach (string qualifiedName in overlays.SubclassableClasses.Order(StringComparer.Ordinal))
        {
            if (!matched.Contains(qualifiedName))
            {
                diagnostics.Warn(
                    "GEN0027",
                    $"The subclassable class '{qualifiedName}' matched no class of the loaded girs; "
                    + "the entry is stale.");
            }
        }

        return new SubclassModel(ordered, byName);
    }

    /// <summary>
    /// Adds one class and everything it derives from, parents first, and
    /// returns the model of the class.
    /// </summary>
    /// <param name="repository">The loaded girs.</param>
    /// <param name="overlays">The overlays holding the allowlist.</param>
    /// <param name="diagnostics">Where a class with no readable class struct is reported.</param>
    /// <param name="ns">The namespace declaring the class.</param>
    /// <param name="declaration">The class.</param>
    /// <param name="byName">The models built so far, keyed by qualified name.</param>
    /// <param name="ordered">The models built so far, in emission order.</param>
    /// <returns>The model, or <see langword="null"/> when the class has no class struct.</returns>
    private static ClassStructModel? AddChain(
        Repository repository,
        Overlays overlays,
        DiagnosticBag diagnostics,
        GirNamespace ns,
        GirClass declaration,
        Dictionary<string, ClassStructModel> byName,
        List<ClassStructModel> ordered)
    {
        string qualifiedName = ns.Name + "." + declaration.Name;
        if (byName.TryGetValue(qualifiedName, out ClassStructModel? existing))
        {
            return existing;
        }

        if (repository.Resolve(declaration.GlibTypeStruct, ns) is not { Kind: GirSymbolKind.Record } symbol
            || symbol.Declaration is not GirRecord typeStruct)
        {
            // Only reported for a class the allowlist named: a parent with no
            // class struct cannot happen in a well formed gir, and a listed
            // class that has none is a misspelling or a gir that moved on.
            if (overlays.IsSubclassable(qualifiedName))
            {
                diagnostics.Warn(
                    "GEN0028",
                    $"The subclassable class '{qualifiedName}' declares no class struct record; "
                    + "no mirror is emitted for it.");
            }

            return null;
        }

        ClassStructModel? parent = null;
        if (repository.Resolve(declaration.Parent, ns) is { Kind: GirSymbolKind.Class } parentSymbol
            && parentSymbol.Declaration is GirClass parentClass
            && parentSymbol.Namespace.Name != "GObject")
        {
            parent = AddChain(repository, overlays, diagnostics, parentSymbol.Namespace, parentClass, byName, ordered);
        }

        ClassStructModel model = new()
        {
            Namespace = ns,
            Owner = declaration,
            TypeStruct = typeStruct,
            Members = Pair(repository, ns, qualifiedName, declaration, typeStruct),
            IsSubclassable = overlays.IsSubclassable(qualifiedName),
            Parent = parent,
        };

        byName.Add(qualifiedName, model);
        ordered.Add(model);
        return model;
    }

    /// <summary>
    /// Pairs the fields of a class struct with the virtual methods of its
    /// class, and stamps every paired method with the key the overlays address
    /// it by.
    /// </summary>
    /// <param name="repository">The loaded girs, for resolving a field type.</param>
    /// <param name="ns">The namespace declaring the class.</param>
    /// <param name="qualifiedName">The qualified gir name of the class.</param>
    /// <param name="declaration">The class.</param>
    /// <param name="typeStruct">The class struct record.</param>
    /// <returns>The members, in gir order.</returns>
    /// <remarks>
    /// A gir spells a function pointer field in one of two ways, and both are
    /// slots: an inline <c>&lt;callback&gt;</c> carries the signature with the
    /// field, and a <c>&lt;type&gt;</c> naming a callback typedef leaves it to
    /// the <c>&lt;virtual-method&gt;</c> alone - which is how
    /// <c>GESClipClass::create_track_element</c> is declared. The virtual
    /// method is the carrier this generator reads in either case, so the second
    /// spelling costs nothing but the resolution of the name.
    /// </remarks>
    private static IReadOnlyList<ClassStructMember> Pair(
        Repository repository,
        GirNamespace ns,
        string qualifiedName,
        GirClass declaration,
        GirRecord typeStruct)
    {
        Dictionary<string, GirVirtualMethod> methods = new(StringComparer.Ordinal);
        foreach (GirVirtualMethod method in declaration.VirtualMethods)
        {
            // A gir that declared the same name twice would make the pairing
            // ambiguous; the first one wins, which is the one the class struct
            // field of that name stands for.
            _ = methods.TryAdd(method.Name, method);
        }

        List<ClassStructMember> members = [];
        foreach (GirField field in typeStruct.Fields)
        {
            bool isFunctionPointer = field.Callback is not null
                || (field.Type is not GirArrayRef
                    && field.Type?.Name is { } typeName
                    && repository.Resolve(typeName, ns) is { Kind: GirSymbolKind.Callback });

            GirVirtualMethod? method = null;
            if (isFunctionPointer && methods.TryGetValue(field.Name, out GirVirtualMethod? candidate))
            {
                method = candidate;
                method.OverlayKey = qualifiedName + "::" + method.Name;
            }

            members.Add(new ClassStructMember(field, method));
        }

        return members;
    }
}
