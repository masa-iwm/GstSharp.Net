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
/// and the return value. A member that materializes an argument tightens the
/// first step into three strict phases — every guard, every handle read, every
/// materialization; <see cref="MaterializesArguments"/> says why.
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
                // A GValue that is only read still crosses by pointer, so it
                // is an `in` parameter rather than a copy of the struct.
                prefix = argument.Direction switch
                {
                    ArgumentDirection.Out => "out ",
                    ArgumentDirection.Ref => "ref ",
                    _ => argument.Kind == ArgumentKind.GValue ? "in " : string.Empty,
                };
            }

            parameters.Add(prefix + argument.PublicType + " " + argument.Name);
        }

        return string.Join(", ", parameters);
    }

    private static void WriteDocumentation(CodeWriter writer, MarshalPlan plan)
    {
        string cType = plan.EntryPoint;
        XmlDocWriter.Write(
            writer,
            plan.Callable.Doc,
            "The <c>" + cType + "</c> function.",
            plan.Callable,
            GeneratedRemarks(plan));

        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.IsHidden)
            {
                continue;
            }

            string fallback = argument.Kind == ArgumentKind.Instance
                ? "The instance the method is called on."
                : "The <c>" + (argument.Source?.Name ?? argument.Name) + "</c> argument.";
            XmlDocWriter.WriteParam(
                writer,
                DocName(argument.Name),
                argument.Doc ?? argument.Source?.Doc,
                fallback,
                ParamNote(argument));
        }

        if (!plan.Return.IsVoid)
        {
            XmlDocWriter.WriteReturns(
                writer,
                plan.Return.Doc,
                "The result of <c>" + cType + "</c>.",
                AdoptsWrapper(plan.Return) ? AdoptedWrapperNote : GValueReturnNote(plan.Return));
        }

        WriteConsumptionExceptions(writer, plan);
        WriteGValueExceptions(writer, plan);

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
    /// Returns the generator authored note of one parameter, or
    /// <see langword="null"/> when the parameter needs none.
    /// </summary>
    /// <param name="argument">The parameter being documented.</param>
    /// <returns>The note lines.</returns>
    private static IReadOnlyList<string>? ParamNote(ArgumentPlan argument) => argument.Kind switch
    {
        ArgumentKind.ConsumedHandle => ConsumptionParamNote(argument),
        ArgumentKind.GValue => GValueParamNote(argument),
        _ => null,
    };

    /// <summary>
    /// Returns the note of a <c>GValue</c> parameter, which states the
    /// ownership and initialization contract of its shape: the gir describes
    /// the C pointer and says none of it.
    /// </summary>
    /// <param name="argument">The value argument.</param>
    /// <returns>The note lines.</returns>
    private static IReadOnlyList<string>? GValueParamNote(ArgumentPlan argument) => argument.Direction switch
    {
        ArgumentDirection.In =>
        [
            "The callee copies what it keeps, so the caller keeps ownership of",
            "<paramref name=\"" + DocName(argument.Name) + "\"/> and still disposes it.",
        ],
        ArgumentDirection.Ref =>
        [
            "The value has to be initialized with the type the call expects before",
            "the call; like the C API, the call raises a warning and does nothing",
            "otherwise.",
        ],
        ArgumentDirection.Out =>
        [
            "On success the caller owns the contents and disposes the value; on",
            "failure it is left empty, and disposing an empty value does nothing.",
        ],
        _ => null,
    };

    /// <summary>
    /// Returns the note of a returned <c>GValue</c>, or <see langword="null"/>
    /// for every other return. The value is the caller's own in both transfer
    /// shapes; what differs is how it got there.
    /// </summary>
    /// <param name="value">The return value.</param>
    /// <returns>The note lines.</returns>
    private static IReadOnlyList<string>? GValueReturnNote(ReturnPlan value)
    {
        if (value.Kind != ArgumentKind.GValue)
        {
            return null;
        }

        return value.Transfer == GirTransfer.Full
            ?
            [
                "Ownership is transferred: dispose the value. It is empty when the call",
                "returns <c>NULL</c>, and disposing an empty value does nothing.",
            ]
            :
            [
                "The value is a copy the caller owns: dispose it. It is empty when the",
                "source has no value to hand out.",
            ];
    }

    /// <summary>
    /// Writes the <see cref="ArgumentException"/> documentation of every
    /// <c>GValue</c> parameter that carries the empty guard, which is the
    /// read-only <c>in</c> shape alone.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The member being documented.</param>
    private static void WriteGValueExceptions(CodeWriter writer, MarshalPlan plan)
    {
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind != ArgumentKind.GValue
                || argument.Direction != ArgumentDirection.In
                || argument.IsHidden)
            {
                continue;
            }

            writer.WriteLine("/// <exception cref=\"ArgumentException\">");
            writer.WriteLine("/// <paramref name=\"" + DocName(argument.Name) + "\"/> is empty.");
            writer.WriteLine("/// </exception>");
        }
    }

    /// <summary>
    /// The entry points that require a writable caps or structure and, like
    /// the C API they bind, warn and write nothing on a frozen one. The C
    /// assert is not turned into a generated guard — the shipped
    /// <c>gst_caps_append_structure</c> carries the same assert and no guard —
    /// so parity with C is stated in the documentation instead. The value is
    /// the first sentence of the note, because the subject of the two differs
    /// in number.
    /// </summary>
    private static readonly Dictionary<string, string> WritableTargets = new(StringComparer.Ordinal)
    {
        ["gst_caps_set_value"] = "The caps have to be writable.",
        ["gst_caps_id_str_set_value"] = "The caps have to be writable.",
        ["gst_structure_id_set_value"] = "The structure has to be writable.",
        ["gst_structure_id_str_set_value"] = "The structure has to be writable.",
        ["gst_structure_set_array"] = "The structure has to be writable.",
        ["gst_structure_set_list"] = "The structure has to be writable.",
    };

    /// <summary>
    /// Returns every generator authored remarks paragraph of a member: the
    /// consumption contract of its consumed arguments and the writability
    /// requirement of the entry points that have one.
    /// </summary>
    /// <param name="plan">The member being documented.</param>
    /// <returns>The paragraphs, or <see langword="null"/> when there are none.</returns>
    private static IReadOnlyList<string>? GeneratedRemarks(MarshalPlan plan)
    {
        List<string> lines = [];
        if (ConsumptionRemarks(plan) is { } consumption)
        {
            lines.AddRange(consumption);
        }

        if (WritableTargets.TryGetValue(plan.EntryPoint, out string? sentence))
        {
            lines.Add("<para>");
            lines.Add(sentence + " Like the C API, the call raises a warning");
            lines.Add("and writes nothing otherwise.");
            lines.Add("</para>");
        }

        return lines.Count == 0 ? null : lines;
    }

    /// <summary>
    /// Returns the remarks paragraphs of a member with a consumed argument, or
    /// <see langword="null"/> when it has none.
    /// </summary>
    /// <param name="plan">The member being documented.</param>
    /// <returns>The paragraphs, one per consumed argument.</returns>
    /// <remarks>
    /// The wording is the contract of the hand written consuming members: the
    /// call is handed a value of its own — a reference for a mini object or a
    /// GObject, a copy for a boxed value — and the wrapper is disposed
    /// afterwards. A consumed GObject carries the extra sentence about the
    /// reach of its dispose, because a GObject wrapper is interned and giving
    /// it up is a statement about the whole process.
    /// </remarks>
    private static IReadOnlyList<string>? ConsumptionRemarks(MarshalPlan plan)
    {
        List<string> lines = [];
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind != ArgumentKind.ConsumedHandle)
            {
                continue;
            }

            string cName = argument.Source?.Name ?? DocName(argument.Name);
            lines.Add("<para>");
            lines.Add("The <c>" + cName + "</c> parameter is <c>transfer-ownership=\"full\"</c>: the call is");
            switch (argument.ConsumedFamily)
            {
                case ConsumedFamily.Boxed:
                    lines.Add("handed a copy of the value and the wrapper is disposed afterwards, which");
                    lines.Add("leaves the caller with exactly what the C call leaves it with. A boxed");
                    lines.Add("value has no reference count to raise, so the copy is what a reference is");
                    lines.Add("there. <see cref=\"Gst.GObject.Boxed.Dispose()\"/> is idempotent, so a");
                    lines.Add("<c>using</c> declaration around the argument stays correct.");
                    break;

                case ConsumedFamily.GObject:
                    lines.Add("handed a reference of its own and the wrapper is disposed afterwards, which");
                    lines.Add("leaves the native reference count exactly where the C call leaves it. A");
                    lines.Add("GObject wrapper is interned, so disposing it gives the object up for the");
                    lines.Add("whole process rather than for one holder: after this call there is no");
                    lines.Add("wrapper for that object anywhere.");
                    lines.Add("<see cref=\"Gst.GObject.Object.Dispose()\"/> is idempotent, so a <c>using</c>");
                    lines.Add("declaration around the argument stays correct.");
                    break;

                default:
                    lines.Add("handed a reference of its own and the wrapper is disposed afterwards, which");
                    lines.Add("leaves the native reference count exactly where the C call leaves it.");
                    lines.Add("<see cref=\"Gst.MiniObject.Dispose()\"/> is idempotent, so a <c>using</c>");
                    lines.Add("declaration around the argument stays correct.");
                    break;
            }

            lines.Add("</para>");
        }

        return lines.Count == 0 ? null : lines;
    }

    /// <summary>
    /// Returns the note of a consumed parameter, which states the consumption
    /// in the words of the hand written members.
    /// </summary>
    /// <param name="argument">The consumed argument.</param>
    /// <returns>The note lines.</returns>
    private static IReadOnlyList<string> ConsumptionParamNote(ArgumentPlan argument)
    {
        List<string> note =
        [
            "The call consumes it: <paramref name=\"" + DocName(argument.Name) + "\"/> is disposed when this",
            "method returns, and using it afterwards throws <see cref=\"ObjectDisposedException\"/>.",
        ];

        if (argument.IsNullable)
        {
            note.Add("It may be <see langword=\"null\"/>, which is the absence of a payload and leaves");
            note.Add("nothing to consume.");
        }

        return note;
    }

    /// <summary>
    /// Writes the exception documentation of a member with a consumed argument:
    /// the <see cref="ArgumentNullException"/> of every non nullable one and
    /// one <see cref="ObjectDisposedException"/> entry that names the wrapper
    /// and every consumed argument, the way the hand written members word it.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The member being documented.</param>
    private static void WriteConsumptionExceptions(CodeWriter writer, MarshalPlan plan)
    {
        List<ArgumentPlan> consumed = [];
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind == ArgumentKind.ConsumedHandle)
            {
                consumed.Add(argument);
            }
        }

        if (consumed.Count == 0)
        {
            return;
        }

        foreach (ArgumentPlan argument in consumed)
        {
            if (argument.IsNullable)
            {
                continue;
            }

            writer.WriteLine("/// <exception cref=\"ArgumentNullException\">");
            writer.WriteLine("/// <paramref name=\"" + DocName(argument.Name) + "\"/> is <see langword=\"null\"/>.");
            writer.WriteLine("/// </exception>");
        }

        List<string> names = [];
        foreach (ArgumentPlan argument in consumed)
        {
            names.Add("<paramref name=\"" + DocName(argument.Name) + "\"/>");
        }

        string? instance = null;
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind == ArgumentKind.Instance)
            {
                instance = plan.Form == CallableForm.ExtensionMethod
                    ? "<paramref name=\"" + DocName(argument.Name) + "\"/>"
                    : "This wrapper";
            }
        }

        string subject = instance is null
            ? string.Join(" or ", names)
            : instance + " or " + string.Join(" or ", names);

        writer.WriteLine("/// <exception cref=\"ObjectDisposedException\">");
        writer.WriteLine("/// " + subject + " was disposed.");
        writer.WriteLine("/// </exception>");
    }

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
        if (MaterializesArguments(plan))
        {
            // Three strict phases: every guard, then every handle read, then
            // every materialization. A guard that throws must find nothing
            // allocated yet, and a disposed wrapper must throw from its handle
            // read before the first allocation, so that no exit strands what a
            // materializing argument allocated.
            foreach (ArgumentPlan argument in plan.Arguments)
            {
                WriteGuard(writer, plan, argument);
            }

            foreach (ArgumentPlan argument in plan.Arguments)
            {
                WriteHandleLocal(writer, plan, argument);
            }

            foreach (ArgumentPlan argument in plan.Arguments)
            {
                WritePrologue(writer, plan, argument);
            }
        }
        else
        {
            foreach (ArgumentPlan argument in plan.Arguments)
            {
                WriteGuard(writer, plan, argument);
                WritePrologue(writer, plan, argument);
            }
        }

        // A span has to be pinned and a callback that only lives for the
        // duration of the call has to be released again, so both wrap the call
        // in a block; they are closed in reverse order further down. A GValue
        // is pinned the same way: the call is handed the address of the layout
        // field inside the caller's value, and an `in` or `ref` argument may
        // refer into the heap, so the address only holds while a fixed scope
        // does. The import takes a typed pointer rather than a `ref`, because
        // the interop generator refuses a by-ref struct from a referenced
        // assembly (SYSLIB1051).
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
            else if (argument.Kind == ArgumentKind.GValue)
            {
                string storage = argument.Direction == ArgumentDirection.In
                    ? "System.Runtime.CompilerServices.Unsafe.AsRef(in " + argument.Name + ").NativeValue"
                    : argument.Name + ".NativeValue";
                writer.WriteLine(
                    "fixed (" + argument.RawType + " " + argument.Name + "Pointer = &" + storage + ")");
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
        WriteConsumedDisposes(writer, plan);

        if (plan.Throws)
        {
            WriteFailedResultRelease(writer, plan);
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
    /// Releases a return value the call transferred to the caller on its way to
    /// failing, before the error it reported is raised.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The member being written.</param>
    /// <remarks>
    /// <para>
    /// A function that takes a <c>GError**</c> may set the error and still
    /// return something. <c>gst_discoverer_discover_uri</c> is the one that
    /// matters: it fills the error whenever the run saw an error message on the
    /// bus and hands the information object over all the same. The member
    /// raises the error before it wraps the return value, because it has no way
    /// to give the caller both, so a transferred result would be dropped on the
    /// floor with nothing left holding it. Releasing it here is what keeps a
    /// failed call from leaking; whether the caller could reach it instead is a
    /// separate question that this does not answer.
    /// </para>
    /// <para>
    /// Only a transferred return is released. A borrowed one belongs to
    /// whatever produced it, and a value that is not a pointer is not an
    /// allocation. The release of a GObject is the raw unref rather than a
    /// wrapper that is built and disposed: GObject wrappers are interned, so
    /// building one for a handle that already has a live wrapper and disposing
    /// that would end the wrapper the rest of the process is holding, while
    /// dropping the reference the call transferred is the whole of what is
    /// owed. Mini objects and boxed values are not interned, so for those the
    /// wrapper <em>is</em> the release: it adopts the reference or takes the
    /// copy and hands it straight back.
    /// </para>
    /// <para>
    /// The three kinds below are the ones the bound surface has. No throwing
    /// callable returns an owned opaque record, string vector, list or array,
    /// so nothing is emitted for those and a new one would leak the way this
    /// fixes — the shape is pinned by a test for that reason.
    /// </para>
    /// </remarks>
    private static void WriteFailedResultRelease(CodeWriter writer, MarshalPlan plan)
    {
        ReturnPlan value = plan.Return;
        if (value.IsVoid || value.Transfer is not (GirTransfer.Full or GirTransfer.Floating))
        {
            return;
        }

        string? release = value.Kind switch
        {
            ArgumentKind.Handle when value.Flavor == HandleFlavor.GObject =>
                "Gst.Interop.GObjectNative.ObjectUnref(" + ResultLocal + ");",
            ArgumentKind.Handle when value.Flavor == HandleFlavor.Wrapper =>
                TrimNullable(value.PublicType) + ".FromNative(" + ResultLocal
                    + ", Gst.Interop.Transfer.Full)?.Dispose();",
            ArgumentKind.Utf8 => "Gst.Interop.GMarshal.Free(" + ResultLocal + ");",
            _ => null,
        };

        if (release is null)
        {
            return;
        }

        writer.WriteLine("if (errorNative != 0 && " + ResultLocal + " != 0)");
        writer.OpenBlock();
        writer.WriteLine("// The call failed and transferred a value all the same. The throw");
        writer.WriteLine("// below puts it out of reach, so it is released rather than leaked.");
        writer.WriteLine(release);
        writer.CloseBlock();
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
    /// nullable argument needs no guard of its own. A consumed argument gets no
    /// barrier: its <c>Dispose</c> right after the barriers is its last use and
    /// keeps it alive across the call on its own.
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
    /// Disposes every consumed argument, right after the barriers of the call.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The member being written.</param>
    /// <remarks>
    /// The wrapper's own reference goes away with the wrapper, which is what
    /// makes the call consuming rather than borrowing: the callee owns the
    /// minted value, the wrapper owns nothing, and the native side is left
    /// exactly where the C call leaves it. The dispose is unconditional — a
    /// call that answered false has still consumed what it was handed, and the
    /// C function offers no way back — so it sits before everything that can
    /// throw on the way out: before the <c>GException</c> of a throwing member
    /// and before the wrap of an owned return, whose failure throw must find
    /// the argument already consumed. A nullable argument that was null minted
    /// nothing, and the conditional dispose leaves it alone.
    /// </remarks>
    private static void WriteConsumedDisposes(CodeWriter writer, MarshalPlan plan)
    {
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind == ArgumentKind.ConsumedHandle)
            {
                writer.WriteLine(argument.Name + (argument.IsNullable ? "?.Dispose();" : ".Dispose();"));
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
    /// binding follow. The local is emitted only for those members and for the
    /// materializing members of <see cref="MaterializesArguments"/>, because
    /// hoisting it everywhere would rewrite every generated body for a
    /// guarantee that no other member needs.
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
    /// Tests whether a member materializes one of its arguments: an allocation
    /// made for the call that no scope reclaims. The UTF-8 copy of a string the
    /// callee takes ownership of is one, and so is the value minted for a
    /// consuming argument — the reference or the copy the callee takes over.
    /// </summary>
    /// <param name="plan">The member being written.</param>
    /// <returns><see langword="true"/> when one of the arguments materializes.</returns>
    /// <remarks>
    /// Such a member orders its prologue in three strict phases — every guard,
    /// every handle read, every materialization — so that nothing that can
    /// throw runs after the allocation, which nothing but the call itself
    /// releases. Every other member keeps the plain one pass prologue: the
    /// phases guarantee nothing there, and applying them everywhere would
    /// rewrite every generated body.
    /// </remarks>
    private static bool MaterializesArguments(MarshalPlan plan)
    {
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind is ArgumentKind.Utf8Owned or ArgumentKind.ConsumedHandle)
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

    /// <summary>
    /// Writes the validation guards of one argument, which every prologue puts
    /// before anything is allocated.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The member being written.</param>
    /// <param name="argument">The argument to guard.</param>
    private static void WriteGuard(CodeWriter writer, MarshalPlan plan, ArgumentPlan argument)
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

            case ArgumentKind.Utf8 when argument.Direction == ArgumentDirection.In:
            case ArgumentKind.Utf8Owned:
            case ArgumentKind.Handle when argument.Direction == ArgumentDirection.In:
            case ArgumentKind.ConsumedHandle:
                if (!argument.IsNullable)
                {
                    writer.WriteLine("ArgumentNullException.ThrowIfNull(" + name + ");");
                }

                return;

            case ArgumentKind.Callback:
                writer.WriteLine("ArgumentNullException.ThrowIfNull(" + name + ");");
                return;

            // Only the read-only shape is guarded: an empty value has no type
            // for the callee to read, and the C side answers it with a
            // g_critical and a silent no-op. A ref value is storage the callee
            // writes under a contract of its own — which states are valid is
            // the callee's to say — and an out value starts empty by design.
            case ArgumentKind.GValue when argument.Direction == ArgumentDirection.In:
                writer.WriteLine("if (" + name + ".IsEmpty)");
                writer.OpenBlock();
                writer.WriteLine("throw new ArgumentException(");
                writer.WriteLine("    \"An empty value cannot be passed: it has no type for the call to read.\",");
                writer.WriteLine("    nameof(" + name + "));");
                writer.CloseBlock();
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Writes the raw handle of the instance and of a handle argument into a
    /// local, the second phase of a materializing prologue.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The member being written.</param>
    /// <param name="argument">The argument whose handle is read.</param>
    /// <remarks>
    /// Reading <c>Handle</c> throws <see cref="ObjectDisposedException"/> on a
    /// disposed wrapper, so every read happens before the first allocation and
    /// a disposed wrapper throws without stranding what another argument would
    /// have allocated. The local carries the very read the call site emits for
    /// every other member, null-to-zero conversion included. A consumed boxed
    /// argument also reads its boxed type here, because the copy of phase three
    /// is dispatched through it and phase three allocates.
    /// </remarks>
    private static void WriteHandleLocal(CodeWriter writer, MarshalPlan plan, ArgumentPlan argument)
    {
        switch (argument.Kind)
        {
            case ArgumentKind.Instance:
                writer.WriteLine("nint " + InstanceLocal + " = " + InstanceHandle(plan, argument.Name) + ";");
                return;

            case ArgumentKind.Handle when argument.Direction == ArgumentDirection.In:
                writer.WriteLine("nint " + argument.Name + "Native = " + HandleRead(argument) + ";");
                return;

            case ArgumentKind.ConsumedHandle:
                writer.WriteLine("nint " + argument.Name + "Native = " + HandleRead(argument) + ";");
                if (argument.ConsumedFamily == ConsumedFamily.Boxed)
                {
                    writer.WriteLine(
                        "nuint " + argument.Name + "Type = "
                        + (argument.IsNullable
                            ? argument.Name + " is null ? 0 : " + argument.Name + ".BoxedType.Value"
                            : argument.Name + ".BoxedType.Value")
                        + ";");
                }

                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Returns the expression that reads the raw handle out of a handle
    /// argument, turning null into zero when the argument is nullable.
    /// </summary>
    /// <param name="argument">The argument to read.</param>
    /// <returns>The expression to read.</returns>
    private static string HandleRead(ArgumentPlan argument) =>
        argument.IsNullable
            ? argument.Name + " is null ? 0 : " + argument.Name + ".Handle"
            : argument.Name + ".Handle";

    /// <summary>
    /// Returns the expression that mints the value a consuming call takes over:
    /// a reference for a mini object or a GObject, a copy for a boxed value.
    /// </summary>
    /// <param name="argument">The consumed argument.</param>
    /// <returns>The expression to mint.</returns>
    /// <remarks>
    /// The call is never handed the wrapper's own reference, because the
    /// wrapper and the callee would both release it. A consumed argument that
    /// is nullable and null is the absence of a payload: nothing is minted and
    /// zero is passed. The mint reads the locals of the second phase only, so
    /// nothing here can throw after the allocation.
    /// </remarks>
    private static string Minted(ArgumentPlan argument)
    {
        string name = argument.Name;
        string mint = argument.ConsumedFamily switch
        {
            ConsumedFamily.MiniObject => "Gst.GstNative.MiniObjectRef(" + name + "Native)",
            ConsumedFamily.Boxed => "Gst.Interop.GObjectNative.BoxedCopy(" + name + "Type, " + name + "Native)",
            _ => "Gst.Interop.GObjectNative.ObjectRef(" + name + "Native)",
        };

        return argument.IsNullable ? name + " is null ? 0 : " + mint : mint;
    }

    private static void WritePrologue(CodeWriter writer, MarshalPlan plan, ArgumentPlan argument)
    {
        string name = argument.Name;
        switch (argument.Kind)
        {
            case ArgumentKind.Instance:
                if (TakesCallback(plan) && !MaterializesArguments(plan))
                {
                    writer.WriteLine("nint " + InstanceLocal + " = " + InstanceHandle(plan, name) + ";");
                }

                return;

            case ArgumentKind.Error:
                writer.WriteLine("nint errorNative = 0;");
                return;

            case ArgumentKind.Utf8 when argument.Direction == ArgumentDirection.In:
                writer.WriteLine(
                    "System.Span<byte> " + name + "Buffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];");
                writer.WriteLine(
                    "using Gst.Interop.Utf8Scope " + name + "Scope = Gst.Interop.GMarshal.StackUtf8("
                    + name + ", " + name + "Buffer);");
                return;

            case ArgumentKind.Utf8Owned:
                writer.WriteLine("nint " + name + "Native = Gst.Interop.GMarshal.StringToUtf8Ptr(" + name + ");");
                return;

            case ArgumentKind.ConsumedHandle:
                writer.WriteLine("nint " + name + "Owned = " + Minted(argument) + ";");
                return;

            case ArgumentKind.Callback:
                writer.WriteLine(
                    "Gst.Interop.CallbackHandle " + name + "State = Gst.Interop.CallbackHandle.Alloc(" + name + ");");
                return;

            case ArgumentKind.Span:
            case ArgumentKind.UserData:
            case ArgumentKind.DestroyNotify:
                return;

            case ArgumentKind.GValue:
                // The call points at the caller's own storage, so nothing is
                // allocated here. An out value starts zeroed, which is the
                // uninitialized state the callee's g_value_init expects to
                // find; a pre-initialized destination would be a g_critical.
                if (argument.Direction == ArgumentDirection.Out)
                {
                    writer.WriteLine(name + " = default;");
                }

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
                return TakesCallback(plan) || MaterializesArguments(plan)
                    ? InstanceLocal
                    : InstanceHandle(plan, name);

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
                return MaterializesArguments(plan) ? name + "Native" : HandleRead(argument);

            case ArgumentKind.ConsumedHandle:
                return name + "Owned";

            case ArgumentKind.GValue:
                // The pinned address of the layout field inside the caller's
                // value, taken by the fixed scope that wraps the call.
                return name + "Pointer";

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

        // Only the return position reaches this for a GValue; an argument is
        // read or written in place. A borrowed return is copied and an owned
        // one is adopted — contents moved, shell freed — and NULL is the
        // empty value either way.
        ArgumentKind.GValue => argument.Transfer == GirTransfer.Full
            ? "Gst.GObject.Value.TakeOwnership(" + source + ")"
            : "Gst.GObject.Value.CopyFrom(" + source + ")",
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

        // A GValue was read or written in place through the caller's own
        // storage; there is no local to copy back and nothing to release.
        if (argument.Kind == ArgumentKind.GValue)
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
