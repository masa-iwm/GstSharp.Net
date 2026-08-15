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

    /// <summary>The local that holds the converted return value.</summary>
    private const string ConvertedLocal = "result";

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
        XmlDocWriter.Write(writer, plan.Callback.Doc, "The <c>" + cType + "</c> callback.");
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
        XmlDocWriter.Write(writer, plan.Callable.Doc, "The <c>" + cType + "</c> function.");

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
            XmlDocWriter.WriteReturns(writer, plan.Return.Doc, "The result of <c>" + cType + "</c>.");
        }

        if (plan.Throws)
        {
            writer.WriteLine("/// <exception cref=\"Gst.GLib.GException\">The native call failed.</exception>");
        }
    }

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
                return plan.Form == CallableForm.ExtensionMethod ? name + ".Handle" : "Handle";

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

    /// <summary>
    /// Declares the managed value of one trampoline argument, throwing when the
    /// gir promises a value that native code did not deliver.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="argument">The argument to convert.</param>
    /// <param name="expression">The conversion expression.</param>
    /// <param name="message">The message of the exception.</param>
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
        writer.WriteLine(plan.Return.IsVoid ? "return;" : "return default;");
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
                        (plan.Callback.CType ?? plan.DelegateName) + " received a null instance.");
                    arguments.Add(argument.Name + "Value");
                    break;

                case ArgumentKind.Utf8:
                    WriteCallbackLocal(
                        writer,
                        argument,
                        "Gst.Interop.GMarshal.PtrToStringUtf8((nint)" + argument.Name + ")",
                        (plan.Callback.CType ?? plan.DelegateName) + " received a null string.");
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
            writer.WriteLine("return default;");
        }

        writer.CloseBlock();
        writer.CloseBlock();
        writer.CloseBlock();
    }
}
