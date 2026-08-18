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
/// function of the record.</description></item>
/// <item><description><see cref="TypeKind.PlainStruct"/>: a struct that is
/// marshalled by value.</description></item>
/// <item><description><see cref="TypeKind.OpaqueRecord"/>: a sealed wrapper
/// around the bare pointer.</description></item>
/// </list>
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
    private readonly DiagnosticBag _diagnostics;
    private readonly SurfaceBuilder _surfaces;
    private readonly EmissionCensus _census;
    private readonly List<RegistryEntry> _registry;

    /// <summary>Initializes a new instance of the <see cref="RecordEmitter"/> class.</summary>
    /// <param name="repository">The loaded gir repository.</param>
    /// <param name="classifier">The type classifier.</param>
    /// <param name="names">The name mapper.</param>
    /// <param name="types">The type map.</param>
    /// <param name="overlays">The overlay configuration.</param>
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
                layout = TryLayout(ns, record, typeName, publicSurface: true, allowPrivateTail: false, out truncated);
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
                layout = TryLayout(ns, record, typeName, publicSurface: false, allowPrivateTail: true, out truncated);
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
        }

        // A hand written wrapper only reaches a file when the generator still
        // owns the mirror of its native layout.
        bool emitsMirror = kind == TypeKind.MiniObject && layout is not null;
        if (handWritten && !emitsMirror)
        {
            return null;
        }

        TypeSurface surface = BuildSurface(module, ns, record, kind, typeName, handWritten);

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
                    WriteStruct(writer, record, typeName, layout!);
                    break;

                case TypeKind.MiniObject:
                    WriteMiniObject(writer, module, record, typeName, accessors, surface);
                    break;

                case TypeKind.Boxed:
                    WriteBoxed(writer, module, record, typeName, surface);
                    break;

                default:
                    WriteOpaque(writer, module, record, typeName, surface);
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
            WriteRawStruct(writer, record, typeName, layout!, truncated);
        }

        _census.Emitted(module.GirNamespace, "record");
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
        if (truncated)
        {
            writer.WriteLine("/// <para>");
            writer.WriteLine("/// It stops at the first field that has no portable C# spelling. Every field");
            writer.WriteLine("/// behind that one is private to the C implementation and is never read.");
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

    private void WriteStruct(CodeWriter writer, GirRecord record, string typeName, IReadOnlyList<LayoutField> layout)
    {
        XmlDocWriter.Write(writer, record.Doc, FallbackSummary(record, "structure"), record);
        XmlDocWriter.WriteObsolete(writer, record);
        writer.WriteLine("[StructLayout(LayoutKind.Sequential)]");
        writer.WriteLine("public partial struct " + typeName);
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
    /// Plans the members of a wrapper. Plain structs get none: an instance
    /// method of a structure would have to pin the instance, and none of the
    /// structures of this milestone needs one.
    /// </summary>
    /// <param name="module">The module being emitted.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <param name="record">The record being emitted.</param>
    /// <param name="kind">Its classification.</param>
    /// <param name="typeName">Its C# name.</param>
    /// <param name="handWritten">Whether the wrapper itself is hand written.</param>
    /// <returns>The members to emit.</returns>
    private TypeSurface BuildSurface(
        ModuleInfo module,
        GirNamespace ns,
        GirRecord record,
        TypeKind kind,
        string typeName,
        bool handWritten)
    {
        if (handWritten || kind == TypeKind.PlainStruct)
        {
            foreach (GirFunction callable in
                record.Constructors.Concat(record.Methods).Concat(record.Functions))
            {
                _census.Skipped(
                    module.GirNamespace,
                    SkipReason.UnsupportedSignature,
                    callable.CIdentifier is { Length: > 0 } identifier
                        ? identifier
                        : record.Name + "." + callable.Name);
            }

            return new TypeSurface([], [], []);
        }

        List<string> reserved = [.. SurfaceBuilder.WrapperNames, typeName];
        if (kind == TypeKind.MiniObject)
        {
            reserved.AddRange(SurfaceBuilder.MiniObjectNames);
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

        foreach (Accessor accessor in accessors)
        {
            writer.WriteLine();
            XmlDocWriter.Write(writer, accessor.Field.Doc, FallbackSummary(record, accessor.Field), accessor.Field);
            XmlDocWriter.WriteObsolete(writer, accessor.Field);
            WriteAccessor(writer, accessor);
        }

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
        TypeSurface surface)
    {
        XmlDocWriter.Write(writer, record.Doc, FallbackSummary(record, "boxed type"), record);
        XmlDocWriter.WriteObsolete(writer, record);
        writer.WriteLine(
            "public sealed " + (surface.NeedsUnsafe ? "unsafe " : string.Empty) + "partial class " + typeName
            + " : Gst.GObject.Boxed");
        writer.OpenBlock();

        WriteWrapperConstructor(
            writer,
            record,
            typeName,
            "base(handle, new Gst.GObject.GType(GetGType()), transfer)");

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
        TypeSurface surface)
    {
        XmlDocWriter.Write(writer, record.Doc, FallbackSummary(record, "record"), record);
        XmlDocWriter.WriteObsolete(writer, record);
        writer.WriteLine(
            "public sealed " + (surface.NeedsUnsafe ? "unsafe " : string.Empty) + "partial class " + typeName);
        writer.OpenBlock();

        writer.WriteLine("/// <summary>The native instance.</summary>");
        writer.WriteLine("internal nint Handle;");
        writer.WriteLine();
        WriteWrapperConstructor(writer, record, typeName, baseCall: null);

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

    /// <summary>Writes one field accessor of a mini object wrapper.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="accessor">The accessor to write.</param>
    /// <remarks>
    /// The body reads through the raw pointer of the wrapper, so the wrapper
    /// has to stay reachable until the read is done. Without that, the last
    /// statement that mentions the wrapper is the one that took its handle, and
    /// the finalizer may release the instance while the pointer into it is
    /// still being dereferenced.
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
    /// <param name="allowPrivateTail">
    /// <see langword="true"/> when the layout may stop early, as long as every
    /// remaining field is private to the C implementation.
    /// </param>
    /// <param name="truncated">Whether the layout stopped early.</param>
    /// <returns>The fields, or <see langword="null"/> when the record cannot be laid out.</returns>
    private List<LayoutField>? TryLayout(
        GirNamespace ns,
        GirRecord record,
        string typeName,
        bool publicSurface,
        bool allowPrivateTail,
        out bool truncated)
    {
        truncated = false;
        List<LayoutField> layout = [];
        for (int i = 0; i < record.Fields.Count; i++)
        {
            GirField field = record.Fields[i];
            string pascalName = _names.FieldName(ns, record, field);
            if (string.Equals(pascalName, typeName, StringComparison.Ordinal))
            {
                // A member cannot carry the name of its enclosing type.
                pascalName += "Field";
            }

            FieldProjection? projection = Project(ns, field, pascalName, publicSurface);
            if (projection is null)
            {
                if (!allowPrivateTail || !IsTailHidden(record, i))
                {
                    return null;
                }

                truncated = true;
                break;
            }

            bool hidden = IsHidden(field);
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

    private string TypeNameOf(GirNamespace ns, GirRecord record) =>
        _names.TypeName(new GirSymbol(ns, record.Name, GirSymbolKind.Record, record));

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
            // pointer wide.
            return new FieldProjection(NativeInt, null, null);
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
    /// this run emits: a plain struct, or the mirror of a mini object, which is
    /// internal and can therefore only sit in another mirror.
    /// </summary>
    /// <param name="symbol">The embedded record.</param>
    /// <param name="embedded">Its declaration.</param>
    /// <param name="publicSurface">Whether the embedding struct is part of the API.</param>
    /// <returns>The projection, or <see langword="null"/> when there is none.</returns>
    private FieldProjection? ProjectEmbeddedRecord(GirSymbol symbol, GirRecord embedded, bool publicSurface)
    {
        ModuleInfo? module = ModuleMap.Find(symbol.Namespace.Name);
        if (module is not { IsGenerated: true }
            || !embedded.IsIntrospectable
            || _overlays.IsSkipped(symbol.QualifiedName))
        {
            return null;
        }

        string name = module.ClrNamespace + "." + _names.TypeName(symbol);
        return _classifier.Classify(embedded) switch
        {
            TypeKind.MiniObject when !publicSurface => new FieldProjection(name + RawSuffix, null, null),
            TypeKind.PlainStruct => new FieldProjection(name, null, null),
            _ => null,
        };
    }

    /// <summary>How one gir field is spelled in C#.</summary>
    /// <param name="TypeName">The C# type of the field.</param>
    /// <param name="InlineArray">The inline storage type, for a fixed size array.</param>
    /// <param name="Note">A comment that explains a lossy projection.</param>
    private sealed record FieldProjection(string TypeName, InlineArrayInfo? InlineArray, string? Note);

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
