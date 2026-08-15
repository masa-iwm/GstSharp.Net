using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Planning;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>
/// Writes the C# text of a <see cref="SignalPlan"/>: the class that carries the
/// arguments, the event accessors and the trampoline that GObject invokes.
/// </summary>
/// <remarks>
/// <para>
/// One signal becomes four declarations of the class that declares it, in this
/// order: the arguments class (only when the signal carries arguments), the
/// handler delegate (only when the signal returns a value), the event, and the
/// trampoline. The arguments class and the delegate are nested in the declaring
/// type, so <c>Gst.Element.PadAddedSignalArgs</c> cannot collide with the
/// arguments of a signal of the same name on another type.
/// </para>
/// <para>
/// Nothing is allocated per emission besides the arguments class and the
/// wrappers of the arguments, and nothing is reflected over: the trampoline
/// resolves the delegate from the state of the closure and calls it directly.
/// The ownership rules are the ones of a callback, because that is what a
/// handler is:
/// </para>
/// <list type="bullet">
/// <item><description>A <c>GObject</c> argument is looked up through
/// <c>FromNative</c> with <c>Transfer.None</c>. That returns the interned
/// wrapper of the instance, which the handler must not dispose and the
/// trampoline therefore does not dispose either.</description></item>
/// <item><description>A mini object, a boxed record and a <c>GParamSpec</c>
/// argument are wrapped by a wrapper that takes a reference of its own, so the
/// trampoline scopes it with <c>using</c>: the reference is released once the
/// handler returns. The handler may only use such a value for the duration of
/// the emission, which is what the documentation of the property says.
/// </description></item>
/// <item><description>A string argument is read without being freed: the
/// emission owns it.</description></item>
/// </list>
/// <para>
/// The instance is not captured in the state of the closure. A strong reference
/// from the closure to the wrapper would keep the wrapper alive, the wrapper
/// keeps its toggle reference alive and the toggle reference keeps the instance
/// alive, so the pair could never be collected. The trampoline therefore looks
/// the sender up per emission, which is a dictionary lookup.
/// </para>
/// </remarks>
internal static class SignalEmitter
{
    /// <summary>The name of the generated holder of the connected handlers.</summary>
    internal const string ConnectionsName = "SignalConnections";

    /// <summary>The parameter of a trampoline that receives the emitting instance.</summary>
    private const string InstanceParameter = "instance";

    /// <summary>The parameter of a trampoline that receives the state of the closure.</summary>
    private const string UserDataParameter = "userData";

    /// <summary>Writes the members one signal contributes to its declaring type.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="signal">The signal to write.</param>
    /// <param name="module">The module being emitted.</param>
    /// <param name="cType">The C type of the declaring type, for the documentation.</param>
    internal static void WriteSignal(CodeWriter writer, SignalEmission signal, ModuleInfo module, string cType)
    {
        SignalPlan plan = signal.Plan;
        if (plan.ArgsName is not null)
        {
            WriteArgs(writer, plan, signal.ArgsAreNew, cType);
            writer.WriteLine();
        }

        if (plan.HandlerName is not null)
        {
            WriteHandlerDelegate(writer, plan, cType);
            writer.WriteLine();
        }

        WriteEvent(writer, signal, module, cType);
        writer.WriteLine();
        WriteTrampoline(writer, plan, cType);
    }

    /// <summary>
    /// Writes the members one signal of a gir interface contributes to the
    /// extension class of that interface.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="signal">The signal to write.</param>
    /// <param name="module">The module being emitted.</param>
    /// <param name="cType">The C type of the declaring interface, for the documentation.</param>
    /// <param name="interfaceType">The C# interface the accessors extend.</param>
    /// <remarks>
    /// A C# interface cannot declare an event that its implementors do not
    /// implement, and an extension member cannot be an event, so the signal
    /// becomes a pair of extension methods instead. They connect and disconnect
    /// through the same holder that an event of a class uses, so a handler that
    /// was added on an interface is disconnected when the instance is disposed
    /// just like any other.
    /// </remarks>
    internal static void WriteInterfaceSignal(
        CodeWriter writer,
        SignalEmission signal,
        ModuleInfo module,
        string cType,
        string interfaceType)
    {
        SignalPlan plan = signal.Plan;
        if (plan.ArgsName is not null)
        {
            WriteArgs(writer, plan, isNew: false, cType);
            writer.WriteLine();
        }

        if (plan.HandlerName is not null)
        {
            WriteHandlerDelegate(writer, plan, cType);
            writer.WriteLine();
        }

        WriteAccessors(writer, plan, module, cType, interfaceType);
        writer.WriteLine();
        WriteTrampoline(writer, plan, cType);
    }

    /// <summary>Returns the name of the method that connects a handler of an interface signal.</summary>
    /// <param name="plan">The signal.</param>
    /// <returns>The C# method name, for example <c>AddChildAddedHandler</c>.</returns>
    internal static string AddMethodName(SignalPlan plan) => "Add" + plan.Name + "Handler";

    /// <summary>Returns the name of the method that disconnects a handler of an interface signal.</summary>
    /// <param name="plan">The signal.</param>
    /// <returns>The C# method name, for example <c>RemoveChildAddedHandler</c>.</returns>
    internal static string RemoveMethodName(SignalPlan plan) => "Remove" + plan.Name + "Handler";

    /// <summary>
    /// Emits the holder that remembers which handler was connected under which
    /// identifier, so that removing an event handler disconnects exactly the
    /// one that was added.
    /// </summary>
    /// <param name="module">The module to emit.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <returns>The generated file.</returns>
    internal static GeneratedFile EmitConnections(ModuleInfo module, GirNamespace ns)
    {
        CodeWriter writer = new();
        writer.WriteLine("// <auto-generated/>");
        writer.WriteLine("// Generated by GstSharp.Generator from " + ns.Name + "-" + ns.Version + ".gir. Do not edit.");
        writer.WriteLine();
        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        writer.WriteLine("using System;");
        writer.WriteLine("using System.Collections.Generic;");
        writer.WriteLine("using System.Runtime.CompilerServices;");
        writer.WriteLine();
        writer.WriteLine("namespace " + module.ClrNamespace + ";");
        writer.WriteLine();
        writer.WriteLine("/// <summary>The signal handlers the events of this module connected.</summary>");
        writer.WriteLine("/// <remarks>");
        writer.WriteLine("/// <para>");
        writer.WriteLine("/// A C# event removes a handler by delegate, while GObject disconnects one by");
        writer.WriteLine("/// identifier, so the identifier of every connected handler is remembered");
        writer.WriteLine("/// here until it is removed. The table is keyed weakly by the instance, so");
        writer.WriteLine("/// nothing it holds outlives the wrapper that owns the handlers.");
        writer.WriteLine("/// </para>");
        writer.WriteLine("/// <para>");
        writer.WriteLine("/// The connection itself goes through <c>Gst.GObject.Object.ConnectSignal</c>,");
        writer.WriteLine("/// which tracks the identifier as well: disposing an instance disconnects the");
        writer.WriteLine("/// handlers that are still connected, and disconnecting a handler destroys its");
        writer.WriteLine("/// closure, which releases the managed state through the closure notification");
        writer.WriteLine("/// of <c>Gst.Interop.CallbackHandle</c>. No handler state is ever leaked and");
        writer.WriteLine("/// none is ever released twice.");
        writer.WriteLine("/// </para>");
        writer.WriteLine("/// </remarks>");
        writer.WriteLine("internal static class " + ConnectionsName);
        writer.OpenBlock();
        writer.WriteLine(
            "private static readonly ConditionalWeakTable<Gst.GObject.Object, List<Connection>> Connections = new();");
        writer.WriteLine();
        writer.WriteLine("/// <summary>Connects one handler of an event.</summary>");
        writer.WriteLine("/// <param name=\"instance\">The instance the event belongs to.</param>");
        writer.WriteLine("/// <param name=\"signal\">The name of the signal, for example <c>pad-added</c>.</param>");
        writer.WriteLine("/// <param name=\"callback\">The address of the trampoline of the signal.</param>");
        writer.WriteLine("/// <param name=\"handler\">The handler that was added, if any.</param>");
        writer.WriteLine(
            "internal static void Add(Gst.GObject.Object instance, string signal, nint callback, Delegate? handler)");
        writer.OpenBlock();
        writer.WriteLine("if (handler is null)");
        writer.OpenBlock();
        writer.WriteLine("return;");
        writer.CloseBlock();
        writer.WriteLine();
        writer.WriteLine("Gst.Interop.CallbackHandle state = Gst.Interop.CallbackHandle.Alloc(handler);");
        writer.WriteLine("ulong id;");
        writer.WriteLine("try");
        writer.OpenBlock();
        writer.WriteLine("id = instance.ConnectSignal(signal, callback, state, after: false);");
        writer.CloseBlock();
        writer.WriteLine("catch");
        writer.OpenBlock();
        writer.WriteLine("// Nothing took the state over, so the closure notification that would");
        writer.WriteLine("// normally release it never runs.");
        writer.WriteLine("state.Free();");
        writer.WriteLine("throw;");
        writer.CloseBlock();
        writer.WriteLine();
        writer.WriteLine("List<Connection> connections = Connections.GetOrCreateValue(instance);");
        writer.WriteLine("lock (connections)");
        writer.OpenBlock();
        writer.WriteLine("connections.Add(new Connection(signal, handler, id));");
        writer.CloseBlock();
        writer.CloseBlock();
        writer.WriteLine();
        writer.WriteLine("/// <summary>Disconnects the handler that was connected last for a delegate.</summary>");
        writer.WriteLine("/// <param name=\"instance\">The instance the event belongs to.</param>");
        writer.WriteLine("/// <param name=\"signal\">The name of the signal.</param>");
        writer.WriteLine("/// <param name=\"handler\">The handler that was removed, if any.</param>");
        writer.WriteLine(
            "internal static void Remove(Gst.GObject.Object instance, string signal, Delegate? handler)");
        writer.OpenBlock();
        writer.WriteLine("if (handler is null || !Connections.TryGetValue(instance, out List<Connection>? connections))");
        writer.OpenBlock();
        writer.WriteLine("return;");
        writer.CloseBlock();
        writer.WriteLine();
        writer.WriteLine("ulong id = 0;");
        writer.WriteLine("lock (connections)");
        writer.OpenBlock();
        writer.WriteLine("// A delegate may have been added more than once, and removing it takes");
        writer.WriteLine("// the last one away, which is what a C# event does.");
        writer.WriteLine("for (int i = connections.Count - 1; i >= 0; i--)");
        writer.OpenBlock();
        writer.WriteLine("if (string.Equals(connections[i].Signal, signal, StringComparison.Ordinal)");
        writer.WriteLine("    && connections[i].Handler.Equals(handler))");
        writer.OpenBlock();
        writer.WriteLine("id = connections[i].Id;");
        writer.WriteLine("connections.RemoveAt(i);");
        writer.WriteLine("break;");
        writer.CloseBlock();
        writer.CloseBlock();
        writer.CloseBlock();
        writer.WriteLine();
        writer.WriteLine("if (id != 0)");
        writer.OpenBlock();
        writer.WriteLine("// Disconnecting destroys the closure, which releases the state of the");
        writer.WriteLine("// handler through its closure notification.");
        writer.WriteLine("instance.RemoveHandler(id);");
        writer.CloseBlock();
        writer.CloseBlock();
        writer.WriteLine();
        writer.WriteLine("/// <summary>One connected handler.</summary>");
        writer.WriteLine("/// <param name=\"Signal\">The name of the signal.</param>");
        writer.WriteLine("/// <param name=\"Handler\">The delegate that was connected.</param>");
        writer.WriteLine("/// <param name=\"Id\">The identifier GObject gave the handler.</param>");
        writer.WriteLine("private readonly record struct Connection(string Signal, Delegate Handler, ulong Id);");
        writer.CloseBlock();

        return new GeneratedFile(
            module.ProjectDirectory + "/Generated/" + ConnectionsName + ".cs",
            writer.ToSource());
    }

    /// <summary>Returns the documentation fallback that names a signal.</summary>
    /// <param name="plan">The signal.</param>
    /// <param name="cType">The C type of the declaring type.</param>
    /// <returns>The sentence fragment, for example <c>pad-added</c> signal of <c>GstElement</c>.</returns>
    private static string Describe(SignalPlan plan, string cType) =>
        "the <c>" + plan.SignalName + "</c> signal of <c>" + cType + "</c>";

    /// <summary>Returns the message of the exception a missing argument raises.</summary>
    /// <param name="plan">The signal.</param>
    /// <param name="argument">The argument that was null.</param>
    /// <param name="cType">The C type of the declaring type.</param>
    /// <returns>The message text.</returns>
    private static string NullMessage(SignalPlan plan, SignalArgument argument, string cType) =>
        "The " + plan.SignalName + " signal of " + cType + " passed no "
        + (argument.Argument.Source?.Name ?? argument.Argument.Name) + ".";

    private static string TrimNullable(string type) => type.EndsWith('?') ? type[..^1] : type;

    private static void WriteArgs(CodeWriter writer, SignalPlan plan, bool isNew, string cType)
    {
        string name = plan.ArgsName!;
        writer.WriteLine("/// <summary>The arguments of " + Describe(plan, cType) + ".</summary>");
        writer.WriteLine("public " + (isNew ? "new " : string.Empty) + "sealed class " + name);
        writer.OpenBlock();

        List<string> parameters = [];
        foreach (SignalArgument argument in plan.Arguments)
        {
            parameters.Add(argument.Argument.PublicType + " " + argument.Argument.Name);
        }

        writer.WriteLine(
            "/// <summary>Initializes a new instance of the <see cref=\"" + name + "\"/> class.</summary>");
        foreach (SignalArgument argument in plan.Arguments)
        {
            XmlDocWriter.WriteParam(
                writer,
                DocName(argument.Argument.Name),
                argument.Argument.Doc,
                "The <c>" + (argument.Argument.Source?.Name ?? argument.Argument.Name) + "</c> argument.");
        }

        writer.WriteLine("internal " + name + "(" + string.Join(", ", parameters) + ")");
        writer.OpenBlock();
        foreach (SignalArgument argument in plan.Arguments)
        {
            writer.WriteLine(argument.PropertyName + " = " + argument.Argument.Name + ";");
        }

        writer.CloseBlock();

        foreach (SignalArgument argument in plan.Arguments)
        {
            writer.WriteLine();
            XmlDocWriter.Write(
                writer,
                argument.Argument.Doc,
                "The <c>" + (argument.Argument.Source?.Name ?? argument.Argument.Name) + "</c> argument.");

            if (IsOwnedWrapper(argument.Argument))
            {
                // The wrapper holds a reference that the trampoline releases
                // again once the handler has returned.
                writer.WriteLine("/// <remarks>");
                writer.WriteLine("/// The value is only valid while the handler runs. Keep what you need of");
                writer.WriteLine("/// it, or take a reference of your own before the handler returns.");
                writer.WriteLine("/// </remarks>");
            }

            writer.WriteLine(
                "public " + argument.Argument.PublicType + " " + argument.PropertyName + " { get; }");
        }

        writer.CloseBlock();
    }

    private static void WriteHandlerDelegate(CodeWriter writer, SignalPlan plan, string cType)
    {
        writer.WriteLine("/// <summary>The handler of " + Describe(plan, cType) + ".</summary>");
        writer.WriteLine("/// <param name=\"sender\">The instance that emitted the signal.</param>");
        writer.WriteLine("/// <param name=\"args\">The arguments of the signal.</param>");
        XmlDocWriter.WriteReturns(writer, plan.Return.Doc, "The result the emission collects.");
        writer.WriteLine(
            "public delegate " + plan.Return.PublicType + " " + plan.HandlerName
            + "(object? sender, " + plan.ArgsType + " args);");
    }

    private static void WriteEvent(CodeWriter writer, SignalEmission signal, ModuleInfo module, string cType)
    {
        SignalPlan plan = signal.Plan;
        XmlDocWriter.Write(writer, plan.Signal.Doc, "Raised for " + Describe(plan, cType) + ".");
        if (plan.IsDetailed)
        {
            writer.WriteLine("/// <remarks>");
            writer.WriteLine("/// The signal is detailed. The handler is connected to <c>" + plan.SignalName + "</c>");
            writer.WriteLine("/// without a detail, so it runs for every detail of the signal.");
            writer.WriteLine("/// </remarks>");
        }

        XmlDocWriter.WriteObsolete(writer, plan.Signal);
        writer.WriteLine(
            "public " + (signal.IsNew ? "new " : string.Empty) + "event " + plan.EventType + " " + plan.Name);
        writer.OpenBlock();
        string connections = module.ClrNamespace + "." + ConnectionsName;
        writer.WriteLine(
            "add => " + connections + ".Add(this, \"" + plan.SignalName + "\", (nint)("
            + PointerType(plan) + ")&" + plan.TrampolineName + ", value);");
        writer.WriteLine(
            "remove => " + connections + ".Remove(this, \"" + plan.SignalName + "\", value);");
        writer.CloseBlock();
    }

    private static void WriteAccessors(
        CodeWriter writer,
        SignalPlan plan,
        ModuleInfo module,
        string cType,
        string interfaceType)
    {
        string connections = module.ClrNamespace + "." + ConnectionsName;

        XmlDocWriter.Write(writer, plan.Signal.Doc, "Connects a handler of " + Describe(plan, cType) + ".");
        writer.WriteLine("/// <param name=\"self\">The instance to connect the handler to.</param>");
        writer.WriteLine("/// <param name=\"handler\">The handler to connect.</param>");
        if (plan.IsDetailed)
        {
            writer.WriteLine("/// <remarks>");
            writer.WriteLine("/// The signal is detailed. The handler is connected to <c>" + plan.SignalName + "</c>");
            writer.WriteLine("/// without a detail, so it runs for every detail of the signal.");
            writer.WriteLine("/// </remarks>");
        }

        XmlDocWriter.WriteObsolete(writer, plan.Signal);
        writer.WriteLine(
            "public static void " + AddMethodName(plan) + "(this " + interfaceType + " self, "
            + plan.EventType + " handler) =>");
        writer.WriteLine(
            "    " + connections + ".Add((Gst.GObject.Object)self, \"" + plan.SignalName + "\", (nint)("
            + PointerType(plan) + ")&" + plan.TrampolineName + ", handler);");
        writer.WriteLine();
        writer.WriteLine(
            "/// <summary>Disconnects the handler that was connected last for a delegate of "
            + Describe(plan, cType) + ".</summary>");
        writer.WriteLine("/// <param name=\"self\">The instance the handler was connected to.</param>");
        writer.WriteLine("/// <param name=\"handler\">The handler to disconnect.</param>");
        XmlDocWriter.WriteObsolete(writer, plan.Signal);
        writer.WriteLine(
            "public static void " + RemoveMethodName(plan) + "(this " + interfaceType + " self, "
            + plan.EventType + " handler) =>");
        writer.WriteLine(
            "    " + connections + ".Remove((Gst.GObject.Object)self, \"" + plan.SignalName + "\", handler);");
    }

    /// <summary>Returns the function pointer type of the trampoline of a signal.</summary>
    /// <param name="plan">The signal.</param>
    /// <returns>The <c>delegate*</c> type.</returns>
    private static string PointerType(SignalPlan plan)
    {
        List<string> types = ["nint"];
        foreach (SignalArgument argument in plan.Arguments)
        {
            types.Add(argument.Argument.RawType);
        }

        types.Add("nint");
        types.Add(plan.Return.RawType);
        return "delegate* unmanaged[Cdecl]<" + string.Join(", ", types) + ">";
    }

    private static void WriteTrampoline(CodeWriter writer, SignalPlan plan, string cType)
    {
        List<string> parameters = ["nint " + InstanceParameter];
        foreach (SignalArgument argument in plan.Arguments)
        {
            parameters.Add(argument.Argument.RawType + " " + argument.Argument.Name);
        }

        parameters.Add("nint " + UserDataParameter);

        writer.WriteLine("/// <summary>The native handler of " + Describe(plan, cType) + ".</summary>");
        writer.WriteLine(
            "[UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]");
        writer.WriteLine(
            "private static " + plan.Return.RawType + " " + plan.TrampolineName
            + "(" + string.Join(", ", parameters) + ")");
        writer.OpenBlock();
        writer.WriteLine("try");
        writer.OpenBlock();
        writer.WriteLine(
            "if (Gst.Interop.CallbackHandle.GetState<" + plan.EventType + ">(" + UserDataParameter
            + ") is not { } handler)");
        writer.OpenBlock();
        writer.WriteLine(plan.Return.IsVoid ? "return;" : "return default;");
        writer.CloseBlock();
        writer.WriteLine();

        foreach (SignalArgument argument in plan.Arguments)
        {
            WriteArgument(writer, plan, argument, cType);
        }

        List<string> values = [];
        foreach (SignalArgument argument in plan.Arguments)
        {
            values.Add(DocName(argument.Argument.Name) + "Value");
        }

        string sender = "Gst.GObject.Object.FromNative(" + InstanceParameter + ", Gst.Interop.Transfer.None)";
        string arguments = plan.ArgsName is null
            ? "System.EventArgs.Empty"
            : "new " + plan.ArgsType + "(" + string.Join(", ", values) + ")";

        if (plan.Return.IsVoid)
        {
            writer.WriteLine("handler(");
            writer.WriteLine("    " + sender + ",");
            writer.WriteLine("    " + arguments + ");");
        }
        else
        {
            writer.WriteLine(plan.Return.PublicType + " result = handler(");
            writer.WriteLine("    " + sender + ",");
            writer.WriteLine("    " + arguments + ");");
            writer.WriteLine("return " + ToNative(plan.Return, "result") + ";");
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
    }

    /// <summary>Declares the managed value of one argument of a signal.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The signal.</param>
    /// <param name="argument">The argument to convert.</param>
    /// <param name="cType">The C type of the declaring type.</param>
    private static void WriteArgument(CodeWriter writer, SignalPlan plan, SignalArgument argument, string cType)
    {
        ArgumentPlan value = argument.Argument;
        string local = DocName(value.Name) + "Value";
        string keyword = IsOwnedWrapper(value) ? "using " : string.Empty;
        string declaration = keyword + value.PublicType + " " + local + " = " + FromNative(value);

        if (!CanBeNull(value))
        {
            writer.WriteLine(declaration + ";");
            return;
        }

        // The gir promises a value here, so native code passing none is a bug
        // of the emitter of the signal. Throwing reports it through the trap of
        // the trampoline instead of handing the handler a null it cannot expect.
        writer.WriteLine(declaration);
        writer.WriteLine(
            "    ?? throw new InvalidOperationException(\"" + NullMessage(plan, argument, cType) + "\");");
    }

    /// <summary>
    /// Tests whether the conversion of an argument can produce
    /// <see langword="null"/> although the gir promises a value, which is what
    /// the trampoline turns into an exception.
    /// </summary>
    /// <param name="argument">The argument to test.</param>
    /// <returns><see langword="true"/> when the conversion needs a null check.</returns>
    private static bool CanBeNull(ArgumentPlan argument) =>
        !argument.IsNullable
        && (argument.Kind == ArgumentKind.Utf8
            || (argument.Kind == ArgumentKind.Handle && argument.Flavor != HandleFlavor.ParamSpec));

    /// <summary>
    /// Tests whether the wrapper of an argument owns a reference that the
    /// trampoline has to release again.
    /// </summary>
    /// <param name="argument">The argument to test.</param>
    /// <returns><see langword="true"/> when the value is scoped with <c>using</c>.</returns>
    private static bool IsOwnedWrapper(ArgumentPlan argument) =>
        argument.Kind == ArgumentKind.Handle
        && argument.Flavor is HandleFlavor.Wrapper or HandleFlavor.ParamSpec;

    /// <summary>
    /// Converts the raw value of an argument into the value the handler sees.
    /// Every argument of a signal is borrowed for the duration of the emission,
    /// so the transfer is always <c>none</c>.
    /// </summary>
    /// <param name="argument">The argument to convert.</param>
    /// <returns>The conversion expression.</returns>
    private static string FromNative(ArgumentPlan argument)
    {
        string type = TrimNullable(argument.PublicType);
        return argument.Kind switch
        {
            ArgumentKind.Boolean => argument.Name + " != 0",
            ArgumentKind.Enumeration => "(" + type + ")" + argument.Name,
            ArgumentKind.Wrapper => "new " + type + "(" + argument.Name + ")",
            ArgumentKind.Utf8 => "Gst.Interop.GMarshal.PtrToStringUtf8((nint)" + argument.Name + ")",
            ArgumentKind.Handle => argument.Flavor switch
            {
                HandleFlavor.GObject => "Gst.GObject.Object.FromNative<" + type + ">("
                    + argument.Name + ", Gst.Interop.Transfer.None)",
                HandleFlavor.ParamSpec => argument.IsNullable
                    ? argument.Name + " == 0 ? null : new " + type + "(" + argument.Name
                        + ", Gst.Interop.Transfer.None)"
                    : "new " + type + "(" + argument.Name + ", Gst.Interop.Transfer.None)",
                HandleFlavor.Opaque => type + ".FromNative(" + argument.Name + ")",
                _ => type + ".FromNative(" + argument.Name + ", Gst.Interop.Transfer.None)",
            },
            _ => argument.Name,
        };
    }

    /// <summary>Converts the value a handler returned into the raw value GObject collects.</summary>
    /// <param name="value">The return value of the signal.</param>
    /// <param name="source">The expression holding the managed value.</param>
    /// <returns>The conversion expression.</returns>
    private static string ToNative(ReturnPlan value, string source) => value.Kind switch
    {
        ArgumentKind.Boolean => source + " ? 1 : 0",
        ArgumentKind.Enumeration => "(" + value.RawType + ")" + source,
        ArgumentKind.Wrapper => source + "." + (value.PublicType switch
        {
            "Gst.ClockTime" => "Nanoseconds",
            _ => "Value",
        }),
        _ => source,
    };

    /// <summary>
    /// Returns the name a parameter carries in the documentation, which is the
    /// identifier without the escape of a C# keyword.
    /// </summary>
    /// <param name="name">The C# name of the parameter.</param>
    /// <returns>The documented name.</returns>
    private static string DocName(string name) => name.StartsWith('@') ? name[1..] : name;
}
