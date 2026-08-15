using GstSharp.Generator.GirParsing;
using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Semantic;

/// <summary>
/// The gir element a symbol was declared by.
/// </summary>
internal enum GirSymbolKind
{
    /// <summary>A <c>&lt;class&gt;</c>.</summary>
    Class,

    /// <summary>A <c>&lt;record&gt;</c>.</summary>
    Record,

    /// <summary>An <c>&lt;interface&gt;</c>.</summary>
    Interface,

    /// <summary>An <c>&lt;enumeration&gt;</c> or <c>&lt;bitfield&gt;</c>.</summary>
    Enumeration,

    /// <summary>A namespace level <c>&lt;callback&gt;</c>.</summary>
    Callback,

    /// <summary>An <c>&lt;alias&gt;</c>.</summary>
    Alias,

    /// <summary>A <c>&lt;union&gt;</c>.</summary>
    Union,
}

/// <summary>
/// A named type declared by one of the loaded gir namespaces.
/// </summary>
/// <param name="Namespace">The declaring namespace.</param>
/// <param name="Name">The verbatim gir name.</param>
/// <param name="Kind">The declaring gir element.</param>
/// <param name="Declaration">The parsed declaration.</param>
internal sealed record GirSymbol(GirNamespace Namespace, string Name, GirSymbolKind Kind, GirNode Declaration)
{
    /// <summary>Gets the namespace qualified gir name, for example <c>Gst.Buffer</c>.</summary>
    internal string QualifiedName => Namespace.Name + "." + Name;
}

/// <summary>
/// All loaded gir namespaces plus the symbol table used to resolve type
/// references across them.
/// </summary>
/// <remarks>
/// The <c>&lt;include&gt;</c> lists in the GStreamer gir files are not closed:
/// <c>GstApp</c> references <c>GObject.Object</c> without including
/// <c>GObject</c>. References are therefore resolved against every loaded
/// namespace, with the declaring namespace and its include closure preferred.
/// </remarks>
internal sealed class Repository
{
    private static readonly HashSet<string> PrimitiveNames = new(StringComparer.Ordinal)
    {
        "none",
        "gboolean",
        "gchar",
        "guchar",
        "gint8",
        "guint8",
        "gshort",
        "gushort",
        "gint16",
        "guint16",
        "gint",
        "guint",
        "gint32",
        "guint32",
        "glong",
        "gulong",
        "gint64",
        "guint64",
        "gsize",
        "gssize",
        "gintptr",
        "guintptr",
        "gfloat",
        "gdouble",
        "long double",
        "gunichar",
        "gunichar2",
        "gpointer",
        "gconstpointer",
        "utf8",
        "filename",
        "GType",
        "va_list",
    };

    private readonly Dictionary<string, GirSymbol> _symbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GirNamespace> _namespacesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<GirNamespace, GirRepository> _owningFile = [];
    private readonly Dictionary<GirNamespace, IReadOnlyList<GirNamespace>> _searchOrder = [];
    private readonly Dictionary<GirNode, GirNamespace> _declaringNamespace = new(ReferenceEqualityComparer.Instance);

    private Repository(IReadOnlyList<GirRepository> files, IReadOnlyList<GirNamespace> namespaces)
    {
        Files = files;
        Namespaces = namespaces;

        foreach (GirRepository file in files)
        {
            foreach (GirNamespace ns in file.Namespaces)
            {
                _owningFile[ns] = file;
            }
        }

        foreach (GirNamespace ns in namespaces)
        {
            _namespacesByName[ns.Name] = ns;
            foreach (GirClass declaration in ns.Classes)
            {
                AddSymbol(ns, declaration.Name, GirSymbolKind.Class, declaration);
            }

            foreach (GirRecord declaration in ns.Records)
            {
                AddSymbol(ns, declaration.Name, GirSymbolKind.Record, declaration);
            }

            foreach (GirInterface declaration in ns.Interfaces)
            {
                AddSymbol(ns, declaration.Name, GirSymbolKind.Interface, declaration);
            }

            foreach (GirEnumeration declaration in ns.AllEnumerations)
            {
                AddSymbol(ns, declaration.Name, GirSymbolKind.Enumeration, declaration);
            }

            foreach (GirCallback declaration in ns.Callbacks)
            {
                AddSymbol(ns, declaration.Name, GirSymbolKind.Callback, declaration);
            }

            foreach (GirAlias declaration in ns.Aliases)
            {
                AddSymbol(ns, declaration.Name, GirSymbolKind.Alias, declaration);
            }

            foreach (GirUnion declaration in ns.Unions)
            {
                AddSymbol(ns, declaration.Name, GirSymbolKind.Union, declaration);
            }
        }
    }

    /// <summary>Gets the parsed gir files, ordered by file name.</summary>
    internal IReadOnlyList<GirRepository> Files { get; }

    /// <summary>Gets every loaded namespace, ordered by name.</summary>
    internal IReadOnlyList<GirNamespace> Namespaces { get; }

    /// <summary>
    /// Loads every <c>*.gir</c> file of a directory.
    /// </summary>
    /// <param name="referenceDirectory">Directory holding the gir files.</param>
    /// <returns>The loaded repository.</returns>
    internal static Repository Load(string referenceDirectory)
    {
        string[] paths = Directory.GetFiles(referenceDirectory, "*.gir");
        Array.Sort(paths, StringComparer.Ordinal);

        List<GirRepository> files = [];
        foreach (string path in paths)
        {
            files.Add(GirReader.ReadFile(path));
        }

        return FromRepositories(files);
    }

    /// <summary>
    /// Builds a repository from already parsed gir files, for tests.
    /// </summary>
    /// <param name="files">The parsed gir files.</param>
    /// <returns>The loaded repository.</returns>
    internal static Repository FromRepositories(IReadOnlyList<GirRepository> files)
    {
        List<GirNamespace> namespaces = [];
        foreach (GirRepository file in files)
        {
            namespaces.AddRange(file.Namespaces);
        }

        namespaces.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return new Repository(files, namespaces);
    }

    /// <summary>Tests whether a gir type name denotes a built-in type.</summary>
    /// <param name="name">The gir type name.</param>
    /// <returns><see langword="true"/> for built-in types.</returns>
    internal static bool IsPrimitive(string? name) => name is not null && PrimitiveNames.Contains(name);

    /// <summary>Looks a namespace up by name.</summary>
    /// <param name="name">The gir namespace name, for example <c>Gst</c>.</param>
    /// <returns>The namespace, or <see langword="null"/> when it is not loaded.</returns>
    internal GirNamespace? FindNamespace(string name) =>
        _namespacesByName.TryGetValue(name, out GirNamespace? ns) ? ns : null;

    /// <summary>Returns the namespace the declaration belongs to.</summary>
    /// <param name="declaration">A top level declaration.</param>
    /// <returns>The declaring namespace, or <see langword="null"/> when unknown.</returns>
    internal GirNamespace? GetNamespaceOf(GirNode declaration) =>
        _declaringNamespace.TryGetValue(declaration, out GirNamespace? ns) ? ns : null;

    /// <summary>
    /// Resolves a gir type reference such as <c>Buffer</c> or <c>GObject.Object</c>.
    /// </summary>
    /// <param name="reference">The verbatim reference.</param>
    /// <param name="context">The namespace the reference was written in.</param>
    /// <returns>The resolved symbol, or <see langword="null"/>.</returns>
    internal GirSymbol? Resolve(string? reference, GirNamespace? context)
    {
        if (string.IsNullOrEmpty(reference) || IsPrimitive(reference))
        {
            return null;
        }

        int dot = reference.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            return _symbols.TryGetValue(reference, out GirSymbol? qualified) ? qualified : null;
        }

        foreach (GirNamespace candidate in SearchOrder(context))
        {
            if (_symbols.TryGetValue(candidate.Name + "." + reference, out GirSymbol? symbol))
            {
                return symbol;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the type of a type reference, following aliases.
    /// </summary>
    /// <param name="type">The type reference.</param>
    /// <param name="context">The namespace the reference was written in.</param>
    /// <returns>The resolved symbol, or <see langword="null"/>.</returns>
    internal GirSymbol? Resolve(GirTypeRef type, GirNamespace? context) => Resolve(type.Name, context);

    /// <summary>
    /// Follows an alias chain until a non-alias symbol or a built-in type is reached.
    /// </summary>
    /// <param name="symbol">The symbol to start from.</param>
    /// <returns>The resolved symbol, or <see langword="null"/> when the chain ends in a built-in type.</returns>
    internal GirSymbol? ResolveAlias(GirSymbol symbol)
    {
        GirSymbol current = symbol;
        for (int depth = 0; depth < 16; depth++)
        {
            if (current.Kind != GirSymbolKind.Alias || current.Declaration is not GirAlias alias)
            {
                return current;
            }

            GirSymbol? next = Resolve(alias.Target.Name, current.Namespace);
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    /// <summary>
    /// Returns the gir type name an alias chain ends in, for example
    /// <c>guint64</c> for <c>Gst.ClockTime</c>.
    /// </summary>
    /// <param name="name">The gir type name.</param>
    /// <param name="context">The namespace the reference was written in.</param>
    /// <returns>The underlying gir type name.</returns>
    internal string? ResolveAliasedName(string? name, GirNamespace? context)
    {
        string? current = name;
        GirNamespace? currentContext = context;
        for (int depth = 0; depth < 16; depth++)
        {
            if (current is null || IsPrimitive(current))
            {
                return current;
            }

            GirSymbol? symbol = Resolve(current, currentContext);
            if (symbol is null)
            {
                return current;
            }

            if (symbol.Kind != GirSymbolKind.Alias || symbol.Declaration is not GirAlias alias)
            {
                return symbol.QualifiedName;
            }

            current = alias.Target.Name;
            currentContext = symbol.Namespace;
        }

        return current;
    }

    /// <summary>
    /// Returns the transitive <c>&lt;include&gt;</c> closure of a namespace,
    /// ordered by name.
    /// </summary>
    /// <param name="ns">The namespace to start from.</param>
    /// <returns>The included namespaces, excluding <paramref name="ns"/> itself.</returns>
    internal IReadOnlyList<GirNamespace> IncludeClosure(GirNamespace ns)
    {
        List<GirNamespace> closure = [];
        HashSet<string> visited = new(StringComparer.Ordinal) { ns.Name };
        Queue<GirNamespace> pending = new();
        pending.Enqueue(ns);

        while (pending.TryDequeue(out GirNamespace? current))
        {
            if (!_owningFile.TryGetValue(current, out GirRepository? file))
            {
                continue;
            }

            List<string> includes = [.. file.Includes.Select(static include => include.Name)];
            includes.Sort(StringComparer.Ordinal);
            foreach (string include in includes)
            {
                if (!visited.Add(include) || !_namespacesByName.TryGetValue(include, out GirNamespace? included))
                {
                    continue;
                }

                closure.Add(included);
                pending.Enqueue(included);
            }
        }

        return closure;
    }

    private IReadOnlyList<GirNamespace> SearchOrder(GirNamespace? context)
    {
        if (context is null)
        {
            return Namespaces;
        }

        if (_searchOrder.TryGetValue(context, out IReadOnlyList<GirNamespace>? cached))
        {
            return cached;
        }

        List<GirNamespace> order = [context];
        order.AddRange(IncludeClosure(context));
        foreach (GirNamespace ns in Namespaces)
        {
            if (!order.Contains(ns))
            {
                order.Add(ns);
            }
        }

        _searchOrder[context] = order;
        return order;
    }

    private void AddSymbol(GirNamespace ns, string name, GirSymbolKind kind, GirNode declaration)
    {
        _declaringNamespace[declaration] = ns;

        string key = ns.Name + "." + name;
        if (!_symbols.ContainsKey(key))
        {
            _symbols.Add(key, new GirSymbol(ns, name, kind, declaration));
        }
    }
}
