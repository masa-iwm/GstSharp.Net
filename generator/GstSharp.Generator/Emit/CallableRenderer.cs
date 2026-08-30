using System.Globalization;
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

    /// <summary>The local that holds the reference minted for a call that takes the instance over.</summary>
    private const string InstanceOwnedLocal = "instanceOwned";

    /// <summary>The pinned pointer to the instance of a value projected structure.</summary>
    private const string ValueInstanceLocal = "self";

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

    /// <summary>
    /// What the documentation of an adopt in place member says about the
    /// wrapper it answers. The gir describes the C function, which hands back
    /// a pointer that may be a different object; the binding hands back the
    /// wrapper, which is the same one either way.
    /// </summary>
    private static readonly string[] AdoptedInPlaceNote =
    [
        "This wrapper. The call may have replaced the object behind it and the",
        "wrapper now owns the writable one, so the return value exists to let the",
        "call be chained and is never a second wrapper.",
    ];

    /// <summary>
    /// The remarks of an adopt in place member: what the call does to the
    /// wrapper, and the rule that makes it correct. None of it is in the gir,
    /// which describes a C function whose caller holds a bare pointer.
    /// </summary>
    private static readonly string[] AdoptedInPlaceRemarks =
    [
        "<para>",
        "The call consumes the reference of this wrapper and answers one that is",
        "either the same object, when nobody else held it, or a writable copy of it.",
        "The wrapper adopts whatever comes back, so the object it stands for can",
        "change identity across the call and <b>any handle read before the call is",
        "stale</b>.",
        "</para>",
        "<para>",
        "This is single owner surgery: it is only correct while no other wrapper and",
        "no other thread uses this one, which is the rule the C API imposes as well.",
        "</para>",
    ];

    /// <summary>
    /// The sentence that closes the remarks of an adopt in place member on a
    /// mini object, which is the only wrapper that can be a borrow: a boxed
    /// wrapper always owns the value it holds, so it can never refuse for this
    /// reason.
    /// </summary>
    private static readonly string[] BorrowedInstanceNote =
    [
        "<para>",
        "A wrapper that borrows the object for the length of one call has no",
        "reference to give and refuses instead; an object an in place vfunc receives",
        "is writable already.",
        "</para>",
    ];

    /// <summary>
    /// The remarks of a mint and adopt member. The one thing that is not in
    /// the signature is that the two wrappers may stand for the same object,
    /// which is what the C functions answer when they had nothing to change.
    /// </summary>
    private static readonly string[] MintedInstanceRemarks =
    [
        "<para>",
        "This wrapper is left alone: the call is handed a reference minted for it, so",
        "the object this wrapper stands for keeps the reference it owns and both",
        "wrappers are disposed by whoever holds them.",
        "</para>",
        "<para>",
        "The returned wrapper may refer to the same native object as this one when the",
        "call did not need to change it; it is then shared and not writable.",
        "</para>",
    ];

    /// <summary>
    /// The note of a destination the overlays moved off the out position. The
    /// gir calls the parameter storage the callee fills, and the C function
    /// really reads and updates what is already in it, so an instance that was
    /// never initialised is the one way of calling the member that does not
    /// work.
    /// </summary>
    private static readonly string[] RedirectedDestinationNote =
    [
        "Must be an initialised instance; the call updates it in place.",
    ];

    /// <summary>
    /// What the documentation of a callback the library never releases says.
    /// The gir has no annotation for it, and a caller that installs one per
    /// buffer has written a leak that nothing else reports.
    /// </summary>
    private static readonly string[] ForeverCallbackNote =
    [
        "The binding keeps the state of this callback alive for the life of the",
        "process: the library stores the function pointer and calls it from a",
        "streaming thread, and it offers no destroy notification to release the",
        "state again. One handle is leaked per call — install the callback once,",
        "at construction.",
    ];

    /// <summary>
    /// The remarks paragraph of a member that installs a callback of the
    /// forever scope, which states what replacing it costs.
    /// </summary>
    private static readonly string[] ForeverCallbackRemarks =
    [
        "<para>",
        "The callback is installed for the lifetime of the object. Replacing it",
        "does not release the state of the previous one, so a call per buffer or",
        "per state change leaks.",
        "</para>",
    ];

    /// <summary>
    /// The note of a list the call only reads. It states what happens to the
    /// temporary allocation and what an empty sequence means, because neither
    /// is visible in the signature.
    /// </summary>
    private static readonly string[] BorrowedListNote =
    [
        "The call reads the list while it runs and copies whatever it keeps. A",
        "temporary native list is built for the call and released when it returns,",
        "and an empty sequence is passed as the null pointer, which is how C spells",
        "the empty list.",
    ];

    /// <summary>
    /// The note of a list the call takes over. What the caller keeps is the one
    /// thing that is easy to get wrong, so it is the sentence the note ends on.
    /// </summary>
    private static readonly string[] ConsumedListNote =
    [
        "The call takes the list over. The binding hands it a native list of its own",
        "and one reference per element, and releases neither afterwards - the callee",
        "owns both from the moment the call is made, including when it answers false.",
        "Your own objects keep their references and stay usable.",
    ];

    /// <summary>
    /// What the documentation of a <c>ToString</c> that the C side may answer
    /// <c>NULL</c> to says, because the member hands out the empty string
    /// rather than a null reference.
    /// </summary>
    private static readonly string[] EmptyStringNote =
    [
        "Where the C documentation says %NULL, this member answers the empty",
        "string instead, which is what the default value of this structure",
        "describes.",
    ];

    /// <summary>
    /// What the documentation of a <c>GError</c> the call only borrows says:
    /// the exception value never crosses, a temporary built from it does, and
    /// the value it is built from has to carry an error domain.
    /// </summary>
    private static readonly string[] GErrorParamNote =
    [
        "The call is handed a temporary native error built from this value and",
        "releases it again when the call returns. The library copies whatever it",
        "keeps, so the exception object itself is never retained. It needs a",
        "registered error domain: an exception created without one — every",
        "constructor but <c>GException(Quark, int, string)</c> — is rejected.",
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
        writer.WriteLine(
            modifiers + ReadOnlyModifier(plan) + ReturnType(plan) + " " + plan.Name
            + "(" + Parameters(plan) + ")");
        writer.OpenBlock();
        WriteBody(writer, plan);
        writer.CloseBlock();
    }

    /// <summary>
    /// Returns the <c>readonly</c> modifier of a method of a value projected
    /// structure whose instance the gir spells <c>const</c>, and nothing for
    /// every other member.
    /// </summary>
    /// <param name="plan">The member being written.</param>
    /// <returns>The modifier, with its trailing space, or the empty string.</returns>
    /// <remarks>
    /// A <c>readonly</c> member states that the call does not write through
    /// the instance, which is what lets a caller invoke it on a readonly field
    /// without the compiler making a defensive copy first. The gir carries the
    /// fact in the <c>c:type</c> of the instance parameter, so the const-ness
    /// of the C signature is what decides it; a non-const instance stays
    /// writable even when the implementation happens not to write.
    /// </remarks>
    private static string ReadOnlyModifier(MarshalPlan plan) =>
        HasConstValueInstance(plan) ? "readonly " : string.Empty;

    /// <summary>
    /// Tests whether a member is called on a value projected structure the C
    /// function only reads.
    /// </summary>
    /// <param name="plan">The member being written.</param>
    /// <returns><see langword="true"/> when the instance is a <c>const</c> pointer.</returns>
    private static bool HasConstValueInstance(MarshalPlan plan)
    {
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind == ArgumentKind.ValueInstance)
            {
                return IsConstInstance(plan);
            }
        }

        return false;
    }

    /// <summary>Tests whether the gir spells the instance parameter <c>const</c>.</summary>
    /// <param name="plan">The member being written.</param>
    /// <returns><see langword="true"/> when the C type leads with <c>const</c>.</returns>
    private static bool IsConstInstance(MarshalPlan plan) =>
        plan.Callable.InstanceParameter?.Type.CType?.StartsWith("const ", StringComparison.Ordinal) ?? false;

    /// <summary>
    /// Returns the declared return type of a member, which is the planned one
    /// except for two shapes that never hand out <see langword="null"/>. The
    /// <c>ToString</c> of a structure that answers <see langword="null"/> hands
    /// out the empty string instead, and an adopt in place member answers the
    /// wrapper it was called on, which exists by construction.
    /// </summary>
    /// <param name="plan">The member being written.</param>
    /// <returns>The C# type of the return value.</returns>
    private static string ReturnType(MarshalPlan plan) =>
        plan.ReturnsEmptyOnNull || plan.InstanceConsumption == InstanceConsumption.InPlace
            ? TrimNullable(plan.Return.PublicType)
            : plan.Return.PublicType;

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

    /// <summary>
    /// Writes the <c>LibraryImport</c> of the constructor a caller allocated
    /// out parameter takes its storage from.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="factory">The constructor to import.</param>
    internal static void WriteStorageFactoryImport(CodeWriter writer, BoxedStorageFactory factory)
    {
        writer.WriteLine(
            "/// <summary>The <c>" + factory.EntryPoint
            + "</c> entry point, which allocates the storage of a caller allocated out parameter.</summary>");
        writer.WriteLine("/// <returns>A new, zeroed instance the caller owns.</returns>");
        writer.WriteLine(
            "[LibraryImport(\"" + factory.Library + "\", EntryPoint = \"" + factory.EntryPoint + "\")]");
        writer.WriteLine("private static partial nint " + factory.NativeName + "();");
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

    /// <summary>
    /// Returns the visible parameters of a member, in the order the public
    /// signature spells them.
    /// </summary>
    /// <param name="plan">The member being written.</param>
    /// <returns>The visible arguments.</returns>
    /// <remarks>
    /// <para>
    /// The order is the gir order for all but two shapes, and the native call,
    /// the prologue and the import keep the gir order in every case: this is
    /// the public signature alone.
    /// </para>
    /// <para>
    /// A caller allocated boxed out is storage the binding provides rather than
    /// an input, so it trails the arguments the caller chooses, which is where
    /// .NET puts an out parameter. A record the overlays redirected off the out
    /// position is the destination the C function works on, so it leads them,
    /// which is where the instance of an ordinary method sits — the gir spells
    /// <c>gst_sdp_media_set_media_from_caps</c> with its media last only
    /// because it calls it an out.
    /// </para>
    /// </remarks>
    private static List<ArgumentPlan> VisibleParameters(MarshalPlan plan)
    {
        List<ArgumentPlan> leading = [];
        List<ArgumentPlan> middle = [];
        List<ArgumentPlan> trailing = [];
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.IsHidden)
            {
                continue;
            }

            if (argument.Kind == ArgumentKind.Instance)
            {
                leading.Add(argument);
            }
            else if (argument.IsRedirectedDestination)
            {
                leading.Add(argument);
            }
            else if (argument.Kind == ArgumentKind.CallerAllocatedBoxed)
            {
                trailing.Add(argument);
            }
            else
            {
                middle.Add(argument);
            }
        }

        leading.AddRange(middle);
        leading.AddRange(trailing);
        return leading;
    }

    private static string Parameters(MarshalPlan plan)
    {
        List<string> parameters = [];
        foreach (ArgumentPlan argument in VisibleParameters(plan))
        {
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

        foreach (ArgumentPlan argument in VisibleParameters(plan))
        {
            string fallback = argument.Kind == ArgumentKind.Instance
                ? "The instance the method is called on."
                : "The <c>" + (argument.Source?.Name ?? argument.Name) + "</c> argument.";
            XmlDocWriter.WriteParam(
                writer,
                DocName(argument.Name),
                argument.Doc ?? argument.Source?.Doc,
                fallback,
                ParamNote(plan, argument));
        }

        if (!plan.Return.IsVoid)
        {
            // The note of an adopt in place member replaces the gir text
            // rather than following it. The gir describes the pointer the C
            // function answers, which may be a different object; the member
            // answers this wrapper. Written one after the other the two read as
            // a contradiction, so only the one that describes the member is
            // kept.
            bool adoptsInPlace = plan.InstanceConsumption == InstanceConsumption.InPlace;
            IReadOnlyList<string>? returnNote = plan.ReturnsEmptyOnNull
                ? EmptyStringNote
                : adoptsInPlace
                    ? null
                    : AdoptsWrapper(plan.Return) ? AdoptedWrapperNote : GValueReturnNote(plan.Return);
            XmlDocWriter.WriteReturns(
                writer,
                adoptsInPlace ? string.Join('\n', AdoptedInPlaceNote) : plan.Return.Doc,
                "The result of <c>" + cType + "</c>.",
                returnNote);
        }

        WriteConsumptionExceptions(writer, plan);
        WriteInPlaceExceptions(writer, plan);
        WriteGValueExceptions(writer, plan);
        WriteSpanExceptions(writer, plan);
        WriteGErrorExceptions(writer, plan);

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
    /// <param name="plan">The member being documented.</param>
    /// <param name="argument">The parameter being documented.</param>
    /// <returns>The note lines.</returns>
    private static IReadOnlyList<string>? ParamNote(MarshalPlan plan, ArgumentPlan argument)
    {
        // A destination the overlays moved off the out position reads as an
        // ordinary argument on the surface, and the one thing that makes it
        // different is the one thing a caller has to know.
        if (argument.IsRedirectedDestination)
        {
            return RedirectedDestinationNote;
        }

        return argument.Kind switch
        {
            ArgumentKind.ConsumedHandle => ConsumptionParamNote(argument),
            ArgumentKind.GValue => GValueParamNote(plan, argument),
            ArgumentKind.GError when argument.Direction == ArgumentDirection.In => GErrorParamNote,
            ArgumentKind.CallerAllocatedBoxed => CallerAllocatedParamNote(argument),
            ArgumentKind.Span => SpanParamNote(plan, argument),
            ArgumentKind.Callback when argument.Scope == GirScope.Forever => ForeverCallbackNote,
            ArgumentKind.ListIn => argument.Transfer == GirTransfer.Full
                ? ConsumedListNote
                : BorrowedListNote,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the note of a span whose length the member hands to the C
    /// function under a name of its own, or <see langword="null"/> when the
    /// gir already says which argument that is.
    /// </summary>
    /// <param name="plan">The member being documented.</param>
    /// <param name="argument">The span being documented.</param>
    /// <returns>The note lines.</returns>
    /// <remarks>
    /// Three shapes need it. A span of a size the C declaration fixes states
    /// that size, because the guard is the only other place a caller would
    /// meet it. A length the overlays supplied is a fact about the C
    /// implementation that the gir does not carry, and a length two spans share
    /// is read off exactly one of them. In the latter two a hidden C argument
    /// is taken from this span's <see cref="System.Span{T}.Length"/>, and with
    /// nothing in the documentation saying so a caller has no way to find out
    /// which argument, or which span, decides it.
    /// </remarks>
    private static IReadOnlyList<string>? SpanParamNote(MarshalPlan plan, ArgumentPlan argument)
    {
        if (argument.FixedLength is int fixedLength)
        {
            string count = fixedLength.ToString(CultureInfo.InvariantCulture);
            return argument.IsNullable
                ?
                [
                    "The C declaration sizes this buffer at " + count + " elements; pass exactly "
                    + count + ", or an empty span for <c>NULL</c>.",
                ]
                :
                [
                    "The C declaration sizes this buffer at " + count + " elements; pass exactly "
                    + count + ".",
                ];
        }

        if (argument.LengthArgument is not int length)
        {
            return null;
        }

        ArgumentPlan lengthArgument = plan.Arguments[length];
        if (lengthArgument.OwnerArgument is not int owned
            || owned < 0
            || !ReferenceEquals(plan.Arguments[owned], argument))
        {
            return null;
        }

        if (!argument.LengthIsOverridden && !SharesLength(plan, argument, length))
        {
            return null;
        }

        return
        [
            "Its number of elements is passed to the C function as the <c>"
            + (lengthArgument.Source?.Name ?? DocName(lengthArgument.Name)) + "</c> argument.",
        ];
    }

    /// <summary>Tests whether a second span is counted by the same length argument.</summary>
    /// <param name="plan">The member being documented.</param>
    /// <param name="argument">The span that owns the length.</param>
    /// <param name="length">The index of the length argument.</param>
    /// <returns><see langword="true"/> when another span shares it.</returns>
    private static bool SharesLength(MarshalPlan plan, ArgumentPlan argument, int length)
    {
        foreach (ArgumentPlan other in plan.Arguments)
        {
            if (other.Kind == ArgumentKind.Span
                && !ReferenceEquals(other, argument)
                && other.LengthArgument == length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the note of a caller allocated boxed out parameter, which states
    /// who allocated the storage and who releases it: the gir describes the C
    /// contract, where the caller declares the structure and the binding's
    /// caller never sees one.
    /// </summary>
    /// <param name="argument">The out argument.</param>
    /// <returns>The note lines.</returns>
    private static IReadOnlyList<string> CallerAllocatedParamNote(ArgumentPlan argument) =>
        argument.IsNullable
            ?
            [
                "The binding allocates the storage; on success the caller owns",
                "<paramref name=\"" + DocName(argument.Name) + "\"/> and disposes it. On failure it is",
                "<see langword=\"null\"/>.",
            ]
            :
            [
                "The binding allocates the storage; on return the caller owns",
                "<paramref name=\"" + DocName(argument.Name) + "\"/> and disposes it.",
            ];

    /// <summary>
    /// The entry points whose writable <c>GValue</c> parameter must arrive
    /// empty rather than holding the type the call works on. They are the
    /// <c>init</c> functions of the fundamental containers, which call
    /// <c>g_value_init</c> themselves: the generic note of the <c>ref</c> shape
    /// states the opposite contract and would be wrong on them.
    /// </summary>
    private static readonly HashSet<string> ZeroedValueTargets = new(StringComparer.Ordinal)
    {
        "gst_value_array_init",
        "gst_value_list_init",
    };

    /// <summary>
    /// The entry points whose writable <c>GValue</c> parameter is handed
    /// straight to a callback and is never read or written by the call itself.
    /// <c>gst_iterator_fold</c> is the one: its <c>ret</c> is the accumulator of
    /// the fold, passed to the function as it stands (<c>gstiterator.c</c> at
    /// 1.28.6 and at 1.24.0 alike). The generic note of the <c>ref</c> shape
    /// promises a warning from the C API on an uninitialized value, and there is
    /// none — the value reaches the function, where every setter of the
    /// projection throws instead.
    /// </summary>
    private static readonly HashSet<string> ForwardedValueTargets = new(StringComparer.Ordinal)
    {
        "gst_iterator_fold",
    };

    /// <summary>
    /// Returns the note of a <c>GValue</c> parameter, which states the
    /// ownership and initialization contract of its shape: the gir describes
    /// the C pointer and says none of it.
    /// </summary>
    /// <param name="plan">The member being documented.</param>
    /// <param name="argument">The value argument.</param>
    /// <returns>The note lines.</returns>
    private static IReadOnlyList<string>? GValueParamNote(MarshalPlan plan, ArgumentPlan argument)
    {
        if (argument.Direction == ArgumentDirection.Ref && ZeroedValueTargets.Contains(plan.EntryPoint))
        {
            return
            [
                "The value has to be empty, that is of type zero: the call initializes",
                "it. Like the C API, the call raises a critical on a value that already",
                "holds a type and leaves it untouched.",
            ];
        }

        if (argument.Direction == ArgumentDirection.Ref && ForwardedValueTargets.Contains(plan.EntryPoint))
        {
            return
            [
                "The value has to be initialized with the type the function writes",
                "before the call. The call itself never reads or writes it: it hands it to",
                "the function as it stands, and a setter on an uninitialized value throws",
                "inside the function, which stops the fold as the remarks above describe.",
            ];
        }

        return NoteOf(argument);
    }

    /// <summary>The note of a <c>GValue</c> parameter of each shape.</summary>
    /// <param name="argument">The value argument.</param>
    /// <returns>The note lines.</returns>
    private static IReadOnlyList<string>? NoteOf(ArgumentPlan argument) => argument.Direction switch
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
    /// Writes the exception documentation of an adopt in place member: the
    /// states of the wrapper that the call refuses, and the one the C function
    /// leaves behind when it could not make the copy it needed.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The member being documented.</param>
    /// <remarks>
    /// The refusal of a borrowed wrapper is only reachable on a mini object:
    /// that is the wrapper an in place vfunc override receives, and a boxed
    /// wrapper always owns the value it holds.
    /// </remarks>
    private static void WriteInPlaceExceptions(CodeWriter writer, MarshalPlan plan)
    {
        if (plan.InstanceConsumption != InstanceConsumption.InPlace)
        {
            return;
        }

        writer.WriteLine("/// <exception cref=\"ObjectDisposedException\">This wrapper was disposed.</exception>");
        writer.WriteLine("/// <exception cref=\"InvalidOperationException\">");
        if (plan.InstanceIsBorrowable)
        {
            writer.WriteLine("/// This wrapper borrows the object for the length of one call and has no");
            writer.WriteLine("/// reference to give, or the writable copy could not be made. In the second");
            writer.WriteLine("/// case the C function released the object all the same, so this wrapper is");
            writer.WriteLine("/// left disposed.");
        }
        else
        {
            writer.WriteLine("/// The writable copy could not be made. The C function released the value of");
            writer.WriteLine("/// this wrapper all the same, so this wrapper is left disposed.");
        }

        writer.WriteLine("/// </exception>");
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
    /// Writes the <see cref="ArgumentException"/> documentation of every span
    /// the body guards, off the same classification the guard is written from.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The member being documented.</param>
    /// <remarks>
    /// A length rule that only the generated body states is one a caller meets
    /// at run time and nowhere else. The rules are all about the caller's own
    /// argument, so each is documented on the parameter that carries it, the
    /// way <see cref="WriteGValueExceptions"/> documents the empty value.
    /// </remarks>
    private static void WriteSpanExceptions(CodeWriter writer, MarshalPlan plan)
    {
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            string name = DocName(argument.Name);
            string sentence;
            switch (ClassifySpanGuard(plan, argument, out ArgumentPlan? owner, out string? countType))
            {
                case SpanGuard.FixedLength:
                    string fixedLength = (argument.FixedLength ?? 0).ToString(CultureInfo.InvariantCulture);
                    sentence = "<paramref name=\"" + name + "\"/> does not have exactly " + fixedLength
                        + " elements" + (argument.IsNullable ? " and is not empty" : string.Empty) + ".";
                    break;

                case SpanGuard.SharedLength:
                    sentence = "<paramref name=\"" + name + "\"/> does not have the same length as "
                        + "<paramref name=\"" + DocName(owner!.Name) + "\"/>.";
                    break;

                case SpanGuard.NarrowLength:
                    sentence = "<paramref name=\"" + name + "\"/> has more than "
                        + NarrowCountLimit(countType!) + " elements.";
                    break;

                default:
                    continue;
            }

            writer.WriteLine("/// <exception cref=\"ArgumentException\">");
            writer.WriteLine("/// " + sentence);
            writer.WriteLine("/// </exception>");
        }
    }

    /// <summary>
    /// Documents the validation of every <c>GError</c> the member builds a
    /// temporary from.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The member being documented.</param>
    /// <remarks>
    /// The rule is about the caller's own argument and is met at run time and
    /// nowhere else, so it is documented on the parameter that carries it, the
    /// way <see cref="WriteSpanExceptions"/> documents a length. A nullable
    /// error is exempt: the absent value is one the member passes on as
    /// <c>NULL</c> and validates nothing about.
    /// </remarks>
    private static void WriteGErrorExceptions(CodeWriter writer, MarshalPlan plan)
    {
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind != ArgumentKind.GError
                || argument.Direction != ArgumentDirection.In
                || argument.IsNullable)
            {
                continue;
            }

            writer.WriteLine("/// <exception cref=\"ArgumentException\">");
            writer.WriteLine(
                "/// <paramref name=\"" + DocName(argument.Name)
                + "\"/> carries no error domain, no message, or a message with an embedded null.");
            writer.WriteLine("/// </exception>");
        }
    }

    /// <summary>The subject of a writability note whose instance is a caps.</summary>
    private const string WritableCaps = "The caps have to be writable.";

    /// <summary>The subject of a writability note whose instance is a structure.</summary>
    private const string WritableStructure = "The structure has to be writable.";

    /// <summary>
    /// What a frozen instance costs a member that writes into it directly.
    /// </summary>
    private static readonly string[] WritesNothing = ["and writes nothing otherwise."];

    /// <summary>
    /// What a frozen instance costs a walk that answers a <c>gboolean</c>: the
    /// assert is checked before the first field, so the function is never called
    /// and the <see langword="false"/> the member then answers is the same
    /// <see langword="false"/> a function that stopped the walk produces.
    /// </summary>
    private static readonly string[] SkipsTheWalkAndAnswersFalse =
    [
        "and does not call the function otherwise; the <see langword=\"false\"/> it then",
        "answers is the one a walk the function stopped answers.",
    ];

    /// <summary>
    /// What a frozen instance costs a walk that answers nothing at all.
    /// </summary>
    private static readonly string[] SkipsTheWalk = ["and does not call the function otherwise."];

    /// <summary>
    /// The entry points that require a writable caps or structure and, like
    /// the C API they bind, warn and do nothing on a frozen one. The C
    /// assert is not turned into a generated guard — the shipped
    /// <c>gst_caps_append_structure</c> carries the same assert and no guard —
    /// so parity with C is stated in the documentation instead. The value is
    /// the first sentence of the note plus the clause that says what the call
    /// does instead: the subject of the members differs in number, and a walk
    /// that never runs is not the same event as a write that never lands.
    /// The four walks assert on the same <c>IS_MUTABLE</c> as the setters
    /// (<c>gststructure.c</c> at 1.28.6, where the two deprecated spellings
    /// reach it through their <c>_id_str</c> twin, and directly at 1.24.0).
    /// </summary>
    private static readonly Dictionary<string, (string Subject, string[] Consequence)> WritableTargets =
        new(StringComparer.Ordinal)
        {
            ["gst_caps_set_value"] = (WritableCaps, WritesNothing),
            ["gst_caps_id_str_set_value"] = (WritableCaps, WritesNothing),
            ["gst_structure_id_set_value"] = (WritableStructure, WritesNothing),
            ["gst_structure_id_str_set_value"] = (WritableStructure, WritesNothing),
            ["gst_structure_set_array"] = (WritableStructure, WritesNothing),
            ["gst_structure_set_list"] = (WritableStructure, WritesNothing),
            ["gst_video_content_light_level_add_to_caps"] = (WritableCaps, WritesNothing),
            ["gst_video_mastering_display_info_add_to_caps"] = (WritableCaps, WritesNothing),
            ["gst_structure_map_in_place"] = (WritableStructure, SkipsTheWalkAndAnswersFalse),
            ["gst_structure_map_in_place_id_str"] = (WritableStructure, SkipsTheWalkAndAnswersFalse),
            ["gst_structure_filter_and_map_in_place"] = (WritableStructure, SkipsTheWalk),
            ["gst_structure_filter_and_map_in_place_id_str"] = (WritableStructure, SkipsTheWalk),
        };

    /// <summary>
    /// What a self consuming member says beyond the shape it belongs to. Each
    /// entry was read off the C implementation of the 1.28 branch and states
    /// the one thing the signature and the gir do not: whether a
    /// <see langword="null"/> answer is a normal one, and what the caller owes
    /// the value it gets back.
    /// </summary>
    private static readonly Dictionary<string, string[]> SelfConsumingRemarks = new(StringComparer.Ordinal)
    {
        ["gst_caps_merge"] =
        [
            "The answer can also be the caps that were merged <em>in</em> rather than",
            "these: gstcaps.c answers the second caps whole when they are ANY and these",
            "are not, and it answers these whole when these are ANY. Both are consumed",
            "either way, so the wrapper this hands back is the only one left holding",
            "whichever of the two came through.",
        ],
        ["gst_memory_make_mapped"] =
        [
            "The returned memory is mapped when the call succeeds. Unmapping it is the",
            "caller's, with <see cref=\"Gst.Memory.Unmap(Gst.MapInfo)\"/> on the wrapper",
            "this answers and the <see cref=\"Gst.MapInfo\"/> it filled in; the mapping is",
            "on the returned memory, which is this one only when it could be mapped as it",
            "was.",
            "<see langword=\"null\"/> is a normal answer and means the memory could",
            "neither be mapped nor copied into one that can be. <c>info</c> holds",
            "nothing usable then, because gstmemory.c fills it in only on a mapping",
            "that succeeded. The reference minted for the call is spent either way;",
            "this wrapper keeps its own.",
        ],
    };

    /// <summary>
    /// The <c>_make_writable</c> entry points that are not entry points at all
    /// on the oldest GStreamer this binding runs against, and are called
    /// through <c>gst_mini_object_make_writable</c> instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each of these was a preprocessor macro that expands to
    /// <c>gst_mini_object_make_writable</c> — <c>gstcaps.h:256</c> at 1.24.0
    /// and its eight siblings — and became an exported function only in
    /// 1.27.2. The gir carries no <c>version</c> attribute for them, because
    /// to a C caller they have always been there, so nothing in the
    /// introspection data says that binding the symbol by name raises
    /// <c>EntryPointNotFoundException</c> on 1.24 and 1.26. Calling what the
    /// macro expanded to binds a symbol that has been exported all along and
    /// is the very code the function runs.
    /// </para>
    /// <para>
    /// The list is closed by construction rather than by luck: a
    /// <c>_make_writable</c> added in a later release carries a
    /// <c>version</c> attribute and is an exported function from its first
    /// release, so it belongs on the other arm. And the other arm is where a
    /// member has to be when its C implementation is more than the forward.
    /// <c>gst_video_overlay_composition_make_writable</c> copies when a
    /// rectangle of an otherwise writable composition is shared
    /// (video-overlay-composition.c:588-597), which
    /// <c>gst_mini_object_make_writable</c> knows nothing about, which is why
    /// the rerouting is a list of the nine forwards rather than a rule over
    /// the suffix.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> InlinedMakeWritable = new(StringComparer.Ordinal)
    {
        "gst_buffer_list_make_writable",
        "gst_caps_make_writable",
        "gst_context_make_writable",
        "gst_event_make_writable",
        "gst_memory_make_writable",
        "gst_message_make_writable",
        "gst_query_make_writable",
        "gst_sample_make_writable",
        "gst_tag_list_make_writable",
    };

    /// <summary>
    /// Tests whether a member is called through the runtime import of
    /// <c>gst_mini_object_make_writable</c> rather than through an import of
    /// its own, which is what <see cref="InlinedMakeWritable"/> lists.
    /// </summary>
    /// <param name="plan">The member being written.</param>
    /// <returns><see langword="true"/> when the member imports nothing of its own.</returns>
    internal static bool CallsMiniObjectMakeWritable(MarshalPlan plan) =>
        plan.InstanceConsumption == InstanceConsumption.InPlace
        && InlinedMakeWritable.Contains(plan.EntryPoint);

    /// <summary>
    /// The entry points whose gir documentation says that the reference of the
    /// daily jam is stolen from the caller while the C code takes a reference
    /// of its own. The generator copies gir documentation verbatim, so the two
    /// members would otherwise ship a sentence that contradicts the contract of
    /// the binding: the argument is a borrow, and its wrapper stays the
    /// caller's to dispose. Verified against <c>gstvideotimecode.c</c> at
    /// 1.28.6, where <c>gst_video_time_code_init</c> calls
    /// <c>g_date_time_ref</c>; the gir sentence is on the upstream report
    /// backlog.
    /// </summary>
    private static readonly HashSet<string> StolenReferenceTargets = new(StringComparer.Ordinal)
    {
        "gst_video_time_code_new",
        "gst_video_time_code_init",
    };

    /// <summary>
    /// The entry points that take a writable pointer to the structure they are
    /// called on and do not write through it. The C signature is what the
    /// <c>readonly</c> modifier follows, so these stay writable members; what
    /// they must not carry is the sentence that says they mutate. The C of
    /// <c>gst_map_info_get_data</c> spells its instance a plain
    /// <c>GstMapInfo*</c> for a read that never writes, which is a signature
    /// the upstream report backlog carries alongside the two GstRTSPRange
    /// annotations that fixups.json corrects.
    /// </summary>
    private static readonly HashSet<string> NonMutatingValueTargets = new(StringComparer.Ordinal)
    {
        "gst_map_info_get_data",
    };

    /// <summary>
    /// What a member of a value projected structure documents beyond the
    /// mutation sentence: the state the instance is left in when the call
    /// fails, and the lifetimes a call ties together. None of it is in the gir,
    /// and each entry was read off the C implementation.
    /// </summary>
    private static readonly Dictionary<string, string[]> ValueStructRemarks = new(StringComparer.Ordinal)
    {
        ["gst_video_colorimetry_from_string"] =
        [
            "A string the parser does not accept answers <see langword=\"false\"/> and leaves",
            "this instance exactly as it was.",
        ],
        ["gst_video_mastering_display_info_from_string"] =
        [
            "A string the parser does not accept answers <see langword=\"false\"/> and leaves",
            "<paramref name=\"minfo\"/> zeroed.",
        ],
        ["gst_video_content_light_level_from_string"] =
        [
            "A string the parser does not accept answers <see langword=\"false\"/> and leaves",
            "this instance zeroed.",
        ],
        ["gst_video_mastering_display_info_from_caps"] =
        [
            "When the caps carry no such information the call answers",
            "<see langword=\"false\"/> and the contents of this instance are unspecified:",
            "the C function leaves it untouched or zeroed depending on how far the read",
            "got. Read it only after the call answered <see langword=\"true\"/>.",
        ],
        ["gst_video_content_light_level_from_caps"] =
        [
            "When the caps carry no such information the call answers",
            "<see langword=\"false\"/> and the contents of this instance are unspecified:",
            "the C function leaves it untouched or zeroed depending on how far the read",
            "got. Read it only after the call answered <see langword=\"true\"/>.",
        ],
        ["gst_video_meta_transform_matrix_init"] =
        [
            "The matrix stores the two <see cref=\"Gst.Video.VideoInfo\"/> pointers rather",
            "than copies of what they describe, so both wrappers have to stay alive and",
            "undisposed for as long as the matrix is used.",
        ],
        ["gst_map_info_clear"] =
        [
            "This is a full unmap and does the same as",
            "<see cref=\"Gst.Memory.Unmap(Gst.MapInfo)\"/>. Never call it on the mapping a",
            "<c>Gst.Buffer.MapScope</c> holds: that scope unmaps what it mapped when it is",
            "disposed, and a second unmap releases a reference nobody owns.",
        ],
    };

    /// <summary>
    /// The fact the three <c>edit</c> members share: the list they take has
    /// been ignored upstream since the FIXME that says so was written.
    /// </summary>
    private static readonly string[] IgnoredLayersRemarks =
    [
        "GStreamer ignores this list. ges_timeline_element_edit forwards to",
        "ges_timeline_element_edit_full with NULL and never reads it",
        "(ges-timeline-element.c:2533-2543, where the upstream FIXME says so), and the",
        "two deprecated wrappers forward to it (ges-container.c:1063-1070,",
        "ges-track-element.c:1823-1831). Pass null.",
    ];

    /// <summary>
    /// What a list argument does upstream, for the entry points where the
    /// answer is not the one the signature suggests. Each was read off the C
    /// implementation of the 1.28 branch and is stated on the member rather
    /// than in the parameter note, because it is about that one call and not
    /// about the marshalling shape.
    /// </summary>
    private static readonly Dictionary<string, string[]> ListArgumentRemarks = new(StringComparer.Ordinal)
    {
        ["ges_container_edit"] =
        [
            .. IgnoredLayersRemarks,
            "This overload takes the new priority as an <see langword=\"int\"/> while the",
            "member it does not hide, <c>GES.TimelineElement.Edit</c>, takes it as a",
            "<see langword=\"long\"/>: an integer literal binds this one, and an argument",
            "of type <see langword=\"long\"/> reaches the other.",
        ],
        ["ges_timeline_element_edit"] = IgnoredLayersRemarks,
        ["ges_track_element_edit"] = IgnoredLayersRemarks,
        ["ges_container_group"] =
        [
            "A list of one answers that element's own wrapper rather than a new group,",
            "and takes no new reference (ges-container.c:1007-1014, upstream FIXME). A",
            "null or empty list answers a new, empty group (ges-group.c:461). Where the",
            "members are clips, the clips after the first are merged into the first and",
            "removed from their layer (ges-clip.c:2238-2330).",
        ],
        ["ges_layer_set_active_for_tracks"] =
        [
            "A null list means every track of the timeline (ges-layer.c:1083). Every",
            "track named has to belong to the timeline of this layer; one that does not",
            "makes the call answer false (ges-layer.c:1089).",
        ],
        ["gst_uri_set_path_segments"] =
        [
            "On a URI that is not writable the call answers false and the list is leaked:",
            "C takes ownership before it checks (gsturi.c:2518-2532). Test",
            "<see cref=\"Gst.Uri.IsWritable\"/> first.",
        ],
        ["gst_uri_to_string_with_keys"] =
        [
            "A null or empty sequence asks for the unordered query string, which is what",
            "the C function falls back to when it is given no keys.",
        ],
    };

    /// <summary>
    /// The note of the indexer of a fundamental container, shared by the three
    /// of them. The return note already states that the value is an owned copy
    /// to dispose, so this one only adds what it does not: the copy is a
    /// standalone value, unaffected by what happens to the container next.
    /// </summary>
    private static readonly IReadOnlyList<string> MemberCopyNote =
    [
        "<para>",
        "The copy is independent of the container: appending to it, or disposing",
        "it, leaves a member already read out untouched.",
        "</para>",
    ];

    /// <summary>
    /// The note of a member of a fundamental value container whose behaviour
    /// the gir describes in C terms only. Each states what a caller of the C#
    /// member has to know and cannot read off the signature: what the returned
    /// value is, and what the call does with a duplicate or with two equal
    /// operands.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyList<string>> ValueContainerTargets =
        new(StringComparer.Ordinal)
        {
            ["gst_value_array_get_value"] = MemberCopyNote,
            ["gst_value_list_get_value"] = MemberCopyNote,
            ["gst_value_unique_list_get_value"] = MemberCopyNote,
            ["gst_value_list_merge"] =
            [
                "<para>",
                "The result is not always a list: merging two values that compare equal",
                "yields that single value, so read the type of the destination before",
                "treating it as a container.",
                "</para>",
            ],
            ["gst_value_unique_list_append_value"] =
            [
                "<para>",
                "A value the set already contains is dropped silently, which is what",
                "makes the set unique; the call reports nothing either way.",
                "</para>",
            ],
        };

    /// <summary>
    /// The failure value of a filtering map is "remove this field", so a
    /// handler that throws loses one.
    /// </summary>
    private static readonly string[] FilterAndMapThrowNote =
    [
        "<para>",
        "An exception the function throws does not reach this caller: it is reported",
        "through <c>Gst.Interop.ExceptionTrap</c> and the function is answered",
        "<see langword=\"false\"/>, which this call reads as a request to remove the field",
        "that was being visited. A handler that has to fail without losing data has to",
        "catch its own exceptions.",
        "</para>",
    ];

    /// <summary>
    /// The failure value of the four plain structure walks stops the walk and
    /// is answered to the caller.
    /// </summary>
    private static readonly string[] WalkStopsThrowNote =
    [
        "<para>",
        "An exception the function throws does not reach this caller: it is reported",
        "through <c>Gst.Interop.ExceptionTrap</c> and the function is answered",
        "<see langword=\"false\"/>, which stops the walk and is what this call then",
        "returns. A failed walk is therefore indistinguishable from one the function",
        "stopped on purpose.",
        "</para>",
    ];

    /// <summary>
    /// The failure value of a fold stops it, and a stopped fold reports success.
    /// </summary>
    private static readonly string[] FoldThrowNote =
    [
        "<para>",
        "An exception the function throws does not reach this caller: it is reported",
        "through <c>Gst.Interop.ExceptionTrap</c> and the function is answered",
        "<see langword=\"false\"/>, which stops the fold. A fold the function stopped",
        "answers <c>GST_ITERATOR_OK</c>, so a failed one is indistinguishable from one",
        "that stopped on purpose, and the accumulator holds whatever was written before",
        "the failure.",
        "</para>",
    ];

    /// <summary>
    /// A void handler has no failure value, so the walk carries on.
    /// </summary>
    private static readonly string[] IteratorForeachThrowNote =
    [
        "<para>",
        "An exception the function throws does not reach this caller: it is reported",
        "through <c>Gst.Interop.ExceptionTrap</c>. The function answers nothing, so the",
        "walk carries on with the next element and this call still reports the result of",
        "the walk itself.",
        "</para>",
    ];

    /// <summary>
    /// What a handler that throws costs, for the eight members that hand a
    /// <c>GValue</c> to one.
    /// </summary>
    /// <remarks>
    /// A managed exception must never unwind through a native frame, so every
    /// <c>scope=call</c> trampoline catches it, reports it through
    /// <c>Gst.Interop.ExceptionTrap</c> and answers the call with the failure
    /// value of the callback. What that failure value <em>means</em> is the
    /// caller's to say, and for one of these it is not benign:
    /// <c>gst_structure_filter_and_map_in_place</c> reads <c>FALSE</c> as
    /// "remove this field", so a handler that threw loses the field it was
    /// visiting. Nothing on the signature says so, which is why it is said
    /// here. Verified against <c>gststructure.c</c> and <c>gstiterator.c</c> at
    /// 1.28.6.
    /// </remarks>
    private static readonly Dictionary<string, IReadOnlyList<string>> ThrowingHandlerTargets =
        new(StringComparer.Ordinal)
        {
            ["gst_structure_filter_and_map_in_place"] = FilterAndMapThrowNote,
            ["gst_structure_filter_and_map_in_place_id_str"] = FilterAndMapThrowNote,
            ["gst_structure_foreach"] = WalkStopsThrowNote,
            ["gst_structure_foreach_id_str"] = WalkStopsThrowNote,
            ["gst_structure_map_in_place"] = WalkStopsThrowNote,
            ["gst_structure_map_in_place_id_str"] = WalkStopsThrowNote,
            ["gst_iterator_fold"] = FoldThrowNote,
            ["gst_iterator_foreach"] = IteratorForeachThrowNote,
        };

    /// <summary>
    /// What a caller does about <c>GST_ITERATOR_RESYNC</c>, which neither
    /// iterator walk handles for it.
    /// </summary>
    private static readonly string[] IteratorResyncNote =
    [
        "<para>",
        "A collection that changed while the walk was running stops it with",
        "<c>GST_ITERATOR_RESYNC</c>, and the walk does not resynchronise by itself: the",
        "caller decides whether to call <see cref=\"Resync\"/> and walk again. A second",
        "walk starts the collection over, so every element the function has already",
        "seen is handed to it again.",
        "</para>",
    ];

    /// <summary>
    /// The paragraph the two iterator walks carry. Both answer
    /// <c>GST_ITERATOR_RESYNC</c> and stop when the collection changed under
    /// them, and neither resynchronises by itself — <c>gst_iterator_fold</c>
    /// leaves the loop on that result (<c>gstiterator.c</c> at 1.28.6) and
    /// <c>gst_iterator_foreach</c> is a fold. What the caller has to do about it
    /// is not in the gir documentation, and it is not free of consequence: the
    /// walk that follows a resync starts the collection over, so the elements
    /// that were already handed to the function arrive a second time.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyList<string>> IteratorWalkTargets =
        new(StringComparer.Ordinal)
        {
            ["gst_iterator_fold"] = IteratorResyncNote,
            ["gst_iterator_foreach"] = IteratorResyncNote,
        };

    /// <summary>
    /// Returns every generator authored remarks paragraph of a member: the
    /// consumption contract of its consumed arguments, the writability
    /// requirement of the entry points that have one, the correction of the gir
    /// sentence that claims a stolen reference, the behaviour note of the
    /// members of a fundamental value container, what a member of a value
    /// projected structure does to the instance it is called on, and what a
    /// call really does with the list it is given.
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

        if (plan.InstanceConsumption == InstanceConsumption.InPlace)
        {
            lines.AddRange(AdoptedInPlaceRemarks);
            if (plan.InstanceIsBorrowable)
            {
                lines.AddRange(BorrowedInstanceNote);
            }
        }
        else if (plan.InstanceConsumption == InstanceConsumption.Minted)
        {
            lines.AddRange(MintedInstanceRemarks);
        }

        if (SelfConsumingRemarks.TryGetValue(plan.EntryPoint, out string[]? selfConsuming))
        {
            lines.Add("<para>");
            lines.AddRange(selfConsuming);
            lines.Add("</para>");
        }

        if (WritableTargets.TryGetValue(plan.EntryPoint, out (string Subject, string[] Consequence) writability))
        {
            lines.Add("<para>");
            lines.Add(writability.Subject + " Like the C API, the call raises a warning");
            lines.AddRange(writability.Consequence);
            lines.Add("</para>");
        }

        if (StolenReferenceTargets.Contains(plan.EntryPoint))
        {
            lines.Add("<para>");
            lines.Add("The documentation above says that the reference of the daily jam is stolen");
            lines.Add("from the caller. It is not: the C function takes a reference of its own, so");
            lines.Add("the caller keeps the value it passes and disposes it as usual.");
            lines.Add("</para>");
        }

        if (ValueContainerTargets.TryGetValue(plan.EntryPoint, out IReadOnlyList<string>? container))
        {
            lines.AddRange(container);
        }

        if (IteratorWalkTargets.TryGetValue(plan.EntryPoint, out IReadOnlyList<string>? resync))
        {
            lines.AddRange(resync);
        }

        if (ThrowingHandlerTargets.TryGetValue(plan.EntryPoint, out IReadOnlyList<string>? throwing))
        {
            lines.AddRange(throwing);
        }

        if (InstallsForeverCallback(plan))
        {
            lines.AddRange(ForeverCallbackRemarks);
        }

        if (MutatesValueInstance(plan))
        {
            lines.Add("<para>");
            lines.Add("Mutates this instance; call it on a variable, not on a copy returned by a");
            lines.Add("property.");
            lines.Add("</para>");
        }

        if (ValueStructRemarks.TryGetValue(plan.EntryPoint, out string[]? note))
        {
            lines.Add("<para>");
            lines.AddRange(note);
            lines.Add("</para>");
        }

        if (ListArgumentRemarks.TryGetValue(plan.EntryPoint, out string[]? list))
        {
            lines.Add("<para>");
            lines.AddRange(list);
            lines.Add("</para>");
        }

        return lines.Count == 0 ? null : lines;
    }

    /// <summary>
    /// Tests whether a member hands over a callback the library never releases
    /// again.
    /// </summary>
    /// <param name="plan">The member being documented.</param>
    /// <returns><see langword="true"/> when one of its callbacks has the forever scope.</returns>
    /// <remarks>
    /// The paragraph is keyed on the scope of the arguments rather than on a
    /// list of entry points, because the scope is the fact that makes it true:
    /// every member that installs such a callback leaks one handle per call,
    /// and a correction that gives a sixth member the same scope has to
    /// document it without a second edit.
    /// </remarks>
    private static bool InstallsForeverCallback(MarshalPlan plan)
    {
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind == ArgumentKind.Callback && argument.Scope == GirScope.Forever)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests whether a member writes through the value projected structure it
    /// is called on, which a caller has to be told because a C# structure is
    /// copied by every assignment and by every property that hands one out.
    /// </summary>
    /// <param name="plan">The member being documented.</param>
    /// <returns><see langword="true"/> when the member mutates the instance.</returns>
    private static bool MutatesValueInstance(MarshalPlan plan)
    {
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind == ArgumentKind.ValueInstance)
            {
                return !IsConstInstance(plan) && !NonMutatingValueTargets.Contains(plan.EntryPoint);
            }
        }

        return false;
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

            // The state of a callback is allocated last, after every other
            // prologue. Nothing but the call itself, or the finally of a call
            // scoped callback, releases it, so no prologue that can throw -
            // the UTF-8 copy of a string with an embedded NUL is one - may run
            // after it. The gir order is kept among the callbacks and among
            // everything else.
            foreach (ArgumentPlan argument in plan.Arguments)
            {
                if (argument.Kind != ArgumentKind.Callback)
                {
                    WritePrologue(writer, plan, argument);
                }
            }

            // The reference a conversion takes over is minted after every
            // other argument, so that a prologue which can throw - the UTF-8
            // copy of a string, the walk of a sequence - runs while there is
            // still nothing to strand. Nothing releases this one but the call.
            if (plan.InstanceConsumption == InstanceConsumption.Minted)
            {
                writer.WriteLine(
                    "nint " + InstanceOwnedLocal + " = Gst.GstNative.MiniObjectRef(" + InstanceLocal + ");");
            }

            foreach (ArgumentPlan argument in plan.Arguments)
            {
                if (argument.Kind == ArgumentKind.Callback)
                {
                    WritePrologue(writer, plan, argument);
                }
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
            if (argument.Kind == ArgumentKind.ValueInstance)
            {
                // The instance is the caller's own storage, which may be a
                // field of a heap object, so the address only holds while a
                // fixed scope does. A readonly member cannot take the address
                // of `this` directly, and Unsafe.AsRef is what turns the
                // readonly reference back into the writable one the fixed
                // statement needs; the member still writes nothing.
                string storage = IsConstInstance(plan)
                    ? "System.Runtime.CompilerServices.Unsafe.AsRef(in this)"
                    : "this";
                writer.WriteLine("fixed (" + argument.RawType + " " + ValueInstanceLocal + " = &" + storage + ")");
                writer.OpenBlock();
                scopes.Add(argument);
            }
            else if (argument.Kind == ArgumentKind.Span)
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

        // Storage the binding allocated goes first. Every other out conversion
        // may throw — a handle whose GType does not match the wrapper it is
        // asked for does — and a throw between the call and this hand over
        // would leave nobody holding the allocation. Nothing here depends on
        // the other conversions, so the order costs nothing.
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind == ArgumentKind.CallerAllocatedBoxed)
            {
                WriteEpilogue(writer, plan, argument);
            }
        }

        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind != ArgumentKind.CallerAllocatedBoxed)
            {
                WriteEpilogue(writer, plan, argument);
            }
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
        // Storage the binding allocated for an out parameter is released the
        // same way, and for the same reason: the throw below runs before the
        // epilogue that would have handed it over.
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind != ArgumentKind.CallerAllocatedBoxed)
            {
                continue;
            }

            writer.WriteLine("if (errorNative != 0)");
            writer.OpenBlock();
            writer.WriteLine(FromNative(argument, argument.Name + "Native") + "?.Dispose();");
            writer.CloseBlock();
        }

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
    /// So a member that takes a callback is a materializing member of
    /// <see cref="MaterializesArguments"/>: every guard runs first, every
    /// handle is read into a local next, and the allocation comes last. The
    /// locals are emitted only for those members, because hoisting them
    /// everywhere would rewrite every generated body for a guarantee that no
    /// other member needs.
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
    /// made for the call that no scope reclaims. The state of a callback is
    /// one, and so is the UTF-8 copy of a string the callee takes ownership
    /// of, the value minted for a consuming argument — the reference or the
    /// copy the callee takes over — and the storage a caller allocated boxed
    /// out parameter is filled into.
    /// </summary>
    /// <param name="plan">The member being written.</param>
    /// <returns><see langword="true"/> when one of the arguments materializes.</returns>
    /// <remarks>
    /// <para>
    /// Such a member orders its prologue in three strict phases — every guard,
    /// every handle read, every materialization — so that nothing that can
    /// throw runs after the allocation, which nothing but the call itself
    /// releases. Every other member keeps the plain one pass prologue: the
    /// phases guarantee nothing there, and applying them everywhere would
    /// rewrite every generated body.
    /// </para>
    /// <para>
    /// A callback argument counts, because the interleaved prologue used to
    /// allocate its state before the guard and before the UTF-8 copy of a
    /// later parameter, and either of those throwing stranded the handle. The
    /// third phase writes the callbacks after everything else for the same
    /// reason, so the allocation is the last statement before the call.
    /// </para>
    /// </remarks>
    private static bool MaterializesArguments(MarshalPlan plan)
    {
        if (TakesCallback(plan))
        {
            return true;
        }

        // A call that takes the instance over materializes it: an adopt in
        // place member hands its own reference to the call, and a mint and
        // adopt member raises one for it. Both have to happen after every
        // guard and every handle read, which is what the phases give them.
        if (plan.InstanceConsumption != InstanceConsumption.None)
        {
            return true;
        }

        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind is ArgumentKind.Utf8Owned or ArgumentKind.ConsumedHandle
                or ArgumentKind.CallerAllocatedBoxed)
            {
                return true;
            }

            // A borrowed list is not one: its scope reclaims the spine and the
            // strings whether the call returns or throws, exactly as the scope
            // of a string vector does. A consumed list is, and the phases are
            // what put every guard and every handle read before the mint, so
            // that nothing which can throw runs between the mint and the call
            // that takes it over.
            if (argument.Kind == ArgumentKind.ListIn && argument.Transfer == GirTransfer.Full)
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

            // The instance of a value projected structure is `this`, which a
            // caller cannot pass as null and no wrapper can have disposed.
            case ArgumentKind.ValueInstance:
                return;

            case ArgumentKind.Utf8 when argument.Direction == ArgumentDirection.In:
            case ArgumentKind.Utf8Owned:
            case ArgumentKind.Handle when argument.Direction == ArgumentDirection.In:
            case ArgumentKind.ConsumedHandle:
            case ArgumentKind.Strv when argument.Direction == ArgumentDirection.In:
            case ArgumentKind.ListIn when argument.Direction == ArgumentDirection.In:
                if (!argument.IsNullable)
                {
                    writer.WriteLine("ArgumentNullException.ThrowIfNull(" + name + ");");
                }

                return;

            // A callback the C function accepts as NULL is not guarded: the
            // absence of a function is a value the callee acts on, and the
            // call site hands it the null pointer rather than a trampoline.
            case ArgumentKind.Callback:
                if (!argument.IsNullable)
                {
                    writer.WriteLine("ArgumentNullException.ThrowIfNull(" + name + ");");
                }

                return;

            // Storage the binding allocates itself. There is nothing the
            // caller could hand over and nothing to validate, and the guard
            // phase must stay free of allocations for the phase order to mean
            // anything.
            case ArgumentKind.CallerAllocatedBoxed:
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

            // The temporary GError the prologue builds needs a registered
            // domain and a message, because g_error_new_literal answers NULL
            // with a critical without them. The validation is a guard rather
            // than a check inside the allocation: a member that also mints a
            // consumed handle orders its prologue in three phases, and a guard
            // that throws has to find nothing allocated yet.
            case ArgumentKind.GError when argument.Direction == ArgumentDirection.In:
                if (!argument.IsNullable)
                {
                    writer.WriteLine("ArgumentNullException.ThrowIfNull(" + name + ");");
                }

                writer.WriteLine(
                    "Gst.GLib.GException.ValidateForNative(" + name + ", nameof(" + name + "));");
                return;

            case ArgumentKind.Span:
                WriteSpanGuard(writer, plan, argument);
                return;

            default:
                return;
        }
    }

    /// <summary>The length rule one span argument is guarded by.</summary>
    private enum SpanGuard
    {
        /// <summary>Nothing about the length is the caller's to get wrong.</summary>
        None,

        /// <summary>The C declaration sizes the block itself.</summary>
        FixedLength,

        /// <summary>The count the call is handed is another span's.</summary>
        SharedLength,

        /// <summary>The count the call is handed cannot hold every length.</summary>
        NarrowLength,
    }

    /// <summary>
    /// Classifies the length rule of a span, which is read once and answers
    /// both the guard the body carries and the exception its documentation
    /// states. A member whose body throws is a member whose documentation says
    /// so because the two are written off this one answer.
    /// </summary>
    /// <param name="plan">The member being written.</param>
    /// <param name="argument">The span to classify.</param>
    /// <param name="owner">The span the shared count is read off.</param>
    /// <param name="countType">The C# type of a count that is too narrow.</param>
    /// <returns>The rule.</returns>
    private static SpanGuard ClassifySpanGuard(
        MarshalPlan plan,
        ArgumentPlan argument,
        out ArgumentPlan? owner,
        out string? countType)
    {
        owner = null;
        countType = null;

        if (argument.Kind != ArgumentKind.Span || argument.IsHidden)
        {
            return SpanGuard.None;
        }

        if (argument.FixedLength is not null)
        {
            return SpanGuard.FixedLength;
        }

        if (argument.LengthArgument is not int length
            || plan.Arguments[length].OwnerArgument is not int owned
            || owned < 0)
        {
            return SpanGuard.None;
        }

        if (!ReferenceEquals(plan.Arguments[owned], argument))
        {
            owner = plan.Arguments[owned];
            return SpanGuard.SharedLength;
        }

        countType = NarrowCountType(plan.Arguments[length]);
        return countType is null ? SpanGuard.None : SpanGuard.NarrowLength;
    }

    /// <summary>
    /// Returns the C# type of a hidden count that cannot hold every length a
    /// span can have, or <see langword="null"/> when it can.
    /// </summary>
    /// <param name="length">The hidden count argument.</param>
    /// <returns>The type name, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The count is a cast of <see cref="System.Span{T}.Length"/>, and a cast
    /// into a type that cannot hold it wraps silently: a 256 element span
    /// counted by a <c>guint8</c> tells the C function there is nothing to
    /// read, and a 65536 element one counted by a <c>guint16</c> does the
    /// same, while the pointer it is handed is real. Only the types narrower
    /// than <see cref="int"/> can wrap, because a length is a non-negative
    /// <see cref="int"/> to begin with.
    /// </remarks>
    private static string? NarrowCountType(ArgumentPlan length) => length.RawType switch
    {
        "sbyte" or "byte" or "short" or "ushort" => length.RawType,
        _ => null,
    };

    /// <summary>
    /// The largest length a narrow count can hold, spelled out for the message
    /// and for the documentation.
    /// </summary>
    /// <param name="countType">The type <see cref="NarrowCountType"/> answered.</param>
    /// <returns>The limit.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="countType"/> is not one of the types
    /// <see cref="NarrowCountType"/> answers. The two are written together, so
    /// a type added to one and not to the other fails here rather than being
    /// documented with the limit of a type it is not.
    /// </exception>
    private static string NarrowCountLimit(string countType) => countType switch
    {
        "sbyte" => "127",
        "byte" => "255",
        "short" => "32767",
        "ushort" => "65535",
        _ => throw new InvalidOperationException(
            $"'{countType}' is not a narrow count type; NarrowCountType and NarrowCountLimit "
            + "have to list the same types."),
    };

    /// <summary>Writes the length guard of a span.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="plan">The member being written.</param>
    /// <param name="argument">The span to guard.</param>
    /// <remarks>
    /// When two arrays name the same length parameter, only one of them is the
    /// owner the call site reads <c>Length</c> off. Every other span is passed
    /// along with a count that came from somewhere else, so a caller that hands
    /// over a shorter one would have the C function read past its end. The
    /// owner has a requirement of its own when the C argument is narrower than
    /// a length: what the call is handed is a cast, and a cast that wraps hands
    /// the library a count that has nothing to do with the span. Both state a
    /// requirement the C declaration does not.
    /// </remarks>
    private static void WriteSpanGuard(CodeWriter writer, MarshalPlan plan, ArgumentPlan argument)
    {
        string name = argument.Name;
        switch (ClassifySpanGuard(plan, argument, out ArgumentPlan? owner, out string? countType))
        {
            // Exact, and never merely "at least": the C function reads the
            // size its declaration states whenever the pointer is not NULL, so
            // a shorter span is an over-read and a longer one hides a caller
            // that expected more of the call than it does. An empty span pins
            // to a null pointer, which is the NULL the nullable ones document.
            case SpanGuard.FixedLength:
                string fixedLength = (argument.FixedLength ?? 0).ToString(CultureInfo.InvariantCulture);
                writer.WriteLine(
                    "if (" + name + ".Length != " + fixedLength
                    + (argument.IsNullable ? " && " + name + ".Length != 0" : string.Empty) + ")");
                writer.OpenBlock();
                writer.WriteLine("throw new ArgumentException(");
                writer.WriteLine(
                    "    \"" + DocName(name) + " must have exactly " + fixedLength + " elements"
                    + (argument.IsNullable ? ", or none at all" : string.Empty) + ".\",");
                writer.WriteLine("    nameof(" + name + "));");
                writer.CloseBlock();
                return;

            case SpanGuard.SharedLength:
                writer.WriteLine("if (" + name + ".Length != " + owner!.Name + ".Length)");
                writer.OpenBlock();
                writer.WriteLine("throw new ArgumentException(");
                writer.WriteLine(
                    "    \"" + DocName(name) + " must have the same length as " + DocName(owner.Name)
                    + ": the call reads one length for both.\",");
                writer.WriteLine("    nameof(" + name + "));");
                writer.CloseBlock();
                return;

            case SpanGuard.NarrowLength:
                writer.WriteLine("if (" + name + ".Length > " + countType + ".MaxValue)");
                writer.OpenBlock();
                writer.WriteLine("throw new ArgumentException(");
                writer.WriteLine(
                    "    \"" + DocName(name) + " must have at most " + NarrowCountLimit(countType!)
                    + " elements: the call takes its count as a " + countType + ".\",");
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
                // An adopt in place member gives the reference of the wrapper
                // to the call, so the read is the one that refuses a borrowed
                // wrapper: it has no reference of its own to give.
                writer.WriteLine(
                    "nint " + InstanceLocal + " = "
                    + (plan.InstanceConsumption == InstanceConsumption.InPlace
                        ? "BeginMakeWritable()"
                        : InstanceHandle(plan, argument.Name))
                    + ";");
                return;

            // There is no handle to read: the pointer is taken by the fixed
            // scope that wraps the call, which is opened after the prologue.
            case ArgumentKind.ValueInstance:
                return;

            case ArgumentKind.Handle when argument.Direction == ArgumentDirection.In:
                writer.WriteLine("nint " + argument.Name + "Native = " + HandleRead(argument) + ";");
                return;

            // The storage of a caller allocated out is allocated in the third
            // phase, after every read that can throw, so nothing is read here.
            case ArgumentKind.CallerAllocatedBoxed:
                return;

            // There is no single handle to read: the list is built element by
            // element in the third phase, which is where the reads that can
            // throw - a disposed wrapper, a string with an embedded NUL - and
            // the allocations happen together, inside a factory that releases
            // what it already made when one of them throws.
            case ArgumentKind.ListIn:
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
            // The handle of the instance is read in the second phase of a
            // materializing member, and read at the call site of every other.
            case ArgumentKind.Instance:
                return;

            case ArgumentKind.ValueInstance:
                return;

            case ArgumentKind.Error:
                writer.WriteLine("nint errorNative = 0;");
                return;

            // The temporary error belongs to the scope, which releases it when
            // the call returns and when the call throws. Every callee that
            // takes one copies what it keeps.
            case ArgumentKind.GError when argument.Direction == ArgumentDirection.In:
                writer.WriteLine(
                    "using Gst.Interop.GErrorScope " + name + "Scope = Gst.Interop.GMarshal.AllocError("
                    + name + ");");
                return;

            case ArgumentKind.Utf8 when argument.Direction == ArgumentDirection.In:
                writer.WriteLine(
                    "System.Span<byte> " + name + "Buffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];");
                writer.WriteLine(
                    "using Gst.Interop.Utf8Scope " + name + "Scope = Gst.Interop.GMarshal.StackUtf8("
                    + name + ", " + name + "Buffer);");
                return;

            // The vector and every string in it belong to the scope, which
            // releases both when the call returns and when the call throws.
            // The callee reads them and copies whatever it keeps.
            case ArgumentKind.Strv when argument.Direction == ArgumentDirection.In:
                writer.WriteLine(
                    "using Gst.Interop.StrvScope " + name + "Scope = Gst.Interop.GMarshal.AllocStrv("
                    + name + ");");
                return;

            // The spine, and the UTF-8 copies of a list of strings, belong to
            // the scope, which releases them when the call returns and when it
            // throws. The scope also holds the element wrappers, which is what
            // keeps them reachable across the call.
            case ArgumentKind.ListIn when argument.Transfer != GirTransfer.Full:
                writer.WriteLine(
                    "using Gst.Interop.GListScope " + name + "Scope = Gst.Interop.GMarshal.AllocList("
                    + name + ", singly: " + (argument.IsSinglyLinked ? "true" : "false") + ");");
                return;

            // The consumed half: one value is minted per element and the whole
            // list is handed over, so nothing is released here or afterwards.
            case ArgumentKind.ListIn:
                writer.WriteLine(
                    "nint " + name + "Owned = Gst.Interop.GMarshal.ConsumeList("
                    + name + ", singly: " + (argument.IsSinglyLinked ? "true" : "false") + ");");
                return;

            case ArgumentKind.Utf8Owned:
                writer.WriteLine("nint " + name + "Native = Gst.Interop.GMarshal.StringToUtf8Ptr(" + name + ");");
                return;

            case ArgumentKind.ConsumedHandle:
                writer.WriteLine("nint " + name + "Owned = " + Minted(argument) + ";");
                return;

            // The library sizes and zeroes the record the callee fills, which
            // is the last step of the prologue for the same reason a mint is:
            // nothing but the epilogue releases it again.
            case ArgumentKind.CallerAllocatedBoxed:
                writer.WriteLine(
                    "nint " + name + "Native = " + argument.StorageFactory!.NativeName + "();");
                return;

            // No handle is allocated for a callback that is not there: the
            // default handle carries the null user data the call site passes
            // along, and freeing it is a no-op.
            case ArgumentKind.Callback:
                writer.WriteLine(
                    "Gst.Interop.CallbackHandle " + name + "State = "
                    + (argument.IsNullable ? name + " is null ? default : " : string.Empty)
                    + "Gst.Interop.CallbackHandle.Alloc(" + name + ");");
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

        // A member whose entry point is a macro on the oldest supported
        // GStreamer calls the runtime import of what that macro expands to.
        string target = CallsMiniObjectMakeWritable(plan)
            ? "Gst.GstNative.MiniObjectMakeWritable"
            : plan.NativeName;
        string call = target + "(" + string.Join(", ", arguments) + ")";
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
            case ArgumentKind.Instance when plan.InstanceConsumption == InstanceConsumption.Minted:
                return InstanceOwnedLocal;

            case ArgumentKind.Instance:
                return MaterializesArguments(plan)
                    ? InstanceLocal
                    : InstanceHandle(plan, name);

            case ArgumentKind.ValueInstance:
                return ValueInstanceLocal;

            case ArgumentKind.Error:
                return "&errorNative";

            case ArgumentKind.UserData:
                return plan.Arguments[argument.OwnerArgument ?? 0].Name + "State.UserData";

            case ArgumentKind.DestroyNotify:
            {
                // Nothing was allocated for an absent callback, so there is
                // nothing for the callee to notify: handing it a notification
                // over a null user data would free a handle that never was.
                ArgumentPlan owner = plan.Arguments[argument.OwnerArgument ?? 0];
                string notify = "(nint)Gst.Interop.CallbackHandle.DestroyNotify";
                return owner.IsNullable ? owner.Name + " is null ? 0 : " + notify : notify;
            }

            // The C function branches on the function pointer, so an absent
            // callback has to reach it as the null pointer rather than as a
            // trampoline with no delegate behind it.
            case ArgumentKind.Callback:
                return argument.IsNullable
                    ? name + " is null ? 0 : " + argument.TrampolineType + ".Pointer"
                    : argument.TrampolineType + ".Pointer";

            case ArgumentKind.Span:
                return name + "Pointer";

            case ArgumentKind.ArrayLength when argument.Direction == ArgumentDirection.In:
                return "(" + argument.RawType + ")" + plan.Arguments[argument.OwnerArgument ?? 0].Name + ".Length";

            case ArgumentKind.Utf8 when argument.Direction == ArgumentDirection.In:
            case ArgumentKind.Strv when argument.Direction == ArgumentDirection.In:
            case ArgumentKind.GError when argument.Direction == ArgumentDirection.In:
                return name + "Scope.Pointer";

            case ArgumentKind.ListIn when argument.Transfer != GirTransfer.Full:
                return name + "Scope.Head";

            case ArgumentKind.ListIn:
                return name + "Owned";

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

            case ArgumentKind.CallerAllocatedBoxed:
                return name + "Native";

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

        // The storage was allocated by the binding, so the wrapper adopts it
        // whatever the gir says the C function transfers: what it describes is
        // the caller's own structure, which the binding is.
        ArgumentKind.CallerAllocatedBoxed =>
            TrimNullable(argument.PublicType) + ".FromNative(" + source + ", Gst.Interop.Transfer.Full)",
        ArgumentKind.Strv => "Gst.Interop.GMarshal.StrvToArray(" + source + ", free: "
            + (argument.Transfer == GirTransfer.None ? "false" : "true") + ")",

        // Only the return position reaches this for a GValue; an argument is
        // read or written in place. A borrowed return is copied and an owned
        // one is adopted — contents moved, shell freed — and NULL is the
        // empty value either way.
        ArgumentKind.GValue => argument.Transfer == GirTransfer.Full
            ? "Gst.GObject.Value.TakeOwnership(" + source + ")"
            : "Gst.GObject.Value.CopyFrom(" + source + ")",

        // Only the return position reaches this for a GError; an argument is
        // built into a temporary the scope owns. The three fields are copied
        // and the pointer is never freed, because the library that produced it
        // keeps owning it.
        ArgumentKind.GError => "Gst.GLib.GException.FromBorrowed(" + source + ")",
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

            // The runtime wrapper of a GParamSpec is constructed rather than
            // interned or made by a typed factory, and its constructor refuses
            // the null pointer, so the null test the other flavours perform
            // inside their factory is written out here. The parentheses matter:
            // the caller may append a `?? throw` or an `is { }` to what comes
            // back, and both bind tighter than the conditional.
            HandleFlavor.ParamSpec => "(" + source + " == 0 ? null : new " + type + "(" + source + ", "
                + TransferLiteral(transfer) + "))",
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
        if (argument.Kind == ArgumentKind.CallerAllocatedBoxed)
        {
            WriteCallerAllocatedEpilogue(writer, argument);
            return;
        }

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
                declare: false,
                fixedLength: argument.FixedLength);
            return;
        }

        writer.WriteLine(name + " = " + FromNative(argument, name + "Native") + ";");
    }

    /// <summary>
    /// Hands the storage of a caller allocated out parameter to the caller, or
    /// frees it again when the call reported that it filled nothing.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="argument">The out argument.</param>
    /// <remarks>
    /// <para>
    /// The wrapper adopts the storage, so disposing it is the free the
    /// allocation asks for and the caller owes exactly one of them. A callee
    /// that answers a <c>gboolean</c> leaves the record untouched when it
    /// answers false — <c>gst_video_info_dma_drm_from_video_info</c> returns
    /// before it writes anything — so the storage is released here rather than
    /// handed over zeroed, and the parameter is null.
    /// </para>
    /// <para>
    /// The wrap cannot fail: the constructor of the record does not answer
    /// NULL, and a wrapper of a non-zero pointer is never null. The throw says
    /// so rather than suppressing the nullability with an operator that would
    /// hide a real one.
    /// </para>
    /// </remarks>
    private static void WriteCallerAllocatedEpilogue(CodeWriter writer, ArgumentPlan argument)
    {
        string name = argument.Name;
        string adopt = FromNative(argument, name + "Native");
        if (!argument.IsNullable)
        {
            writer.WriteLine(name + " = " + adopt);
            writer.WriteLine(
                "    ?? throw new InvalidOperationException(\""
                + argument.StorageFactory!.EntryPoint + " returned no value.\");");
            return;
        }

        writer.WriteLine("if (" + ResultLocal + " != 0)");
        writer.OpenBlock();
        writer.WriteLine(name + " = " + adopt + ";");
        writer.CloseBlock();
        writer.WriteLine("else");
        writer.OpenBlock();
        writer.WriteLine("// The call filled nothing, so the storage goes back through");
        writer.WriteLine("// the boxed free the wrapper disposes through.");
        writer.WriteLine(adopt + "?.Dispose();");
        writer.WriteLine(name + " = null;");
        writer.CloseBlock();
    }

    private static void WriteReturn(CodeWriter writer, MarshalPlan plan)
    {
        ReturnPlan value = plan.Return;
        if (value.IsVoid)
        {
            return;
        }

        // The wrapper adopts what the call answered and hands itself back. A
        // zero is the copy the C function could not make: it consumed the
        // reference all the same, so the adoption leaves the wrapper disposed
        // and raises the failure rather than answering a wrapper that stands
        // for nothing.
        if (plan.InstanceConsumption == InstanceConsumption.InPlace)
        {
            writer.WriteLine("AdoptWritable(" + ResultLocal + ");");
            writer.WriteLine("return this;");
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
                declare: true,
                fixedLength: value.FixedLength);
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
        if (plan.ReturnsEmptyOnNull)
        {
            // The C function answers NULL for a structure it cannot describe,
            // and the default of a struct is such a value. object.ToString has
            // to answer something for every instance, so the empty string is
            // what a description that does not exist reads as.
            writer.WriteLine("return " + expression + " ?? string.Empty;");
            return;
        }

        bool needsCheck = !value.IsNullable
            && value.Kind is ArgumentKind.Handle or ArgumentKind.Utf8 or ArgumentKind.Strv
                or ArgumentKind.GError;

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
        bool declare,
        int? fixedLength = null)
    {
        // A block whose size the C declaration fixes carries no count of its
        // own, so the length is the literal size rather than the value an
        // argument came back with.
        string length = fixedLength is int size
            ? size.ToString(CultureInfo.InvariantCulture)
            : "(int)" + plan.Arguments[lengthArgument ?? 0].Name + "Native";
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

        string userData = "userData";
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.Kind == ArgumentKind.UserData)
            {
                userData = argument.Name;
            }
        }

        // A callback of the async scope is invoked once and nothing else ever
        // releases its state, so the trampoline frees the handle it was called
        // through. The outer scope covers the early return of a state that
        // cannot be read as well as the body and the trapped exception; Free
        // already does nothing to a zero pointer.
        if (plan.SelfFreeing)
        {
            writer.WriteLine("try");
            writer.OpenBlock();
        }

        writer.WriteLine("try");
        writer.OpenBlock();

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

                case ArgumentKind.BorrowedGValue:
                    // The view is built over the pointer and owns nothing, so
                    // there is no epilogue: it stops being usable when the
                    // invocation returns, which is what its ref struct shape
                    // states and the compiler enforces.
                    writer.WriteLine(
                        argument.PublicType + " " + argument.Name + "Value = " + argument.Name + " != null");
                    writer.WriteLine("    ? new " + argument.PublicType + "(ref *" + argument.Name + ")");
                    writer.WriteLine(
                        "    : throw new InvalidOperationException(\"" + NullMessage(plan, argument) + "\");");
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

        if (plan.SelfFreeing)
        {
            writer.CloseBlock();
            writer.WriteLine("finally");
            writer.OpenBlock();
            writer.WriteLine("Gst.Interop.CallbackHandle.FromUserData(" + userData + ").Free();");
            writer.CloseBlock();
        }

        writer.CloseBlock();
        writer.CloseBlock();
    }
}
