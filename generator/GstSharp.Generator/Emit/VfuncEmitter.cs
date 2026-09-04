using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Planning;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>
/// Writes the subclassing surface of an allowlisted class: the
/// <c>On&lt;Vfunc&gt;</c> members a subclass overrides, the chain-up helpers
/// that reach the implementation below it, the trampolines the class struct
/// slots are set to and the registration that declares them.
/// </summary>
/// <remarks>
/// The shape is the hand written one of stage 1, which
/// <c>docs/subclassing.md</c> §4 describes and which the package validation
/// baseline holds in place. Everything a slot needs is decided by
/// <see cref="MarshalPlanner.PlanVirtualMethod"/>; what is written here is the
/// rendering of those decisions.
/// </remarks>
internal sealed class VfuncEmitter
{
    /// <summary>The directory the generated partials live in.</summary>
    internal const string DirectoryName = "Subclassing";

    /// <summary>The census category one emitted slot counts under.</summary>
    private const string Category = "vfunc";

    /// <summary>
    /// The per class facts about registering a subclass that no gir states: the
    /// pad templates the base class needs to find on the class, and the slot a
    /// subclass has to declare because the base class calls it unguarded.
    /// </summary>
    private static readonly Dictionary<string, SubclassBaseRule> BaseRules =
        new(StringComparer.Ordinal)
        {
            ["GstBase.BaseSrc"] = new(["src"], []),
            ["GstBase.PushSrc"] = new(["src"], []),
            ["GstBase.BaseSink"] = new(["sink"], []),
            ["GstBase.BaseTransform"] = new(["sink", "src"], []),
            ["GstBase.Aggregator"] = new(["src"], [new("aggregate", null)]),
            ["GstAudio.AudioBaseSink"] = new(
                ["sink"],
                [new("create_ringbuffer", "without a ring buffer the element cannot leave the NULL state")]),
            ["GstAudio.AudioBaseSrc"] = new(
                ["src"],
                [new("create_ringbuffer", "without a ring buffer the element cannot leave the NULL state")]),
            ["GstAudio.AudioSink"] = new(
                ["sink"],
                [
                    new(
                        "prepare",
                        "the ring buffer cannot be acquired without it - "
                        + "gst_audio_sink_ring_buffer_acquire starts out with a failure and only the slot "
                        + "turns it into a success"),
                    new(
                        "unprepare",
                        "the ring buffer cannot be released without it - "
                        + "gst_audio_sink_ring_buffer_release starts out with a failure the same way, and a "
                        + "ring buffer that is never released is still acquired when it is finalized"),
                    new(
                        "write",
                        "the thread of the ring buffer stops before it starts when the slot is NULL, "
                        + "and the element plays nothing without saying why"),
                ]),
            ["GstAudio.AudioSrc"] = new(
                ["src"],
                [
                    new(
                        "prepare",
                        "the ring buffer cannot be acquired without it - "
                        + "gst_audio_src_ring_buffer_acquire starts out with a failure and only the slot "
                        + "turns it into a success"),
                    new(
                        "unprepare",
                        "the ring buffer cannot be released without it - "
                        + "gst_audio_src_ring_buffer_release starts out with a failure the same way, and a "
                        + "ring buffer that is never released is still acquired when it is finalized"),
                    new(
                        "read",
                        "the thread of the ring buffer stops before it starts when the slot is NULL, "
                        + "and the element produces nothing without saying why"),
                ]),
            ["GstAudio.AudioFilter"] = new(["sink", "src"], []),
            ["GstAudio.AudioDecoder"] = new(
                ["sink", "src"],
                [
                    new(
                        "handle_frame",
                        "the base class calls it for every buffer it parsed out of the stream and for the "
                        + "drain at the end of it, unguarded - a decoder without it decodes nothing"),
                ]),
            ["GstAudio.AudioEncoder"] = new(
                ["sink", "src"],
                [
                    new(
                        "handle_frame",
                        "the base class calls it for every block of samples and for the drain at the end of "
                        + "the stream, unguarded - an encoder without it encodes nothing"),
                ]),
            ["GstBase.BaseParse"] = new(
                ["sink", "src"],
                [
                    new(
                        "handle_frame",
                        "the base class calls it for every frame it collected, unguarded - it is where the "
                        + "framing a parser exists for is decided"),
                ]),
            ["GstVideo.VideoSink"] = new(["sink"], []),
            ["GstVideo.VideoFilter"] = new(["sink", "src"], []),
            ["GstVideo.VideoDecoder"] = new(
                ["sink", "src"],
                [
                    new(
                        "handle_frame",
                        "the base class calls it for every frame it gathered, unguarded - a decoder without "
                        + "it decodes nothing"),
                ]),
            ["GstVideo.VideoEncoder"] = new(
                ["sink", "src"],
                [
                    new(
                        "handle_frame",
                        "the base class calls it for every frame it was handed, unguarded - an encoder "
                        + "without it encodes nothing"),
                ]),
        };

    /// <summary>
    /// The value a trampoline answers when the override threw, per return type.
    /// Everything else answers the zero of its type, which is what a slot that
    /// says nothing about failure leaves behind.
    /// </summary>
    private static readonly Dictionary<string, string> FailureValues =
        new(StringComparer.Ordinal)
        {
            ["Gst.StateChangeReturn"] = "Gst.StateChangeReturn.Failure",
            ["Gst.FlowReturn"] = "Gst.FlowReturn.Error",
            ["Gst.ClockReturn"] = "Gst.ClockReturn.Error",
        };

    /// <summary>
    /// The value a chain-up leaves in an <c>out</c> parameter when the parent
    /// class left the slot NULL, for the types whose zero means something else
    /// than "no value". A C caller pre-sets the storage to this before it
    /// checks the slot, so a chain-up that wrote the zero of the type would say
    /// "timestamp 0" where C says "no timestamp".
    /// </summary>
    private static readonly Dictionary<string, string> NoValues =
        new(StringComparer.Ordinal)
        {
            ["Gst.ClockTime"] = "Gst.ClockTime.None",
        };

    private readonly Dictionary<string, HashSet<string>> _emittedSlots;
    private readonly MarshalPlanner _planner;
    private readonly EmissionCensus _census;
    private readonly Overlays _overlays;
    private readonly DiagnosticBag _diagnostics;

    /// <summary>Creates the emitter of one run.</summary>
    /// <param name="planner">The planner that decides how a slot is marshalled.</param>
    /// <param name="census">The census of the run.</param>
    /// <param name="overlays">The corrections of the run.</param>
    /// <param name="diagnostics">Where a slot that cannot be emitted is reported.</param>
    /// <param name="emittedSlots">
    /// The slots every class of the run has emitted so far, keyed by qualified
    /// gir name and shared by every module: a class declares its own
    /// <c>&lt;Vfunc&gt;Override</c> with <c>new</c> when an ancestor already
    /// carries one of that name, and a parent is always emitted first.
    /// </param>
    internal VfuncEmitter(
        MarshalPlanner planner,
        EmissionCensus census,
        Overlays overlays,
        DiagnosticBag diagnostics,
        Dictionary<string, HashSet<string>> emittedSlots)
    {
        _emittedSlots = emittedSlots;
        _planner = planner;
        _census = census;
        _overlays = overlays;
        _diagnostics = diagnostics;
    }

    /// <summary>Writes the subclassing partial of every allowlisted class of one module.</summary>
    /// <param name="module">The module being emitted.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <param name="subclasses">The class structs of the run.</param>
    /// <returns>One file per allowlisted class.</returns>
    internal IReadOnlyList<GeneratedFile> Emit(ModuleInfo module, GirNamespace ns, SubclassModel subclasses)
    {
        List<GeneratedFile> files = [];
        foreach (ClassStructModel model in subclasses.ClassStructs)
        {
            if (!model.IsSubclassable || !ReferenceEquals(model.Namespace, ns))
            {
                continue;
            }

            if (EmitOne(module, ns, model) is { } file)
            {
                files.Add(file);
            }
        }

        return files;
    }

    /// <summary>Renders the surface of one class.</summary>
    /// <param name="module">The module being emitted.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <param name="model">The class struct of the class.</param>
    /// <returns>The file.</returns>
    /// <remarks>
    /// A class none of whose own slots survived the planner is emitted all the
    /// same: <c>AudioFilter</c> declares one slot and it is not bindable, and
    /// yet a managed audio filter is a useful thing that overrides the slots of
    /// <c>BaseTransform</c> - which it can only do through a registration and a
    /// constructor of its own.
    /// </remarks>
    private GeneratedFile? EmitOne(ModuleInfo module, GirNamespace ns, ClassStructModel model)
    {
        PlanningContext context = new(
            module,
            ns,
            TypeKind.GObjectClass,
            ModuleMap.ClrNamespaceOf(ns.Name) + "." + model.Owner.Name);

        List<VirtualMethodPlan> plans = [];
        foreach (ClassStructMember slot in model.Slots)
        {
            GirVirtualMethod method = slot.Method!;
            string key = model.KeyOf(method.Name);
            if (_overlays.IsVirtualSkipped(key))
            {
                _census.SkippedVirtual(module.GirNamespace, key, _overlays.VirtualSkipReason(key));
                continue;
            }

            VirtualMethodPlan? plan = _planner.PlanVirtualMethod(
                method,
                ClassStructEmitter.MemberNameOf(slot.Field),
                context,
                out string reason);

            if (plan is null)
            {
                _census.SkippedVirtual(module.GirNamespace, key, reason);
                continue;
            }

            plans.Add(plan);
            _census.Emitted(module.GirNamespace, Category);
        }

        HashSet<string> mine = new(StringComparer.Ordinal);
        HashSet<string> inherited = new(StringComparer.Ordinal);
        for (ClassStructModel? parent = model.Parent; parent is not null; parent = parent.Parent)
        {
            if (_emittedSlots.TryGetValue(parent.QualifiedName, out HashSet<string>? names))
            {
                inherited.UnionWith(names);
                continue;
            }

            // Everything the `new` rule decides is read out of this set, and a
            // parent that has not been emitted yet leaves it empty rather than
            // wrong: the member would be written without `new` and, worse, the
            // return type collision below would not be seen. A subclassable
            // parent is always emitted first - modules in dependency order,
            // classes parent first within one - and a class whose own slots are
            // all skipped still has an entry, so a miss here is an ordering bug
            // of this generator and nothing a gir can cause.
            if (parent.IsSubclassable)
            {
                throw new InvalidOperationException(
                    $"'{model.QualifiedName}' is emitted before its parent '{parent.QualifiedName}'; "
                    + "the subclassing surface has to be written parent first.");
            }
        }

        foreach (VirtualMethodPlan plan in plans)
        {
            _ = mine.Add(plan.Name);
            _ = mine.Add(SignatureOf(plan));
            _ = mine.Add(AnsweredSignatureOf(plan));
            ReportReturnTypeCollision(model, plan, inherited);
        }

        _emittedSlots[model.QualifiedName] = mine;

        string type = model.Owner.Name;
        string mirror = ClassStructEmitter.MirrorNameOf(model);
        string cName = model.Owner.CType ?? model.Owner.Name;
        CodeWriter writer = new();
        writer.WriteLine("// <auto-generated/>");
        writer.WriteLine("// Generated by GstSharp.Generator from " + ns.Name + "-" + ns.Version + ".gir. Do not edit.");
        writer.WriteLine();
        writer.WriteLine("#nullable enable");
        writer.WriteLine();

        // A class whose own slots are all skipped still carries a
        // registration, because a subclass of it overrides the slots of its
        // parents; what it does not carry is a trampoline, and with it the
        // attribute and the calling convention those two namespaces are for.
        if (plans.Count > 0)
        {
            writer.WriteLine("using System.Runtime.CompilerServices;");
            writer.WriteLine("using System.Runtime.InteropServices;");
            writer.WriteLine();
        }

        writer.WriteLine("namespace " + module.ClrNamespace + ";");
        writer.WriteLine();
        writer.WriteLine("/// <content>The subclassing surface of <c>" + cName + "</c>.</content>");
        writer.WriteLine("public unsafe partial class " + type);
        writer.OpenBlock();

        WriteConstructor(writer, type, cName);
        foreach (VirtualMethodPlan plan in plans)
        {
            writer.WriteLine();
            WriteOverrideDeclaration(writer, plan, mirror, cName, inherited.Contains(plan.Name));
        }

        writer.WriteLine();
        WriteDefineSubclass(writer, model, plans, cName);

        foreach (VirtualMethodPlan plan in plans)
        {
            writer.WriteLine();
            WriteVirtual(writer, plan, cName, inherited.Contains(SignatureOf(plan)));
        }

        foreach (VirtualMethodPlan plan in plans)
        {
            writer.WriteLine();
            WriteInstanceChainUp(writer, plan, model, inherited.Contains(SignatureOf(plan)));
        }

        foreach (VirtualMethodPlan plan in plans)
        {
            writer.WriteLine();
            WriteStaticChainUp(writer, plan, model);
        }

        if (plans.Count > 0)
        {
            writer.WriteLine();
            WriteParentClassOf(writer, mirror, cName);
        }

        foreach (VirtualMethodPlan plan in plans)
        {
            writer.WriteLine();
            WriteTrampoline(writer, plan, type);
        }

        writer.CloseBlock();
        return new GeneratedFile(
            module.ProjectDirectory + "/Generated/" + DirectoryName + "/" + type + ".Subclass.cs",
            writer.ToSource());
    }

    private static void WriteConstructor(CodeWriter writer, string type, string cName)
    {
        writer.WriteLine("/// <summary>");
        writer.WriteLine("/// Constructs the managed subclass that");
        writer.WriteLine("/// <see cref=\"Gst.GObject.SubclassType.NewInstance()\"/> created an instance of.");
        writer.WriteLine("/// </summary>");
        writer.WriteLine("/// <param name=\"args\">The instance, as the registration handed it out.</param>");
        writer.WriteLine(
            "/// <exception cref=\"System.ArgumentException\">The instance is not a <c>" + cName + "</c>.</exception>");
        writer.WriteLine("protected " + type + "(Gst.GObject.SubclassCtorArgs args)");
        writer.WriteLine("    : this(args.HandleFor(GetGType()), args.Transfer)");
        writer.OpenBlock();
        writer.CloseBlock();
    }

    private static void WriteOverrideDeclaration(
        CodeWriter writer,
        VirtualMethodPlan plan,
        string mirror,
        string cName,
        bool hides)
    {
        writer.WriteLine("/// <summary>");
        writer.WriteLine(
            "/// Gets the declaration of <c>" + cName + "." + plan.Method.Name + "</c>, for a subclass that");
        writer.WriteLine("/// overrides <see cref=\"On" + plan.Name + "\"/>.");
        writer.WriteLine("/// </summary>");
        writer.WriteLine(
            "public static " + (hides ? "new " : string.Empty) + "Gst.GObject.VfuncOverride " + plan.Name
            + "Override { get; } = new(");
        writer.WriteLine("    &GetGType,");
        writer.WriteLine("    " + mirror + "." + plan.SlotMember + "Offset,");
        writer.WriteLine("    (nint)(" + FunctionPointerType(plan) + ")&" + plan.Name + "Trampoline);");
    }

    private void WriteDefineSubclass(
        CodeWriter writer,
        ClassStructModel model,
        IReadOnlyList<VirtualMethodPlan> plans,
        string cName)
    {
        BaseRules.TryGetValue(model.QualifiedName, out SubclassBaseRule? rule);
        bool mandatory = rule is { PadTemplates.Count: > 0 };
        bool hides = false;
        bool element = false;
        for (ClassStructModel? ancestor = model; ancestor is not null; ancestor = ancestor.Parent)
        {
            element |= string.Equals(ancestor.QualifiedName, "Gst.Element", StringComparison.Ordinal);
            if (ancestor != model)
            {
                hides |= ancestor.IsSubclassable;
            }
        }

        // A GstElementClass is what carries the metadata and the pad templates,
        // so a class that is not an element - Gst.Pad and what derives from it -
        // is configured through the GObject level facade instead. The
        // registration reads the same fact off the parent type and hands the
        // matching one out.
        string facade = element ? "Gst.GObject.ClassConfig" : "Gst.GObject.ObjectClassConfig";
        string configure = mandatory ? "Action<" + facade + ">" : "Action<" + facade + ">?";
        string type = model.Owner.Name;

        List<(string Name, string? Reason)> required = [];
        foreach (RequiredSlot slot in rule?.Required ?? [])
        {
            string? name = null;
            foreach (VirtualMethodPlan plan in plans)
            {
                if (string.Equals(plan.Method.Name, slot.Slot, StringComparison.Ordinal))
                {
                    name = plan.Name;
                }
            }

            if (name is null)
            {
                _diagnostics.Warn(
                    "GEN0034",
                    $"The class '{model.QualifiedName}' requires an override of '{slot.Slot}', which is not part "
                    + "of the emitted surface; the registration cannot check for it.");
                continue;
            }

            required.Add((name, slot.Reason));
        }

        WriteDefineSubclassDoc(writer, cName, rule, mandatory, generic: false);
        writer.WriteLine(
            "public static " + (hides ? "new " : string.Empty) + "Gst.GObject.SubclassType DefineSubclass(");
        writer.WriteLine("    string typeName,");
        writer.WriteLine("    " + configure + " configureClass,");
        writer.WriteLine("    params Gst.GObject.VfuncOverride[] overrides) =>");
        writer.WriteLine("    DefineSubclassCore(typeName, configureClass, overrides, null);");

        writer.WriteLine();
        WriteDefineSubclassDoc(writer, cName, rule, mandatory, generic: true);
        writer.WriteLine(
            "public static " + (hides ? "new " : string.Empty) + "Gst.GObject.SubclassType DefineSubclass<TSelf>(");
        writer.WriteLine("    string typeName,");
        writer.WriteLine("    " + configure + " configureClass,");
        writer.WriteLine("    params Gst.GObject.VfuncOverride[] overrides)");
        writer.WriteLine("    where TSelf : " + type + ", Gst.GObject.IManagedSubclass<TSelf> =>");
        writer.WriteLine(
            "    DefineSubclassCore(typeName, configureClass, overrides, static args => TSelf.CreateWrapper(args));");

        writer.WriteLine();
        writer.WriteLine("private static Gst.GObject.SubclassType DefineSubclassCore(");
        writer.WriteLine("    string typeName,");
        writer.WriteLine("    " + configure + " configureClass,");
        writer.WriteLine("    Gst.GObject.VfuncOverride[] overrides,");
        writer.WriteLine("    Func<Gst.GObject.SubclassCtorArgs, Gst.GObject.Object>? wrapFactory)");
        writer.OpenBlock();
        if (mandatory)
        {
            writer.WriteLine("ArgumentNullException.ThrowIfNull(configureClass);");
        }

        if (required.Count > 0)
        {
            writer.WriteLine("ArgumentNullException.ThrowIfNull(overrides);");
        }

        foreach ((string name, string? reason) in required)
        {
            writer.WriteLine();
            writer.WriteLine("bool declared" + name + " = false;");
            writer.WriteLine("foreach (Gst.GObject.VfuncOverride candidate in overrides)");
            writer.OpenBlock();
            writer.WriteLine("if (candidate.Function == " + name + "Override.Function)");
            writer.OpenBlock();
            writer.WriteLine("declared" + name + " = true;");
            writer.WriteLine("break;");
            writer.CloseBlock();
            writer.CloseBlock();
            writer.WriteLine();
            writer.WriteLine("if (!declared" + name + ")");
            writer.OpenBlock();
            writer.WriteLine("throw new ArgumentException(");
            writer.WriteLine(
                "    \"A managed " + cName + " has to declare " + name + "Override: "
                + (reason ?? "the base class calls the slot unguarded") + ".\",");
            writer.WriteLine("    nameof(overrides));");
            writer.CloseBlock();
        }

        if (mandatory || required.Count > 0)
        {
            writer.WriteLine();
        }

        writer.WriteLine("Gst.GObject.SubclassType type = Gst.GObject.SubclassType.Define(");
        writer.WriteLine("    new Gst.GObject.GType(GetGType()), typeName, configureClass, overrides, wrapFactory);");
        foreach (string template in rule?.PadTemplates ?? [])
        {
            writer.WriteLine("type.RequirePadTemplate(\"" + template + "\");");
        }

        writer.WriteLine("return type;");
        writer.CloseBlock();
    }

    /// <summary>
    /// Writes the documentation the two public registration overloads share.
    /// </summary>
    /// <param name="writer">The writer of the file.</param>
    /// <param name="cName">The C name of the class being registered.</param>
    /// <param name="rule">The per class facts of the class, or null.</param>
    /// <param name="mandatory">Whether the class initialiser is required.</param>
    /// <param name="generic">Whether the overload takes the subclass itself.</param>
    private static void WriteDefineSubclassDoc(
        CodeWriter writer,
        string cName,
        SubclassBaseRule? rule,
        bool mandatory,
        bool generic)
    {
        writer.WriteLine("/// <summary>Registers a managed subclass of <c>" + cName + "</c> with GObject.</summary>");
        if (generic)
        {
            writer.WriteLine("/// <typeparam name=\"TSelf\">");
            writer.WriteLine("/// The subclass itself, which states how its wrapper is built.");
            writer.WriteLine("/// </typeparam>");
        }

        writer.WriteLine("/// <param name=\"typeName\">The <c>GType</c> name, unique in the process.</param>");
        writer.WriteLine("/// <param name=\"configureClass\">");
        writer.WriteLine("/// Describes the class while it is being initialised.");
        if (mandatory)
        {
            writer.WriteLine(
                "/// It <b>has to</b> add a pad template named " + TemplateList(rule!.PadTemplates) + ".");
        }

        writer.WriteLine("/// </param>");
        writer.WriteLine("/// <param name=\"overrides\">The slots the subclass takes over.</param>");
        writer.WriteLine("/// <returns>The registration.</returns>");
        if (generic)
        {
            writer.WriteLine("/// <remarks>");
            writer.WriteLine("/// An instance of the registered type that native code creates - one an element");
            writer.WriteLine("/// factory made, a pad a base class built from a template - is wrapped as");
            writer.WriteLine("/// <typeparamref name=\"TSelf\"/> through");
            writer.WriteLine("/// <see cref=\"Gst.GObject.IManagedSubclass{TSelf}.CreateWrapper\"/>, so the overrides");
            writer.WriteLine("/// of the subclass run for it. The non generic overload registers no such factory");
            writer.WriteLine("/// and its instances arrive as the nearest wrapped ancestor.");
            writer.WriteLine("/// </remarks>");
        }

        writer.WriteLine(
            "/// <exception cref=\"System.ArgumentNullException\">An argument is <see langword=\"null\"/>.</exception>");
        writer.WriteLine("/// <exception cref=\"System.ArgumentException\">");
        writer.WriteLine("/// The type name is not a legal <c>GType</c> name, or a declared slot belongs to a");
        writer.WriteLine("/// class that <c>" + cName + "</c> does not derive from.");
        writer.WriteLine("/// </exception>");
        writer.WriteLine("/// <exception cref=\"System.InvalidOperationException\">");
        writer.WriteLine("/// The type name is taken, or the class initialiser failed.");
        writer.WriteLine("/// </exception>");
    }

    private static string TemplateList(IReadOnlyList<string> templates)
    {
        List<string> quoted = [];
        foreach (string template in templates)
        {
            quoted.Add("<c>" + template + "</c>");
        }

        return string.Join(" and one named ", quoted);
    }

    private static void WriteVirtual(CodeWriter writer, VirtualMethodPlan plan, string cName, bool hides)
    {
        XmlDocWriter.Write(
            writer,
            plan.Method.Doc,
            "Runs <c>" + cName + "." + plan.Method.Name + "</c>.",
            plan.Method,
            Remarks(plan, hides));

        WriteParameterDocs(writer, plan);
        WriteReturnDoc(writer, plan);
        writer.WriteLine(
            "protected " + (hides ? "new " : string.Empty) + "virtual " + ReturnType(plan) + " On" + plan.Name
            + "(" + PublicParameters(plan) + ") =>");
        writer.WriteLine("    ChainUp" + plan.Name + "(" + PublicArguments(plan) + ");");
    }

    /// <summary>
    /// Builds the generator authored part of the remarks of a slot: the note
    /// the overlays carry for it, and the note that a member of the same shape
    /// further up is hidden - a class struct that redeclares the slot of its
    /// parent, such as <c>GstBaseSrcClass.query</c> over
    /// <c>GstElementClass.query</c>, is a second slot and not an override of
    /// the first one.
    /// </summary>
    /// <param name="plan">The slot being written.</param>
    /// <param name="hides">Whether a base class carries a member of the same shape.</param>
    /// <returns>The lines, or <see langword="null"/> when there is nothing to add.</returns>
    private static IReadOnlyList<string>? Remarks(VirtualMethodPlan plan, bool hides)
    {
        if (plan.DocNote is null && !hides)
        {
            return null;
        }

        List<string> lines = [];
        if (plan.DocNote is { } note)
        {
            List<string> wrapped = Wrap(XmlDocWriter.Escape(note));
            wrapped[0] = "<para>" + wrapped[0];
            wrapped[^1] += "</para>";
            lines.AddRange(wrapped);
        }

        if (hides)
        {
            lines.Add(
                "<para>This hides the member of the same shape a base class carries. The two are");
            lines.Add(
                "different class struct slots and <c>" + plan.Method.Name + "</c> here is the one");
            lines.Add(
                "that runs for an instance of this type, so the hidden one is not overridden");
            lines.Add("from a subclass of this class.</para>");
        }

        return lines;
    }

    /// <summary>
    /// Builds the key that says whether a base class already carries the very
    /// member a slot declares: the managed name and the parameter types, with
    /// the parameter names dropped, because C# hides by shape. An <c>out</c>
    /// and a <c>ref</c> parameter share a shape, so they share a key.
    /// </summary>
    /// <param name="plan">The slot being written.</param>
    /// <returns>The key.</returns>
    /// <summary>
    /// The signature of a slot with the type it answers appended, which is what
    /// separates a member that may hide an inherited one from a member that
    /// only looks like it.
    /// </summary>
    /// <param name="plan">The slot.</param>
    /// <returns>The rendering.</returns>
    private static string AnsweredSignatureOf(VirtualMethodPlan plan) =>
        SignatureOf(plan) + " : " + ReturnType(plan);

    /// <summary>
    /// Reports the one collision the <c>new</c> rule must never paper over: a
    /// slot whose managed member has the name and the parameters of an
    /// inherited one but answers something else. C# allows <c>new</c> to change
    /// the return type, so the two members would compile and the element would
    /// run whichever one the static type of the caller picked - the base class
    /// calling through its own slot would reach the wrong override. There is no
    /// mechanical way out: the slot needs a managed name of its own, which is a
    /// decision, so the run stops instead.
    /// </summary>
    /// <param name="model">The class being emitted.</param>
    /// <param name="plan">The slot being emitted.</param>
    /// <param name="inherited">The signatures the ancestors of the class emitted.</param>
    private void ReportReturnTypeCollision(
        ClassStructModel model,
        VirtualMethodPlan plan,
        HashSet<string> inherited)
    {
        if (!inherited.Contains(SignatureOf(plan)) || inherited.Contains(AnsweredSignatureOf(plan)))
        {
            return;
        }

        string prefix = SignatureOf(plan) + " : ";
        string answered = "another type";
        foreach (string entry in inherited)
        {
            if (entry.StartsWith(prefix, StringComparison.Ordinal))
            {
                answered = entry[prefix.Length..];
            }
        }

        _diagnostics.Error(
            "GEN0040",
            $"The slot '{model.KeyOf(plan.Method.Name)}' would be emitted as 'On{plan.Name}' answering "
            + $"'{ReturnType(plan)}', which hides an inherited member of the same parameters answering "
            + $"'{answered}'. A managed name cannot carry both; skip the slot through 'skipVirtuals' or "
            + "give it a name of its own.");
    }

    private static string SignatureOf(VirtualMethodPlan plan)
    {
        List<string> parts = [];
        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Bucket == VfuncBucket.SpanCount)
            {
                continue;
            }

            string modifier = Modifier(argument);
            parts.Add((modifier.Length == 0 ? string.Empty : "ref ") + PublicType(argument));
        }

        return plan.Name + "(" + string.Join(", ", parts) + ")";
    }

    private static void WriteInstanceChainUp(
        CodeWriter writer,
        VirtualMethodPlan plan,
        ClassStructModel model,
        bool hides)
    {
        XmlDocWriter.Write(
            writer,
            null,
            "Runs the implementation of <c>" + plan.Method.Name + "</c> below the managed override.",
            null,
            Remarks(plan, hides));

        WriteParameterDocs(writer, plan);
        WriteReturnDoc(writer, plan);
        writer.WriteLine(
            "protected " + (hides ? "new " : string.Empty) + ReturnType(plan) + " ChainUp" + plan.Name
            + "(" + PublicParameters(plan) + ")");
        writer.OpenBlock();

        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (NeedsNullCheck(argument))
            {
                writer.WriteLine("ArgumentNullException.ThrowIfNull(" + argument.Argument.Name + ");");
            }
        }

        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Argument.Kind == ArgumentKind.Utf8)
            {
                string name = argument.Argument.Name;
                writer.WriteLine(
                    "System.Span<byte> " + name + "Buffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];");
                writer.WriteLine(
                    "using Gst.Interop.Utf8Scope " + name + "Scope = Gst.Interop.GMarshal.StackUtf8("
                    + name + ", " + name + "Buffer);");
            }
        }

        bool mints = false;
        bool handsOver = false;
        foreach (VfuncArgument argument in plan.Arguments)
        {
            mints |= (argument.Bucket == VfuncBucket.Adopt && !argument.IsBoxed)
                || (argument.Bucket == VfuncBucket.InOutHandle && !argument.IsIdentity);
            handsOver |= argument.Bucket == VfuncBucket.InOutHandOver;
        }

        string instance = "Handle";
        if (mints || handsOver)
        {
            instance = "instance";
            writer.WriteLine("nint instance = Handle;");
        }

        // The value of an inout argument is handed over to the parent slot, and
        // a parent that has none would leave the caller with nothing where it
        // handed a value in. The slot is therefore read before anything is
        // given up, and the documented default answered with the argument
        // untouched.
        if (handsOver)
        {
            writer.WriteLine(
                "if ((" + FunctionPointerType(plan) + ")ParentClassOf(" + instance + ")->" + plan.SlotMember
                + " is null)");
            writer.OpenBlock();
            if (plan.Return.IsVoid)
            {
                writer.WriteLine("return;");
            }
            else if (plan.NullSlotDefault is { } fallback && !fallback.TrimStart().StartsWith('{'))
            {
                writer.WriteLine("return " + fallback + ";");
            }
            else
            {
                writer.WriteLine("throw new InvalidOperationException(");
                writer.WriteLine(
                    "    \"" + model.Owner.Name + "." + plan.Method.Name
                    + " has no parent implementation; override On" + plan.Name + ".\");");
            }

            writer.CloseBlock();
            writer.WriteLine();
        }

        List<string> raw = [];
        int pinned = 0;
        foreach (VfuncArgument argument in plan.Arguments)
        {
            ArgumentPlan value = argument.Argument;
            string local = value.Name + "Native";
            switch (argument.Bucket)
            {
                // The block is pinned for the call: the parent slot is handed
                // the address of the elements, and the caller of the chain-up
                // may well have named a managed array.
                case VfuncBucket.Span:
                    writer.WriteLine(
                        "fixed (" + value.ElementType + "* " + local + " = " + value.Name + ")");
                    writer.OpenBlock();
                    pinned++;
                    raw.Add(local);
                    break;
                case VfuncBucket.SpanCount:
                    raw.Add("checked((" + value.RawType + ")" + argument.CountOf + ".Length)");
                    break;
                case VfuncBucket.Adopt:
                    // A boxed value has no reference to mint: the wrapper hands
                    // its own value over and is left detached, which is what
                    // the parent slot takes over.
                    writer.WriteLine(
                        "nint " + local + " = " + value.Name + (argument.IsBoxed ? ".HandOver();" : ".Handle;"));
                    raw.Add(local);
                    break;
                case VfuncBucket.InOutHandOver:
                    writer.WriteLine(
                        "nint " + local + " = " + value.Name + " is null ? nint.Zero : " + value.Name
                        + ".HandOver();");
                    raw.Add("&" + local);
                    break;
                case VfuncBucket.OutScalar:
                    writer.WriteLine(value.RawType.TrimEnd('*') + " " + local + " = default;");
                    raw.Add("&" + local);
                    break;
                case VfuncBucket.OutHandle:
                    writer.WriteLine("nint " + local + " = nint.Zero;");
                    raw.Add("&" + local);
                    break;
                case VfuncBucket.InOutHandle:
                    writer.WriteLine(
                        "nint " + local + " = " + value.Name + " is null ? nint.Zero : " + value.Name + ".Handle;");
                    if (argument.IsIdentity)
                    {
                        writer.WriteLine("nint " + value.Name + "Entry = " + local + ";");
                    }

                    raw.Add("&" + local);
                    break;
                default:
                    raw.Add(ToNativeArgument(argument));
                    break;
            }
        }

        // The reference every consuming slot is handed is minted after every
        // handle has been read, so that a disposed wrapper throws without
        // leaving a reference behind that nothing releases.
        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Bucket is VfuncBucket.Adopt or VfuncBucket.InOutHandle && !argument.IsBoxed)

            {
                // An identity preserving inout handle is neither owned nor
                // consumed by the slot: the caller keeps the reference of the
                // value on entry and releases the answer only when the two
                // differ, so a reference minted for the parent is one nobody
                // gives back.
                if (argument.Bucket == VfuncBucket.InOutHandle && argument.IsIdentity)
                {
                    continue;
                }

                string local = argument.Argument.Name + "Native";
                string mint = MintExpression(argument.Argument, local);
                if (argument.Bucket == VfuncBucket.InOutHandle)
                {
                    writer.WriteLine("if (" + local + " != nint.Zero)");
                    writer.OpenBlock();
                    writer.WriteLine(mint + ";");
                    writer.CloseBlock();
                }
                else
                {
                    writer.WriteLine(mint + ";");
                }
            }
        }

        string call = raw.Count == 0
            ? "ChainUp" + plan.Name + "(" + instance + ")"
            : "ChainUp" + plan.Name + "(" + instance + ", " + string.Join(", ", raw) + ")";

        if (plan.Return.IsVoid)
        {
            writer.WriteLine(call + ";");
        }
        else if (AnswersHandle(plan))
        {
            writer.WriteLine(plan.Return.RawType + " resultNative = " + call + ";");
            if (plan.NonNullReturn is null)
            {
                writer.WriteLine(ReturnType(plan) + " result = " + WrapReturn(plan, "resultNative") + ";");
            }
            else
            {
                writer.WriteLine(
                    ReturnType(plan) + " result = " + WrapReturn(plan, "resultNative"));
                writer.WriteLine("    ?? throw new InvalidOperationException(");
                writer.WriteLine(
                    "        \"" + plan.Method.Name + " answered null below the managed override.\");");
            }
        }
        else
        {
            writer.WriteLine(ReturnType(plan) + " result = " + call + ";");
        }

        writer.WriteLine("GC.KeepAlive(this);");
        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Bucket is VfuncBucket.BorrowGObject or VfuncBucket.BorrowMiniObject
                or VfuncBucket.BorrowBoxed or VfuncBucket.BorrowWrapper or VfuncBucket.BorrowOpaque)
            {
                writer.WriteLine("GC.KeepAlive(" + argument.Argument.Name + ");");
            }
        }

        foreach (VfuncArgument argument in plan.Arguments)
        {
            ArgumentPlan value = argument.Argument;
            string local = value.Name + "Native";
            switch (argument.Bucket)
            {
                case VfuncBucket.Adopt:
                    // The wrapper of a boxed value handed its value over
                    // already and is detached; there is nothing to dispose.
                    if (!argument.IsBoxed)
                    {
                        writer.WriteLine(value.Name + ".Dispose();");
                    }

                    break;
                case VfuncBucket.InOutHandOver:
                    // The wrapper the caller passed in is detached, so what the
                    // parent slot left behind is wrapped afresh: the reference
                    // the caller gave up is the one the new wrapper owns.
                    writer.WriteLine(
                        value.Name + " = " + local + " == nint.Zero ? null : "
                        + AdoptExpression(value, local) + ";");
                    break;
                case VfuncBucket.OutScalar:
                    writer.WriteLine(value.Name + " = " + FromNativeScalar(value, local) + ";");
                    break;
                case VfuncBucket.OutHandle:
                    // An identity preserving answer names the very handle the
                    // slot was given, which carries no reference of its own:
                    // the wrapper of the input is handed back instead of a
                    // second one that would claim the caller's reference.
                    writer.WriteLine(
                        value.Name + " = " + local + " == nint.Zero ? null : "
                        + (argument.IsIdentity && argument.IdentityReference is { } reference
                            ? local + " == " + HandleOf(plan, reference) + " ? " + reference + " : "
                            : string.Empty)
                        + AdoptExpression(value, local) + ";");
                    break;
                case VfuncBucket.InOutHandle:
                    if (argument.IsIdentity)
                    {
                        // The parent left the handle alone, so the wrapper the
                        // caller holds still names it and still owns whatever
                        // reference it owned.
                        writer.WriteLine("if (" + local + " != " + value.Name + "Entry)");
                        writer.OpenBlock();
                    }

                    writer.WriteLine(value.Name + "?.Dispose();");
                    writer.WriteLine(
                        value.Name + " = " + local + " == nint.Zero ? null : "
                        + AdoptExpression(value, local) + ";");
                    if (argument.IsIdentity)
                    {
                        writer.CloseBlock();
                    }

                    break;
                default:
                    break;
            }
        }

        if (!plan.Return.IsVoid)
        {
            writer.WriteLine("return result;");
        }

        for (int block = 0; block < pinned; block++)
        {
            writer.CloseBlock();
        }

        writer.CloseBlock();
    }

    private static void WriteStaticChainUp(CodeWriter writer, VirtualMethodPlan plan, ClassStructModel model)
    {
        string pointer = FunctionPointerType(plan);
        List<string> parameters = ["nint " + plan.InstanceName];
        List<string> arguments = [plan.InstanceName];
        foreach (VfuncArgument argument in plan.Arguments)
        {
            parameters.Add(argument.Argument.RawType + " " + argument.Argument.Name);
            arguments.Add(argument.Argument.Name);
        }

        bool raw = AnswersHandle(plan);
        writer.WriteLine(
            "private static " + (raw ? plan.Return.RawType : ReturnType(plan)) + " ChainUp" + plan.Name + "("
            + string.Join(", ", parameters) + ")");
        writer.OpenBlock();
        writer.WriteLine(pointer + " slot =");
        writer.WriteLine(
            "    (" + pointer + ")ParentClassOf(" + plan.InstanceName + ")->" + plan.SlotMember + ";");
        writer.WriteLine();
        writer.WriteLine("if (slot is null)");
        writer.OpenBlock();
        WriteNullSlotBranch(writer, plan, model);
        writer.CloseBlock();
        writer.WriteLine();
        string call = "slot(" + string.Join(", ", arguments) + ")";
        if (plan.Return.IsVoid)
        {
            writer.WriteLine(call + ";");
        }
        else
        {
            writer.WriteLine("return " + (raw ? call : FromNativeReturn(plan, call)) + ";");
        }

        writer.CloseBlock();
    }

    /// <summary>
    /// Writes what a chain-up does when the parent class left the slot NULL,
    /// which is the behaviour the base class documents for that case.
    /// </summary>
    /// <param name="writer">Where the branch is written.</param>
    /// <param name="plan">The slot being written.</param>
    /// <param name="model">The class struct of the class.</param>
    /// <remarks>
    /// An overlay default that opens with <c>{</c> is a statement block the
    /// branch consists of, written out verbatim: a slot that hands one of its
    /// arguments back, or that fills its <c>out</c> parameters with something
    /// the emitter cannot derive, says so itself. Everything else is an
    /// expression the branch answers after it has released what it was handed.
    /// </remarks>
    private static void WriteNullSlotBranch(CodeWriter writer, VirtualMethodPlan plan, ClassStructModel model)
    {
        if (plan.NullSlotDefault is { } block && block.TrimStart().StartsWith('{'))
        {
            string body = block.Trim();
            body = body[1..^1].Replace("\r", string.Empty, StringComparison.Ordinal);
            foreach (string line in body.Split('\n'))
            {
                string text = line.Trim();
                if (text.Length > 0)
                {
                    writer.WriteLine(text);
                }
            }

            return;
        }

        // The reference a consuming slot would have taken over is released
        // here: nothing below the managed override exists to take it.
        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Bucket == VfuncBucket.Adopt)
            {
                writer.WriteLine(
                    ReleaseExpression(argument.Argument, argument.Argument.Name, argument.IsBoxed) + ";");
            }
        }

        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Bucket is not (VfuncBucket.OutHandle or VfuncBucket.OutScalar))
            {
                continue;
            }

            ArgumentPlan value = argument.Argument;
            string written = argument.Bucket == VfuncBucket.OutHandle
                ? "default"
                : NoValues.TryGetValue(Bare(value.PublicType), out string? none)
                    ? ToNativeScalar(value, none)
                    : "default";

            // The same rule as the write back: the caller may have asked for
            // nothing here, and this branch runs before any managed code did.
            if (argument.IsOptional)
            {
                writer.WriteLine("if (" + value.Name + " != null)");
                writer.OpenBlock();
                writer.WriteLine("*" + value.Name + " = " + written + ";");
                writer.CloseBlock();
            }
            else
            {
                writer.WriteLine("*" + value.Name + " = " + written + ";");
            }
        }

        if (plan.Return.IsVoid)
        {
            writer.WriteLine("return;");
        }
        else if (plan.NullSlotDefault is { } expression)
        {
            writer.WriteLine("return " + expression + ";");
        }
        else
        {
            writer.WriteLine("throw new InvalidOperationException(");
            writer.WriteLine(
                "    \"" + model.Owner.Name + "." + plan.Method.Name
                + " has no parent implementation; override On" + plan.Name + ".\");");
        }
    }

    private static void WriteParentClassOf(CodeWriter writer, string mirror, string cName)
    {
        writer.WriteLine("/// <summary>");
        writer.WriteLine("/// Returns the class the registration captured, which is the one an override");
        writer.WriteLine("/// chains up through.");
        writer.WriteLine("/// </summary>");
        writer.WriteLine("/// <param name=\"instance\">The native <c>" + cName + "</c>.</param>");
        writer.WriteLine("/// <returns>The parent class of the managed subclass.</returns>");
        writer.WriteLine("private static " + mirror + "* ParentClassOf(nint instance) =>");
        writer.WriteLine(
            "    (" + mirror + "*)Gst.GObject.SubclassRegistry.DescriptorFor(instance).ParentClass;");
    }

    private static void WriteTrampoline(CodeWriter writer, VirtualMethodPlan plan, string type)
    {
        string rawReturn = plan.Return.IsVoid ? "void" : plan.Return.RawType;
        List<string> parameters = ["nint " + plan.InstanceName];
        List<string> forward = [plan.InstanceName];
        foreach (VfuncArgument argument in plan.Arguments)
        {
            parameters.Add(argument.Argument.RawType + " " + argument.Argument.Name);
            forward.Add(argument.Argument.Name);
        }

        writer.WriteLine("[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
        writer.WriteLine(
            "private static " + rawReturn + " " + plan.Name + "Trampoline(" + string.Join(", ", parameters) + ")");
        writer.OpenBlock();
        writer.WriteLine("try");
        writer.OpenBlock();
        writer.WriteLine(
            "if (Gst.GObject.Object.TryGetOrFabricate(" + plan.InstanceName + ") is not " + type + " managed)");
        writer.OpenBlock();
        string fallback = "ChainUp" + plan.Name + "(" + string.Join(", ", forward) + ")";
        if (plan.Return.IsVoid)
        {
            writer.WriteLine(fallback + ";");
            writer.WriteLine("return;");
        }
        else if (AnswersHandle(plan))
        {
            // No wrapper is built here: the answer of the parent slot is
            // already what the caller of the slot expects, with the reference
            // count the parent left behind.
            writer.WriteLine("return " + fallback + ";");
        }
        else
        {
            WriteAnswer(writer, plan, fallback, "chained");
        }

        writer.CloseBlock();
        writer.WriteLine();
        WriteTrampolineBody(writer, plan);
        writer.CloseBlock();
        writer.WriteLine("catch (Exception exception)");
        writer.OpenBlock();
        writer.WriteLine("Gst.Interop.ExceptionTrap.Report(exception);");
        if (!plan.Return.IsVoid)
        {
            writer.WriteLine("return " + FailureValue(plan) + ";");
        }

        writer.CloseBlock();
        writer.CloseBlock();
    }

    private static void WriteTrampolineBody(CodeWriter writer, VirtualMethodPlan plan)
    {
        List<string> call = [];
        List<VfuncArgument> produced = [];
        foreach (VfuncArgument argument in plan.Arguments)
        {
            ArgumentPlan value = argument.Argument;
            string local = value.Name + "Value";
            switch (argument.Bucket)
            {
                case VfuncBucket.Cast:
                    call.Add(FromNativeScalar(value, value.Name));
                    break;
                case VfuncBucket.BorrowGObject:
                    writer.WriteLine(
                        Nullable(value.PublicType) + " " + local + " = Gst.GObject.Object.FromNative<"
                        + Bare(value.PublicType) + ">(" + value.Name + ", Gst.Interop.Transfer.None);");
                    call.Add(NullAssert(value, local));
                    break;
                case VfuncBucket.BorrowMiniObject:
                case VfuncBucket.BorrowBoxed:
                    writer.WriteLine(
                        "using " + Nullable(value.PublicType) + " " + local + " = " + value.Name
                        + " == nint.Zero ? null : " + Bare(value.PublicType) + ".Borrow(" + value.Name + ");");
                    call.Add(NullAssert(value, local));
                    break;
                case VfuncBucket.BorrowWrapper:
                    writer.WriteLine(
                        "using " + Nullable(value.PublicType) + " " + local + " = " + Bare(value.PublicType)
                        + ".FromNative(" + value.Name + ", Gst.Interop.Transfer.None);");
                    call.Add(NullAssert(value, local));
                    break;
                case VfuncBucket.BorrowOpaque:
                    writer.WriteLine(
                        Nullable(value.PublicType) + " " + local + " = " + Bare(value.PublicType)
                        + ".FromNative(" + value.Name + ");");
                    call.Add(NullAssert(value, local));
                    break;
                case VfuncBucket.Span:
                    writer.WriteLine(
                        value.PublicType + " " + local + " = new(" + value.Name + ", checked((int)"
                        + CountNameOf(plan, value.Name) + "));");
                    call.Add(local);
                    break;
                case VfuncBucket.SpanCount:
                    break;
                case VfuncBucket.Adopt:
                    writer.WriteLine(
                        "using " + Nullable(value.PublicType) + " " + local + " = " + Bare(value.PublicType)
                        + ".FromNative(" + value.Name + ", Gst.Interop.Transfer.Full);");
                    call.Add(NullAssert(value, local));
                    break;
                case VfuncBucket.OutScalar:
                    writer.WriteLine(Bare(value.PublicType) + " " + local + " = default;");
                    call.Add("out " + local);
                    produced.Add(argument);
                    break;
                case VfuncBucket.OutHandle:
                    writer.WriteLine(Nullable(value.PublicType) + " " + local + " = null;");
                    call.Add("out " + local);
                    produced.Add(argument);
                    break;
                case VfuncBucket.InOutHandle:
                    writer.WriteLine("nint " + value.Name + "Entry = *" + value.Name + ";");
                    writer.WriteLine(
                        Nullable(value.PublicType) + " " + local + " = " + value.Name
                        + "Entry == nint.Zero ? null : " + Bare(value.PublicType) + ".Borrow("
                        + value.Name + "Entry);");
                    call.Add("ref " + local);
                    produced.Add(argument);
                    break;
                case VfuncBucket.InOutHandOver:
                    // The caller gave its reference up on entry, so the wrapper
                    // owns it and is not scoped by a using: what the override
                    // leaves behind is handed back with that reference, and the
                    // wrapper of a value it replaced is disposed below.
                    writer.WriteLine(
                        Nullable(value.PublicType) + " " + local + " = " + AdoptExpression(value, "*" + value.Name)
                        + ";");
                    writer.WriteLine(Nullable(value.PublicType) + " " + value.Name + "Entry = " + local + ";");
                    writer.WriteLine("bool " + value.Name + "HandedOver = false;");
                    call.Add("ref " + local);
                    produced.Add(argument);
                    break;
                default:
                    break;
            }
        }

        string invocation = "managed.On" + plan.Name + "(" + string.Join(", ", call) + ")";
        if (produced.Count == 0)
        {
            if (plan.Return.IsVoid)
            {
                writer.WriteLine(invocation + ";");
            }
            else
            {
                WriteAnswer(writer, plan, invocation, "result");
            }

            return;
        }

        writer.WriteLine();
        writer.WriteLine("try");
        writer.OpenBlock();
        if (plan.Return.IsVoid)
        {
            writer.WriteLine(invocation + ";");
        }
        else
        {
            writer.WriteLine(ReturnType(plan) + " result = " + invocation + ";");
        }

        // What an inout hand over argument leaves behind is written back
        // whatever the override answered: the caller of such a slot releases
        // what it finds there on the failure path too, and the value it handed
        // in is gone either way.
        foreach (VfuncArgument argument in produced)
        {
            if (argument.Bucket == VfuncBucket.InOutHandOver)
            {
                string name = argument.Argument.Name;
                writer.WriteLine(
                    "*" + name + " = " + name + "Value is null ? nint.Zero : " + name + "Value.HandOver();");
                writer.WriteLine(name + "HandedOver = true;");
            }
        }

        bool guarded = false;
        foreach (VfuncArgument argument in produced)
        {
            guarded |= argument.Bucket is VfuncBucket.OutHandle or VfuncBucket.InOutHandle;
        }

        guarded &= string.Equals(Bare(plan.Return.PublicType), "Gst.FlowReturn", StringComparison.Ordinal);
        if (guarded)
        {
            writer.WriteLine();
            writer.WriteLine("if (result == Gst.FlowReturn.Ok)");
            writer.OpenBlock();
        }

        foreach (VfuncArgument argument in produced)
        {
            if (argument.Bucket == VfuncBucket.InOutHandOver)
            {
                continue;
            }

            if (!guarded || argument.Bucket != VfuncBucket.OutScalar)
            {
                WriteWriteBack(writer, argument);
            }
        }

        if (guarded)
        {
            writer.CloseBlock();
            foreach (VfuncArgument argument in produced)
            {
                if (argument.Bucket == VfuncBucket.OutScalar)
                {
                    WriteWriteBack(writer, argument);
                }
            }
        }

        if (!plan.Return.IsVoid)
        {
            writer.WriteLine("return " + ToNativeReturn(plan, "result") + ";");
        }

        writer.CloseBlock();
        writer.WriteLine("finally");
        writer.OpenBlock();
        foreach (VfuncArgument argument in produced)
        {
            if (argument.Bucket == VfuncBucket.OutScalar)
            {
                continue;
            }

            string name = argument.Argument.Name;
            if (argument.Bucket != VfuncBucket.InOutHandOver)
            {
                writer.WriteLine(name + "Value?.Dispose();");
                continue;
            }

            // The override threw before anything was handed back: the caller
            // reads a null where it handed a value in, and the reference it
            // gave up is released here rather than at the next collection.
            writer.WriteLine("if (!" + name + "HandedOver)");
            writer.OpenBlock();
            writer.WriteLine("*" + name + " = nint.Zero;");
            writer.WriteLine("if (" + name + "Value is { IsDisposed: false })");
            writer.OpenBlock();
            writer.WriteLine(name + "Value.Dispose();");
            writer.CloseBlock();
            writer.CloseBlock();
            writer.WriteLine();

            // An override that answered a different value left the one it was
            // given to the trampoline. Releasing it here rather than leaving it
            // to the finalizer is what returns a pooled buffer to its pool.
            writer.WriteLine(
                "if (" + name + "Entry is { IsDisposed: false } && !ReferenceEquals(" + name + "Entry, "
                + name + "Value))");
            writer.OpenBlock();
            writer.WriteLine(name + "Entry.Dispose();");
            writer.CloseBlock();
        }

        writer.CloseBlock();
    }

    /// <summary>Writes the answer of a slot out, converting it through a local when it is a handle.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The slot being written.</param>
    /// <param name="expression">The managed value.</param>
    private static void WriteAnswer(CodeWriter writer, VirtualMethodPlan plan, string expression, string local)
    {
        if (plan.ReturnBucket is VfuncReturnBucket.Cast)
        {
            writer.WriteLine("return " + ToNativeReturn(plan, expression) + ";");
            return;
        }

        writer.WriteLine(Nullable(plan.Return.PublicType) + " " + local + " = " + expression + ";");
        if (plan.NonNullReturn is not null)
        {
            // The caller of the slot dereferences the answer without checking
            // it, so an override that answered nothing is reported and the
            // trampoline answers the failure value the overlay names.
            writer.WriteLine("if (" + local + " is null)");
            writer.OpenBlock();
            writer.WriteLine("throw new InvalidOperationException(");
            writer.WriteLine(
                "    \"On" + plan.Name + " answered null, which " + plan.Method.Name
                + " does not allow.\");");
            writer.CloseBlock();
            writer.WriteLine();
        }

        writer.WriteLine("return " + ToNativeReturn(plan, local) + ";");
    }

    private static void WriteWriteBack(CodeWriter writer, VfuncArgument argument)
    {
        ArgumentPlan value = argument.Argument;
        string local = value.Name + "Value";

        // A caller that wants less than the slot produces passes no storage for
        // the rest - gst_element_get_state (element, NULL, NULL, timeout) is the
        // routine call - so an optional argument is written only when there is
        // somewhere to write it. The reference of a handle nobody asked for is
        // not minted either: nothing would ever release it.
        bool guarded = argument.IsOptional;
        if (guarded)
        {
            writer.WriteLine("if (" + value.Name + " != null)");
            writer.OpenBlock();
        }

        if (argument.Bucket == VfuncBucket.OutScalar)
        {
            writer.WriteLine("*" + value.Name + " = " + ToNativeScalar(value, local) + ";");
            if (guarded)
            {
                writer.CloseBlock();
            }

            return;
        }

        string handle = value.Name + "Handle";
        writer.WriteLine(
            "nint " + handle + " = " + local + " is null ? nint.Zero : " + local + ".Handle;");

        string reference = argument.IdentityReference ?? (value.Name + "Entry");
        string condition = argument.IsIdentity
            ? handle + " != nint.Zero && " + handle + " != " + reference
            : handle + " != nint.Zero";

        writer.WriteLine("if (" + condition + ")");
        writer.OpenBlock();

        // The reference the wrapper owns is what the caller of the slot ends up
        // with: one is minted here and the wrapper gives its own back when it is
        // disposed, so the count lands where a C implementation leaves it.
        writer.WriteLine(MintExpression(value, handle) + ";");
        writer.CloseBlock();
        writer.WriteLine("*" + value.Name + " = " + handle + ";");
        if (guarded)
        {
            writer.CloseBlock();
        }
    }

    /// <summary>
    /// Breaks a sentence into lines short enough for the documentation comment,
    /// so that what the overlays carry as one string does not become a line no
    /// editor of this repository would leave alone.
    /// </summary>
    /// <param name="text">The sentence.</param>
    /// <returns>The lines, at least one.</returns>
    private static List<string> Wrap(string text)
    {
        const int Width = 88;
        List<string> lines = [];
        string current = string.Empty;
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length == 0)
            {
                current = word;
            }
            else if (current.Length + 1 + word.Length <= Width)
            {
                current = current + " " + word;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }

        lines.Add(current);
        return lines;
    }

    /// <summary>
    /// Writes the documentation of every argument: what the gir says about it,
    /// followed by the sentence its marshalling bucket owes the reader.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The slot being written.</param>
    private static void WriteParameterDocs(CodeWriter writer, VirtualMethodPlan plan)
    {
        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Bucket == VfuncBucket.SpanCount)
            {
                continue;
            }

            ArgumentPlan value = argument.Argument;
            XmlDocWriter.WriteParam(
                writer,
                DocName(value.Name),
                value.Doc ?? value.Source?.Doc,
                "The <c>" + (value.Source?.Name ?? DocName(value.Name)) + "</c> argument.",
                OwnershipNote(argument));
        }
    }

    /// <summary>
    /// Writes the documentation of the value the slot answers: what the gir
    /// says about it, followed by what the override owes the caller.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The slot being written.</param>
    private static void WriteReturnDoc(CodeWriter writer, VirtualMethodPlan plan)
    {
        if (plan.Return.IsVoid)
        {
            return;
        }

        List<string> note = [];
        switch (plan.ReturnBucket)
        {
            case VfuncReturnBucket.OwnedGObject:
                note.Add("A returned object is handed to the caller with one added reference; the");
                note.Add("wrapper keeps its own.");
                break;
            case VfuncReturnBucket.OwnedMiniObject:
                note.Add("The object you return is handed to the element; copy or ref it first if");
                note.Add("you need it afterwards. The wrapper is detached by the return and throws");
                note.Add("from then on, exactly like the wrapper of an argument the slot consumed.");
                break;
            case VfuncReturnBucket.BorrowedHandle:
                note.Add("The answer is borrowed: no reference is added for the caller, so the");
                note.Add("override has to keep the object alive by other means.");
                break;
            default:
                break;
        }

        if (plan.NonNullReturn is not null)
        {
            note.Add("Answering <see langword=\"null\"/> is not allowed: the caller of the slot does");
            note.Add("not check for it. A null answer is reported through the exception trap and");
            note.Add("the slot answers a value the caller fails cleanly on.");
        }

        XmlDocWriter.WriteReturns(
            writer,
            plan.Return.Doc,
            "What <c>" + plan.Method.Name + "</c> answers.",
            note.Count == 0 ? null : note);
    }

    /// <summary>
    /// The sentence the marshalling of an argument owes the reader, which no
    /// gir states: who owns what the override is handed, and what the caller
    /// does with what it writes back.
    /// </summary>
    /// <param name="argument">The argument being documented.</param>
    /// <returns>The lines, or <see langword="null"/> when there is nothing to say.</returns>
    private static IReadOnlyList<string>? OwnershipNote(VfuncArgument argument)
    {
        List<string> note = [];
        switch (argument.Bucket)
        {
            // A GObject wrapper is the one borrow that may simply be kept: it
            // is interned and owns a reference of its own, which the toggle
            // reference keeps for as long as the wrapper lives. There is no
            // copy of an element to make either.
            case VfuncBucket.BorrowGObject:
                note.Add("The element lends this for the duration of the call. Keeping the wrapper is");
                note.Add("safe: a GObject wrapper is interned and its reference outlives the call.");
                break;
            case VfuncBucket.BorrowMiniObject:
            case VfuncBucket.BorrowWrapper:
                note.Add("The element lends this for the duration of the call; keep a copy to retain it.");
                break;

            // The wrapper of a lent boxed value holds no copy of it, so what the
            // override writes is what the caller reads back, and the wrapper is
            // invalidated when the call returns.
            case VfuncBucket.BorrowBoxed:
                note.Add("The caller lends this for the duration of the call and reads back what the");
                note.Add("override wrote into it. The wrapper stops meaning anything once the call");
                note.Add("returns: Copy() is what gives a wrapper of your own to anything that has to");
                note.Add("outlive it - a copy of the value, or a reference of its own to the same one");
                note.Add("when the boxed type is reference counted, as a codec frame and a codec");
                note.Add("state are.");
                break;
            case VfuncBucket.Span:
                note.Add("The memory belongs to the caller and is only valid while the call runs.");
                break;
            case VfuncBucket.BorrowOpaque:
                note.Add("The wrapper only holds the pointer the call was given, which is usually an");
                note.Add("address on the stack of the caller: it stops meaning anything once the call");
                note.Add("returns, so read what is needed out of it before then.");
                break;
            case VfuncBucket.Adopt:
                note.Add("The override takes ownership of it: chain up to hand it on, or it is");
                note.Add("released when the override returns. Copy it to keep it beyond the call.");
                break;
            case VfuncBucket.OutHandle:
            case VfuncBucket.InOutHandle:
                note.Add("What the override leaves here is handed to the caller with one added");
                note.Add("reference; the wrapper keeps its own.");
                break;

            case VfuncBucket.InOutHandOver:
                note.Add("The caller gives its reference up: the wrapper handed in owns it, and");
                note.Add("whatever the override leaves here is handed on with that reference rather");
                note.Add("than a second one. A wrapper that was replaced or handed on is detached, so");
                note.Add("using it afterwards throws; reference it first to keep it.");
                break;
            default:
                break;
        }

        if (argument.IsIdentity)
        {
            note.Add("Answering the very value that was handed in is allowed and is how an in");
            note.Add("place implementation says so: the caller compares the two and takes no");
            note.Add("second reference for an unchanged answer.");
        }

        return note.Count == 0 ? null : note;
    }

    private static string PublicParameters(VirtualMethodPlan plan)
    {
        List<string> parts = [];
        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Bucket == VfuncBucket.SpanCount)
            {
                continue;
            }

            parts.Add(Modifier(argument) + PublicType(argument) + " " + argument.Argument.Name);
        }

        return string.Join(", ", parts);
    }

    private static string PublicArguments(VirtualMethodPlan plan)
    {
        List<string> parts = [];
        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Bucket == VfuncBucket.SpanCount)
            {
                continue;
            }

            parts.Add(Modifier(argument) + argument.Argument.Name);
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Whether the value a slot answers is a handle, which the static chain-up
    /// hands on raw: nothing but the protected instance member is allowed to
    /// build a wrapper, because a wrapper nobody holds gives its reference back
    /// only when the finalizer runs.
    /// </summary>
    /// <param name="plan">The slot.</param>
    /// <returns>Whether the answer is a handle.</returns>
    private static bool AnswersHandle(VirtualMethodPlan plan) =>
        plan.ReturnBucket is VfuncReturnBucket.OwnedGObject or VfuncReturnBucket.OwnedMiniObject
            or VfuncReturnBucket.BorrowedHandle;

    /// <summary>The C# type the managed members of a slot answer.</summary>
    /// <param name="plan">The slot.</param>
    /// <returns>The type, which is nullable for every handle a slot may leave NULL.</returns>
    private static string ReturnType(VirtualMethodPlan plan) =>
        plan.ReturnBucket is VfuncReturnBucket.Void or VfuncReturnBucket.Cast
            ? plan.Return.PublicType
            : plan.NonNullReturn is null
                ? Nullable(plan.Return.PublicType)
                : Bare(plan.Return.PublicType);

    private static string Modifier(VfuncArgument argument) => argument.Bucket switch
    {
        VfuncBucket.OutScalar or VfuncBucket.OutHandle => "out ",
        VfuncBucket.InOutHandle or VfuncBucket.InOutHandOver => "ref ",
        _ => string.Empty,
    };

    private static string PublicType(VfuncArgument argument) => argument.Bucket switch
    {
        VfuncBucket.OutHandle or VfuncBucket.InOutHandle or VfuncBucket.InOutHandOver =>
            Nullable(argument.Argument.PublicType),
        VfuncBucket.OutScalar => Bare(argument.Argument.PublicType),
        _ => argument.Argument.PublicType,
    };

    private static bool NeedsNullCheck(VfuncArgument argument) =>
        argument.Bucket is VfuncBucket.Adopt or VfuncBucket.BorrowGObject or VfuncBucket.BorrowMiniObject
            or VfuncBucket.BorrowBoxed or VfuncBucket.BorrowWrapper or VfuncBucket.BorrowOpaque
        && !argument.Argument.PublicType.EndsWith('?');

    private static string FunctionPointerType(VirtualMethodPlan plan)
    {
        List<string> parts = ["nint"];
        foreach (VfuncArgument argument in plan.Arguments)
        {
            parts.Add(argument.Argument.RawType);
        }

        parts.Add(plan.Return.IsVoid ? "void" : plan.Return.RawType);
        return "delegate* unmanaged[Cdecl]<" + string.Join(", ", parts) + ">";
    }

    private static string ToNativeArgument(VfuncArgument argument)
    {
        ArgumentPlan value = argument.Argument;
        return argument.Bucket switch
        {
            VfuncBucket.BorrowGObject or VfuncBucket.BorrowMiniObject or VfuncBucket.BorrowBoxed
                or VfuncBucket.BorrowWrapper or VfuncBucket.BorrowOpaque =>
                value.PublicType.EndsWith('?')
                    ? value.Name + " is null ? nint.Zero : " + value.Name + ".Handle"
                    : value.Name + ".Handle",
            _ when value.Kind == ArgumentKind.Utf8 => value.Name + "Scope.Pointer",
            _ => ToNativeScalar(value, value.Name),
        };
    }

    private static string FromNativeScalar(ArgumentPlan value, string source) => value.Kind switch
    {
        ArgumentKind.Boolean => source + " != 0",
        ArgumentKind.Enumeration => "(" + Bare(value.PublicType) + ")" + source,
        ArgumentKind.Wrapper => "new " + Bare(value.PublicType) + "(" + source + ")",
        ArgumentKind.Utf8 => "Gst.Interop.GMarshal.PtrToStringUtf8((nint)" + source + ")",
        _ => source,
    };

    private static string ToNativeScalar(ArgumentPlan value, string source) => value.Kind switch
    {
        ArgumentKind.Boolean => source + " ? 1 : 0",
        ArgumentKind.Enumeration => "(" + value.RawType.TrimEnd('*') + ")" + source,
        ArgumentKind.Wrapper => source + "." + WrapperValue(Bare(value.PublicType)),
        ArgumentKind.Utf8 => source,
        _ => source,
    };

    private static string WrapperValue(string type) => type switch
    {
        "Gst.ClockTime" => "Nanoseconds",
        _ => "Value",
    };

    private static string FromNativeReturn(VirtualMethodPlan plan, string call) => plan.ReturnBucket switch
    {
        VfuncReturnBucket.Cast => plan.Return.Kind switch
        {
            ArgumentKind.Boolean => call + " != 0",
            ArgumentKind.Enumeration => "(" + Bare(plan.Return.PublicType) + ")" + call,
            ArgumentKind.Wrapper => "new " + Bare(plan.Return.PublicType) + "(" + call + ")",
            _ => call,
        },
        VfuncReturnBucket.BorrowedHandle => AdoptExpressionBorrowed(plan, call),
        _ => AdoptReturn(plan, call),
    };

    /// <summary>Builds the wrapper the protected chain-up hands a managed caller.</summary>
    /// <param name="plan">The slot being written.</param>
    /// <param name="handle">The raw handle the parent slot answered.</param>
    /// <returns>The expression.</returns>
    private static string WrapReturn(VirtualMethodPlan plan, string handle) =>
        plan.ReturnBucket == VfuncReturnBucket.BorrowedHandle
            ? AdoptExpressionBorrowed(plan, handle)
            : AdoptReturn(plan, handle);

    private static string AdoptExpressionBorrowed(VirtualMethodPlan plan, string call)
    {
        string type = Bare(plan.Return.PublicType);
        return plan.Return.Flavor == HandleFlavor.GObject
            ? "Gst.GObject.Object.FromNative<" + type + ">(" + call + ", Gst.Interop.Transfer.None)"
            : type + ".FromNative(" + call + ", Gst.Interop.Transfer.None)";
    }

    private static string AdoptReturn(VirtualMethodPlan plan, string call)
    {
        string type = Bare(plan.Return.PublicType);
        return plan.Return.Flavor == HandleFlavor.GObject
            ? "Gst.GObject.Object.FromNative<" + type + ">(" + call + ", Gst.Interop.Transfer.Full)"
            : type + ".FromNative(" + call + ", Gst.Interop.Transfer.Full)";
    }

    private static string ToNativeReturn(VirtualMethodPlan plan, string source) => plan.ReturnBucket switch
    {
        VfuncReturnBucket.Cast => plan.Return.Kind switch
        {
            ArgumentKind.Boolean => "(" + source + ") ? 1 : 0",
            ArgumentKind.Enumeration => "(" + plan.Return.RawType + ")(" + source + ")",
            ArgumentKind.Wrapper => "(" + source + ")." + WrapperValue(Bare(plan.Return.PublicType)),
            _ => source,
        },
        VfuncReturnBucket.BorrowedHandle =>
            source + " is null ? nint.Zero : " + source + ".Handle",
        VfuncReturnBucket.OwnedGObject =>
            source + " is null ? nint.Zero : Gst.Interop.GObjectNative.ObjectRef(" + source + ".Handle)",
        // A mini object is handed over rather than referenced a second time:
        // the wrapper gives its own reference to the caller of the slot and is
        // detached, so a buffer an override produced is writable downstream and
        // a pooled one is back in its pool when the slot returns.
        VfuncReturnBucket.OwnedMiniObject =>
            source + " is null ? nint.Zero : " + source + ".HandOver()",
        _ => source,
    };

    private static string MintExpression(ArgumentPlan value, string handle) =>
        value.Flavor == HandleFlavor.GObject
            ? "Gst.Interop.GObjectNative.ObjectRef(" + handle + ")"
            : "Gst.GstNative.MiniObjectRef(" + handle + ")";

    private static string ReleaseExpression(ArgumentPlan value, string handle, bool boxed = false) =>
        boxed
            ? "Gst.Interop.GObjectNative.BoxedFree(" + Bare(value.PublicType) + ".GetGType(), " + handle + ")"
            : value.Flavor == HandleFlavor.GObject
                ? "Gst.Interop.GObjectNative.ObjectUnref(" + handle + ")"
                : "Gst.GstNative.MiniObjectUnref(" + handle + ")";

    /// <summary>
    /// The expression that reads the raw handle of the argument an identity
    /// preserving answer is compared with.
    /// </summary>
    /// <param name="plan">The slot being written.</param>
    /// <param name="name">The C# name of the argument.</param>
    /// <returns>The expression.</returns>
    private static string HandleOf(VirtualMethodPlan plan, string name)
    {
        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (string.Equals(argument.Argument.Name, name, StringComparison.Ordinal)
                && argument.Argument.PublicType.EndsWith('?'))
            {
                return "(" + name + " is null ? nint.Zero : " + name + ".Handle)";
            }
        }

        return name + ".Handle";
    }

    private static string AdoptExpression(ArgumentPlan value, string handle) =>
        value.Flavor == HandleFlavor.GObject
            ? "Gst.GObject.Object.FromNative<" + Bare(value.PublicType) + ">(" + handle
                + ", Gst.Interop.Transfer.Full)"
            : Bare(value.PublicType) + ".FromNative(" + handle + ", Gst.Interop.Transfer.Full)";

    private static string FailureValue(VirtualMethodPlan plan)
    {
        // The zero of a return type does not always read as a failure: the ring
        // buffer thread of an audio sink loops on a write that answered zero
        // bytes, so a slot may name what a trapped exception answers instead.
        if (plan.FailureValue is { } declared)
        {
            return declared;
        }

        if (plan.NonNullReturn is { } failure)
        {
            return failure;
        }

        if (plan.ReturnBucket is VfuncReturnBucket.BorrowedHandle or VfuncReturnBucket.OwnedGObject
            or VfuncReturnBucket.OwnedMiniObject)
        {
            return "nint.Zero";
        }

        if (plan.Return.Kind == ArgumentKind.Enumeration
            && FailureValues.TryGetValue(Bare(plan.Return.PublicType), out string? value))
        {
            return "(" + plan.Return.RawType + ")" + value;
        }

        return "default";
    }

    /// <summary>Finds the argument that counts the elements of one block.</summary>
    /// <param name="plan">The slot being written.</param>
    /// <param name="span">The name of the block.</param>
    /// <returns>The name of the counting argument.</returns>
    private static string CountNameOf(VirtualMethodPlan plan, string span)
    {
        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Bucket == VfuncBucket.SpanCount
                && string.Equals(argument.CountOf, span, StringComparison.Ordinal))
            {
                return argument.Argument.Name;
            }
        }

        return span + "Length";
    }

    private static string NullAssert(ArgumentPlan value, string local) =>
        value.PublicType.EndsWith('?') ? local : local + "!";

    private static string Bare(string type) => type.EndsWith('?') ? type[..^1] : type;

    private static string Nullable(string type) => type.EndsWith('?') ? type : type + "?";

    private static string DocName(string name) => name.StartsWith('@') ? name[1..] : name;

    /// <summary>The registration facts of one base class that no gir states.</summary>
    /// <param name="PadTemplates">The pad templates the class initialiser has to add.</param>
    /// <param name="Required">
    /// The slots a subclass has to declare, empty when every slot of the class
    /// has an answer for a NULL parent that the element survives.
    /// </param>
    private sealed record SubclassBaseRule(
        IReadOnlyList<string> PadTemplates,
        IReadOnlyList<RequiredSlot> Required);

    /// <summary>One slot a subclass of a base class has to declare.</summary>
    /// <param name="Slot">The gir name of the slot.</param>
    /// <param name="Reason">
    /// Why it has to be declared, as the sentence the registration throws with.
    /// It says "the base class calls the slot unguarded" when it is not given,
    /// which is the reason a required slot usually has.
    /// </param>
    private sealed record RequiredSlot(string Slot, string? Reason);
}
