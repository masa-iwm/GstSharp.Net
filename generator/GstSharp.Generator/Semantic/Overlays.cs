using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GstSharp.Generator.Semantic;

/// <summary>
/// A corrected annotation for a callable, a parameter or a return value.
/// </summary>
internal sealed class AnnotationOverride
{
    /// <summary>Gets or sets the corrected <c>transfer-ownership</c> value.</summary>
    public string? Transfer { get; set; }

    /// <summary>Gets or sets the corrected nullability.</summary>
    public bool? Nullable { get; set; }

    /// <summary>Gets or sets the corrected optionality of an out parameter.</summary>
    public bool? Optional { get; set; }

    /// <summary>Gets or sets the corrected <c>caller-allocates</c> flag.</summary>
    public bool? CallerAllocates { get; set; }

    /// <summary>
    /// Gets or sets how the parameter is passed in C#: <c>in</c>, <c>out</c> or
    /// <c>ref</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>out</c> and <c>ref</c> only correct a parameter the gir spells as a
    /// bare pointer to a plain structure — which the planner would otherwise
    /// pass as a copy the callee writes into and the caller never sees — or to
    /// a <c>GValue</c>, whose projection is a pointer into the caller's own
    /// storage in every direction.
    /// </para>
    /// <para>
    /// <c>in</c> additionally corrects a pointer to a <em>record</em> that the
    /// gir calls a caller allocated out and the C function really reads and
    /// updates in place, which is the annotation
    /// <c>gst_sdp_media_set_media_from_caps</c> carries. The redirect clears
    /// <c>callerAllocates</c> on its own, so an entry that states it as well
    /// only says out loud what the planner already does.
    /// </para>
    /// <para>
    /// Everything else is left alone and reported, because a direction is not a
    /// marshalling this can invent: an out handle, an array or a string needs a
    /// projection of its own.
    /// </para>
    /// </remarks>
    public string? Direction { get; set; }

    /// <summary>
    /// Gets or sets the number of elements a caller allocated out array holds,
    /// for a parameter the gir spells as a pointer to a single value.
    /// </summary>
    /// <remarks>
    /// The C function writes that many elements into storage the caller
    /// provides, which the gir does not say: <c>gst_video_info_align_full</c>
    /// fills four <c>gsize</c> values through a parameter its gir declares as
    /// one <c>gsize*</c>. The size is a fact about the C implementation and
    /// belongs in the overlays for that reason.
    /// </remarks>
    public int? FixedArraySize { get; set; }

    /// <summary>
    /// Gets or sets the corrected <c>scope</c> of a callback parameter:
    /// <c>call</c>, <c>notified</c>, <c>async</c> or <c>forever</c>.
    /// </summary>
    /// <remarks>
    /// The scope is the lifetime of the managed state behind the callback, and
    /// a gir that states the wrong one is a use after free rather than a
    /// missing binding: the <c>GstCollectPads</c> setters annotate their
    /// function <c>(scope call)</c> and the library keeps the pointer for the
    /// life of the object. The correction states what the C implementation
    /// does with the function it is handed, which no other annotation carries.
    /// </remarks>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets whether the return value is dropped, so that the member is
    /// emitted as if the C function returned nothing.
    /// </summary>
    /// <remarks>
    /// It is keyed on <c>#return</c> and states a fact about the C
    /// implementation that no gir annotation carries: the value handed back is
    /// something the caller already holds. <c>gst_value_list_init</c> returns
    /// the very pointer it was given, and binding that return would deep copy a
    /// freshly initialized list into a second owner for nothing.
    /// </remarks>
    public bool? DiscardReturn { get; set; }

    /// <summary>
    /// Gets or sets the message of an <c>[Obsolete]</c> attribute the emitted
    /// member carries.
    /// </summary>
    /// <remarks>
    /// It is keyed on the bare <c>c:identifier</c> of the callable, because it
    /// describes the member rather than any one argument of it. What it exists
    /// for is a member that shipped in a shape the binding cannot correct: the
    /// promise of a stable series keeps the published signature alive, so the
    /// working shape is written by hand beside it and the generated one is
    /// marked here. The attribute is a warning and never an error - the member
    /// still compiles, which is the whole point of keeping it - and the gir
    /// deprecation, where a callable carries one, is what the message replaces:
    /// two attributes on one member do not compile. The key must not name an
    /// accessor that a generated property delegates to, because the property
    /// body would then call an obsolete member and fail the warning-free build
    /// with CS0618.
    /// </remarks>
    public string? Obsolete { get; set; }

    /// <summary>
    /// Gets or sets whether the handler of a signal is handed a borrowed
    /// wrapper of a mini object or boxed argument rather than one that holds a
    /// reference or a copy of its own.
    /// </summary>
    /// <remarks>
    /// It is keyed on a signal argument alone and states a fact about the C
    /// implementation that no gir annotation carries: the signal registered the
    /// argument <c>G_SIGNAL_TYPE_STATIC_SCOPE</c>, so every emission path hands
    /// the handler the object of the emitter rather than a copy GObject made of
    /// it, and whoever sent the value reads back what the handler wrote into it.
    /// <c>GstApp.AppSink::propose-allocation</c> is the one signal of the corpus
    /// that does both. The wrapper such an argument is handed takes no reference
    /// and no copy - which is what leaves the object writable - and is disposed
    /// when the handler returns, exactly as the argument of a virtual method
    /// override is. It is only legal on an argument the planner projects onto a
    /// mini object or a boxed wrapper, and on no key but a signal argument: on
    /// a parameter of a method or of a callback, on an argument of a virtual
    /// method or on a return it is reported as GEN0054 rather than consumed in
    /// silence. <see langword="false"/> states the default and changes nothing,
    /// the way a <c>nullable</c> of <see langword="false"/> does.
    /// </remarks>
    public bool? Borrow { get; set; }
}

/// <summary>
/// A correction of an <c>&lt;array&gt;</c> the gir already spells, whose
/// element count or element type the annotation gets wrong.
/// </summary>
/// <remarks>
/// <para>
/// Every field corrects an attribute of the <c>&lt;array&gt;</c> element and
/// nothing else: this does not promote a bare pointer into an array, because
/// the decision that a pointer is one is exactly the decision a binding must
/// not invent. An entry on a parameter the gir does not spell as an array is
/// reported as GEN0020 and ignored.
/// </para>
/// <para>
/// The return value of a <em>virtual method</em> is the one exception, and only
/// for an entry that states <c>elementType</c> and <c>length</c> together. A
/// slot is a C function pointer rather than a callable the gir annotates, so a
/// class struct field upstream marks <c>introspectable="0"</c> carries no
/// <c>&lt;array&gt;</c> to correct at all — the return of
/// <c>GESTimelineElementClass::list_children_properties</c> is spelled as one
/// <c>GObject.ParamSpec</c> with a <c>GParamSpec**</c> <c>c:type</c>. An entry
/// that names both halves describes the array in full instead of letting the
/// applier infer one.
/// </para>
/// <para>
/// <c>length</c> and <c>fixedSize</c> are mutually exclusive in GIR, so an
/// entry that states one clears the other: an array whose length the overlays
/// name is not also of a size fixed by the C declaration, and the other way
/// round.
/// </para>
/// </remarks>
internal sealed class ArrayOverride
{
    /// <summary>Gets or sets the index of the parameter carrying the element count.</summary>
    public int? Length { get; set; }

    /// <summary>Gets or sets the number of elements the C declaration sizes the array at.</summary>
    public int? FixedSize { get; set; }

    /// <summary>Gets or sets the corrected <c>zero-terminated</c> flag.</summary>
    public bool? ZeroTerminated { get; set; }

    /// <summary>Gets or sets the gir name of the element type.</summary>
    public string? ElementType { get; set; }
}

/// <summary>
/// The statements the generated body of a callable runs before it marshals
/// anything.
/// </summary>
/// <remarks>
/// <para>
/// The generator does not read the statements: it writes them out verbatim, in
/// the order they are given, between the argument guards and the first
/// marshalling statement of the body. What validates them is the C# compiler,
/// and that is the contract of the overlay - the helper a statement calls is
/// hand written in the <c>Custom/</c> partial of the type that carries the
/// member, so a statement that names nothing fails the build rather than
/// quietly guarding nothing.
/// </para>
/// <para>
/// The position is what the entry buys: a guard that throws has to find
/// nothing allocated yet, so it stands after the null checks - which is what
/// lets a statement dereference the very parameter it validates - and before
/// the first <c>stackalloc</c>, scope or handle read.
/// </para>
/// </remarks>
internal sealed class Precondition
{
    /// <summary>
    /// Gets or sets the statements, each of which is written on a line of its
    /// own. The list may not be empty, and no element of it may be blank.
    /// </summary>
    public List<string>? Statements { get; set; }
}

/// <summary>
/// One exact substring of the gir documentation of a type, and the text that
/// stands in its place in the generated documentation.
/// </summary>
/// <remarks>
/// It is the replacing counterpart of <c>docStrip</c>, for the upstream
/// sentence that is not wrong enough to remove and not right enough to render:
/// a documented string grammar the library itself does not write that way. The
/// substring has to stand in the gir documentation exactly once, so that the
/// gir refresh which reworded - or corrected - the sentence reports the entry
/// rather than leaving the generated text as upstream wrote it.
/// </remarks>
internal sealed class DocReplacement
{
    /// <summary>
    /// Gets or sets the substring of the gir documentation that is replaced. It
    /// may not be blank, and it has to stand in the documentation exactly once.
    /// </summary>
    /// <remarks>
    /// It is not nullable, because an entry that omits it is refused while the
    /// overlays load: the blank default is what the refusal reads.
    /// </remarks>
    public string Old { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the text written in its place. It may not be the same text
    /// as <see cref="Old"/>, which would replace nothing.
    /// </summary>
    /// <remarks>
    /// An entry that omits it writes the empty string, which is a removal; that
    /// is legal here, but <c>docStrip</c> is the key written for a removal of a
    /// whole sentence.
    /// </remarks>
    public string New { get; set; } = string.Empty;
}

/// <summary>
/// A record or class field the ledger must not count as a missing binding,
/// because something else already hands the same value out.
/// </summary>
/// <remarks>
/// <para>
/// A field is not a callable, so no skip reason describes one and the hand
/// bound list cannot name one either: the field ledger of the skip report is
/// where a field that carries API in C and none in C# is counted. An entry here
/// says the second half is not true after all, and moves the field into a
/// section of its own instead of leaving it among the ones nothing reads.
/// </para>
/// <para>
/// On a record it also keeps the generator from emitting an accessor for the
/// field, which is what makes it the answer to a name a hand written member
/// already carries: two declarations of one name in a partial class do not
/// compile, and the hand written one is the one that shipped. A class has no
/// mirror and no accessor of a field is ever emitted for one, so there an entry
/// says only who answers it.
/// </para>
/// <para>
/// Exactly one of the two has to be stated, and the check is exclusive: an
/// entry that states neither says nothing about the field, and one that states
/// both says two different things about who answers it. Either way the ledger
/// would go quiet on the strength of a claim nothing can check, so neither is
/// applied and both are reported as stale.
/// </para>
/// </remarks>
internal sealed class FieldSkip
{
    /// <summary>
    /// Gets or sets the generated member that answers the same value, for
    /// example <c>GetFlowReturn</c> for <c>GstPadProbeInfo.flow_ret</c>.
    /// </summary>
    public string? ExposedBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a hand written member under
    /// <c>src/&lt;Project&gt;/Custom/</c> answers the field.
    /// </summary>
    public bool? HandBound { get; set; }

    /// <summary>Gets what the ledger reports the field under.</summary>
    internal string Reason =>
        ExposedBy is { Length: > 0 } member ? member : "hand written";

    /// <summary>
    /// Gets a value indicating whether the entry states exactly one of the two
    /// halves, which is the only shape that says who answers the field.
    /// </summary>
    internal bool IsStated => (ExposedBy is { Length: > 0 }) ^ (HandBound == true);
}

/// <summary>
/// An instance field of a GObject class that the generator exposes through an
/// accessor of its own.
/// </summary>
/// <remarks>
/// <para>
/// A wrapper holds a native instance and mirrors no part of it, so every field
/// of a class is out of reach by default and the ledger counts the whole of
/// them. An entry here is the exception: it says that one field carries an
/// accessor, and it carries the three facts the accessor cannot be written
/// without. <c>lock</c> is the lock the library rewrites the field under, which
/// managed code cannot take; <c>overrides</c> names the streaming thread slots
/// inside which a read is consistent anyway, because the pad holds that lock
/// around the call; the <c>$comment</c> names the header file and line both
/// claims were read from.
/// </para>
/// <para>
/// All three are required. A field whose lock nobody stated is one whose
/// accessor would document nothing, and an <c>overrides</c> list that is empty
/// would say there is no window at all - which is a refusal, not an entry.
/// </para>
/// </remarks>
internal sealed class InstanceField
{
    /// <summary>
    /// Gets or sets the lock the library rewrites the field under, as free text
    /// the generated remark prints, for example <c>STREAM_LOCK</c>.
    /// </summary>
    public string? Lock { get; set; }

    /// <summary>
    /// Gets or sets the gir names of the virtual methods the base class calls on
    /// the streaming thread, which is the window a read of the field is
    /// consistent in.
    /// </summary>
    public List<string>? Overrides { get; set; }

    /// <summary>Gets or sets the header file and line the entry rests on.</summary>
    [JsonPropertyName("$comment")]
    public string? Comment { get; set; }

    /// <summary>
    /// Gets or sets the stem the accessor is named after, in place of the one
    /// the name of the field derives.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets what is wrong with the shape of the entry, read off the entry alone
    /// and without asking what this run made of it.
    /// </summary>
    internal string? ShapeFault =>
        Lock is not { Length: > 0 }
            ? "states no 'lock'"
            : Overrides is not { Count: > 0 }
                ? "states no 'overrides'"
                : Comment is not { Length: > 0 }
                    ? "states no '$comment'"
                    : Name is { Length: 0 }
                        ? "states an empty 'name'"
                        : Name is { } stem && !IsIdentifier(stem)
                            ? $"states the 'name' '{stem}', which is no C# identifier"
                            : null;

    /// <summary>
    /// Tests whether a stem can be pasted into the name of a generated member.
    /// </summary>
    /// <param name="value">The stem the entry states.</param>
    /// <returns><see langword="true"/> when the stem is an identifier.</returns>
    /// <remarks>
    /// The stem is written straight into the declaration of the accessor, so a
    /// value with a space or a punctuation mark in it would be a compile error in
    /// a generated file rather than something the reader of the overlay could act
    /// on.
    /// </remarks>
    private static bool IsIdentifier(string value)
    {
        if (!char.IsLetter(value[0]) && value[0] != '_')
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// A correction of a record field that no gir annotation carries.
/// </summary>
/// <remarks>
/// <para>
/// A gir never states the nullability of a <c>&lt;field&gt;</c>: the attribute
/// exists on parameters and on return values only. A field that is projected
/// onto a reference - a string, or the wrapper of what a pointer points at - is
/// therefore nullable by default, because that is what the corpus says about
/// every one of them, and a non nullable one is a claim about the C
/// implementation that has to be made by hand.
/// </para>
/// <para>
/// <c>nullable: false</c> is one of the three corrections an entry may state.
/// <c>name</c> is the second: the stem the member that reads the field is named
/// after, for a field whose own name is taken by a member that shipped. The
/// third is <c>accessor: false</c>, which holds a field back from the accessors
/// altogether: the pointer stays on the mirror and the field stays on the
/// ledger under the shape that keeps it there. The reason belongs in the
/// <c>$comment</c> of the entry rather than in a key of its own, because it
/// differs per field: a pointer whose consumer contract the binding cannot
/// state, a wrapper the surface has no way of reaching, or storage the library
/// is handed rather than owns.
/// </para>
/// <para>
/// <c>accessor: false</c> is exclusive of the other two: a field that carries
/// no accessor has neither a nullability to correct nor a member to name.
/// <c>nullable</c> and <c>name</c> may be stated together, because they say
/// two different things about one accessor. An entry that states nothing, that
/// states the default the emitters already use, that spells an empty
/// <c>name</c>, or that spells the name the field derives anyway corrects
/// nothing and is reported. Each entry carries a <c>$comment</c> with the C
/// file and line the claim rests on, which is ignored here and read by the
/// reviewer.
/// </para>
/// </remarks>
internal sealed class FieldAnnotation
{
    /// <summary>
    /// Gets or sets a value indicating whether the field may hold the null
    /// pointer. Only <see langword="false"/> is applied.
    /// </summary>
    public bool? Nullable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field is projected onto an
    /// accessor at all. Only <see langword="false"/> is applied.
    /// </summary>
    public bool? Accessor { get; set; }

    /// <summary>
    /// Gets or sets the stem the member that reads the field is named after, in
    /// place of the one the name of the field derives.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets a value indicating whether the field is held back from the
    /// accessors.
    /// </summary>
    internal bool SuppressesAccessor => Accessor == false && Nullable is null && Name is null;

    /// <summary>
    /// Gets a value indicating whether the entry states a correction that
    /// changes what is emitted.
    /// </summary>
    internal bool IsStated =>
        SuppressesAccessor || (Accessor != false && ((Nullable == false) || Name is { Length: > 0 }));

    /// <summary>
    /// Gets what is wrong with the shape of the entry, read off the entry alone
    /// and without asking what this run made of it.
    /// </summary>
    internal string? ShapeFault =>
        Accessor == false && Nullable is not null
            ? "states 'accessor: false' beside 'nullable', and a field that carries no accessor has no "
                + "nullability to correct"
            : Accessor == false && Name is not null
                ? "states 'accessor: false' beside 'name', and a field that carries no accessor has no "
                    + "member to name"
                : Name is { Length: 0 }
                    ? "states an empty 'name'"
                    : IsStated
                        ? null
                        : "states nothing beyond the default";
}

/// <summary>
/// Per platform availability of a native symbol.
/// </summary>
internal sealed class PlatformSupport
{
    /// <summary>Gets or sets the platforms the symbol exists on.</summary>
    public List<string>? Supported { get; set; }

    /// <summary>Gets or sets the platforms the symbol is missing on.</summary>
    public List<string>? Unsupported { get; set; }
}

/// <summary>
/// The hand maintained corrections applied on top of the reference gir files.
/// </summary>
/// <remarks>
/// <para>
/// <c>fixups.json</c> uses these key formats:
/// </para>
/// <list type="bullet">
/// <item><description><c>skip</c>: <c>c:identifier</c> of a callable, the
/// qualified gir name of a type (<c>Gst.Foo</c>), or the GObject spelling of a
/// property (<c>Gst.Element:name</c>) or of a signal
/// (<c>Gst.Element::pad-added</c>) whose C implementation no annotation can
/// describe. A key spelled as a signal is read by the signal loop alone, and
/// one that matched no signal is reported as <c>GEN0055</c>; a key of any of
/// the other shapes that matched nothing is reported as
/// <c>GEN0056</c>.</description></item>
/// <item><description><c>handBound</c>: <c>c:identifier</c> of a callable, or
/// the GObject spelling of a signal (<c>Gst.Element::pad-added</c>) or property
/// (<c>Gst.Element:name</c>) as the census reports it, whose managed surface is
/// hand written. It changes nothing about what is generated; it annotates the
/// ledger, so that a symbol the bindings do cover is reported under
/// <see cref="SkipReason.HandBound"/> instead of counting as a missing
/// binding.</description></item>
/// <item><description><c>rename</c>: qualified gir name of a type
/// (<c>Gst.MessageType</c>), of an enumeration member
/// (<c>Gst.MessageType.state_changed</c>) or a <c>c:identifier</c>. An entry
/// the run never looked up is reported as
/// <c>GEN0057</c>.</description></item>
/// <item><description><c>annotationOverrides</c>: <c>c:identifier</c> of a
/// callable, or the <c>c:type</c> of a callback, which has no identifier of its
/// own; optionally suffixed with <c>#parameter-name</c> or
/// <c>#return</c>. Besides the annotations the gir spells, an entry may state
/// the <c>direction</c> of a pointer to a plain structure, of a pointer to a
/// record the C function works on in place, and the <c>fixedArraySize</c> of a
/// caller allocated out array, all of which are facts about the C
/// implementation that no gir annotation carries; it may also state
/// <c>discardReturn</c> on <c>#return</c> to drop a return value the caller
/// already holds, and the <c>scope</c> of a callback parameter whose gir
/// annotation does not describe how long the library keeps the function it is
/// handed. On the bare identifier, with no suffix, it may state
/// <c>obsolete</c>, the message of an <c>[Obsolete]</c> attribute the emitted
/// member carries, which is how a member that shipped in a shape the binding
/// cannot correct without breaking the series is marked while the corrected
/// shape is written by hand beside it. A signal argument is addressed by the
/// GObject spelling of its signal instead,
/// <c>GES.Project::error-loading-asset#error</c>, the key
/// <c>rename</c> uses for the event of the same signal; only <c>nullable</c>
/// and <c>borrow</c> are read there, because a signal argument has no
/// direction, no array, no callback scope and no discardable return.
/// <c>borrow</c> hands the handler a borrowed wrapper of a mini object or
/// boxed argument instead of one that holds a reference or a copy of its
/// own, which is what leaves the argument writable where the sender of the
/// value reads it back; on an argument of any other shape, and on any key but
/// a signal argument, it is reported as
/// GEN0054.</description></item>
/// <item><description><c>arrayOverrides</c>: keyed like
/// <c>annotationOverrides</c> and applied to a parameter or a return value the
/// gir already spells as an <c>&lt;array&gt;</c>. It corrects the
/// <c>length</c> index, the <c>fixedSize</c>, the <c>zeroTerminated</c> flag
/// or the <c>elementType</c> of that array, which is how a C function that
/// counts its elements off another argument gets a span rather than staying
/// unbound. It never turns a bare pointer into an array.</description></item>
/// <item><description><c>fieldSkips</c>: the <c>c:type</c> of a record and the
/// gir name of one of its fields (<c>GstPadProbeInfo.flow_ret</c>), naming the
/// member that answers the same value. A field of a reserved ABI union is
/// addressed by the field alone, the same way its accessor is named. It keeps
/// the field off the ledger of unbound fields and keeps the generator from
/// emitting an accessor for it; a key that matches no field is reported as
/// stale.</description></item>
/// <item><description><c>fieldAnnotations</c>: keyed like <c>fieldSkips</c>
/// and stating what no gir annotation carries about a record field. Today
/// that is <c>nullable</c>, read only as <c>false</c>, which says the field
/// never holds the null pointer and emits the accessor of it non nullable;
/// <c>name</c>, the stem the member that reads the field is named after in
/// place of the one the field name derives, subject to the same rule that
/// decides between a property and a <c>Get</c> method; and <c>accessor</c>,
/// read only as <c>false</c>, which holds the field back from the accessors
/// and leaves it on the ledger. <c>accessor: false</c> excludes the other
/// two, which may be stated together; an entry that states nothing, that
/// states the default, that spells an empty name or one the field derives
/// anyway is reported as stale.</description></item>
/// <item><description><c>instanceFields</c>: keyed like <c>fieldSkips</c> and
/// naming an instance field of a GObject class that the generator exposes
/// through an accessor of its own — the one ledger section whose entries a
/// wrapper can answer at all. Each entry states the <c>lock</c> the library
/// rewrites the field under, the gir names of the streaming thread
/// <c>overrides</c> that form the window in which a read is consistent, and a
/// <c>$comment</c> with the header file and line the claim rests on; an optional
/// <c>name</c> renames the accessor the way <c>fieldAnnotations</c> does. The
/// shapes the allowlist admits are closed, and an entry is an error rather than
/// a warning: the accessor is public surface.</description></item>
/// <item><description><c>forceOpaque</c>: qualified gir name of a record
/// (<c>Gst.DebugCategory</c>) that must be wrapped behind a pointer rather
/// than copied by value.</description></item>
/// <item><description><c>returnTypeOverrides</c>: <c>c:identifier</c> mapped
/// onto the C# type the member returns. It only narrows a returned handle onto
/// the type that declares the member, which is what turns
/// <c>gst_pipeline_new</c> from a factory of <c>Gst.Element</c> into one of
/// <c>Gst.Pipeline</c>.</description></item>
/// <item><description><c>instanceKeyedCallbacks</c>: qualified gir name of a
/// callback (<c>Gst.PadChainFunction</c>) mapped onto the name of the storage
/// slot the setter writes (<c>chain</c>). It names a callback whose own C
/// signature carries no <c>user_data</c> parameter, so that its trampoline
/// recovers the managed delegate from the instance it is handed as its first
/// argument and the slot instead. Two callbacks that share one slot -
/// <c>Gst.PadEventFunction</c> and <c>Gst.PadEventFullFunction</c>, which
/// gst_pad_set_event_function_full and gst_pad_set_event_full_function_full
/// both write to <c>eventdata</c> - state the same slot name and are mutually
/// exclusive per instance.</description></item>
/// <item><description><c>docNotes</c>: <c>c:identifier</c> of a callable mapped
/// onto a sentence its generated documentation carries, for a part of its
/// contract that neither the gir nor the marshalling states. It is the
/// counterpart of <c>vfuncDocNotes</c> for a member rather than a
/// slot.</description></item>
/// <item><description><c>signalDocNotes</c>: the GObject spelling of a signal
/// (<c>GES.Timeline::select-tracks-for-object</c>) mapped onto a sentence the
/// generated documentation of its event carries, for a part of its contract
/// that neither the gir nor the marshalling states. It is the key the nullable
/// signal argument overrides use without the <c>#argument</c>
/// suffix.</description></item>
/// <item><description><c>preconditions</c>: <c>c:identifier</c> of a callable
/// mapped onto the C# statements the generated body runs before it marshals
/// anything, after the argument guards. They are emitted verbatim and in the
/// order they are written, and the C# compiler is what validates them: the
/// helper each one calls is hand written in the <c>Custom/</c> partial of the
/// type that carries the member, so a statement that names nothing is a build
/// failure rather than a silent omission. It is how a member whose C
/// dereferences what it must not - the meta adders that use the NULL
/// gst_buffer_add_meta answers for a shared buffer - refuses the call instead
/// of crashing the process.</description></item>
/// <item><description><c>handOverRefusals</c>: <c>c:identifier</c> of a callable
/// that refuses what it is handed before it takes it, mapped onto the citation
/// of the C lines where it does. The generated body releases the reference
/// minted for every handed over argument again when the call refuses - a
/// <c>FALSE</c> or a <c>NULL</c> return - which is the one case where the mint
/// would otherwise be left with no owner. The entry states that the refusal
/// return means the reference was not taken, which is a reading of the C that
/// only the C can settle.</description></item>
/// <item><description><c>docStrip</c>: <c>c:identifier</c> of a callable mapped
/// onto the substrings taken out of its gir documentation before it is split
/// into paragraphs. It is the one overlay that removes upstream text rather
/// than adding to it, for the sentence that states a rule of the C which is not
/// the rule of the binding - "@target will steal a reference to the @profile" -
/// and which the generated remark would otherwise have to argue against. Each
/// substring has to occur exactly once in the raw documentation, so that a gir
/// refresh which reworded the sentence is reported rather than silently
/// stripping nothing.</description></item>
/// <item><description><c>typeDocReplace</c>: the qualified gir name of a class
/// (<c>GES.EffectClip</c>) mapped onto the exact substrings of its type
/// documentation that are written differently, each as an <c>old</c> and a
/// <c>new</c>. It is the replacing counterpart of <c>docStrip</c>, applied to
/// the documentation of the type itself rather than of a member, for the
/// upstream sentence that states a grammar the library does not write that way
/// - the asset id of an effect clip. Each <c>old</c> has to stand in the raw
/// documentation exactly once, and a key that named no class the run rendered
/// is reported, so that a gir refresh which corrected the sentence upstream
/// fails the run rather than replacing nothing. Only classes are covered: every
/// other emitter obtains its type documentation on a path of its
/// own.</description></item>
/// </list>
/// </remarks>
internal sealed class Overlays
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly HashSet<string> _skip;
    private readonly HashSet<string> _handBound;
    private readonly HashSet<string> _forceOpaque;
    private readonly Dictionary<string, string> _rename;
    private readonly Dictionary<string, AnnotationOverride> _annotations;
    private readonly Dictionary<string, ArrayOverride> _arrayOverrides;
    private readonly Dictionary<string, PlatformSupport> _platforms;
    private readonly Dictionary<string, string> _returnTypes;
    private readonly Dictionary<string, FieldSkip> _fieldSkips;
    private readonly Dictionary<string, FieldAnnotation> _fieldAnnotations;
    private readonly Dictionary<string, InstanceField> _instanceFields;
    private readonly HashSet<string> _subclassable;
    private readonly Dictionary<string, string> _skipVirtuals;
    private readonly Dictionary<string, string> _vfuncDefaults;
    private readonly HashSet<string> _vfuncIdentityBuffers;
    private readonly Dictionary<string, string> _vfuncNonNullReturns;
    private readonly Dictionary<string, string> _vfuncDocNotes;
    private readonly HashSet<string> _vfuncSpans;
    private readonly HashSet<string> _vfuncFloatingReturns;
    private readonly HashSet<string> _vfuncSiblingArguments;
    private readonly HashSet<string> _lentOpaqueRecords;
    private readonly Dictionary<string, string> _vfuncFailureValues;
    private readonly Dictionary<string, string> _instanceKeyedCallbacks;
    private readonly Dictionary<string, string> _docNotes;
    private readonly Dictionary<string, string> _signalDocNotes;
    private readonly Dictionary<string, IReadOnlyList<string>> _preconditions;
    private readonly Dictionary<string, string> _handOverRefusals;
    private readonly Dictionary<string, IReadOnlyList<string>> _docStrips;
    private readonly Dictionary<string, IReadOnlyList<DocReplacement>> _typeDocReplacements;

    private Overlays(
        HashSet<string> skip,
        HashSet<string> handBound,
        HashSet<string> forceOpaque,
        Dictionary<string, string> rename,
        Dictionary<string, AnnotationOverride> annotations,
        Dictionary<string, ArrayOverride> arrayOverrides,
        Dictionary<string, PlatformSupport> platforms,
        Dictionary<string, string> returnTypes,
        Dictionary<string, FieldSkip> fieldSkips,
        Dictionary<string, FieldAnnotation> fieldAnnotations,
        Dictionary<string, InstanceField> instanceFields,
        HashSet<string> subclassable,
        Dictionary<string, string> skipVirtuals,
        Dictionary<string, string> vfuncDefaults,
        HashSet<string> vfuncIdentityBuffers,
        Dictionary<string, string> vfuncNonNullReturns,
        Dictionary<string, string> vfuncDocNotes,
        HashSet<string> vfuncSpans,
        HashSet<string> vfuncFloatingReturns,
        HashSet<string> vfuncSiblingArguments,
        HashSet<string> lentOpaqueRecords,
        Dictionary<string, string> vfuncFailureValues,
        Dictionary<string, string> instanceKeyedCallbacks,
        Dictionary<string, string> docNotes,
        Dictionary<string, string> signalDocNotes,
        Dictionary<string, IReadOnlyList<string>> preconditions,
        Dictionary<string, string> handOverRefusals,
        Dictionary<string, IReadOnlyList<string>> docStrips,
        Dictionary<string, IReadOnlyList<DocReplacement>> typeDocReplacements)
    {
        _skip = skip;
        _handBound = handBound;
        _forceOpaque = forceOpaque;
        _rename = rename;
        _annotations = annotations;
        _arrayOverrides = arrayOverrides;
        _platforms = platforms;
        _returnTypes = returnTypes;
        _fieldSkips = fieldSkips;
        _fieldAnnotations = fieldAnnotations;
        _instanceFields = instanceFields;
        _subclassable = subclassable;
        _skipVirtuals = skipVirtuals;
        _vfuncDefaults = vfuncDefaults;
        _vfuncIdentityBuffers = vfuncIdentityBuffers;
        _vfuncNonNullReturns = vfuncNonNullReturns;
        _vfuncDocNotes = vfuncDocNotes;
        _vfuncSpans = vfuncSpans;
        _vfuncFloatingReturns = vfuncFloatingReturns;
        _vfuncSiblingArguments = vfuncSiblingArguments;
        _lentOpaqueRecords = lentOpaqueRecords;
        _vfuncFailureValues = vfuncFailureValues;
        _instanceKeyedCallbacks = instanceKeyedCallbacks;
        _docNotes = docNotes;
        _signalDocNotes = signalDocNotes;
        _preconditions = preconditions;
        _handOverRefusals = handOverRefusals;
        _docStrips = docStrips;
        _typeDocReplacements = typeDocReplacements;
    }

    /// <summary>Gets an overlay set without any correction.</summary>
    internal static Overlays Empty { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, AnnotationOverride>(StringComparer.Ordinal),
        new Dictionary<string, ArrayOverride>(StringComparer.Ordinal),
        new Dictionary<string, PlatformSupport>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, FieldSkip>(StringComparer.Ordinal),
        new Dictionary<string, FieldAnnotation>(StringComparer.Ordinal),
        new Dictionary<string, InstanceField>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<DocReplacement>>(StringComparer.Ordinal));

    /// <summary>Gets the skipped identifiers, ordered for reporting.</summary>
    internal IReadOnlyCollection<string> SkippedIdentifiers => _skip;

    /// <summary>
    /// Gets the identifiers whose managed surface is hand written, so that a
    /// run can report the ones it never saw skipped.
    /// </summary>
    internal IReadOnlyCollection<string> HandBoundIdentifiers => _handBound;

    /// <summary>
    /// The qualified gir names of the records kept behind a pointer instead of
    /// being projected as value types.
    /// </summary>
    internal IReadOnlyCollection<string> OpaqueRecords => _forceOpaque;

    /// <summary>
    /// Gets the keys of every declared annotation correction, so that a run
    /// can report the ones no callable, parameter or signal argument matched.
    /// </summary>
    internal IReadOnlyCollection<string> AnnotationOverrideKeys => _annotations.Keys;

    /// <summary>
    /// Gets the keys of every declared array correction, so that a run can
    /// report the ones no array matched.
    /// </summary>
    internal IReadOnlyCollection<string> ArrayOverrideKeys => _arrayOverrides.Keys;

    /// <summary>
    /// Gets the keys of every declared rename, so that a run can report the
    /// ones that named nothing it emitted.
    /// </summary>
    internal IReadOnlyCollection<string> RenameKeys => _rename.Keys;

    /// <summary>
    /// Gets the keys of every declared field skip, so that a run can report the
    /// ones no field of an emitted record or class matched.
    /// </summary>
    internal IReadOnlyCollection<string> FieldSkipKeys => _fieldSkips.Keys;

    /// <summary>
    /// Gets the keys of every declared field annotation, so that a run can
    /// report the ones no field of an emitted record matched.
    /// </summary>
    internal IReadOnlyCollection<string> FieldAnnotationKeys => _fieldAnnotations.Keys;

    /// <summary>Gets the keys of the instance field allowlist, for the stale entry report.</summary>
    internal IReadOnlyCollection<string> InstanceFieldKeys => _instanceFields.Keys;

    /// <summary>
    /// Gets the qualified gir names of the classes a managed subclass may
    /// derive from, so that a run can report the ones no class matched.
    /// </summary>
    internal IReadOnlyCollection<string> SubclassableClasses => _subclassable;

    /// <summary>
    /// Gets the keys of every declared virtual method skip, so that a run can
    /// report the ones no slot of a subclassable class matched.
    /// </summary>
    internal IReadOnlyCollection<string> SkippedVirtualKeys => _skipVirtuals.Keys;

    /// <summary>
    /// Gets the keys of every declared chain-up default, so that a run can
    /// report the ones no emitted slot matched.
    /// </summary>
    internal IReadOnlyCollection<string> VfuncDefaultKeys => _vfuncDefaults.Keys;

    /// <summary>
    /// Gets the keys of every declared identity preserving buffer, so that a
    /// run can report the ones no emitted parameter matched.
    /// </summary>
    internal IReadOnlyCollection<string> VfuncIdentityBufferKeys => _vfuncIdentityBuffers;

    /// <summary>Gets the slots whose managed answer may not be null.</summary>
    internal IReadOnlyCollection<string> VfuncNonNullReturnKeys => _vfuncNonNullReturns.Keys;

    /// <summary>Gets the slots that carry a hand written note in their documentation.</summary>
    internal IReadOnlyCollection<string> VfuncDocNoteKeys => _vfuncDocNotes.Keys;

    /// <summary>Gets the block parameters the slot behind them only reads.</summary>
    internal IReadOnlyCollection<string> VfuncSpanKeys => _vfuncSpans;

    /// <summary>Gets the slots whose answer is handed out floating.</summary>
    internal IReadOnlyCollection<string> VfuncFloatingReturnKeys => _vfuncFloatingReturns;

    /// <summary>
    /// Gets the parameters that hand a slot an instance of the type the slot's
    /// own instance has.
    /// </summary>
    internal IReadOnlyCollection<string> VfuncSiblingArgumentKeys => _vfuncSiblingArguments;

    /// <summary>Gets the opaque records that a virtual method is lent one of.</summary>
    internal IReadOnlyCollection<string> LentOpaqueRecordKeys => _lentOpaqueRecords;

    /// <summary>Gets the slots that answer their own value when an override threw.</summary>
    internal IReadOnlyCollection<string> VfuncFailureValueKeys => _vfuncFailureValues.Keys;

    /// <summary>Gets the callbacks whose state is keyed by the instance they are installed on.</summary>
    internal IReadOnlyCollection<string> InstanceKeyedCallbackKeys => _instanceKeyedCallbacks.Keys;

    /// <summary>Gets the callables that carry a hand written note in their documentation.</summary>
    internal IReadOnlyCollection<string> DocNoteKeys => _docNotes.Keys;

    /// <summary>Gets the signals that carry a hand written note in their documentation.</summary>
    internal IReadOnlyCollection<string> SignalDocNoteKeys => _signalDocNotes.Keys;

    /// <summary>Gets the callables whose generated body opens with hand written statements.</summary>
    internal IReadOnlyCollection<string> PreconditionKeys => _preconditions.Keys;

    /// <summary>
    /// Gets the callables that release the reference minted for a handed over
    /// argument again when they refuse the call.
    /// </summary>
    internal IReadOnlyCollection<string> HandOverRefusalKeys => _handOverRefusals.Keys;

    /// <summary>Gets the callables whose gir documentation is shortened before it is rendered.</summary>
    internal IReadOnlyCollection<string> DocStripKeys => _docStrips.Keys;

    /// <summary>Gets the types part of whose gir documentation is written differently.</summary>
    internal IReadOnlyCollection<string> TypeDocReplaceKeys => _typeDocReplacements.Keys;

    /// <summary>
    /// Loads <c>fixups.json</c> and <c>platform-symbols.json</c> from an overlay
    /// directory. Missing files are treated as empty.
    /// </summary>
    /// <param name="overlayDirectory">Directory holding the overlay files.</param>
    /// <returns>The loaded overlays.</returns>
    internal static Overlays Load(string overlayDirectory)
    {
        FixupsFile fixups = ReadJson<FixupsFile>(Path.Combine(overlayDirectory, "fixups.json")) ?? new FixupsFile();
        PlatformSymbolsFile platforms =
            ReadJson<PlatformSymbolsFile>(Path.Combine(overlayDirectory, "platform-symbols.json"))
            ?? new PlatformSymbolsFile();

        HashSet<string> skip = new(StringComparer.Ordinal);
        foreach (string identifier in fixups.Skip ?? [])
        {
            skip.Add(identifier);
        }

        HashSet<string> handBound = new(StringComparer.Ordinal);
        foreach (string identifier in fixups.HandBound ?? [])
        {
            handBound.Add(identifier);
        }

        HashSet<string> forceOpaque = new(StringComparer.Ordinal);
        foreach (string identifier in fixups.ForceOpaque ?? [])
        {
            forceOpaque.Add(identifier);
        }

        Dictionary<string, string> rename = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.Rename ?? [])
        {
            rename[entry.Key] = entry.Value;
        }

        Dictionary<string, AnnotationOverride> annotations = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, AnnotationOverride> entry in fixups.AnnotationOverrides ?? [])
        {
            annotations[entry.Key] = entry.Value;
        }

        Dictionary<string, ArrayOverride> arrayOverrides = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ArrayOverride> entry in fixups.ArrayOverrides ?? [])
        {
            arrayOverrides[entry.Key] = entry.Value;
        }

        Dictionary<string, PlatformSupport> symbols = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, PlatformSupport> entry in platforms.Symbols ?? [])
        {
            symbols[entry.Key] = entry.Value;
        }

        Dictionary<string, string> returnTypes = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.ReturnTypeOverrides ?? [])
        {
            returnTypes[entry.Key] = entry.Value;
        }

        Dictionary<string, FieldSkip> fieldSkips = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, FieldSkip> entry in fixups.FieldSkips ?? [])
        {
            fieldSkips[entry.Key] = entry.Value;
        }

        Dictionary<string, InstanceField> instanceFields = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, InstanceField> entry in fixups.InstanceFields ?? [])
        {
            instanceFields[entry.Key] = entry.Value;
        }

        Dictionary<string, FieldAnnotation> fieldAnnotations = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, FieldAnnotation> entry in fixups.FieldAnnotations ?? [])
        {
            fieldAnnotations[entry.Key] = entry.Value;
        }

        HashSet<string> subclassable = new(StringComparer.Ordinal);
        foreach (string qualifiedName in fixups.Subclassable ?? [])
        {
            subclassable.Add(qualifiedName);
        }

        Dictionary<string, string> skipVirtuals = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.SkipVirtuals ?? [])
        {
            skipVirtuals[entry.Key] = entry.Value;
        }

        Dictionary<string, string> vfuncDefaults = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.VfuncDefaults ?? [])
        {
            vfuncDefaults[entry.Key] = entry.Value;
        }

        Dictionary<string, string> vfuncDocNotes = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.VfuncDocNotes ?? [])
        {
            vfuncDocNotes[entry.Key] = entry.Value;
        }

        Dictionary<string, string> vfuncNonNullReturns = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.VfuncNonNullReturns ?? [])
        {
            vfuncNonNullReturns[entry.Key] = entry.Value;
        }

        HashSet<string> vfuncSpans = new(StringComparer.Ordinal);
        foreach (string key in fixups.VfuncSpans ?? [])
        {
            vfuncSpans.Add(key);
        }

        HashSet<string> vfuncFloatingReturns = new(StringComparer.Ordinal);
        foreach (string key in fixups.VfuncFloatingReturns ?? [])
        {
            vfuncFloatingReturns.Add(key);
        }

        HashSet<string> vfuncSiblingArguments = new(StringComparer.Ordinal);
        foreach (string key in fixups.VfuncSiblingArguments ?? [])
        {
            vfuncSiblingArguments.Add(key);
        }

        HashSet<string> lentOpaqueRecords = new(StringComparer.Ordinal);
        foreach (string key in fixups.LentOpaqueRecords ?? [])
        {
            lentOpaqueRecords.Add(key);
        }

        Dictionary<string, string> vfuncFailureValues = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.VfuncFailureValues ?? [])
        {
            vfuncFailureValues[entry.Key] = entry.Value;
        }

        Dictionary<string, string> instanceKeyedCallbacks = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.InstanceKeyedCallbacks ?? [])
        {
            instanceKeyedCallbacks[entry.Key] = entry.Value;
        }

        Dictionary<string, string> docNotes = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.DocNotes ?? [])
        {
            docNotes[entry.Key] = entry.Value;
        }

        Dictionary<string, string> signalDocNotes = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.SignalDocNotes ?? [])
        {
            signalDocNotes[entry.Key] = entry.Value;
        }

        Dictionary<string, IReadOnlyList<string>> preconditions = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, Precondition> entry in fixups.Preconditions ?? [])
        {
            // An entry with nothing to emit would be a key that reads as a
            // guard and guards nothing, which is worse than no entry at all:
            // the stale key report would stay silent about it, because the key
            // is consumed by the very member it leaves unguarded.
            if (entry.Value.Statements is not { Count: > 0 } statements)
            {
                throw new InvalidDataException(
                    $"The precondition entry '{entry.Key}' declares no statements; "
                    + "a precondition without a statement guards nothing.");
            }

            // A blank element is the same silence one entry further in: it
            // writes an empty line and guards nothing. A null one is worse,
            // because the writer reads its length and the failure names no key
            // at all.
            if (statements.Any(static statement => string.IsNullOrWhiteSpace(statement)))
            {
                throw new InvalidDataException(
                    $"The precondition entry '{entry.Key}' declares a blank statement; "
                    + "a precondition without a statement guards nothing.");
            }

            preconditions[entry.Key] = [.. statements];
        }

        Dictionary<string, string> handOverRefusals = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.HandOverRefusals ?? [])
        {
            // The value is the citation of the C lines the reading rests on,
            // and the reading is the whole of the entry: an entry that cites
            // nothing states that a refusal leaves the reference untaken
            // without saying where the C says so, which is the one thing a
            // reader of the overlay has to be able to check.
            if (string.IsNullOrWhiteSpace(entry.Value))
            {
                throw new InvalidDataException(
                    $"The hand over refusal entry '{entry.Key}' cites no C lines; "
                    + "the citation is what the entry rests on.");
            }

            handOverRefusals[entry.Key] = entry.Value;
        }

        Dictionary<string, IReadOnlyList<string>> docStrips = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<string>> entry in fixups.DocStrip ?? [])
        {
            // An entry that names no substring takes nothing out, and a
            // blank one would be found in every documentation there is: both
            // are a key that reads as an edit and edits nothing, which the
            // stale key report cannot catch because the key is consumed by
            // the very member it leaves unchanged.
            if (entry.Value is not { Count: > 0 } substrings)
            {
                throw new InvalidDataException(
                    $"The documentation strip entry '{entry.Key}' names no substring; "
                    + "an entry that takes nothing out changes nothing.");
            }

            if (substrings.Any(static substring => string.IsNullOrWhiteSpace(substring)))
            {
                throw new InvalidDataException(
                    $"The documentation strip entry '{entry.Key}' names a blank substring; "
                    + "an entry that takes nothing out changes nothing.");
            }

            docStrips[entry.Key] = [.. substrings];
        }

        Dictionary<string, IReadOnlyList<DocReplacement>> typeDocReplacements =
            new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<DocReplacement>> entry in fixups.TypeDocReplace ?? [])
        {
            // The same three silences a documentation strip has, in the shape a
            // replacement takes them: an entry that names no replacement, one
            // whose 'old' stands in every documentation there is, and one that
            // writes back what it read. None of the three is caught by the stale
            // key report, because the key is consumed by the very type it leaves
            // unchanged.
            if (entry.Value is not { Count: > 0 } replacements)
            {
                throw new InvalidDataException(
                    $"The type documentation replacement entry '{entry.Key}' names no replacement; "
                    + "an entry that replaces nothing changes nothing.");
            }

            foreach (DocReplacement replacement in replacements)
            {
                if (string.IsNullOrWhiteSpace(replacement.Old))
                {
                    throw new InvalidDataException(
                        $"The type documentation replacement entry '{entry.Key}' replaces a blank "
                        + "substring; an entry that replaces nothing changes nothing.");
                }

                if (string.Equals(replacement.Old, replacement.New, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"The type documentation replacement entry '{entry.Key}' writes '"
                        + replacement.Old + "' back over itself; an entry that replaces nothing "
                        + "changes nothing.");
                }
            }

            typeDocReplacements[entry.Key] = [.. replacements];
        }

        HashSet<string> vfuncIdentityBuffers = new(StringComparer.Ordinal);
        foreach (string key in fixups.VfuncIdentityBuffers ?? [])
        {
            vfuncIdentityBuffers.Add(key);
        }

        return new Overlays(
            skip,
            handBound,
            forceOpaque,
            rename,
            annotations,
            arrayOverrides,
            symbols,
            returnTypes,
            fieldSkips,
            fieldAnnotations,
            instanceFields,
            subclassable,
            skipVirtuals,
            vfuncDefaults,
            vfuncIdentityBuffers,
            vfuncNonNullReturns,
            vfuncDocNotes,
            vfuncSpans,
            vfuncFloatingReturns,
            vfuncSiblingArguments,
            lentOpaqueRecords,
            vfuncFailureValues,
            instanceKeyedCallbacks,
            docNotes,
            signalDocNotes,
            preconditions,
            handOverRefusals,
            docStrips,
            typeDocReplacements);
    }

    /// <summary>Tests whether a symbol is skipped by the overlays.</summary>
    /// <param name="key">A <c>c:identifier</c> or a qualified gir name.</param>
    /// <returns><see langword="true"/> when the symbol must not be generated.</returns>
    internal bool IsSkipped(string? key) => key is not null && _skip.Contains(key);

    /// <summary>
    /// Tests whether the managed surface of a symbol is hand written.
    /// </summary>
    /// <param name="key">A <c>c:identifier</c>.</param>
    /// <returns><see langword="true"/> when the symbol is listed as hand bound.</returns>
    /// <remarks>
    /// This says nothing about whether the symbol is generated: it is the
    /// annotation the skip report groups by, so that a call the bindings cover
    /// by hand is not counted among the ones they do not cover at all.
    /// </remarks>
    internal bool IsHandBound(string? key) => key is not null && _handBound.Contains(key);

    /// <summary>
    /// Tests whether a record must be wrapped behind a pointer instead of being
    /// projected as a value type.
    /// </summary>
    /// <param name="qualifiedName">The qualified gir name of the record.</param>
    /// <returns><see langword="true"/> when the record is forced opaque.</returns>
    internal bool IsForcedOpaque(string qualifiedName) => _forceOpaque.Contains(qualifiedName);

    /// <summary>Looks up a name override.</summary>
    /// <param name="key">A qualified gir name or a <c>c:identifier</c>.</param>
    /// <param name="name">The overriding C# name.</param>
    /// <returns><see langword="true"/> when an override exists.</returns>
    internal bool TryGetRename(string key, [NotNullWhen(true)] out string? name) =>
        _rename.TryGetValue(key, out name);

    /// <summary>Looks up the C# type a member returns instead of the mapped one.</summary>
    /// <param name="cIdentifier">The <c>c:identifier</c> of the callable.</param>
    /// <param name="type">The overriding C# type.</param>
    /// <returns><see langword="true"/> when an override exists.</returns>
    internal bool TryGetReturnTypeOverride(string cIdentifier, [NotNullWhen(true)] out string? type) =>
        _returnTypes.TryGetValue(cIdentifier, out type);

    /// <summary>Looks up an annotation correction.</summary>
    /// <param name="key">A <c>c:identifier</c>, optionally suffixed with <c>#parameter</c>.</param>
    /// <returns>The correction, or <see langword="null"/>.</returns>
    internal AnnotationOverride? GetAnnotationOverride(string key) =>
        _annotations.TryGetValue(key, out AnnotationOverride? value) ? value : null;

    /// <summary>Looks up the correction of an array.</summary>
    /// <param name="key">A <c>c:identifier</c> suffixed with <c>#parameter</c> or <c>#return</c>.</param>
    /// <returns>The correction, or <see langword="null"/>.</returns>
    internal ArrayOverride? GetArrayOverride(string key) =>
        _arrayOverrides.TryGetValue(key, out ArrayOverride? value) ? value : null;

    /// <summary>Looks up the member that answers a record field.</summary>
    /// <param name="key">
    /// The <c>c:type</c> of the record and the gir name of the field, for
    /// example <c>GstPadProbeInfo.flow_ret</c>.
    /// </param>
    /// <returns>The entry, or <see langword="null"/> when there is none.</returns>
    internal FieldSkip? GetFieldSkip(string key) =>
        _fieldSkips.TryGetValue(key, out FieldSkip? value) ? value : null;

    /// <summary>Looks up the correction of a record field.</summary>
    /// <param name="key">
    /// The <c>c:type</c> of the record and the gir name of the field, for
    /// example <c>GstRTSPUrl.host</c>.
    /// </param>
    /// <returns>The entry, or <see langword="null"/> when there is none.</returns>
    internal FieldAnnotation? GetFieldAnnotation(string key) =>
        _fieldAnnotations.TryGetValue(key, out FieldAnnotation? value) ? value : null;

    /// <summary>Returns the allowlist entry of one instance field, if there is one.</summary>
    /// <param name="key">The <c>c:type</c> of the class and the gir name of the field.</param>
    /// <returns>The entry, or <see langword="null"/> when the field is not allowlisted.</returns>
    internal InstanceField? GetInstanceField(string key) =>
        _instanceFields.TryGetValue(key, out InstanceField? value) ? value : null;

    /// <summary>
    /// Tests whether a class may be derived from by a managed subclass, which
    /// is what makes the generator emit its class struct mirror and its
    /// virtual method surface.
    /// </summary>
    /// <param name="qualifiedName">The qualified gir name of the class.</param>
    /// <returns><see langword="true"/> when the class is listed.</returns>
    /// <remarks>
    /// The list is an allowlist rather than everything the girs declare: a
    /// class struct mirror is emitted for every parent of a listed class as
    /// well, because the mirror of a subclassable class embeds them, but only
    /// the listed ones get a managed surface.
    /// </remarks>
    internal bool IsSubclassable(string qualifiedName) => _subclassable.Contains(qualifiedName);

    /// <summary>Tests whether one virtual method is kept out of the surface.</summary>
    /// <param name="key">The slot, as <c>Gst.Element::pad_added</c>.</param>
    /// <returns><see langword="true"/> when the slot is listed.</returns>
    /// <remarks>
    /// The mirror still lays the slot out — a class struct is an ABI and not a
    /// menu — so what a skip removes is the <c>OnX</c> member, its trampoline
    /// and its chain-up helper.
    /// </remarks>
    internal bool IsVirtualSkipped(string key) => _skipVirtuals.ContainsKey(key);

    /// <summary>Reads why one virtual method is kept out of the surface.</summary>
    /// <param name="key">The slot, as <c>Gst.Element::pad_added</c>.</param>
    /// <returns>The reason the skip ledger prints.</returns>
    internal string VirtualSkipReason(string key) =>
        _skipVirtuals.TryGetValue(key, out string? reason) ? reason : "Manual";

    /// <summary>
    /// Looks up what a chain-up answers when the parent class leaves the slot
    /// null.
    /// </summary>
    /// <param name="key">The slot, as <c>GstBase.BaseSrc::start</c>.</param>
    /// <param name="expression">
    /// The C# expression the chain-up returns, which may name the parameters of
    /// the chain-up helper.
    /// </param>
    /// <returns><see langword="true"/> when a default is declared.</returns>
    /// <remarks>
    /// A slot with no entry has no documented default, so its chain-up throws
    /// rather than inventing one: those are the slots a subclass has to
    /// implement, which is what the base class states by calling them
    /// unguarded.
    /// </remarks>
    internal bool TryGetVfuncDefault(string key, [NotNullWhen(true)] out string? expression) =>
        _vfuncDefaults.TryGetValue(key, out expression);

    /// <summary>
    /// Tests whether a buffer parameter may be handed back unchanged instead of
    /// as a new reference.
    /// </summary>
    /// <param name="key">The parameter, as <c>GstBase.BaseSrc::create#buf</c>.</param>
    /// <returns><see langword="true"/> when the parameter is listed.</returns>
    /// <remarks>
    /// The caller of such a slot compares the pointer it gets back with the one
    /// it passed in and only releases the input when the two differ, so a
    /// reference taken on the way out of an override that hands back what it
    /// was given would never be released.
    /// </remarks>
    internal bool IsIdentityBuffer(string key) => _vfuncIdentityBuffers.Contains(key);

    /// <summary>
    /// Tests whether the slot behind a block parameter only reads it, which
    /// makes the parameter a <c>ReadOnlySpan</c> instead of a <c>Span</c>.
    /// </summary>
    /// <param name="key">The parameter, as <c>GstAudio.AudioSink::write#data</c>.</param>
    /// <returns><see langword="true"/> when the parameter is listed.</returns>
    /// <remarks>
    /// The gir spells such a block as an array counted by another parameter,
    /// and the constness of the C declaration is the only thing that would say
    /// which way the data travels - which a <c>gpointer</c> does not say.
    /// </remarks>
    internal bool IsReadOnlySpan(string key) => _vfuncSpans.Contains(key);

    /// <summary>
    /// Tests whether the answer of a slot leaves the trampoline as one new
    /// floating reference the caller of the slot owns.
    /// </summary>
    /// <param name="key">The slot, as <c>Gst.Device::create_element</c>.</param>
    /// <returns><see langword="true"/> when the slot is listed.</returns>
    /// <remarks>
    /// No gir transfer kind spells "floating", and the one the corpus uses for
    /// such a slot is <c>none</c>, which reads as a borrow. The difference is a
    /// use-after-free rather than a nuance: the caller of
    /// gst_device_create_element owns the reference the slot produced and drops
    /// it with a bare unref (gstdevice.c:206-226).
    /// </remarks>
    internal bool IsFloatingReturn(string key) => _vfuncFloatingReturns.Contains(key);

    /// <summary>
    /// Tests whether a parameter hands the slot an instance of the type the
    /// instance of the slot has.
    /// </summary>
    /// <param name="key">The parameter, as <c>GES.TimelineElement::deep_copy#copy</c>.</param>
    /// <returns><see langword="true"/> when the parameter is listed.</returns>
    /// <remarks>
    /// Nothing in the gir says that the object a slot is handed is a second
    /// instance of the very type the slot runs for, and the difference is one
    /// of ownership rather than of type: the base class has just created it and
    /// still means to drop the reference it holds, so the wrapper must be
    /// resolved without settling anything.
    /// </remarks>
    internal bool IsSiblingArgument(string key) => _vfuncSiblingArguments.Contains(key);

    /// <summary>Tests whether an opaque record is one a virtual method is lent.</summary>
    /// <param name="qualifiedName">The record, as <c>GstVideo.VideoFrame</c>.</param>
    /// <returns><see langword="true"/> when the record is listed.</returns>
    /// <remarks>
    /// The wrapper of such a record holds nothing but the pointer, and the
    /// pointer is regularly an address on the stack of the caller of the slot,
    /// so the trampoline forgets it when the call returns. The set is stated
    /// here rather than derived, because the modules are planned and emitted one
    /// after the other: the lenders of a record of the core module are slots of
    /// modules that have not been reached when the core module is written out.
    /// </remarks>
    internal bool IsLentOpaqueRecord(string qualifiedName) => _lentOpaqueRecords.Contains(qualifiedName);

    /// <summary>
    /// Looks up what a trampoline answers when the managed override threw, for
    /// a slot whose caller reads more into the zero of the return type than
    /// "it failed".
    /// </summary>
    /// <param name="key">The slot, as <c>GstAudio.AudioSink::write</c>.</param>
    /// <param name="failure">Receives the C# expression.</param>
    /// <returns>Whether the slot declares a failure value of its own.</returns>
    internal bool TryGetVfuncFailureValue(string key, [NotNullWhen(true)] out string? failure) =>
        _vfuncFailureValues.TryGetValue(key, out failure);

    /// <summary>
    /// Looks up the value a trampoline answers for a slot whose caller does not
    /// tolerate a NULL answer, when the managed override answered none.
    /// </summary>
    /// <param name="key">The key of the slot.</param>
    /// <param name="failure">Receives the C# expression.</param>
    /// <returns>Whether the answer of the slot may not be null.</returns>
    internal bool TryGetVfuncNonNullReturn(string key, [NotNullWhen(true)] out string? failure) =>
        _vfuncNonNullReturns.TryGetValue(key, out failure);

    /// <summary>
    /// Looks up the hand written note the documentation of a slot carries, for
    /// the part of its contract that neither the gir nor the marshalling states.
    /// </summary>
    /// <param name="key">The key of the slot.</param>
    /// <param name="note">Receives the sentence.</param>
    /// <returns>Whether the slot has a note.</returns>
    internal bool TryGetVfuncDocNote(string key, [NotNullWhen(true)] out string? note) =>
        _vfuncDocNotes.TryGetValue(key, out note);

    /// <summary>
    /// Looks up the storage slot a callback that carries no <c>user_data</c>
    /// occupies on the instance it is installed on.
    /// </summary>
    /// <param name="key">The qualified gir name of the callback.</param>
    /// <param name="slot">Receives the slot name.</param>
    /// <returns>Whether the callback is keyed by its instance.</returns>
    internal bool TryGetInstanceKeyedSlot(string key, [NotNullWhen(true)] out string? slot) =>
        _instanceKeyedCallbacks.TryGetValue(key, out slot);

    /// <summary>
    /// Looks up the hand written note the documentation of a callable carries,
    /// for the part of its contract that neither the gir nor the marshalling
    /// states.
    /// </summary>
    /// <param name="key">The <c>c:identifier</c> of the callable.</param>
    /// <param name="note">Receives the sentence.</param>
    /// <returns>Whether the callable has a note.</returns>
    internal bool TryGetDocNote(string key, [NotNullWhen(true)] out string? note) =>
        _docNotes.TryGetValue(key, out note);

    /// <summary>
    /// Looks up the hand written note the documentation of a signal carries,
    /// for the part of its contract that neither the gir nor the marshalling
    /// states.
    /// </summary>
    /// <param name="key">The GObject spelling of the signal, <c>Ns.Type::signal-name</c>.</param>
    /// <param name="note">Receives the sentence.</param>
    /// <returns>Whether the signal has a note.</returns>
    internal bool TryGetSignalDocNote(string key, [NotNullWhen(true)] out string? note) =>
        _signalDocNotes.TryGetValue(key, out note);

    /// <summary>
    /// Looks up the statements the generated body of a callable runs before it
    /// marshals anything.
    /// </summary>
    /// <param name="cIdentifier">The <c>c:identifier</c> of the callable.</param>
    /// <param name="statements">Receives the statements, in the order they are written.</param>
    /// <returns>Whether the callable carries preconditions.</returns>
    internal bool TryGetPreconditions(
        string cIdentifier,
        [NotNullWhen(true)] out IReadOnlyList<string>? statements) =>
        _preconditions.TryGetValue(cIdentifier, out statements);

    /// <summary>
    /// Looks up whether a callable that refuses what it is handed leaves the
    /// reference minted for the argument untaken, so that the generated body
    /// releases it again on the refusal path.
    /// </summary>
    /// <param name="cIdentifier">The <c>c:identifier</c> of the callable.</param>
    /// <param name="citation">Receives the citation of the C lines the reading rests on.</param>
    /// <returns>Whether the callable releases a minted reference on refusal.</returns>
    internal bool TryGetHandOverRefusal(string cIdentifier, [NotNullWhen(true)] out string? citation) =>
        _handOverRefusals.TryGetValue(cIdentifier, out citation);

    /// <summary>
    /// Looks up the substrings taken out of the gir documentation of a
    /// callable before it is split into paragraphs.
    /// </summary>
    /// <param name="cIdentifier">The <c>c:identifier</c> of the callable.</param>
    /// <param name="substrings">Receives the substrings, in the order they are written.</param>
    /// <returns>Whether the documentation of the callable is shortened.</returns>
    internal bool TryGetDocStrips(
        string cIdentifier,
        [NotNullWhen(true)] out IReadOnlyList<string>? substrings) =>
        _docStrips.TryGetValue(cIdentifier, out substrings);

    /// <summary>
    /// Looks up the parts of the gir documentation of a type that are written
    /// differently in the generated documentation.
    /// </summary>
    /// <param name="qualifiedName">The qualified gir name of the type.</param>
    /// <param name="replacements">Receives the replacements, in the order they are written.</param>
    /// <returns>Whether the documentation of the type is rewritten.</returns>
    internal bool TryGetTypeDocReplacements(
        string qualifiedName,
        [NotNullWhen(true)] out IReadOnlyList<DocReplacement>? replacements) =>
        _typeDocReplacements.TryGetValue(qualifiedName, out replacements);

    /// <summary>Looks up the platform availability of a native symbol.</summary>
    /// <param name="cIdentifier">The <c>c:identifier</c> of the symbol.</param>
    /// <returns>The availability, or <see langword="null"/> when the symbol is portable.</returns>
    internal PlatformSupport? GetPlatformSupport(string cIdentifier) =>
        _platforms.TryGetValue(cIdentifier, out PlatformSupport? value) ? value : null;

    private static T? ReadJson<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, SerializerOptions);
    }

    private sealed class FixupsFile
    {
        public List<string>? Skip { get; set; }

        public List<string>? HandBound { get; set; }

        public List<string>? ForceOpaque { get; set; }

        public Dictionary<string, string>? Rename { get; set; }

        public Dictionary<string, AnnotationOverride>? AnnotationOverrides { get; set; }

        public Dictionary<string, ArrayOverride>? ArrayOverrides { get; set; }

        public Dictionary<string, string>? ReturnTypeOverrides { get; set; }

        public Dictionary<string, FieldSkip>? FieldSkips { get; set; }

        public Dictionary<string, FieldAnnotation>? FieldAnnotations { get; set; }

        public Dictionary<string, InstanceField>? InstanceFields { get; set; }

        public List<string>? Subclassable { get; set; }

        public Dictionary<string, string>? SkipVirtuals { get; set; }

        public Dictionary<string, string>? VfuncDefaults { get; set; }

        public List<string>? VfuncIdentityBuffers { get; set; }

        public Dictionary<string, string>? VfuncNonNullReturns { get; set; }

        public Dictionary<string, string>? VfuncDocNotes { get; set; }

        public List<string>? VfuncSpans { get; set; }

        public List<string>? VfuncFloatingReturns { get; set; }

        public List<string>? VfuncSiblingArguments { get; set; }

        public List<string>? LentOpaqueRecords { get; set; }

        public Dictionary<string, string>? VfuncFailureValues { get; set; }

        public Dictionary<string, string>? InstanceKeyedCallbacks { get; set; }

        public Dictionary<string, string>? DocNotes { get; set; }

        public Dictionary<string, string>? SignalDocNotes { get; set; }

        public Dictionary<string, Precondition>? Preconditions { get; set; }

        public Dictionary<string, string>? HandOverRefusals { get; set; }

        public Dictionary<string, List<string>>? DocStrip { get; set; }

        public Dictionary<string, List<DocReplacement>>? TypeDocReplace { get; set; }
    }

    private sealed class PlatformSymbolsFile
    {
        public Dictionary<string, PlatformSupport>? Symbols { get; set; }
    }
}
