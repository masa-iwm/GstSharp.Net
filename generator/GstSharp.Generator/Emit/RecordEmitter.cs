using System.Diagnostics;
using System.Globalization;
using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Planning;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>
/// Emits the <c>&lt;record&gt;</c> declarations of a gir namespace, one file
/// per generated type.
/// </summary>
/// <remarks>
/// <para>
/// Four projections are covered, following <see cref="Classifier"/>:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="TypeKind.MiniObject"/>: a sealed wrapper that
/// derives from the hand written <c>Gst.MiniObject</c>, plus an internal struct
/// that mirrors the native layout whenever the gir fields can be laid
/// out.</description></item>
/// <item><description><see cref="TypeKind.Boxed"/>: a sealed wrapper that
/// derives from <c>Gst.GObject.Boxed</c> and imports the <c>glib:get-type</c>
/// function of the record, plus a mirror of as much of the native layout as
/// can be projected.</description></item>
/// <item><description><see cref="TypeKind.PlainStruct"/>: a struct that is
/// marshalled by value.</description></item>
/// <item><description><see cref="TypeKind.OpaqueRecord"/>: a sealed wrapper
/// around the bare pointer, with the same mirror.</description></item>
/// </list>
/// <para>
/// Every wrapper that owns a mirror also carries a get only property per field
/// the mirror projects onto a value, because the fields of these structures are
/// public API in C and most of them have no accessor function. A wrapper is a
/// pointer into memory that GStreamer owns, so the property reads the live
/// structure rather than a snapshot; writing stays with the C setters and with
/// the hand written glue, which is where the writability rules of the
/// individual types are stated.
/// </para>
/// <para>
/// Fields keep their gir order, because that order is the ABI. Every generated
/// struct carries an explicit <c>StructLayout</c>: it states the intent, and it
/// is also what tells the C# compiler that the fields are written by native
/// code, which suppresses the "never assigned" warnings that are errors in this
/// repository.
/// </para>
/// </remarks>
internal sealed class RecordEmitter
{
    /// <summary>Suffix of the generated mirror of a native layout.</summary>
    internal const string RawSuffix = "Raw";

    private const string NativeInt = "nint";

    /// <summary>
    /// Wrappers that are hand written and must not be generated. The mini
    /// object base class carries the reference counting of every mini object,
    /// so it cannot derive from itself; only its native layout is emitted,
    /// because the mirrors of the derived types embed it by value.
    /// </summary>
    private static readonly HashSet<string> HandWrittenWrappers = new(StringComparer.Ordinal)
    {
        "Gst.MiniObject",
    };

    private readonly Repository _repository;
    private readonly Classifier _classifier;
    private readonly NameMapper _names;
    private readonly TypeMap _types;
    private readonly Overlays _overlays;
    private readonly SkipRules _skipRules;
    private readonly DiagnosticBag _diagnostics;
    private readonly SurfaceBuilder _surfaces;
    private readonly EmissionCensus _census;
    private readonly List<RegistryEntry> _registry;

    /// <summary>The records whose layout is being measured, which guards the recursion.</summary>
    private readonly HashSet<string> _completeLayouts = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="RecordEmitter"/> class.</summary>
    /// <param name="repository">The loaded gir repository.</param>
    /// <param name="classifier">The type classifier.</param>
    /// <param name="names">The name mapper.</param>
    /// <param name="types">The type map.</param>
    /// <param name="overlays">The overlay configuration.</param>
    /// <param name="skipRules">The rules that decide what is not generated.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    /// <param name="surfaces">The member builder.</param>
    /// <param name="census">The census of the run.</param>
    /// <param name="registry">Receives the types that the module registers.</param>
    internal RecordEmitter(
        Repository repository,
        Classifier classifier,
        NameMapper names,
        TypeMap types,
        Overlays overlays,
        SkipRules skipRules,
        DiagnosticBag diagnostics,
        SurfaceBuilder surfaces,
        EmissionCensus census,
        List<RegistryEntry> registry)
    {
        _repository = repository;
        _classifier = classifier;
        _names = names;
        _types = types;
        _overlays = overlays;
        _skipRules = skipRules;
        _diagnostics = diagnostics;
        _surfaces = surfaces;
        _census = census;
        _registry = registry;
    }

    /// <summary>Emits every generated record of one module.</summary>
    /// <param name="module">The module to emit.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <returns>The generated files, ordered by relative path.</returns>
    internal IReadOnlyList<GeneratedFile> Emit(ModuleInfo module, GirNamespace ns)
    {
        List<GeneratedFile> files = [];
        foreach (GirRecord record in ns.Records)
        {
            if (Emit(module, ns, record) is { } file)
            {
                files.Add(file);
            }
        }

        files.Sort(static (left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return files;
    }

    /// <summary>Emits one record.</summary>
    /// <param name="module">The module the record belongs to.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <param name="record">The record to emit.</param>
    /// <returns>The generated file, or <see langword="null"/> when nothing is emitted.</returns>
    internal GeneratedFile? Emit(ModuleInfo module, GirNamespace ns, GirRecord record)
    {
        string qualifiedName = ns.Name + "." + record.Name;
        if (!record.IsIntrospectable || _overlays.IsSkipped(qualifiedName) || Classifier.IsPrivateShell(record))
        {
            return null;
        }

        TypeKind kind = _classifier.Classify(record);
        if (kind is not (TypeKind.MiniObject or TypeKind.Boxed or TypeKind.PlainStruct or TypeKind.OpaqueRecord))
        {
            return null;
        }

        string typeName = TypeNameOf(ns, record);
        bool handWritten = HandWrittenWrappers.Contains(qualifiedName);

        List<LayoutField>? layout = null;
        bool truncated = false;
        List<Accessor> accessors = [];

        switch (kind)
        {
            case TypeKind.PlainStruct:
                layout = TryLayout(ns, record, typeName, publicSurface: true, LayoutTail.None, out truncated);
                if (layout is null or { Count: 0 })
                {
                    // The classifier only reports PlainStruct for records whose
                    // fields are all blittable, so this cannot happen.
                    Debug.Assert(false, "A plain struct has a field that cannot be laid out: " + qualifiedName);
                    _diagnostics.Error(
                        "GEN0006",
                        $"Record '{qualifiedName}' is classified as a plain struct but has a field that cannot be laid out; the type is skipped.");
                    return null;
                }

                break;

            case TypeKind.MiniObject:
                layout = TryLayout(ns, record, typeName, publicSurface: false, LayoutTail.Private, out truncated);
                if (layout is { Count: 0 })
                {
                    layout = null;
                }

                if (layout is null && record.Fields.Count > 0)
                {
                    _diagnostics.Warn(
                        "GEN0007",
                        $"Record '{qualifiedName}' has a public field that cannot be laid out; the mirror of its native layout is not emitted.");
                }

                if (layout is not null && !handWritten)
                {
                    accessors = BuildAccessors(ns, typeName, layout);
                }

                break;

            case TypeKind.Boxed:
            case TypeKind.OpaqueRecord:
                // A wrapper is a pointer, so nothing needs the whole structure:
                // the mirror carries as long a prefix of it as can be projected
                // and the accessors of that prefix are emitted. There is no
                // warning to report, because a prefix always succeeds.
                layout = TryLayout(ns, record, typeName, publicSurface: false, LayoutTail.Prefix, out truncated);
                if (layout is { Count: 0 })
                {
                    layout = null;
                }

                if (layout is not null)
                {
                    accessors = BuildAccessors(ns, typeName, layout);
                }

                break;
        }

        // The accessors claim their names before anything the gir declares is
        // planned, so that a method named after a field is the member that is
        // reported as colliding rather than the field silently losing.
        if (accessors.Count > 0)
        {
            accessors = KeepUnclaimedAccessors(module, qualifiedName, kind, typeName, accessors);
        }

        // A hand written wrapper only reaches a file when the generator still
        // owns the mirror of its native layout.
        bool emitsMirror = layout is not null
            && kind is TypeKind.MiniObject or TypeKind.Boxed or TypeKind.OpaqueRecord;
        if (handWritten && !emitsMirror)
        {
            return null;
        }

        TypeSurface surface = BuildSurface(module, ns, record, kind, typeName, layout, handWritten, accessors);

        CodeWriter writer = new();
        WriteHeader(
            writer,
            module,
            ns,
            kind,
            layout,
            NeedsSystem(record, kind, layout, accessors) || surface.NeedsUnsafe,
            !handWritten && surface.NeedsUnsafe,
            !handWritten && surface.ParameterArrays.Count > 0);

        if (!handWritten)
        {
            writer.WriteLine();
            switch (kind)
            {
                case TypeKind.PlainStruct:
                    WriteStruct(writer, module, record, typeName, layout!, surface);
                    break;

                case TypeKind.MiniObject:
                    WriteMiniObject(writer, module, record, typeName, accessors, surface);
                    break;

                case TypeKind.Boxed:
                    WriteBoxed(writer, module, record, typeName, accessors, surface);
                    break;

                default:
                    WriteOpaque(writer, module, record, typeName, accessors, surface);
                    break;
            }

            if (record.GlibGetType is { Length: > 0 } && kind is TypeKind.MiniObject or TypeKind.Boxed)
            {
                _registry.Add(new RegistryEntry(module.ClrNamespace + "." + typeName, record.IsDeprecated));
            }
        }

        if (emitsMirror)
        {
            writer.WriteLine();
            WriteRawStruct(writer, record, kind, typeName, layout!, truncated);
        }

        _census.Emitted(module.GirNamespace, "record");
        for (int i = 0; i < accessors.Count; i++)
        {
            _census.Emitted(module.GirNamespace, "field accessor");
        }

        return new GeneratedFile(module.ProjectDirectory + "/Generated/" + typeName + ".cs", writer.ToSource());
    }

    private static string CTypeOf(GirRecord record) =>
        record.CType is { Length: > 0 } cType ? cType : record.Name;

    private static string FallbackSummary(GirRecord record, string noun) =>
        $"The <c>{CTypeOf(record)}</c> {noun}.";

    private static string FallbackSummary(GirRecord record, GirField field) =>
        $"The <c>{field.Name}</c> field of <c>{CTypeOf(record)}</c>.";

    /// <summary>
    /// Tests whether a field belongs to the C implementation rather than to the
    /// API. The gir marks those fields <c>private="1"</c>, <c>readable="0"</c>
    /// or both.
    /// </summary>
    /// <param name="field">The field to inspect.</param>
    /// <returns><see langword="true"/> when the field carries no API.</returns>
    private static bool IsHidden(GirField field) => field.IsPrivate || !field.IsReadable;

    private static bool IsTailHidden(GirRecord record, int from)
    {
        for (int i = from; i < record.Fields.Count; i++)
        {
            if (!IsHidden(record.Fields[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tests whether the file declares an <c>[Obsolete]</c> attribute, which is
    /// the only reason a generated record needs the <c>System</c> namespace.
    /// </summary>
    /// <param name="record">The record being emitted.</param>
    /// <param name="kind">Its classification.</param>
    /// <param name="layout">The laid out fields, if any.</param>
    /// <param name="accessors">The emitted accessors.</param>
    /// <returns><see langword="true"/> when <c>using System;</c> is needed.</returns>
    private static bool NeedsSystem(
        GirRecord record,
        TypeKind kind,
        IReadOnlyList<LayoutField>? layout,
        IReadOnlyList<Accessor> accessors)
    {
        if (record.IsDeprecated)
        {
            return true;
        }

        // Private fields and the fields of a mirror carry no attributes.
        if (kind == TypeKind.PlainStruct && layout is not null)
        {
            foreach (LayoutField field in layout)
            {
                if (!field.IsPrivate && field.Field.IsDeprecated)
                {
                    return true;
                }
            }
        }

        foreach (Accessor accessor in accessors)
        {
            if (accessor.Field.IsDeprecated)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteHeader(
        CodeWriter writer,
        ModuleInfo module,
        GirNamespace ns,
        TypeKind kind,
        IReadOnlyList<LayoutField>? layout,
        bool needsSystem,
        bool needsEntryPoints,
        bool inlineArrays)
    {
        writer.WriteLine("// <auto-generated/>");
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"// Generated by GstSharp.Generator from {ns.Name}-{ns.Version}.gir. Do not edit."));
        writer.WriteLine();

        // The auto-generated marker above switches the nullable context off,
        // so a generated file has to ask for it back before it can spell a
        // nullable reference type.
        writer.WriteLine("#nullable enable");
        writer.WriteLine();

        // Both a fixed size field and a caller allocated array parameter are
        // spelled with the InlineArray attribute.
        bool needsCompilerServices = inlineArrays;
        if (layout is not null)
        {
            foreach (LayoutField field in layout)
            {
                needsCompilerServices |= field.InlineArray is not null;
            }
        }

        // StructLayout for every emitted struct, LibraryImport for the type
        // function of a boxed type and for every generated entry point.
        bool needsInteropServices = layout is not null
            || kind is TypeKind.Boxed or TypeKind.MiniObject
            || needsEntryPoints;

        if (needsSystem)
        {
            writer.WriteLine("using System;");
        }

        if (needsCompilerServices)
        {
            writer.WriteLine("using System.Runtime.CompilerServices;");
        }

        if (needsInteropServices)
        {
            writer.WriteLine("using System.Runtime.InteropServices;");
        }

        if (needsSystem || needsCompilerServices || needsInteropServices)
        {
            writer.WriteLine();
        }

        writer.WriteLine("namespace " + module.ClrNamespace + ";");
    }

    /// <summary>Writes the inline storage type of a fixed size array.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="visibility">The visibility of the storage type.</param>
    /// <param name="summary">The documentation of the storage type.</param>
    /// <param name="inline">The storage to write.</param>
    /// <remarks>
    /// A fixed size field and a caller allocated array parameter are spelled the
    /// same way, so both go through this: the type is nested in the declaration
    /// that needs it and carries the length in its own definition, which is what
    /// keeps the size out of the call site.
    /// </remarks>
    internal static void WriteInlineArray(
        CodeWriter writer,
        string visibility,
        string summary,
        InlineArrayInfo inline)
    {
        writer.WriteLine("/// <summary>" + summary + "</summary>");
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[InlineArray({inline.Length})]"));
        writer.WriteLine(visibility + " struct " + inline.TypeName);
        writer.OpenBlock();
        writer.WriteLine("private " + inline.ElementTypeName + " _element0;");
        writer.CloseBlock();
    }

    private static void WriteWrapperConstructor(
        CodeWriter writer,
        GirRecord record,
        string typeName,
        string? baseCall)
    {
        writer.WriteLine("/// <summary>Wraps a native <c>" + CTypeOf(record) + "</c>.</summary>");
        writer.WriteLine("/// <param name=\"handle\">The native instance.</param>");
        if (baseCall is null)
        {
            writer.WriteLine("internal " + typeName + "(nint handle) => Handle = handle;");
            return;
        }

        writer.WriteLine(
            "/// <param name=\"transfer\">How ownership of <paramref name=\"handle\"/> is transferred.</param>");
        writer.WriteLine("internal " + typeName + "(nint handle, Gst.Interop.Transfer transfer)");
        writer.WriteLine("    : " + baseCall);
        writer.OpenBlock();
        writer.CloseBlock();
    }

    private static void WriteFromNative(CodeWriter writer, GirRecord record, string typeName)
    {
        writer.WriteLine(
            "/// <summary>Wraps a native <c>" + CTypeOf(record)
            + "</c>, mapping the null pointer onto <see langword=\"null\"/>.</summary>");
        writer.WriteLine("/// <param name=\"handle\">The native instance, or <c>0</c>.</param>");
        writer.WriteLine(
            "/// <param name=\"transfer\">How ownership of <paramref name=\"handle\"/> is transferred.</param>");
        writer.WriteLine(
            "/// <returns>The wrapper, or <see langword=\"null\"/> when <paramref name=\"handle\"/> is <c>0</c>.</returns>");
        writer.WriteLine("internal static " + typeName + "? FromNative(nint handle, Gst.Interop.Transfer transfer) =>");
        writer.WriteLine("    handle == 0 ? null : new(handle, transfer);");
    }

    private static void WriteRawStruct(
        CodeWriter writer,
        GirRecord record,
        TypeKind kind,
        string typeName,
        IReadOnlyList<LayoutField> layout,
        bool truncated)
    {
        writer.WriteLine("/// <summary>The native layout of <c>" + CTypeOf(record) + "</c>.</summary>");
        writer.WriteLine("/// <remarks>");
        writer.WriteLine("/// <para>");
        writer.WriteLine("/// The mirror is only ever read through a pointer into memory that GStreamer");
        writer.WriteLine("/// owns; it is never allocated, assigned or copied.");
        writer.WriteLine("/// </para>");
        if (truncated && kind == TypeKind.MiniObject)
        {
            writer.WriteLine("/// <para>");
            writer.WriteLine("/// It stops at the first field that has no portable C# spelling. Every field");
            writer.WriteLine("/// behind that one is private to the C implementation and is never read.");
            writer.WriteLine("/// </para>");
        }
        else if (truncated)
        {
            // A boxed or opaque record stops at the first field it cannot
            // project whatever follows it, so what is behind the stop may well
            // be public API in C. The offsets in front of it are exact and that
            // is all a read through the pointer needs.
            writer.WriteLine("/// <para>");
            writer.WriteLine("/// Prefix mirror of the C struct: field offsets are exact, <c>sizeof</c> is NOT");
            writer.WriteLine("/// the C size; never allocate from it.");
            writer.WriteLine("/// </para>");
        }

        writer.WriteLine("/// </remarks>");
        writer.WriteLine("[StructLayout(LayoutKind.Sequential)]");
        writer.WriteLine("internal unsafe struct " + typeName + RawSuffix);
        writer.OpenBlock();

        bool first = true;
        foreach (LayoutField field in layout)
        {
            if (!first)
            {
                writer.WriteLine();
            }

            first = false;
            if (field.Note is { } note)
            {
                writer.WriteLine("// " + note);
            }

            writer.WriteLine("/// <summary>The <c>" + field.Field.Name + "</c> field.</summary>");
            writer.WriteLine("internal " + field.TypeName + " " + field.PascalName + ";");
        }

        foreach (LayoutField field in layout)
        {
            if (field.InlineArray is not { } inline)
            {
                continue;
            }

            writer.WriteLine();
            WriteInlineArray(
                writer,
                "internal",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Inline storage of the {inline.Length} elements of the <c>{field.Field.Name}</c> field."),
                inline);
        }

        writer.CloseBlock();
    }

    private void WriteStruct(
        CodeWriter writer,
        ModuleInfo module,
        GirRecord record,
        string typeName,
        IReadOnlyList<LayoutField> layout,
        TypeSurface surface)
    {
        XmlDocWriter.Write(writer, record.Doc, FallbackSummary(record, "structure"), record);
        XmlDocWriter.WriteObsolete(writer, record);
        writer.WriteLine("[StructLayout(LayoutKind.Sequential)]");
        writer.WriteLine(
            "public " + (surface.NeedsUnsafe ? "unsafe " : string.Empty) + "partial struct " + typeName);
        writer.OpenBlock();

        bool first = true;
        foreach (LayoutField field in layout)
        {
            if (!first)
            {
                writer.WriteLine();
            }

            first = false;
            WriteStructField(writer, record, field);
        }

        foreach (LayoutField field in layout)
        {
            if (field.InlineArray is not { } inline)
            {
                continue;
            }

            writer.WriteLine();
            WriteInlineArray(
                writer,
                field.IsPrivate ? "private" : "public",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Inline storage of the {inline.Length} elements of the <c>{field.Field.Name}</c> field of <c>{CTypeOf(record)}</c>."),
                inline);
        }

        ClassEmitter.WriteMembers(writer, surface, module, first: false);
        writer.CloseBlock();
    }

    private void WriteStructField(CodeWriter writer, GirRecord record, LayoutField field)
    {
        if (field.Note is { } note)
        {
            writer.WriteLine("// " + note);
        }

        if (field.IsPrivate)
        {
            // Padding and implementation details of the C structure. They are
            // spelled out because they take up space, but they carry no API.
            writer.WriteLine("/// <summary>" + FallbackSummary(record, field.Field) + "</summary>");
            writer.WriteLine("private " + field.TypeName + " " + field.Name + ";");
            return;
        }

        XmlDocWriter.Write(writer, field.Field.Doc, FallbackSummary(record, field.Field), field.Field);
        XmlDocWriter.WriteObsolete(writer, field.Field);
        writer.WriteLine("public " + field.TypeName + " " + field.Name + ";");
    }

    /// <summary>
    /// Plans the members of a wrapper.
    /// </summary>
    /// <param name="module">The module being emitted.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <param name="record">The record being emitted.</param>
    /// <param name="kind">Its classification.</param>
    /// <param name="typeName">Its C# name.</param>
    /// <param name="layout">The laid out fields of a value projected structure, if any.</param>
    /// <param name="handWritten">Whether the wrapper itself is hand written.</param>
    /// <param name="accessors">The field accessors, which have already claimed their names.</param>
    /// <returns>The members to emit.</returns>
    /// <remarks>
    /// A plain struct goes through the same builder as every other kind. Its
    /// instance methods pin <c>this</c> instead of reading a handle, which is
    /// what <see cref="ArgumentKind.ValueInstance"/> describes, and its field
    /// names are reserved beside the inherited ones: a struct carries its
    /// fields in the same declaration space as its methods, so a method that
    /// took the name of a field would not compile.
    /// </remarks>
    private TypeSurface BuildSurface(
        ModuleInfo module,
        GirNamespace ns,
        GirRecord record,
        TypeKind kind,
        string typeName,
        IReadOnlyList<LayoutField>? layout,
        bool handWritten,
        IReadOnlyList<Accessor> accessors)
    {
        if (handWritten)
        {
            foreach (GirFunction callable in
                record.Constructors.Concat(record.Methods).Concat(record.Functions))
            {
                // Nothing of a hand written wrapper is planned, so no member
                // reaches a rule of its own. The overlays are still asked,
                // because an entry that names one of these symbols is a
                // decision that was taken about it, and the ledger says so
                // rather than filing every member of the record under the
                // catch all reason.
                SkipReason reason = _skipRules.GetSkipReason(callable) == SkipReason.OverlaySkip
                    ? SkipReason.OverlaySkip
                    : SkipReason.UnsupportedSignature;

                _census.Skipped(
                    module.GirNamespace,
                    reason,
                    callable.CIdentifier is { Length: > 0 } identifier
                        ? identifier
                        : record.Name + "." + callable.Name);
            }

            return new TypeSurface([], [], []);
        }

        List<string> reserved = ReservedNames(kind, typeName);
        foreach (Accessor accessor in accessors)
        {
            reserved.Add(accessor.Name);
        }

        if (kind == TypeKind.PlainStruct && layout is not null)
        {
            foreach (LayoutField field in layout)
            {
                reserved.Add(field.Name);

                // A fixed size field nests the storage type it is spelled with
                // in the structure, so that name is taken as well. The mirror
                // of a wrapper nests its own inside Raw, where no member of the
                // wrapper can reach it, which is why only a plain struct
                // reserves them here.
                if (field.InlineArray is { } inline)
                {
                    reserved.Add(inline.TypeName);
                }
            }
        }

        PlanningContext context = new(module, ns, kind, module.ClrNamespace + "." + typeName);
        return _surfaces.Build(record, context, CallableForm.InstanceMethod, reserved, [], includeProperties: false);
    }

    private void WriteMiniObject(
        CodeWriter writer,
        ModuleInfo module,
        GirRecord record,
        string typeName,
        IReadOnlyList<Accessor> accessors,
        TypeSurface surface)
    {
        XmlDocWriter.Write(writer, record.Doc, FallbackSummary(record, "mini object"), record);
        XmlDocWriter.WriteObsolete(writer, record);
        writer.WriteLine(
            "public sealed " + (accessors.Count > 0 || surface.NeedsUnsafe ? "unsafe " : string.Empty)
            + "partial class " + typeName + " : Gst.MiniObject");
        writer.OpenBlock();

        WriteWrapperConstructor(writer, record, typeName, "base(handle, transfer)");

        WriteAccessors(writer, record, accessors);

        writer.WriteLine();
        WriteFromNative(writer, record, typeName);

        ClassEmitter.WriteMembers(writer, surface, module, first: false);
        WriteTypeRegistration(writer, module, record, typeName);
        writer.CloseBlock();
    }

    private void WriteBoxed(
        CodeWriter writer,
        ModuleInfo module,
        GirRecord record,
        string typeName,
        IReadOnlyList<Accessor> accessors,
        TypeSurface surface)
    {
        XmlDocWriter.Write(writer, record.Doc, FallbackSummary(record, "boxed type"), record);
        XmlDocWriter.WriteObsolete(writer, record);
        writer.WriteLine(
            "public sealed " + (accessors.Count > 0 || surface.NeedsUnsafe ? "unsafe " : string.Empty)
            + "partial class " + typeName + " : Gst.GObject.Boxed");
        writer.OpenBlock();

        WriteWrapperConstructor(
            writer,
            record,
            typeName,
            "base(handle, new Gst.GObject.GType(GetGType()), transfer)");

        WriteAccessors(writer, record, accessors);

        writer.WriteLine();
        WriteFromNative(writer, record, typeName);

        ClassEmitter.WriteMembers(writer, surface, module, first: false);
        WriteTypeRegistration(writer, module, record, typeName);
        writer.CloseBlock();
    }

    private void WriteOpaque(
        CodeWriter writer,
        ModuleInfo module,
        GirRecord record,
        string typeName,
        IReadOnlyList<Accessor> accessors,
        TypeSurface surface)
    {
        XmlDocWriter.Write(writer, record.Doc, FallbackSummary(record, "record"), record);
        XmlDocWriter.WriteObsolete(writer, record);
        writer.WriteLine(
            "public sealed " + (accessors.Count > 0 || surface.NeedsUnsafe ? "unsafe " : string.Empty)
            + "partial class " + typeName);
        writer.OpenBlock();

        writer.WriteLine("/// <summary>The native instance.</summary>");
        writer.WriteLine("internal nint Handle;");
        writer.WriteLine();
        WriteWrapperConstructor(writer, record, typeName, baseCall: null);

        WriteAccessors(writer, record, accessors);

        writer.WriteLine();
        writer.WriteLine(
            "/// <summary>Wraps a native <c>" + CTypeOf(record)
            + "</c>, mapping the null pointer onto <see langword=\"null\"/>.</summary>");
        writer.WriteLine("/// <param name=\"handle\">The native instance, or <c>0</c>.</param>");
        writer.WriteLine(
            "/// <returns>The wrapper, or <see langword=\"null\"/> when <paramref name=\"handle\"/> is <c>0</c>.</returns>");
        writer.WriteLine("/// <remarks>");
        writer.WriteLine("/// The wrapper of an opaque record is a bare pointer holder: the gir");
        writer.WriteLine("/// describes no way of releasing one, so it does not take part in the");
        writer.WriteLine("/// ownership of what it points at.");
        writer.WriteLine("/// </remarks>");
        writer.WriteLine("internal static " + typeName + "? FromNative(nint handle) =>");
        writer.WriteLine("    handle == 0 ? null : new(handle);");

        ClassEmitter.WriteMembers(writer, surface, module, first: false);
        writer.CloseBlock();
    }

    /// <summary>
    /// Writes the type function and the factory of a wrapper that the module
    /// registers with the type registry.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="module">The module being emitted.</param>
    /// <param name="record">The record being emitted.</param>
    /// <param name="typeName">Its C# name.</param>
    private static void WriteTypeRegistration(
        CodeWriter writer,
        ModuleInfo module,
        GirRecord record,
        string typeName)
    {
        if (record.GlibGetType is not { Length: > 0 } getType)
        {
            return;
        }

        writer.WriteLine();
        ClassEmitter.WriteTypeFunction(writer, module, getType, CTypeOf(record), hidesBase: false);
        writer.WriteLine();
        ClassEmitter.WriteFactory(writer, typeName, isAbstract: false, hidesBase: false);
    }

    /// <summary>Writes the field accessors of a wrapper.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="record">The record being emitted.</param>
    /// <param name="accessors">The accessors to write, in field order.</param>
    private static void WriteAccessors(CodeWriter writer, GirRecord record, IReadOnlyList<Accessor> accessors)
    {
        foreach (Accessor accessor in accessors)
        {
            writer.WriteLine();
            XmlDocWriter.Write(writer, accessor.Field.Doc, FallbackSummary(record, accessor.Field), accessor.Field);
            XmlDocWriter.WriteObsolete(writer, accessor.Field);
            WriteAccessor(writer, accessor);
        }
    }

    /// <summary>Writes one field accessor of a wrapper.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="accessor">The accessor to write.</param>
    /// <remarks>
    /// <para>
    /// The body reads through the raw pointer of the wrapper, so the wrapper
    /// has to stay reachable until the read is done. Without that, the last
    /// statement that mentions the wrapper is the one that took its handle, and
    /// the finalizer may release the instance while the pointer into it is
    /// still being dereferenced.
    /// </para>
    /// <para>
    /// The handle is read exactly the way a generated instance method reads it,
    /// which is what makes a disposed boxed wrapper throw an
    /// <c>ObjectDisposedException</c> here rather than dereference the null
    /// pointer. The wrapper of an opaque record has no disposed state to check,
    /// so the same expression is a plain read there.
    /// </para>
    /// </remarks>
    private static void WriteAccessor(CodeWriter writer, Accessor accessor)
    {
        writer.WriteLine("public " + accessor.TypeName + " " + accessor.Name);
        writer.OpenBlock();
        writer.WriteLine("get");
        writer.OpenBlock();
        writer.WriteLine(accessor.TypeName + " value = " + accessor.Expression + ";");
        writer.WriteLine("System.GC.KeepAlive(this);");
        writer.WriteLine("return value;");
        writer.CloseBlock();
        writer.CloseBlock();
    }

    /// <summary>
    /// The names a generated wrapper carries before anything the gir declares
    /// is planned: the members of the runtime base types and the name of the
    /// wrapper itself.
    /// </summary>
    /// <param name="kind">The classification of the record.</param>
    /// <param name="typeName">The C# name of the wrapper.</param>
    /// <returns>The reserved names.</returns>
    private static List<string> ReservedNames(TypeKind kind, string typeName)
    {
        List<string> reserved = [.. SurfaceBuilder.WrapperNames, typeName];
        if (kind == TypeKind.MiniObject)
        {
            reserved.AddRange(SurfaceBuilder.MiniObjectNames);
        }

        return reserved;
    }

    /// <summary>
    /// Drops the accessors whose name a runtime member, the wrapper itself or
    /// an earlier accessor already carries.
    /// </summary>
    /// <param name="module">The module being emitted.</param>
    /// <param name="qualifiedName">The gir name of the record, for the report.</param>
    /// <param name="kind">The classification of the record.</param>
    /// <param name="typeName">The C# name of the wrapper.</param>
    /// <param name="accessors">The accessors of the layout, in field order.</param>
    /// <returns>The accessors that keep their name.</returns>
    /// <remarks>
    /// The surviving names are reserved before the callables of the record are
    /// planned, so that a method that would carry the name of a field is the
    /// one reported as colliding. The field wins because it is the only
    /// binding of what it names, while a method that collides can be renamed
    /// through <c>fixups.json</c>.
    /// </remarks>
    private List<Accessor> KeepUnclaimedAccessors(
        ModuleInfo module,
        string qualifiedName,
        TypeKind kind,
        string typeName,
        IReadOnlyList<Accessor> accessors)
    {
        HashSet<string> used = new(ReservedNames(kind, typeName), StringComparer.Ordinal);
        List<Accessor> kept = [];
        foreach (Accessor accessor in accessors)
        {
            if (used.Add(accessor.Name))
            {
                kept.Add(accessor);
                continue;
            }

            _census.Skipped(
                module.GirNamespace,
                SkipReason.NameCollision,
                qualifiedName + "." + accessor.Field.Name);
            _diagnostics.Warn(
                "GEN0018",
                $"The '{accessor.Field.Name}' field of '{qualifiedName}' would be emitted as "
                + $"'{typeName}.{accessor.Name}', which is already taken; the accessor is skipped. "
                + "Add a rename to fixups.json to bind it.");
        }

        return kept;
    }

    private List<Accessor> BuildAccessors(GirNamespace ns, string typeName, IReadOnlyList<LayoutField> layout)
    {
        string cast = "((" + typeName + RawSuffix + "*)Handle)->";
        List<Accessor> accessors = [];
        foreach (LayoutField field in layout)
        {
            // Private fields, embedded structures and inline arrays carry no
            // API; pointers wait for the emitters that wrap what they point at.
            if (field.IsPrivate
                || field.InlineArray is not null
                || field.Field.Type is not { } type
                || type.IsPointer)
            {
                continue;
            }

            MappedType mapped = _types.Map(type, ns);
            string raw = cast + field.PascalName;
            string? publicType = null;
            string? expression = null;

            switch (mapped.Kind)
            {
                case MarshalKind.Blittable:
                    publicType = mapped.PublicType;
                    expression = string.Equals(mapped.PublicType, mapped.RawType, StringComparison.Ordinal)
                        ? raw
                        : "new(" + raw + ")";
                    break;

                case MarshalKind.GType:
                case MarshalKind.Quark:
                    publicType = mapped.PublicType;
                    expression = "new(" + raw + ")";
                    break;

                case MarshalKind.Boolean:
                    publicType = mapped.PublicType;
                    expression = raw + " != 0";
                    break;

                case MarshalKind.Enum:
                case MarshalKind.Flags:
                    // The mirror only spells the enumeration out when it is
                    // generated next to the record; otherwise the field holds
                    // the underlying integer and there is nothing to expose.
                    if (string.Equals(field.TypeName, mapped.PublicType, StringComparison.Ordinal))
                    {
                        publicType = mapped.PublicType;
                        expression = raw;
                    }

                    break;
            }

            if (publicType is null || expression is null)
            {
                continue;
            }

            accessors.Add(new Accessor(field.Field, field.PascalName, publicType, expression));
        }

        return accessors;
    }

    /// <summary>
    /// Projects the fields of a record onto C#, in gir order.
    /// </summary>
    /// <param name="ns">The gir namespace of the record.</param>
    /// <param name="record">The record to lay out.</param>
    /// <param name="typeName">The C# name of the record.</param>
    /// <param name="publicSurface">
    /// <see langword="true"/> for a struct that is part of the API, which uses
    /// the blittable public wrappers such as <c>Gst.ClockTime</c>;
    /// <see langword="false"/> for a mirror of the native layout, which uses
    /// the interop types.
    /// </param>
    /// <param name="tail">How far the layout may fall short of the whole structure.</param>
    /// <param name="truncated">Whether the layout stopped early.</param>
    /// <returns>The fields, or <see langword="null"/> when the record cannot be laid out.</returns>
    private List<LayoutField>? TryLayout(
        GirNamespace ns,
        GirRecord record,
        string typeName,
        bool publicSurface,
        LayoutTail tail,
        out bool truncated)
    {
        truncated = false;
        List<LayoutField> layout = [];
        int unionAt = FirstUnionField(record);
        for (int i = 0; i <= record.Fields.Count; i++)
        {
            // A union has no C# spelling of a guaranteed size, and the gir
            // keeps it out of the field list of the record, so the layout has
            // to stop where it sits. Laying the fields behind it out anyway
            // would put every one of them at the wrong offset.
            if (i == unionAt)
            {
                if (tail != LayoutTail.Prefix)
                {
                    return null;
                }

                truncated = true;
                break;
            }

            if (i == record.Fields.Count)
            {
                break;
            }

            GirField field = record.Fields[i];
            string pascalName = PublicFieldName(ns, record, field, typeName, barePointer: false);

            FieldProjection? projection = Project(ns, field, pascalName, publicSurface);
            if (projection is null)
            {
                if (tail == LayoutTail.None || (tail == LayoutTail.Private && !IsTailHidden(record, i)))
                {
                    return null;
                }

                truncated = true;
                break;
            }

            bool hidden = IsHidden(field);

            // A public field of a value projected record that lands on a bare
            // pointer takes the Ptr suffix, so that the name it derives from the
            // gir stays free for the typed accessor that reads what the address
            // points at; NameMapper.PointerFieldSuffix states why. Nothing else
            // is touched: a private field is named by PrivateFieldName, an
            // inline array is storage rather than an address, and the mirror of
            // a mini object is internal and keeps the plain name because no
            // public accessor competes for it.
            if (publicSurface && !hidden && projection.IsPointer)
            {
                pascalName = PublicFieldName(ns, record, field, typeName, barePointer: true);
            }

            layout.Add(new LayoutField(
                field,
                hidden ? _names.PrivateFieldName(ns, record, field) : pascalName,
                pascalName,
                projection.TypeName,
                hidden,
                projection.InlineArray,
                projection.Note));
        }

        return layout;
    }

    /// <summary>
    /// Returns the number of fields that sit in front of the first union of a
    /// record, which is where a layout of that record has to stop.
    /// </summary>
    /// <param name="record">The record to inspect.</param>
    /// <returns>The field index, or <see cref="int.MaxValue"/> when there is no union.</returns>
    private static int FirstUnionField(GirRecord record)
    {
        int index = int.MaxValue;
        foreach (GirUnion union in record.Unions)
        {
            index = Math.Min(index, union.FieldIndex);
        }

        return index;
    }

    private string TypeNameOf(GirNamespace ns, GirRecord record) =>
        _names.TypeName(new GirSymbol(ns, record.Name, GirSymbolKind.Record, record));

    /// <summary>Names one field that carries API, in the declaring record.</summary>
    /// <param name="ns">The gir namespace of the record.</param>
    /// <param name="record">The declaring record.</param>
    /// <param name="field">The field to name.</param>
    /// <param name="typeName">The C# name of the declaring record.</param>
    /// <param name="barePointer">Whether the field is projected onto a bare pointer.</param>
    /// <returns>The C# field name.</returns>
    private string PublicFieldName(
        GirNamespace ns,
        GirRecord record,
        GirField field,
        string typeName,
        bool barePointer)
    {
        string name = _names.FieldName(ns, record, field, barePointer);

        // A member cannot carry the name of its enclosing type.
        return string.Equals(name, typeName, StringComparison.Ordinal) ? name + "Field" : name;
    }

    private FieldProjection? Project(GirNamespace ns, GirField field, string pascalName, bool publicSurface)
    {
        if (field.Bits is not null)
        {
            return null;
        }

        if (field.Callback is not null)
        {
            // An inline vtable slot is one function pointer wide.
            return new FieldProjection(NativeInt, null, null);
        }

        if (field.Type is not { } type)
        {
            return null;
        }

        if (type is GirArrayRef { FixedSize: int length } array)
        {
            if (array.ElementType is not { } elementType)
            {
                return null;
            }

            FieldProjection? element = ProjectType(ns, elementType, publicSurface);
            if (element is null || element.InlineArray is not null)
            {
                return null;
            }

            InlineArrayInfo inline = new(pascalName + "Array", element.TypeName, length);
            return new FieldProjection(inline.TypeName, inline, element.Note);
        }

        return ProjectType(ns, type, publicSurface);
    }

    private FieldProjection? ProjectType(GirNamespace ns, GirTypeRef type, bool publicSurface)
    {
        // Only the outermost field can be a fixed size array; nested inline
        // storage is not laid out.
        if (type.IsVarArgs || type is GirArrayRef { FixedSize: not null })
        {
            return null;
        }

        if (type.IsPointer)
        {
            // Whatever the mapping makes of the pointee, a pointer field is one
            // pointer wide. The projection carries that it is an address rather
            // than a value, because a public field of a value type that lands on
            // one is renamed; see NameMapper.PointerFieldSuffix.
            return new FieldProjection(NativeInt, null, null, IsPointer: true);
        }

        if (!TryProjectByValue(ns, type, publicSurface, out FieldProjection? byValue))
        {
            return byValue;
        }

        MappedType mapped = _types.Map(type, ns);
        switch (mapped.Kind)
        {
            case MarshalKind.Void:
            case MarshalKind.Unsupported:
                return null;

            case MarshalKind.Enum:
            case MarshalKind.Flags:
                if (mapped.Symbol is { } symbol
                    && string.Equals(symbol.Namespace.Name, ns.Name, StringComparison.Ordinal))
                {
                    return new FieldProjection(mapped.PublicType, null, null);
                }

                return new FieldProjection(
                    mapped.RawType,
                    null,
                    $"<c>{type.CType ?? type.Name}</c> is not generated in this module; the field keeps its underlying type.");

            case MarshalKind.Pointer:
                // A gpointer or a gconstpointer, which the gir spells without a
                // star, so the check above does not catch it. It is an address
                // like any other pointer field and is named like one.
                return new FieldProjection(mapped.RawType, null, null, IsPointer: true);

            case MarshalKind.GType:
            case MarshalKind.Quark:
                return new FieldProjection(publicSurface ? mapped.PublicType : mapped.RawType, null, null);

            case MarshalKind.Boolean:
                return new FieldProjection(
                    mapped.RawType,
                    null,
                    "<c>gboolean</c> is a 32 bit integer; every non zero value is true.");

            case MarshalKind.Blittable:
                // Aliases such as GstClockTime end in a built-in type but have
                // a wrapper of their own on the public surface.
                return new FieldProjection(publicSurface ? mapped.PublicType : mapped.RawType, null, null);

            default:
                return new FieldProjection(mapped.RawType, null, null);
        }
    }

    /// <summary>
    /// Projects a named type that the gir spells without a pointer, which the
    /// type map cannot do: it answers what a value of the type marshals as, not
    /// how much space it takes up when it is embedded.
    /// </summary>
    /// <param name="ns">The gir namespace of the referencing record.</param>
    /// <param name="type">The field type.</param>
    /// <param name="publicSurface">Whether the embedding struct is part of the API.</param>
    /// <param name="projection">
    /// The projection, or <see langword="null"/> when the type cannot be
    /// embedded at all.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the type map decides, <see langword="false"/>
    /// when <paramref name="projection"/> is the answer.
    /// </returns>
    private bool TryProjectByValue(
        GirNamespace ns,
        GirTypeRef type,
        bool publicSurface,
        out FieldProjection? projection)
    {
        projection = null;
        if (type.Name is null || Repository.IsPrimitive(_repository.ResolveAliasedName(type.Name, ns)))
        {
            return true;
        }

        GirSymbol? symbol = _repository.Resolve(type.Name, ns);
        symbol = symbol is null ? null : _repository.ResolveAlias(symbol);
        switch (symbol)
        {
            case { Kind: GirSymbolKind.Enumeration }:
                return true;

            case { Kind: GirSymbolKind.Callback }:
                // A function pointer slot; the gir spells those without a star.
                projection = new FieldProjection(NativeInt, null, null);
                return false;

            case { Kind: GirSymbolKind.Record, Declaration: GirRecord embedded }:
                projection = ProjectEmbeddedRecord(symbol, embedded, publicSurface);
                return false;

            default:
                // Unions have no C# spelling that is guaranteed to have the
                // size of the C type, and a class or an interface only ever
                // appears behind a pointer.
                return false;
        }
    }

    /// <summary>
    /// Projects a record that is embedded by value. It has to be a type that
    /// this run emits: a plain struct, or a mirror, which is internal and can
    /// therefore only sit in another mirror.
    /// </summary>
    /// <param name="symbol">The embedded record.</param>
    /// <param name="embedded">Its declaration.</param>
    /// <param name="publicSurface">Whether the embedding struct is part of the API.</param>
    /// <returns>The projection, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// The mirror of a boxed, an opaque or a mini object record is only
    /// embeddable when its own layout is complete: a prefix is shorter than the
    /// C structure, so everything behind it in the embedding structure would sit
    /// at the wrong offset. A prefix is fine at the end of the chain, where
    /// nothing follows it, which is why it is a mirror of its own and not one of
    /// these. No mini object mirror is a prefix today, and the check is what
    /// keeps that from silently shifting every field behind one if it changes.
    /// </remarks>
    private FieldProjection? ProjectEmbeddedRecord(GirSymbol symbol, GirRecord embedded, bool publicSurface)
    {
        ModuleInfo? module = ModuleMap.Find(symbol.Namespace.Name);
        if (module is not { IsGenerated: true }
            || !embedded.IsIntrospectable
            || _overlays.IsSkipped(symbol.QualifiedName))
        {
            return null;
        }

        string typeName = _names.TypeName(symbol);
        string name = module.ClrNamespace + "." + typeName;
        return _classifier.Classify(embedded) switch
        {
            TypeKind.MiniObject or TypeKind.Boxed or TypeKind.OpaqueRecord when !publicSurface
                && HasCompleteLayout(symbol.Namespace, embedded, typeName) =>
                new FieldProjection(name + RawSuffix, null, null),
            TypeKind.PlainStruct => new FieldProjection(name, null, null),
            _ => null,
        };
    }

    /// <summary>
    /// Tests whether a record lays out in full: every field projected, the
    /// fields that are private to the C implementation projected as the padding
    /// they are, no union and no truncation.
    /// </summary>
    /// <param name="ns">The gir namespace of the record.</param>
    /// <param name="record">The record to inspect.</param>
    /// <param name="typeName">Its C# name.</param>
    /// <returns><see langword="true"/> when the mirror has the size of the C structure.</returns>
    private bool HasCompleteLayout(GirNamespace ns, GirRecord record, string typeName)
    {
        // C cannot embed a structure in itself, but the guard costs nothing and
        // keeps a malformed gir from recursing without end.
        string qualifiedName = ns.Name + "." + record.Name;
        if (!_completeLayouts.Add(qualifiedName))
        {
            return false;
        }

        try
        {
            return TryLayout(ns, record, typeName, publicSurface: false, LayoutTail.None, out _)
                is { Count: > 0 };
        }
        finally
        {
            _completeLayouts.Remove(qualifiedName);
        }
    }

    /// <summary>How far a layout may fall short of the whole C structure.</summary>
    private enum LayoutTail
    {
        /// <summary>
        /// Every field has to be projected. This is what a record that
        /// marshals by value needs: it is passed around as a copy, so a
        /// mirror that is short of the C size would corrupt the stack.
        /// </summary>
        None,

        /// <summary>
        /// The layout may stop early, as long as every remaining field is
        /// private to the C implementation. This is what the mirror of a mini
        /// object uses: the wrapper is a pointer, and nothing behind the stop
        /// carries API.
        /// </summary>
        Private,

        /// <summary>
        /// The layout may stop at the first field it cannot project, whatever
        /// follows it. This is what the mirror of a boxed or opaque record
        /// uses: the wrapper is a pointer, the offsets in front of the stop
        /// are exact, and the total size is never needed.
        /// </summary>
        Prefix,
    }

    /// <summary>How one gir field is spelled in C#.</summary>
    /// <param name="TypeName">The C# type of the field.</param>
    /// <param name="InlineArray">The inline storage type, for a fixed size array.</param>
    /// <param name="Note">A comment that explains a lossy projection.</param>
    /// <param name="IsPointer">
    /// Whether the field is a bare pointer, that is an <c>nint</c> that stands
    /// for a C address rather than for a value of its own. A function pointer
    /// slot is not one: it is spelled the same way, but no typed accessor is
    /// ever going to compete with it for the name.
    /// </param>
    private sealed record FieldProjection(
        string TypeName,
        InlineArrayInfo? InlineArray,
        string? Note,
        bool IsPointer = false);

    /// <summary>One field of a generated struct.</summary>
    /// <param name="Field">The gir field.</param>
    /// <param name="Name">The name the field is emitted under.</param>
    /// <param name="PascalName">The name the field carries when it is not private.</param>
    /// <param name="TypeName">The C# type of the field.</param>
    /// <param name="IsPrivate">Whether the field is private to the C implementation.</param>
    /// <param name="InlineArray">The inline storage type, for a fixed size array.</param>
    /// <param name="Note">A comment that explains a lossy projection.</param>
    private sealed record LayoutField(
        GirField Field,
        string Name,
        string PascalName,
        string TypeName,
        bool IsPrivate,
        InlineArrayInfo? InlineArray,
        string? Note);

    /// <summary>One generated field accessor of a mini object wrapper.</summary>
    /// <param name="Field">The gir field.</param>
    /// <param name="Name">The name of the property.</param>
    /// <param name="TypeName">The C# type of the property.</param>
    /// <param name="Expression">The expression body of the property.</param>
    private sealed record Accessor(GirField Field, string Name, string TypeName, string Expression);
}
