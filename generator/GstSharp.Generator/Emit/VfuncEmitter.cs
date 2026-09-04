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
            ["GstBase.BaseSrc"] = new(["src"], null),
            ["GstBase.PushSrc"] = new(["src"], null),
            ["GstBase.BaseSink"] = new(["sink"], null),
            ["GstBase.BaseTransform"] = new(["sink", "src"], null),
            ["GstBase.Aggregator"] = new(["src"], "aggregate"),
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
    /// <returns>One file per allowlisted class that has at least one slot.</returns>
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
    /// <returns>The file, or <see langword="null"/> when no slot survived the planner.</returns>
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
                context);

            if (plan is null)
            {
                _census.SkippedVirtual(module.GirNamespace, key, "UnsupportedSignature");
                continue;
            }

            plans.Add(plan);
            _census.Emitted(module.GirNamespace, Category);
        }

        if (plans.Count == 0)
        {
            _diagnostics.Warn(
                "GEN0033",
                $"The subclassable class '{model.QualifiedName}' has no slot the planner could project; "
                + "no subclassing surface is emitted for it.");
            return null;
        }

        HashSet<string> mine = new(StringComparer.Ordinal);
        HashSet<string> inherited = new(StringComparer.Ordinal);
        for (ClassStructModel? parent = model.Parent; parent is not null; parent = parent.Parent)
        {
            if (_emittedSlots.TryGetValue(parent.QualifiedName, out HashSet<string>? names))
            {
                inherited.UnionWith(names);
            }
        }

        foreach (VirtualMethodPlan plan in plans)
        {
            _ = mine.Add(plan.Name);
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
        writer.WriteLine("using System.Runtime.CompilerServices;");
        writer.WriteLine("using System.Runtime.InteropServices;");
        writer.WriteLine();
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
            WriteVirtual(writer, plan, cName);
        }

        foreach (VirtualMethodPlan plan in plans)
        {
            writer.WriteLine();
            WriteInstanceChainUp(writer, plan);
        }

        foreach (VirtualMethodPlan plan in plans)
        {
            writer.WriteLine();
            WriteStaticChainUp(writer, plan, model);
        }

        writer.WriteLine();
        WriteParentClassOf(writer, mirror, cName);

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
        writer.WriteLine("/// <see cref=\"Gst.GObject.SubclassType.NewInstance\"/> created an instance of.");
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
        string configure = mandatory ? "Action<Gst.GObject.ClassConfig>" : "Action<Gst.GObject.ClassConfig>?";
        bool hides = false;
        for (ClassStructModel? parent = model.Parent; parent is not null; parent = parent.Parent)
        {
            hides |= parent.IsSubclassable;
        }

        string? required = null;
        if (rule?.RequiredOverride is { } slot)
        {
            foreach (VirtualMethodPlan plan in plans)
            {
                if (string.Equals(plan.Method.Name, slot, StringComparison.Ordinal))
                {
                    required = plan.Name;
                }
            }

            if (required is null)
            {
                _diagnostics.Warn(
                    "GEN0034",
                    $"The class '{model.QualifiedName}' requires an override of '{slot}', which is not part of "
                    + "the emitted surface; the registration cannot check for it.");
            }
        }

        writer.WriteLine("/// <summary>Registers a managed subclass of <c>" + cName + "</c> with GObject.</summary>");
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
        writer.WriteLine(
            "/// <exception cref=\"System.ArgumentNullException\">An argument is <see langword=\"null\"/>.</exception>");
        writer.WriteLine("/// <exception cref=\"System.ArgumentException\">");
        writer.WriteLine("/// The type name is not a legal <c>GType</c> name, or a declared slot belongs to a");
        writer.WriteLine("/// class that <c>" + cName + "</c> does not derive from.");
        writer.WriteLine("/// </exception>");
        writer.WriteLine("/// <exception cref=\"System.InvalidOperationException\">");
        writer.WriteLine("/// The type name is taken, or the class initialiser failed.");
        writer.WriteLine("/// </exception>");

        string declaration = "public static " + (hides ? "new " : string.Empty) + "Gst.GObject.SubclassType DefineSubclass(";
        if (!mandatory && required is null)
        {
            writer.WriteLine(declaration);
            writer.WriteLine("    string typeName,");
            writer.WriteLine("    " + configure + " configureClass,");
            writer.WriteLine("    params Gst.GObject.VfuncOverride[] overrides) =>");
            writer.WriteLine(
                "    Gst.GObject.SubclassType.Define(new Gst.GObject.GType(GetGType()), typeName, configureClass, overrides);");
            return;
        }

        writer.WriteLine(declaration);
        writer.WriteLine("    string typeName,");
        writer.WriteLine("    " + configure + " configureClass,");
        writer.WriteLine("    params Gst.GObject.VfuncOverride[] overrides)");
        writer.OpenBlock();
        if (mandatory)
        {
            writer.WriteLine("ArgumentNullException.ThrowIfNull(configureClass);");
        }

        if (required is not null)
        {
            writer.WriteLine("ArgumentNullException.ThrowIfNull(overrides);");
            writer.WriteLine();
            writer.WriteLine("bool declared = false;");
            writer.WriteLine("foreach (Gst.GObject.VfuncOverride candidate in overrides)");
            writer.OpenBlock();
            writer.WriteLine("if (candidate.Function == " + required + "Override.Function)");
            writer.OpenBlock();
            writer.WriteLine("declared = true;");
            writer.WriteLine("break;");
            writer.CloseBlock();
            writer.CloseBlock();
            writer.WriteLine();
            writer.WriteLine("if (!declared)");
            writer.OpenBlock();
            writer.WriteLine("throw new ArgumentException(");
            writer.WriteLine(
                "    \"A managed " + cName + " has to declare " + required
                + "Override: the base class calls the slot unguarded.\",");
            writer.WriteLine("    nameof(overrides));");
            writer.CloseBlock();
        }

        writer.WriteLine();
        writer.WriteLine(
            "Gst.GObject.SubclassType type = Gst.GObject.SubclassType.Define(new Gst.GObject.GType(GetGType()), typeName, configureClass, overrides);");
        foreach (string template in rule?.PadTemplates ?? [])
        {
            writer.WriteLine("type.RequirePadTemplate(\"" + template + "\");");
        }

        writer.WriteLine("return type;");
        writer.CloseBlock();
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

    private static void WriteVirtual(CodeWriter writer, VirtualMethodPlan plan, string cName)
    {
        writer.WriteLine("/// <summary>Runs <c>" + cName + "." + plan.Method.Name + "</c>.</summary>");
        WriteParameterDocs(writer, plan);
        WriteReturnDoc(writer, plan);
        writer.WriteLine(
            "protected virtual " + ReturnType(plan) + " On" + plan.Name + "(" + PublicParameters(plan) + ") =>");
        writer.WriteLine("    ChainUp" + plan.Name + "(" + PublicArguments(plan) + ");");
    }

    private static void WriteInstanceChainUp(CodeWriter writer, VirtualMethodPlan plan)
    {
        writer.WriteLine(
            "/// <summary>Runs the implementation of <c>" + plan.Method.Name
            + "</c> below the managed override.</summary>");
        WriteParameterDocs(writer, plan);
        WriteReturnDoc(writer, plan);
        writer.WriteLine(
            "protected " + ReturnType(plan) + " ChainUp" + plan.Name + "(" + PublicParameters(plan) + ")");
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
        foreach (VfuncArgument argument in plan.Arguments)
        {
            mints |= argument.Bucket is VfuncBucket.Adopt or VfuncBucket.InOutHandle;
        }

        string instance = "Handle";
        if (mints)
        {
            instance = "instance";
            writer.WriteLine("nint instance = Handle;");
        }

        List<string> raw = [];
        foreach (VfuncArgument argument in plan.Arguments)
        {
            ArgumentPlan value = argument.Argument;
            string local = value.Name + "Native";
            switch (argument.Bucket)
            {
                case VfuncBucket.Adopt:
                    writer.WriteLine("nint " + local + " = " + value.Name + ".Handle;");
                    raw.Add(local);
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
            if (argument.Bucket is VfuncBucket.Adopt or VfuncBucket.InOutHandle)
            {
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
        else
        {
            writer.WriteLine(ReturnType(plan) + " result = " + call + ";");
        }

        writer.WriteLine("GC.KeepAlive(this);");
        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Bucket is VfuncBucket.BorrowGObject or VfuncBucket.BorrowMiniObject
                or VfuncBucket.BorrowWrapper)
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
                    writer.WriteLine(value.Name + ".Dispose();");
                    break;
                case VfuncBucket.OutScalar:
                    writer.WriteLine(value.Name + " = " + FromNativeScalar(value, local) + ";");
                    break;
                case VfuncBucket.OutHandle:
                    writer.WriteLine(
                        value.Name + " = " + local + " == nint.Zero ? null : "
                        + AdoptExpression(value, local) + ";");
                    break;
                case VfuncBucket.InOutHandle:
                    writer.WriteLine(value.Name + "?.Dispose();");
                    writer.WriteLine(
                        value.Name + " = " + local + " == nint.Zero ? null : "
                        + AdoptExpression(value, local) + ";");
                    break;
                default:
                    break;
            }
        }

        if (!plan.Return.IsVoid)
        {
            writer.WriteLine("return result;");
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

        writer.WriteLine(
            "private static " + ReturnType(plan) + " ChainUp" + plan.Name + "("
            + string.Join(", ", parameters) + ")");
        writer.OpenBlock();
        writer.WriteLine(pointer + " slot =");
        writer.WriteLine(
            "    (" + pointer + ")ParentClassOf(" + plan.InstanceName + ")->" + plan.SlotMember + ";");
        writer.WriteLine();
        writer.WriteLine("if (slot is null)");
        writer.OpenBlock();

        // The reference a consuming slot would have taken over is released
        // here: nothing below the managed override exists to take it.
        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Bucket == VfuncBucket.Adopt)
            {
                writer.WriteLine(ReleaseExpression(argument.Argument, argument.Argument.Name) + ";");
            }
        }

        foreach (VfuncArgument argument in plan.Arguments)
        {
            if (argument.Bucket is VfuncBucket.OutScalar or VfuncBucket.OutHandle)
            {
                writer.WriteLine("*" + argument.Argument.Name + " = default;");
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

        writer.CloseBlock();
        writer.WriteLine();
        string call = "slot(" + string.Join(", ", arguments) + ")";
        if (plan.Return.IsVoid)
        {
            writer.WriteLine(call + ";");
        }
        else
        {
            writer.WriteLine("return " + FromNativeReturn(plan, call) + ";");
        }

        writer.CloseBlock();
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
            "if (Gst.GObject.Object.TryGetInterned(" + plan.InstanceName + ") is not " + type + " managed)");
        writer.OpenBlock();
        string fallback = "ChainUp" + plan.Name + "(" + string.Join(", ", forward) + ")";
        if (plan.Return.IsVoid)
        {
            writer.WriteLine(fallback + ";");
            writer.WriteLine("return;");
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

        bool guarded = false;
        foreach (VfuncArgument argument in produced)
        {
            guarded |= argument.Bucket != VfuncBucket.OutScalar;
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
            if (argument.Bucket != VfuncBucket.OutScalar)
            {
                writer.WriteLine(argument.Argument.Name + "Value?.Dispose();");
            }
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
        writer.WriteLine("return " + ToNativeReturn(plan, local) + ";");
    }

    private static void WriteWriteBack(CodeWriter writer, VfuncArgument argument)
    {
        ArgumentPlan value = argument.Argument;
        string local = value.Name + "Value";
        if (argument.Bucket == VfuncBucket.OutScalar)
        {
            writer.WriteLine("*" + value.Name + " = " + ToNativeScalar(value, local) + ";");
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
    }

    private static void WriteParameterDocs(CodeWriter writer, VirtualMethodPlan plan)
    {
        foreach (VfuncArgument argument in plan.Arguments)
        {
            writer.WriteLine(
                "/// <param name=\"" + DocName(argument.Argument.Name)
                + "\">The argument the slot carries under this name.</param>");
        }
    }

    private static void WriteReturnDoc(CodeWriter writer, VirtualMethodPlan plan)
    {
        if (!plan.Return.IsVoid)
        {
            writer.WriteLine("/// <returns>What the slot answers.</returns>");
        }
    }

    private static string PublicParameters(VirtualMethodPlan plan)
    {
        List<string> parts = [];
        foreach (VfuncArgument argument in plan.Arguments)
        {
            parts.Add(Modifier(argument) + PublicType(argument) + " " + argument.Argument.Name);
        }

        return string.Join(", ", parts);
    }

    private static string PublicArguments(VirtualMethodPlan plan)
    {
        List<string> parts = [];
        foreach (VfuncArgument argument in plan.Arguments)
        {
            parts.Add(Modifier(argument) + argument.Argument.Name);
        }

        return string.Join(", ", parts);
    }

    /// <summary>The C# type the managed members of a slot answer.</summary>
    /// <param name="plan">The slot.</param>
    /// <returns>The type, which is nullable for every handle a slot may leave NULL.</returns>
    private static string ReturnType(VirtualMethodPlan plan) =>
        plan.ReturnBucket is VfuncReturnBucket.Void or VfuncReturnBucket.Cast
            ? plan.Return.PublicType
            : Nullable(plan.Return.PublicType);

    private static string Modifier(VfuncArgument argument) => argument.Bucket switch
    {
        VfuncBucket.OutScalar or VfuncBucket.OutHandle => "out ",
        VfuncBucket.InOutHandle => "ref ",
        _ => string.Empty,
    };

    private static string PublicType(VfuncArgument argument) => argument.Bucket switch
    {
        VfuncBucket.OutHandle or VfuncBucket.InOutHandle => Nullable(argument.Argument.PublicType),
        VfuncBucket.OutScalar => Bare(argument.Argument.PublicType),
        _ => argument.Argument.PublicType,
    };

    private static bool NeedsNullCheck(VfuncArgument argument) =>
        argument.Bucket is VfuncBucket.Adopt or VfuncBucket.BorrowGObject or VfuncBucket.BorrowMiniObject
            or VfuncBucket.BorrowWrapper
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
            VfuncBucket.BorrowGObject or VfuncBucket.BorrowMiniObject or VfuncBucket.BorrowWrapper =>
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
        VfuncReturnBucket.OwnedMiniObject =>
            source + " is null ? nint.Zero : Gst.GstNative.MiniObjectRef(" + source + ".Handle)",
        _ => source,
    };

    private static string MintExpression(ArgumentPlan value, string handle) =>
        value.Flavor == HandleFlavor.GObject
            ? "Gst.Interop.GObjectNative.ObjectRef(" + handle + ")"
            : "Gst.GstNative.MiniObjectRef(" + handle + ")";

    private static string ReleaseExpression(ArgumentPlan value, string handle) =>
        value.Flavor == HandleFlavor.GObject
            ? "Gst.Interop.GObjectNative.ObjectUnref(" + handle + ")"
            : "Gst.GstNative.MiniObjectUnref(" + handle + ")";

    private static string AdoptExpression(ArgumentPlan value, string handle) =>
        value.Flavor == HandleFlavor.GObject
            ? "Gst.GObject.Object.FromNative<" + Bare(value.PublicType) + ">(" + handle
                + ", Gst.Interop.Transfer.Full)"
            : Bare(value.PublicType) + ".FromNative(" + handle + ", Gst.Interop.Transfer.Full)";

    private static string FailureValue(VirtualMethodPlan plan)
    {
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

    private static string NullAssert(ArgumentPlan value, string local) =>
        value.PublicType.EndsWith('?') ? local : local + "!";

    private static string Bare(string type) => type.EndsWith('?') ? type[..^1] : type;

    private static string Nullable(string type) => type.EndsWith('?') ? type : type + "?";

    private static string DocName(string name) => name.StartsWith('@') ? name[1..] : name;

    /// <summary>The registration facts of one base class that no gir states.</summary>
    /// <param name="PadTemplates">The pad templates the class initialiser has to add.</param>
    /// <param name="RequiredOverride">
    /// The gir name of the slot a subclass has to declare, or
    /// <see langword="null"/> when every slot has a documented default.
    /// </param>
    private sealed record SubclassBaseRule(IReadOnlyList<string> PadTemplates, string? RequiredOverride);
}
