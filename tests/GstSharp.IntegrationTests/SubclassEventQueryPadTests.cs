using Gst;
using Gst.Base;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The three shapes the mini object borrow unlocked: an event a slot takes
/// over, a query a slot is lent and writes into, and a pad an override creates
/// and hands back.
/// </summary>
/// <remarks>
/// The slots are called through the class struct, the way GStreamer calls
/// them, so that the reference counts around the call are observable. Nothing
/// here is version dependent.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class SubclassEventQueryPadTests
{
    private const long Duration = 1234567890L;

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassEventQueryPadTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// An <c>event</c> override is handed the event: the wrapper adopts it, the
    /// chain-up passes the ownership on to the parent slot, and the wrapper is
    /// consumed by that — using it afterwards is a mistake the wrapper catches.
    /// The event itself is released exactly once.
    /// </summary>
    [Fact]
    public void TheEventOverrideHandsItsEventOnWhenItChainsUp()
    {
        using ProbeEventSink sink = new();

        nint mine = NewCustomEvent();

        // A reference of the test's own, so that the handle stays readable
        // after the sink has released the one it was given.
        _ = TestNatives.MiniObjectRef(mine);
        Assert.Equal(2, Refcount(mine));

        bool handled = CallEvent(sink, mine);

        _output.WriteLine(FormattableString.Invariant(
            $"chain-up: handled={handled}, seen={sink.Seen}, consumed={sink.WrapperWasConsumed}, refcount={Refcount(mine)}"));

        Assert.True(handled);
        Assert.Equal(EventType.CustomDownstream, sink.Seen);

        // The chain-up consumed the wrapper: the ownership went to the parent
        // slot and the wrapper stands for nothing afterwards.
        Assert.True(sink.WrapperWasConsumed);

        // The parent slot released the reference the sink was given, and only
        // the test's own is left.
        Assert.Equal(1, Refcount(mine));
        TestNatives.MiniObjectUnref(mine);
    }

    /// <summary>
    /// An <c>event</c> override that does not chain up owns the event all the
    /// same: the wrapper releases it when the call ends, so the reference the
    /// caller handed over is neither leaked nor released twice.
    /// </summary>
    [Fact]
    public void TheEventOverrideThatKeepsItsEventReleasesItOnce()
    {
        using ProbeEventSink sink = new() { ChainUpTheEvent = false };

        nint mine = NewCustomEvent();
        _ = TestNatives.MiniObjectRef(mine);

        bool handled = CallEvent(sink, mine);

        _output.WriteLine(FormattableString.Invariant(
            $"no chain-up: handled={handled}, refcount={Refcount(mine)}"));

        Assert.True(handled);
        Assert.Equal(EventType.CustomDownstream, sink.Seen);
        Assert.Equal(1, Refcount(mine));

        TestNatives.MiniObjectUnref(mine);
    }

    /// <summary>
    /// A <c>query</c> override of an element is lent the query and writes the
    /// answer into it: the caller reads what the override wrote, the query is
    /// still the caller's, and the wrapper is dead once the call is over.
    /// </summary>
    [Fact]
    public void TheElementQueryOverrideWritesIntoTheQueryItIsLent()
    {
        using ProbeQueryElement element = new();
        using Query query = Query.NewDuration(Format.Time);

        Assert.Equal(1, Refcount(query.Handle));

        bool answered = CallElementQuery(element, query.Handle);

        query.ParseDuration(out Format format, out long duration);
        _output.WriteLine(FormattableString.Invariant(
            $"element query: answered={answered}, {format} {duration}, refcount={Refcount(query.Handle)}"));

        Assert.True(answered);
        Assert.Equal(Format.Time, format);
        Assert.Equal(Duration, duration);

        // The lent query took no reference of its own on the way in or out.
        Assert.Equal(1, Refcount(query.Handle));

        // And the wrapper the override was given is over: keeping it is what
        // the borrow refuses.
        Query kept = Assert.IsType<Query>(element.Kept);
        Assert.True(kept.IsDisposed);
        _ = Assert.Throws<ObjectDisposedException>(() => kept.GetStructure());
    }

    /// <summary>
    /// The same for the <c>query</c> slot of <c>GstBaseSrc</c>, which is a
    /// different slot of the same name one class further down.
    /// </summary>
    [Fact]
    public void TheBaseSrcQueryOverrideWritesIntoTheQueryItIsLent()
    {
        using ProbeQuerySrc source = new();
        using Query query = Query.NewDuration(Format.Time);

        Gst.Base.BaseSrcClassRaw* klass = (Gst.Base.BaseSrcClassRaw*)ClassOf(source);
        Assert.NotEqual(nint.Zero, klass->Query);

        bool answered = ((delegate* unmanaged[Cdecl]<nint, nint, int>)klass->Query)(
            source.Handle, query.Handle) != 0;

        query.ParseDuration(out Format format, out long duration);
        _output.WriteLine(FormattableString.Invariant(
            $"basesrc query: answered={answered}, {format} {duration}, refcount={Refcount(query.Handle)}"));

        Assert.True(answered);
        Assert.Equal(Duration, duration);
        Assert.Equal(Format.Time, format);
        Assert.Equal(1, Refcount(query.Handle));
    }

    /// <summary>
    /// A <c>request_new_pad</c> override creates the pad, adds it to the
    /// element and answers it borrowed: the caller of
    /// <see cref="Element.RequestPad(PadTemplate, string, Caps)"/> is handed a
    /// reference of its own and the element keeps the one it took by adding the
    /// pad.
    /// </summary>
    [Fact]
    public void TheRequestNewPadOverrideHandsBackThePadItAdded()
    {
        using ProbeRequestElement element = new();

        PadTemplate template = element.GetPadTemplate("sink_%u")
            ?? throw new InvalidOperationException("The request template is missing.");

        using Pad? requested = element.RequestPad(template, "sink_0", null);

        Assert.NotNull(requested);
        Assert.Equal("sink_0", requested.GetName());
        Assert.Equal(1, element.Requested);

        using Pad? found = element.GetStaticPad("sink_0");
        Assert.NotNull(found);
        Assert.Equal(requested.Handle, found.Handle);

        // The element owns the pad it added and the caller owns the answer, so
        // the object outlives the wrapper the override built. GObject holds its
        // count right behind the class pointer, which is frozen ABI.
        int references = ObjectRefcount(requested.Handle);
        _output.WriteLine(FormattableString.Invariant($"pad references: {references}"));

        // One for the element, which owns the pad it added, and one for the
        // caller: the wrapper of an already interned object is not a second
        // owner, which is why the lookup above did not raise the count.
        Assert.Equal(2, references);

        element.ReleaseRequestPad(requested);
        Assert.Null(element.GetStaticPad("sink_0"));
    }

    private static nint NewCustomEvent()
    {
        using Structure structure = Structure.NewEmpty("gstsharp-test-mark");
        using Event @event = Event.NewCustom(EventType.CustomDownstream, structure);

        // The wrapper is disposed at the end of the statement, so the handle is
        // handed on with the reference the test then owns alone.
        return TestNatives.MiniObjectRef(@event.Handle);
    }

    private static bool CallEvent(BaseSink sink, nint @event)
    {
        Gst.Base.BaseSinkClassRaw* klass = (Gst.Base.BaseSinkClassRaw*)ClassOf(sink);

        Assert.NotEqual(nint.Zero, klass->Event);
        return ((delegate* unmanaged[Cdecl]<nint, nint, int>)klass->Event)(sink.Handle, @event) != 0;
    }

    private static bool CallElementQuery(Element element, nint query)
    {
        Gst.ElementClassRaw* klass = (Gst.ElementClassRaw*)ClassOf(element);

        Assert.NotEqual(nint.Zero, klass->Query);
        return ((delegate* unmanaged[Cdecl]<nint, nint, int>)klass->Query)(element.Handle, query) != 0;
    }

    private static nint ClassOf(Gst.GObject.Object instance) => *(nint*)instance.Handle;

    private static int Refcount(nint handle) => ((MiniObjectRaw*)handle)->Refcount;

    /// <summary>Reads the reference count of a <c>GObject</c>.</summary>
    /// <param name="handle">The object.</param>
    /// <returns>How many references the object has.</returns>
    /// <remarks>
    /// <c>GObject</c> lays its count out right behind the class pointer of
    /// <c>GTypeInstance</c>, and that layout is as frozen as the class struct
    /// offsets the ABI probes read.
    /// </remarks>
    private static int ObjectRefcount(nint handle) => *(int*)(handle + sizeof(nint));
}

/// <summary>
/// A managed sink whose <c>event</c> override sees the event it is handed and
/// either passes it on or keeps it.
/// </summary>
internal sealed class ProbeEventSink : BaseSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeEventSink";

    private static readonly PadTemplate SinkTemplate = ProbeTemplates.Any("sink", PadDirection.Sink);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        config =>
        {
            config.SetMetadata(
                "GstSharp probe event sink",
                "Sink/Testing",
                "Observes the events it is handed",
                "GstSharp.Net integration tests");
            config.AddPadTemplate(SinkTemplate);
        },
        EventOverride);

    /// <summary>Creates a managed sink.</summary>
    internal ProbeEventSink()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets whether the override passes the event on.</summary>
    internal bool ChainUpTheEvent { get; init; } = true;

    /// <summary>Gets the type of the last event the override saw.</summary>
    internal EventType Seen { get; private set; }

    /// <summary>Gets whether the chain-up consumed the wrapper of the event.</summary>
    internal bool WrapperWasConsumed { get; private set; }

    /// <inheritdoc/>
    protected override bool OnEvent(Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        Seen = @event.Type;

        if (!ChainUpTheEvent)
        {
            // The event was handed over, and the wrapper releases it when the
            // call ends.
            return true;
        }

        bool handled = ChainUpEvent(@event);
        WrapperWasConsumed = @event.IsDisposed;
        return handled;
    }
}

/// <summary>
/// A managed element whose <c>query</c> override answers a duration query out
/// of the query it is lent.
/// </summary>
internal sealed class ProbeQueryElement : Element
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeQueryElement";

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        config => config.SetMetadata(
            "GstSharp probe query element",
            "Generic/Testing",
            "Answers a duration query",
            "GstSharp.Net integration tests"),
        QueryOverride);

    /// <summary>Creates a managed element.</summary>
    internal ProbeQueryElement()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the wrapper the last call was given.</summary>
    internal Query? Kept { get; private set; }

    /// <inheritdoc/>
    protected override bool OnQuery(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);

        Kept = query;

        if (query.Type != QueryType.Duration)
        {
            return ChainUpQuery(query);
        }

        query.SetDuration(Format.Time, 1234567890L);
        return true;
    }
}

/// <summary>
/// A managed source whose <c>query</c> override — the slot of
/// <c>GstBaseSrcClass</c>, not the one of <c>GstElementClass</c> — answers a
/// duration query.
/// </summary>
internal sealed class ProbeQuerySrc : BaseSrc
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeQuerySrc";

    private static readonly PadTemplate SrcTemplate = ProbeTemplates.Any("src", PadDirection.Src);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        config =>
        {
            config.SetMetadata(
                "GstSharp probe query source",
                "Source/Testing",
                "Answers a duration query",
                "GstSharp.Net integration tests");
            config.AddPadTemplate(SrcTemplate);
        },
        QueryOverride);

    /// <summary>Creates a managed source.</summary>
    internal ProbeQuerySrc()
        : base(Definition.NewInstance())
    {
    }

    /// <inheritdoc/>
    protected override bool OnQuery(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Type != QueryType.Duration)
        {
            return ChainUpQuery(query);
        }

        query.SetDuration(Format.Time, 1234567890L);
        return true;
    }
}

/// <summary>
/// A managed element whose <c>request_new_pad</c> override creates the pad,
/// adds it and answers it.
/// </summary>
internal sealed class ProbeRequestElement : Element
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeRequestElement";

    private static readonly PadTemplate SinkTemplate = NewRequestTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        config =>
        {
            config.SetMetadata(
                "GstSharp probe request element",
                "Generic/Testing",
                "Creates the pads it is asked for",
                "GstSharp.Net integration tests");
            config.AddPadTemplate(SinkTemplate);
        },
        RequestNewPadOverride);

    private int _requested;

    /// <summary>Creates a managed element.</summary>
    internal ProbeRequestElement()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets how many pads the override created.</summary>
    internal int Requested => Volatile.Read(ref _requested);

    /// <inheritdoc/>
    protected override Pad? OnRequestNewPad(PadTemplate templ, string? name, Caps? caps)
    {
        ArgumentNullException.ThrowIfNull(templ);

        Pad pad = Pad.NewFromTemplate(templ, name);

        if (!AddPad(pad))
        {
            pad.Dispose();
            return null;
        }

        _ = Interlocked.Increment(ref _requested);

        // The element owns the pad now; the answer is borrowed, and the caller
        // of gst_element_request_pad takes the reference it needs itself.
        return pad;
    }

    private static PadTemplate NewRequestTemplate()
    {
        using Caps caps = Caps.NewAny();

        return PadTemplate.New("sink_%u", PadDirection.Sink, PadPresence.Request, caps)
            ?? throw new InvalidOperationException("The request template could not be created.");
    }
}
