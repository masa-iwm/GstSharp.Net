using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Planning;

namespace GstSharp.Generator.Emit;

/// <summary>
/// Writes the C# text of a <see cref="MarshalPlan"/>: the public member, the
/// marshalling of its arguments and the <c>LibraryImport</c> behind it.
/// </summary>
/// <remarks>
/// <para>
/// Every emitter goes through this class, so that a method of a class, of a
/// record, of an interface and of the global function holder are marshalled by
/// exactly the same code.
/// </para>
/// <para>
/// The generated body follows one shape: guards and locals first, then the
/// scopes that have to wrap the call (a <c>fixed</c> for a span, a
/// <c>try</c>/<c>finally</c> for a callback that only lives for the duration of
/// the call), then the call itself, then the error check, the out parameters
/// and the return value.
/// </para>
/// </remarks>
internal static class CallableRenderer
{
    /// <summary>The local that holds the raw return value.</summary>
    private const string ResultLocal = "nativeResult";

    /// <summary>The local that holds the raw handle of the instance.</summary>
    private const string InstanceLocal = "instanceHandle";

    /// <summary>The local that holds the converted return value.</summary>
    private const string ConvertedLocal = "result";

    /// <summary>The local that holds the element pointers of a returned list.</summary>
    private const string ItemsLocal = "nativeItems";

    /// <summary>The loop variable that walks the element pointers of a returned list.</summary>
    private const string ItemLocal = "nativeItem";

    /// <summary>The local that holds one adopted element of a returned list.</summary>
    private const string ElementLocal = "adopted";

    /// <summary>
    /// What the documentation of a returned wrapper says about its ownership,
    /// which the gir does not describe.
    /// </summary>
    private static readonly string[] AdoptedWrapperNote =
    [
        "The wrapper owns a reference of its own, which is a copy for a boxed type:",
        "dispose it when you are done, and note that changes made to a copy of a",
        "boxed value are not written back.",
    ];

    /// <summary>Writes the public member of a plan.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The plan to write.</param>
    internal static void WriteMember(CodeWriter writer, MarshalPlan plan)
    {
        WriteDocumentation(writer, plan);
        XmlDocWriter.WriteObsolete(writer, plan.Callable);

        string hiding = plan.IsNew ? "new " : string.Empty;
        string modifiers = plan.IsOverride
            ? "public override "
            : plan.Form == CallableForm.InstanceMethod
                ? "public " + hiding
                : "public static " + hiding;
        writer.WriteLine(modifiers + plan.Return.PublicType + " " + plan.Name + "(" + Parameters(plan) + ")");
        writer.OpenBlock();
        WriteBody(writer, plan);
        writer.CloseBlock();
    }

    /// <summary>Writes the <c>LibraryImport</c> declaration of a plan.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The plan to write.</param>
    /// <param name="library">The logical native library name.</param>
    internal static void WriteImport(CodeWriter writer, MarshalPlan plan, string library)
    {
        List<string> parameters = [];
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            parameters.Add(argument.RawType + " " + argument.Name);
        }

        writer.WriteLine("/// <summary>The <c>" + plan.EntryPoint + "</c> entry point.</summary>");
        writer.WriteLine("[LibraryImport(\"" + library + "\", EntryPoint = \"" + plan.EntryPoint + "\")]");
        writer.WriteLine(
            "private static partial " + plan.Return.RawType + " " + plan.NativeName
            + "(" + string.Join(", ", parameters) + ");");
    }

    /// <summary>Writes the delegate and the trampoline of a callback.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The callback to write.</param>
    internal static void WriteCallback(CodeWriter writer, CallbackPlan plan)
    {
        string cType = plan.Callback.CType ?? plan.Callback.Name;
        XmlDocWriter.Write(writer, plan.Callback.Doc, "The <c>" + cType + "</c> callback.", plan.Callback);
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (!argument.IsHidden)
            {
                XmlDocWriter.WriteParam(
                    writer,
                    DocName(argument.Name),
                    argument.Doc,
                    "The <c>" + (argument.Source?.Name ?? argument.Name) + "</c> argument.");
            }
        }

        if (!plan.Return.IsVoid)
        {
            XmlDocWriter.WriteReturns(writer, plan.Return.Doc, "The result of the callback.");
        }

        XmlDocWriter.WriteObsolete(writer, plan.Callback);

        List<string> parameters = [];
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.IsHidden)
            {
                continue;
            }

            string prefix = argument.Direction == ArgumentDirection.Ref ? "ref " : string.Empty;
            parameters.Add(prefix + argument.PublicType + " " + argument.Name);
        }

        writer.WriteLine(
            "public delegate " + plan.Return.PublicType + " " + plan.DelegateName
            + "(" + string.Join(", ", parameters) + ");");
        writer.WriteLine();
        WriteTrampoline(writer, plan);
    }

    /// <summary>
    /// Returns the name a parameter carries in the XML documentation, which is
    /// the identifier without the escape of a C# keyword.
    /// </summary>
    /// <param name="name">The C# name of the parameter.</param>
    /// <returns>The documented name.</returns>
    private static string DocName(string name) => name.StartsWith('@') ? name[1..] : name;

    /// <summary>
    /// Returns the type a raw pointer parameter points at, that is the raw type
    /// without its trailing star.
    /// </summary>
    /// <param name="rawType">The raw type.</param>
    /// <returns>The pointee type.</returns>
    private static string Pointee(string rawType) => rawType.TrimEnd('*');

    private static string TransferLiteral(GirTransfer transfer) =>
        transfer is GirTransfer.Full or GirTransfer.Floating
            ? "Gst.Interop.Transfer.Full"
            : "Gst.Interop.Transfer.None";

    private static string WrapperConversion(string publicType) => publicType switch
    {
        "Gst.ClockTime" => "Nanoseconds",
        "Gst.GObject.GType" => "Value",
        _ => "Value",
    };

    private static string Parameters(MarshalPlan plan)
    {
        List<string> parameters = [];
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.IsHidden)
            {
                continue;
            }

            string prefix;
            if (argument.Kind == ArgumentKind.Instance)
            {
                prefix = plan.Form == CallableForm.ExtensionMethod ? "this " : string.Empty;
            }
            else
            {
                prefix = argument.Direction switch
                {
                    ArgumentDirection.Out => "out ",
                    ArgumentDirection.Ref => "ref ",
                    _ => string.Empty,
                };
            }

            parameters.Add(prefix + argument.PublicType + " " + argument.Name);
        }

        return string.Join(", ", parameters);
    }

    private static void WriteDocumentation(CodeWriter writer, MarshalPlan plan)
    {
        string cType = plan.EntryPoint;
        XmlDocWriter.Write(writer, plan.Callable.Doc, "The <c>" + cType + "</c> function.", plan.Callable);

        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.IsHidden)
            {
                continue;
            }

            string fallback = argument.Kind == ArgumentKind.Instance
                ? "The instance the method is called on."
                : "The <c>" + (argument.Source?.Name ?? argument.Name) + "</c> argument.";
            XmlDocWriter.WriteParam(writer, DocName(argument.Name), argument.Doc ?? argument.Source?.Doc, fallback);
        }

        if (!plan.Return.IsVoid)
        {
            XmlDocWriter.WriteReturns(
                writer,
                plan.Return.Doc,
                "The result of <c>" + cType + "</c>.",
                AdoptsWrapper(plan.Return) ? AdoptedWrapperNote : null);
        }

        if (plan.Throws)
        {
            writer.WriteLine("/// <exception cref=\"Gst.GLib.GException\">The native call failed.</exception>");
        }
    }

    /// <summary>
    /// Tests whether the returned value is a mini object or a boxed record that
    /// the call does not transfer.
    /// </summary>
    /// <param name="value">The return value.</param>
    /// <returns><see langword="true"/> when the wrapper takes a reference of its own.</returns>
    /// <remarks>
    /// The gir documents what the C function returns, which is a borrowed
    /// pointer. The wrapper is not borrowed: it references a mini object and it
    /// copies a boxed value, so it has to be disposed and a boxed copy is not
    /// the instance the getter was called on. Saying so on every such member is
    /// the only place a caller reads it.
    /// </remarks>
    private static bool AdoptsWrapper(ReturnPlan value) =>
        value.Kind == ArgumentKind.Handle
        && value.Flavor == HandleFlavor.Wrapper
        && value.Transfer == GirTransfer.None;

    /// <summary>
    /// Returns the value a trampoline hands back when the managed handler could
    /// not run, either because it threw or because its state was already
    /// released.
    /// </summary>
    /// <param name="value">The return value of the trampoline.</param>
    /// <returns>The expression to return.</returns>
    /// <remarks>
    /// The default of an enumeration is its zero member, and the zero member of
    /// <c>GstFlowReturn</c> is <c>GST_FLOW_OK</c>: reporting that after the
    /// handler threw tells the pipeline that a buffer it never got was
    /// accepted. The rule is to use the error member of the enumeration when it
    /// has one; <c>GstFlowReturn</c> is the only enumeration this milestone
    /// returns from a trampoline, so it is the only one spelled out.
    /// </remarks>
    internal static string FailureLiteral(ReturnPlan value) =>
        value.Kind == ArgumentKind.Enumeration
        && string.Equals(value.PublicType, "Gst.FlowReturn", StringComparison.Ordinal)
            ? "(" + value.RawType + ")Gst.FlowReturn.Error"
            : "default";

    private static void WriteBody(CodeWriter writer, MarshalPlan plan)
    {
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            WritePrologue(writer, plan, argument);
        }

        // A span has to be pinned and a callback that only lives for the
        // duration of the call has to be released again, so both wrap the call
        // in a block. They are closed in reverse order further down.
        List<ArgumentPlan> scopes = [];
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind == ArgumentKind.Span)
            {
                writer.WriteLine(
                    "fixed (" + argument.RawType + " " + argument.Name + "Pointer = " + argument.Name + ")");
                writer.OpenBlock();
                scopes.Add(argument);
            }
            else if (argument.Kind == ArgumentKind.Callback && argument.Scope == GirScope.Call)
            {
                writer.WriteLine("try");
                writer.OpenBlock();
                scopes.Add(argument);
            }
        }

        WriteCall(writer, plan);
        WriteKeepAlive(writer, plan);

        if (plan.Throws)
        {
            writer.WriteLine("Gst.GLib.GException.ThrowIfSet(ref errorNative);");
        }

        foreach (ArgumentPlan argument in plan.Arguments)
        {
            WriteEpilogue(writer, plan, argument);
        }

        WriteReturn(writer, plan);

        for (int i = scopes.Count - 1; i >= 0; i--)
        {
            writer.CloseBlock();
            if (scopes[i].Kind != ArgumentKind.Callback)
            {
                continue;
            }

            writer.WriteLine("finally");
            writer.OpenBlock();
            writer.WriteLine(scopes[i].Name + "State.Free();");
            writer.CloseBlock();
        }
    }

    /// <summary>
    /// Keeps every wrapper a member hands to native code reachable until the
    /// native call returned: the instance and each handle argument.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The member being written.</param>
    /// <remarks>
    /// The call takes the raw handle out of the wrapper, and nothing mentions
    /// the wrapper afterwards, so the collector is free to finalize it while
    /// the call is still running. The finalizer releases the instance, and the
    /// call is then working on freed memory. That holds for an argument just as
    /// much as for the instance, and a static function has nothing but its
    /// arguments. The barriers are emitted right after the call, because that
    /// is the last use of the handles, and in declaration order, which puts the
    /// instance first. <c>GC.KeepAlive</c> accepts a null reference, so a
    /// nullable argument needs no guard of its own.
    /// </remarks>
    private static void WriteKeepAlive(CodeWriter writer, MarshalPlan plan)
    {
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            // The instance is hidden on an instance method, where it is spelled
            // "this", so it is never filtered on visibility.
            if (argument.Kind == ArgumentKind.Instance)
            {
                writer.WriteLine(
                    "System.GC.KeepAlive("
                    + (plan.Form == CallableForm.ExtensionMethod ? argument.Name : "this") + ");");
            }
            else if (argument.Kind == ArgumentKind.Handle
                && argument.Direction == ArgumentDirection.In
                && !argument.IsHidden)
            {
                writer.WriteLine("System.GC.KeepAlive(" + argument.Name + ");");
            }
        }
    }

    /// <summary>
    /// Tests whether a member hands native code a managed callback, which is
    /// what makes the order of its prologue observable.
    /// </summary>
    /// <param name="plan">The member being written.</param>
    /// <returns><see langword="true"/> when one of the arguments is a callback.</returns>
    /// <remarks>
    /// <para>
    /// Allocating the state of a callback is the one step of a prologue that
    /// takes a resource the collector cannot reclaim: a <c>GCHandle</c> that is
    /// freed by the destroy notification of the native call, or by the
    /// <c>finally</c> of a call scoped callback, and by nothing else. If
    /// anything between the allocation and the call throws, that never happens
    /// and the handle, the delegate and everything the closure captured are
    /// pinned for the life of the process.
    /// </para>
    /// <para>
    /// Reading <c>Handle</c> is exactly such a step, because a disposed wrapper
    /// throws <see cref="ObjectDisposedException"/> from it, and it used to sit
    /// after the allocation because the call site is where the handle is read.
    /// So the read is hoisted into a local ahead of the allocation whenever a
    /// callback is present, which is the order the hand written surfaces of the
    /// binding follow. The local is emitted only for those members, because
    /// hoisting it everywhere would rewrite every generated body for a
    /// guarantee that nothing but a callback needs.
    /// </para>
    /// </remarks>
    private static bool TakesCallback(MarshalPlan plan)
    {
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind == ArgumentKind.Callback)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the expression that reads the raw handle out of the instance a
    /// member is called on.
    /// </summary>
    /// <param name="plan">The member being written.</param>
    /// <param name="name">The name of the instance argument.</param>
    /// <returns>The expression to read.</returns>
    private static string InstanceHandle(MarshalPlan plan, string name) =>
        plan.Form == CallableForm.ExtensionMethod ? name + ".Handle" : "Handle";

    private static void WritePrologue(CodeWriter writer, MarshalPlan plan, ArgumentPlan argument)
    {
        string name = argument.Name;
        switch (argument.Kind)
        {
            case ArgumentKind.Instance:
                if (plan.Form == CallableForm.ExtensionMethod)
                {
                    writer.WriteLine("ArgumentNullException.ThrowIfNull(" + name + ");");
                }

                if (TakesCallback(plan))
                {
                    writer.WriteLine("nint " + InstanceLocal + " = " + InstanceHandle(plan, name) + ";");
                }

                return;

            case ArgumentKind.Error:
                writer.WriteLine("nint errorNative = 0;");
                return;

            case ArgumentKind.Utf8 when argument.Direction == ArgumentDirection.In:
                if (!argument.IsNullable)
                {
                    writer.WriteLine("ArgumentNullException.ThrowIfNull(" + name + ");");
                }

                writer.WriteLine(
                    "System.Span<byte> " + name + "Buffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];");
                writer.WriteLine(
                    "using Gst.Interop.Utf8Scope " + name + "Scope = Gst.Interop.GMarshal.StackUtf8("
                    + name + ", " + name + "Buffer);");
                return;

            case ArgumentKind.Utf8Owned:
                if (!argument.IsNullable)
                {
                    writer.WriteLine("ArgumentNullException.ThrowIfNull(" + name + ");");
                }

                writer.WriteLine("nint " + name + "Native = Gst.Interop.GMarshal.StringToUtf8Ptr(" + name + ");");
                return;

            case ArgumentKind.Handle when argument.Direction == ArgumentDirection.In:
                if (!argument.IsNullable)
                {
                    writer.WriteLine("ArgumentNullException.ThrowIfNull(" + name + ");");
                }

                return;

            case ArgumentKind.Callback:
                writer.WriteLine("ArgumentNullException.ThrowIfNull(" + name + ");");
                writer.WriteLine(
                    "Gst.Interop.CallbackHandle " + name + "State = Gst.Interop.CallbackHandle.Alloc(" + name + ");");
                return;

            case ArgumentKind.Span:
            case ArgumentKind.UserData:
            case ArgumentKind.DestroyNotify:
                return;

            case ArgumentKind.PlainStruct when argument.RawType.EndsWith('*'):
                writer.WriteLine(
                    argument.PublicType + " " + name + "Native = "
                    + (argument.Direction == ArgumentDirection.Out ? "default;" : name + ";"));
                return;

            case ArgumentKind.PlainStruct:
                return;

            default:
                break;
        }

        if (argument.Direction == ArgumentDirection.In && argument.Kind != ArgumentKind.ArrayLength)
        {
            return;
        }

        if (argument.Kind == ArgumentKind.ArrayLength && argument.Direction == ArgumentDirection.In)
        {
            return;
        }

        // Everything that comes back through a pointer gets a local that the
        // call writes into.
        string pointee = Pointee(argument.RawType);
        string initial = argument.Direction == ArgumentDirection.Ref
            ? ToNative(argument, name)
            : "default";
        writer.WriteLine(pointee + " " + name + "Native = " + initial + ";");
    }

    private static void WriteCall(CodeWriter writer, MarshalPlan plan)
    {
        List<string> arguments = [];
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            arguments.Add(Argument(plan, argument));
        }

        string call = plan.NativeName + "(" + string.Join(", ", arguments) + ")";
        if (plan.Return.IsVoid)
        {
            writer.WriteLine(call + ";");
            return;
        }

        writer.WriteLine(plan.Return.RawType + " " + ResultLocal + " = " + call + ";");
    }

    private static string Argument(MarshalPlan plan, ArgumentPlan argument)
    {
        string name = argument.Name;
        switch (argument.Kind)
        {
            case ArgumentKind.Instance:
                return TakesCallback(plan) ? InstanceLocal : InstanceHandle(plan, name);

            case ArgumentKind.Error:
                return "&errorNative";

            case ArgumentKind.UserData:
                return plan.Arguments[argument.OwnerArgument ?? 0].Name + "State.UserData";

            case ArgumentKind.DestroyNotify:
                return "(nint)Gst.Interop.CallbackHandle.DestroyNotify";

            case ArgumentKind.Callback:
                return argument.TrampolineType + ".Pointer";

            case ArgumentKind.Span:
                return name + "Pointer";

            case ArgumentKind.ArrayLength when argument.Direction == ArgumentDirection.In:
                return "(" + argument.RawType + ")" + plan.Arguments[argument.OwnerArgument ?? 0].Name + ".Length";

            case ArgumentKind.Utf8 when argument.Direction == ArgumentDirection.In:
                return name + "Scope.Pointer";

            case ArgumentKind.Utf8Owned:
                return name + "Native";

            case ArgumentKind.PlainStruct:
                // A structure that the gir spells with a star is copied into a
                // local, which the call then reads and writes through.
                return argument.RawType.EndsWith('*') ? "&" + name + "Native" : name;

            case ArgumentKind.Handle when argument.Direction == ArgumentDirection.In:
                return argument.IsNullable ? name + " is null ? 0 : " + name + ".Handle" : name + ".Handle";

            default:
                break;
        }

        return argument.Direction == ArgumentDirection.In ? ToNative(argument, name) : "&" + name + "Native";
    }

    /// <summary>Converts a public value into the raw value the call takes.</summary>
    /// <param name="argument">The argument to convert.</param>
    /// <param name="name">The name of the public value.</param>
    /// <returns>The conversion expression.</returns>
    private static string ToNative(ArgumentPlan argument, string name) => argument.Kind switch
    {
        ArgumentKind.Boolean => name + " ? 1 : 0",
        ArgumentKind.Enumeration => "(" + Pointee(argument.RawType) + ")" + name,
        ArgumentKind.Wrapper => name + "." + WrapperConversion(argument.PublicType),
        _ => name,
    };

    /// <summary>Converts a raw value into the public value of an argument.</summary>
    /// <param name="argument">The argument to convert.</param>
    /// <param name="source">The expression holding the raw value.</param>
    /// <returns>The conversion expression.</returns>
    private static string FromNative(ArgumentPlan argument, string source) => argument.Kind switch
    {
        ArgumentKind.Boolean => source + " != 0",
        ArgumentKind.Enumeration => "(" + TrimNullable(argument.PublicType) + ")" + source,
        ArgumentKind.Wrapper => "new " + TrimNullable(argument.PublicType) + "(" + source + ")",
        ArgumentKind.Utf8 => StringConversion(argument.Transfer, source),
        ArgumentKind.Handle => HandleConversion(argument.Flavor, TrimNullable(argument.PublicType), source, argument.Transfer),
        ArgumentKind.Strv => "Gst.Interop.GMarshal.StrvToArray(" + source + ", free: "
            + (argument.Transfer == GirTransfer.None ? "false" : "true") + ")",
        _ => source,
    };

    private static string TrimNullable(string type) => type.EndsWith('?') ? type[..^1] : type;

    private static string StringConversion(GirTransfer transfer, string source) =>
        transfer is GirTransfer.Full or GirTransfer.Floating
            ? "Gst.Interop.GMarshal.PtrToStringUtf8AndFree(" + source + ")"
            : "Gst.Interop.GMarshal.PtrToStringUtf8(" + source + ")";

    private static string HandleConversion(HandleFlavor flavor, string type, string source, GirTransfer transfer) =>
        flavor switch
        {
            HandleFlavor.GObject => "Gst.GObject.Object.FromNative<" + type + ">(" + source + ", "
                + TransferLiteral(transfer) + ")",
            HandleFlavor.Opaque => type + ".FromNative(" + source + ")",
            _ => type + ".FromNative(" + source + ", " + TransferLiteral(transfer) + ")",
        };

    private static void WriteEpilogue(CodeWriter writer, MarshalPlan plan, ArgumentPlan argument)
    {
        if (argument.IsHidden || argument.Direction == ArgumentDirection.In)
        {
            return;
        }

        string name = argument.Name;
        if (argument.Kind == ArgumentKind.ArrayOut)
        {
            WriteArrayConversion(
                writer,
                plan,
                argument.ElementType!,
                name + "Native",
                argument.LengthArgument,
                argument.Transfer,
                target: name,
                declare: false);
            return;
        }

        writer.WriteLine(name + " = " + FromNative(argument, name + "Native") + ";");
    }

    private static void WriteReturn(CodeWriter writer, MarshalPlan plan)
    {
        ReturnPlan value = plan.Return;
        if (value.IsVoid)
        {
            return;
        }

        if (value.Kind == ArgumentKind.ArrayOut)
        {
            WriteArrayConversion(
                writer,
                plan,
                value.ElementType!,
                ResultLocal,
                value.LengthArgument,
                value.Transfer,
                target: ConvertedLocal,
                declare: true);
            writer.WriteLine("return " + ConvertedLocal + ";");
            return;
        }

        if (value.Kind == ArgumentKind.GListReturn)
        {
            WriteListConversion(writer, value);
            return;
        }

        ArgumentPlan projection = new()
        {
            Kind = value.Kind,
            Name = ConvertedLocal,
            PublicType = value.PublicType,
            RawType = value.RawType,
            Transfer = value.Transfer,
            Flavor = value.Flavor,
            IsNullable = value.IsNullable,
        };

        string expression = FromNative(projection, ResultLocal);
        bool needsCheck = !value.IsNullable
            && value.Kind is ArgumentKind.Handle or ArgumentKind.Utf8 or ArgumentKind.Strv;

        if (!needsCheck)
        {
            writer.WriteLine("return " + expression + ";");
            return;
        }

        writer.WriteLine("return " + expression);
        writer.WriteLine(
            "    ?? throw new InvalidOperationException(\"" + plan.EntryPoint + " returned no value.\");");
    }

    /// <summary>
    /// Writes the materialization of a returned <c>GList</c>.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="value">The return value, whose element projection is read here.</param>
    /// <remarks>
    /// <para>
    /// The three steps are in this order on purpose, and the barriers of the
    /// call have already been written when this runs. The element pointers are
    /// copied out of the spine first, the spine is released next, and the
    /// elements are adopted last, so that an adoption that throws — every one
    /// of them can — cannot leave a managed value pointing into freed nodes and
    /// cannot free the same spine twice. No managed type ever holds the
    /// <c>GList*</c>: it is gone before this method returns.
    /// </para>
    /// <para>
    /// The two halves of the transfer are read separately. The spine is freed
    /// unless the library keeps owning it (<c>transfer-ownership="none"</c>),
    /// and the elements are adopted with the reference the call handed over
    /// under <c>full</c> and with one of their own under <c>container</c> and
    /// <c>none</c>. A <c>NULL</c> element is dropped rather than turned into a
    /// null entry, and a <c>NULL</c> list is an empty list, which is why the
    /// member never returns <see langword="null"/>.
    /// </para>
    /// </remarks>
    private static void WriteListConversion(CodeWriter writer, ReturnPlan value)
    {
        string elementType = value.ElementType!;
        GirTransfer elementTransfer = value.Transfer is GirTransfer.Full or GirTransfer.Floating
            ? GirTransfer.Full
            : GirTransfer.None;
        string collect = value.Transfer == GirTransfer.None ? "Collect" : "CollectAndFreeSpine";
        string conversion = value.ElementKind == ArgumentKind.Utf8
            ? StringConversion(elementTransfer, ItemLocal)
            : HandleConversion(value.Flavor, elementType, ItemLocal, elementTransfer);

        writer.WriteLine(
            "nint[] " + ItemsLocal + " = Gst.Interop.GListMarshal." + collect + "(" + ResultLocal + ");");
        writer.WriteLine(
            "System.Collections.Generic.List<" + elementType + "> " + ConvertedLocal
            + " = new(" + ItemsLocal + ".Length);");
        writer.WriteLine("foreach (nint " + ItemLocal + " in " + ItemsLocal + ")");
        writer.OpenBlock();
        writer.WriteLine("if (" + ItemLocal + " != 0 && " + conversion + " is { } " + ElementLocal + ")");
        writer.OpenBlock();
        writer.WriteLine(ConvertedLocal + ".Add(" + ElementLocal + ");");
        writer.CloseBlock();
        writer.CloseBlock();
        writer.WriteLine();
        writer.WriteLine("return " + ConvertedLocal + ";");
    }

    private static void WriteArrayConversion(
        CodeWriter writer,
        MarshalPlan plan,
        string elementType,
        string source,
        int? lengthArgument,
        GirTransfer transfer,
        string target,
        bool declare)
    {
        string length = "(int)" + plan.Arguments[lengthArgument ?? 0].Name + "Native";
        writer.WriteLine(declare ? elementType + "[]? " + target + " = null;" : target + " = null;");
        writer.WriteLine("if (" + source + " != 0)");
        writer.OpenBlock();
        writer.WriteLine(target + " = new " + elementType + "[" + length + "];");
        writer.WriteLine(
            "new System.ReadOnlySpan<" + elementType + ">((void*)" + source + ", " + length + ").CopyTo("
            + target + ");");
        if (transfer != GirTransfer.None)
        {
            writer.WriteLine("Gst.Interop.GMarshal.Free(" + source + ");");
        }

        writer.CloseBlock();
    }

    /// <summary>Returns the message of the exception a missing argument raises.</summary>
    /// <param name="plan">The callback.</param>
    /// <param name="argument">The argument that was null.</param>
    /// <returns>The message text.</returns>
    private static string NullMessage(CallbackPlan plan, ArgumentPlan argument) =>
        (plan.Callback.CType ?? plan.DelegateName) + " passed no "
        + (argument.Source?.Name ?? DocName(argument.Name)) + ".";

    /// <summary>
    /// Declares the managed value of one trampoline argument, throwing when the
    /// gir promises a value that native code did not deliver.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="argument">The argument to convert.</param>
    /// <param name="expression">The conversion expression.</param>
    /// <param name="message">The message of the exception.</param>
    /// <remarks>
    /// The exception is raised inside the <c>try</c> of the trampoline, so a
    /// conversion that produced nothing is reported through
    /// <c>Gst.Interop.ExceptionTrap</c> and answered with the failure value of
    /// the callback. The managed handler is never entered: its signature
    /// excludes the null, and null forgiving it into one would hand a consumer
    /// a value its type says cannot be there.
    /// </remarks>
    private static void WriteCallbackLocal(
        CodeWriter writer,
        ArgumentPlan argument,
        string expression,
        string message)
    {
        string declaration = argument.PublicType + " " + argument.Name + "Value = " + expression;
        if (argument.IsNullable)
        {
            writer.WriteLine(declaration + ";");
            return;
        }

        writer.WriteLine(declaration);
        writer.WriteLine("    ?? throw new InvalidOperationException(\"" + message + "\");");
    }

    private static void WriteTrampoline(CodeWriter writer, CallbackPlan plan)
    {
        List<string> rawParameters = [];
        List<string> pointerTypes = [];
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            rawParameters.Add(argument.RawType + " " + argument.Name);
            pointerTypes.Add(argument.RawType);
        }

        pointerTypes.Add(plan.Return.RawType);

        writer.WriteLine(
            "/// <summary>The native entry point of <see cref=\"" + plan.DelegateType + "\"/>.</summary>");
        XmlDocWriter.WriteObsolete(writer, plan.Callback);
        writer.WriteLine("internal static unsafe class " + plan.DelegateName + "Trampoline");
        writer.OpenBlock();
        writer.WriteLine("/// <summary>Gets the address that is handed to native code.</summary>");
        writer.WriteLine(
            "internal static nint Pointer => (nint)(delegate* unmanaged[Cdecl]<"
            + string.Join(", ", pointerTypes) + ">)&Invoke;");
        writer.WriteLine();
        writer.WriteLine("[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
        writer.WriteLine(
            "private static " + plan.Return.RawType + " Invoke(" + string.Join(", ", rawParameters) + ")");
        writer.OpenBlock();
        writer.WriteLine("try");
        writer.OpenBlock();

        string userData = "userData";
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind == ArgumentKind.UserData)
            {
                userData = argument.Name;
            }
        }

        writer.WriteLine(
            "if (Gst.Interop.CallbackHandle.GetState<" + plan.DelegateType + ">(" + userData
            + ") is not { } callback)");
        writer.OpenBlock();
        writer.WriteLine(plan.Return.IsVoid ? "return;" : "return " + FailureLiteral(plan.Return) + ";");
        writer.CloseBlock();
        writer.WriteLine();

        List<string> arguments = [];
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind == ArgumentKind.UserData)
            {
                continue;
            }

            switch (argument.Kind)
            {
                case ArgumentKind.PlainStruct:
                    arguments.Add("ref *(" + argument.PublicType + "*)" + argument.Name);
                    break;

                case ArgumentKind.Handle:
                    WriteCallbackLocal(
                        writer,
                        argument,
                        HandleConversion(
                            argument.Flavor,
                            TrimNullable(argument.PublicType),
                            argument.Name,
                            argument.Transfer),
                        NullMessage(plan, argument));
                    arguments.Add(argument.Name + "Value");
                    break;

                case ArgumentKind.Utf8:
                    WriteCallbackLocal(
                        writer,
                        argument,
                        "Gst.Interop.GMarshal.PtrToStringUtf8((nint)" + argument.Name + ")",
                        NullMessage(plan, argument));
                    arguments.Add(argument.Name + "Value");
                    break;

                default:
                    arguments.Add(FromNative(argument, argument.Name));
                    break;
            }
        }

        string invocation = "callback(" + string.Join(", ", arguments) + ")";
        if (plan.Return.IsVoid)
        {
            writer.WriteLine(invocation + ";");
        }
        else
        {
            ArgumentPlan projection = new()
            {
                Kind = plan.Return.Kind,
                Name = ConvertedLocal,
                PublicType = plan.Return.PublicType,
                RawType = plan.Return.RawType,
            };

            writer.WriteLine("return " + ToNative(projection, invocation) + ";");
        }

        writer.CloseBlock();
        writer.WriteLine("catch (Exception exception)");
        writer.OpenBlock();
        writer.WriteLine("Gst.Interop.ExceptionTrap.Report(exception);");
        if (!plan.Return.IsVoid)
        {
            writer.WriteLine("return " + FailureLiteral(plan.Return) + ";");
        }

        writer.CloseBlock();
        writer.CloseBlock();
        writer.CloseBlock();
    }
}
