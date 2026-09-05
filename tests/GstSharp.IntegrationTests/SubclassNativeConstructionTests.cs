using Gst;
using Gst.Base;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;
using GObjectObject = Gst.GObject.Object;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Native-initiated construction: an instance of a managed subclass that
/// GStreamer creates - an element an element factory made, a pad a base class
/// built from a template, a pad a <c>create_new_pad</c> override answered - is
/// wrapped as the type it really is, and its overrides run for it.
/// </summary>
/// <remarks>
/// Everything here needs a registered <c>GType</c> and a running library, so it
/// is an integration test; what the fabrication decides before it touches
/// native code is pinned in <c>GstSharp.Core.Tests</c>.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class SubclassNativeConstructionTests
{
    private static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(10);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassNativeConstructionTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// An element an element factory made of a type that states its wrapper is
    /// the managed type itself: its override runs, no ancestor wrapper is
    /// reported, and the wrapper ends up owning the single reference a C#
    /// created element owns — one that is sunk, and that the object really
    /// goes away with.
    /// </summary>
    [Fact]
    public void AFactoryMadeElementIsWrappedAsItsManagedType()
    {
        Assert.True(ProbeFactoryElement.IsRegistered);

        using FallbackWatch watch = new(ProbeFactoryElement.RegisteredType);

        Element? element = ElementFactory.Make(ProbeFactoryElement.FactoryName, "made");

        Assert.NotNull(element);

        ProbeFactoryElement managed = Assert.IsType<ProbeFactoryElement>(element);
        nint handle = managed.Handle;

        // The factory hands out a floating reference, the fabrication adopts
        // the instance and the caller of FromNative owns the reference it was
        // given: what is left is the toggle reference of the wrapper, which is
        // the end state of an element C# created itself. One reference and the
        // floating flag still set would be that state's look-alike, and the
        // count alone cannot tell the two apart.
        Assert.Equal(1u, RefCountOf(handle));
        Assert.Equal(0, GObjectNative.ObjectIsFloating(handle));

        Assert.Equal(StateChangeReturn.Success, managed.SetState(State.Ready));
        Assert.Equal(StateChangeReturn.Success, managed.SetState(State.Null));

        _output.WriteLine(FormattableString.Invariant(
            $"transitions: {string.Join(", ", managed.Transitions)}"));

        Assert.Equal(
            [StateChange.NullToReady, StateChange.ReadyToNull],
            managed.Transitions);

        Assert.Equal(1u, RefCountOf(handle));
        Assert.Equal(0, GObjectNative.ObjectIsFloating(handle));
        Assert.Empty(watch.Reported);

        // And the one reference really is the wrapper's: disposing it frees the
        // element. A count cannot say that — an element that was left immortal
        // by an unref too few reads exactly the same — so the object is asked
        // to say it itself.
        WeakProbe.Arm(handle);
        managed.Dispose();

        Assert.Equal(1, WeakProbe.Freed);
    }

    /// <summary>
    /// The path the whole stage exists for: an element named in a pipeline
    /// description, whose wrapper no C# code ever asks for. GStreamer creates
    /// the instance, starts its task, and the first managed code it reaches is
    /// the <c>create</c> trampoline on that task's thread — which is where the
    /// wrapper is fabricated, exactly once, for the instance the bin holds.
    /// </summary>
    [Fact]
    public void AnElementAPipelineDescriptionNamedIsFabricatedOnItsStreamingThread()
    {
        Assert.True(ProbeFactoryPushSrc.IsRegistered);

        ProbeFactoryPushSrc.Reset();

        using FallbackWatch watch = new(ProbeFactoryPushSrc.RegisteredType);

        // Nothing in this description is a managed API call on the instance:
        // gst_parse_launch asks the factory for it and puts it in a bin.
        Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            ProbeFactoryPushSrc.FactoryName + " name=src ! fakesink name=sink"));

        nint handle;
        ProbeFactoryPushSrc managed;

        try
        {
            try
            {
                Assert.Equal(0, ProbeFactoryPushSrc.WrappersBuilt);

                using Bus bus = pipeline.GetBus();

                Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

                using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, BusTimeout);

                // Every wait here is bounded: a source that never ends has to make
                // the test red rather than hold the suite.
                Assert.NotNull(message);
                Assert.Equal(MessageType.Eos, message.Type);

                // One instance, one wrapper. A second one would mean the gate let
                // two threads through, or that a lookup missed what was interned.
                Assert.Equal(1, ProbeFactoryPushSrc.WrappersBuilt);

                managed = Assert.IsType<ProbeFactoryPushSrc>(ProbeFactoryPushSrc.LastWrapper);

                _output.WriteLine(FormattableString.Invariant(
                    $"test thread {Environment.CurrentManagedThreadId}, wrapper thread {ProbeFactoryPushSrc.WrapperThreadId}, create thread {managed.CreateThreadId}, produced {managed.Produced}"));

                // The fabrication happened where GStreamer was, not where the test
                // is: this is the streaming thread the whole ledger is written for.
                Assert.NotEqual(0, ProbeFactoryPushSrc.WrapperThreadId);
                Assert.NotEqual(Environment.CurrentManagedThreadId, ProbeFactoryPushSrc.WrapperThreadId);
                Assert.Equal(ProbeFactoryPushSrc.WrapperThreadId, managed.CreateThreadId);

                // The override really ran for this instance, over and over.
                Assert.Equal(ProbeFactoryPushSrc.BufferCount, managed.Produced);

                // Asking the bin for the element by name is a transfer full call on
                // the application thread: it finds the wrapper the streaming thread
                // built rather than making a second one, and settles the reference
                // it was handed.
                // It is not disposed here: it is the very wrapper this test still
                // needs, and a GObject wrapper is interned rather than owned.
                Element? found = pipeline.GetByName("src");

                Assert.NotNull(found);
                Assert.Same(managed, found);

                handle = managed.Handle;

                // The bin holds one reference and the wrapper holds the toggle
                // reference; get_by_name handed out a third, which the settle
                // dropped again. The instance is sunk, which is what the factory
                // path leaves behind.
                Assert.Equal(2u, RefCountOf(handle));
                Assert.Equal(0, GObjectNative.ObjectIsFloating(handle));

                // And the managed type is the type it arrived as: no ancestor
                // wrapper was ever built for it.
                Assert.Empty(watch.Reported);
            }
            finally
            {
                pipeline.SetState(State.Null);
            }

            // Both wrappers of the element have to go before the element can:
            // the bin releases it when the pipeline does, and the fabricated
            // wrapper holds the toggle reference until it is disposed.
            WeakProbe.Arm(handle);
            managed.Dispose();
        }
        finally
        {
            // The outer finally is what a using declaration cannot be here: the
            // pipeline has to be released before the assertion below, and still
            // released when one of the assertions above throws first.
            pipeline.Dispose();
        }

        Assert.Equal(1, WeakProbe.Freed);
    }

    /// <summary>
    /// The same through <c>MakeWithProperties</c>: the properties are native
    /// ones, given while the instance is being built, and the instance that
    /// comes out is still wrapped as the managed type.
    /// </summary>
    [Fact]
    public void AFactoryMadeElementTakesItsNativePropertiesAndStaysManaged()
    {
        Assert.True(ProbeFactoryElement.IsRegistered);

        using Element? element = ElementFactory.MakeWithProperties(
            ProbeFactoryElement.FactoryName,
            new Dictionary<string, object?> { ["name"] = "made-with-properties" });

        Assert.NotNull(element);

        ProbeFactoryElement managed = Assert.IsType<ProbeFactoryElement>(element);

        Assert.Equal("made-with-properties", managed.GetName());
        Assert.Equal(1u, RefCountOf(managed.Handle));
    }

    /// <summary>
    /// A type registered without a wrapper factory keeps the behaviour it
    /// always had: the instance arrives as the nearest wrapped ancestor, and
    /// the registry says so once.
    /// </summary>
    [Fact]
    public void AnElementWithoutAWrapperFactoryArrivesAsItsAncestor()
    {
        Assert.True(ProbePlainFactoryElement.IsRegistered);

        using FallbackWatch watch = new(ProbePlainFactoryElement.RegisteredType);

        using Element? element = ElementFactory.Make(ProbePlainFactoryElement.FactoryName, "plain");

        Assert.NotNull(element);

        // The ancestor wrapper, not the managed type. Gst.Element is abstract,
        // so what stands for it is the concrete wrapper the registry builds.
        Assert.IsNotType<ProbePlainFactoryElement>(element);
        Assert.Equal(ProbePlainFactoryElement.RegisteredType, element.NativeType);

        TypeFallback[] reported = watch.Reported;

        _output.WriteLine(FormattableString.Invariant(
            $"fallbacks: {string.Join(", ", reported.Select(static entry => entry.InstanceType.Name))}"));

        // The registry reports an exact native type once for the process, so a
        // second element of the same type raises nothing further.
        Assert.Single(reported);
        Assert.Equal(new GType(Element.GetGType()), reported[0].WrapperType);
    }

    /// <summary>
    /// A pad template that names a managed pad type makes <c>GstBaseSrc</c>
    /// build one while the element is constructed: the pad is wrapped as the
    /// managed type when it is first reached, its override runs when the pad is
    /// linked, and the references on it are the parent's and the wrapper's.
    /// </summary>
    [Fact]
    public void TheSourcePadBuiltFromAManagedTemplateIsTheManagedPadType()
    {
        using ProbeManagedPadSrc source = new();
        using Element? sink = ElementFactory.Make("fakesink", "managed-pad-sink");

        Assert.NotNull(sink);

        using Pad? pad = source.GetStaticPad("src");

        Assert.NotNull(pad);

        ProbeManagedPad managed = Assert.IsType<ProbeManagedPad>(pad);

        // The element that owns the pad holds one reference and the wrapper
        // holds the toggle reference; get_static_pad handed out one more, which
        // the interned wrapper released again.
        Assert.Equal(2u, RefCountOf(managed.Handle));
        Assert.Equal(0, managed.LinkedCalls);

        using Pad? peer = sink.GetStaticPad("sink");

        Assert.NotNull(peer);
        Assert.Equal(PadLinkReturn.Ok, managed.Link(peer));

        // The linked class closure runs on the thread that links.
        Assert.Equal(1, managed.LinkedCalls);
        Assert.Equal(2u, RefCountOf(managed.Handle));

        Assert.True(managed.Unlink(peer));
    }

    /// <summary>
    /// Once the wrapper of a fabricated instance is disposed, no second wrapper
    /// is ever made for that instance: the slot chains up to the class the
    /// managed type derives from, and the instance arrives as its ancestor.
    /// </summary>
    /// <remarks>
    /// The instance outlives its wrapper here - the element owns its pad - so
    /// this is the window the disposed marker exists for.
    /// </remarks>
    [Fact]
    public void ADisposedWrapperIsNeverFabricatedAgain()
    {
        using ProbeManagedPadSrc source = new();
        using Element? sink = ElementFactory.Make("fakesink", "disposed-wrapper-sink");

        Assert.NotNull(sink);

        Pad? pad = source.GetStaticPad("src");

        Assert.NotNull(pad);

        ProbeManagedPad managed = Assert.IsType<ProbeManagedPad>(pad);
        nint handle = managed.Handle;

        managed.Dispose();

        // The element still holds the pad, which is what makes it reachable
        // again.
        Assert.Equal(1u, RefCountOf(handle));

        using Pad? again = source.GetStaticPad("src");

        Assert.NotNull(again);
        Assert.Equal(handle, again.Handle);
        Assert.Equal(typeof(Pad), again.GetType());

        using Pad? peer = sink.GetStaticPad("sink");

        Assert.NotNull(peer);

        // The slot runs, finds no wrapper, and chains up rather than building
        // one: nothing here throws and nothing becomes a managed pad again.
        Assert.Equal(PadLinkReturn.Ok, again.Link(peer));

        using Pad? third = source.GetStaticPad("src");

        Assert.NotNull(third);
        Assert.Equal(typeof(Pad), third.GetType());

        Assert.True(again.Unlink(peer));
    }

    /// <summary>
    /// A dispose that has begun but has not written its marker yet is already
    /// enough to refuse a fabrication: the flag of the wrapper goes up first,
    /// and while the table still carries that wrapper no second one is built
    /// for the instance.
    /// </summary>
    /// <remarks>
    /// This is the window between the two, held open on one thread by the seam
    /// the runtime keeps for it, so that the refusal can be asserted instead of
    /// raced for.
    /// </remarks>
    [Fact]
    public void AFabricationIsRefusedWhileADisposeIsUnderWay()
    {
        using ProbeManagedPadSrc source = new();
        using Element? sink = ElementFactory.Make("fakesink", "half-disposed-sink");

        Assert.NotNull(sink);

        Pad? pad = source.GetStaticPad("src");

        Assert.NotNull(pad);

        ProbeManagedPad managed = Assert.IsType<ProbeManagedPad>(pad);
        nint handle = managed.Handle;
        int built = ProbeManagedPad.WrappersBuilt;

        // The flag is set and nothing else: the toggle reference is still
        // installed, the wrapper is still interned, and no marker has been
        // written on the instance.
        managed.SimulateInterruptedDispose();

        try
        {
            Assert.False(GObjectObject.WasSubclassDisposed(handle));

            // The lookup the trampolines use answers nothing, which is what
            // makes a slot chain up...
            Assert.Null(GObjectObject.TryGetOrFabricate(handle));

            // ...and it answers nothing by refusing, not by building a second
            // wrapper that would then hold a toggle reference of its own.
            Assert.Equal(built, ProbeManagedPad.WrappersBuilt);

            // Reaching the pad again hands out the ancestor wrapper, as it does
            // after a finished dispose, and still fabricates nothing.
            using Pad? again = source.GetStaticPad("src");

            Assert.NotNull(again);
            Assert.Equal(handle, again.Handle);
            Assert.Equal(typeof(Pad), again.GetType());
            Assert.Equal(built, ProbeManagedPad.WrappersBuilt);

            using Pad? peer = sink.GetStaticPad("sink");

            Assert.NotNull(peer);

            // The same through a real slot: linking fires the linked closure of
            // the pad, which reaches no managed pad and chains up.
            Assert.Equal(PadLinkReturn.Ok, again.Link(peer));
            Assert.Equal(built, ProbeManagedPad.WrappersBuilt);
            Assert.Equal(0, managed.LinkedCalls);
            Assert.True(again.Unlink(peer));
        }
        finally
        {
            // Dispose would return on the flag, so the half done release is
            // finished through the other half of the seam.
            managed.CompleteInterruptedDispose();
        }
    }

    /// <summary>
    /// A <c>create_new_pad</c> override that answers a pad of a managed type is
    /// what a requested pad is: the wrapper the override built is the one the
    /// request answers, and the overrides of the pad run while the aggregator
    /// streams.
    /// </summary>
    [Fact]
    public void ARequestedAggregatorPadIsTheManagedPadTheOverrideBuilt()
    {
        const int Buffers = 3;

        using Pipeline pipeline = Pipeline.New("managed-aggregator-pad");
        using ProbeCreateNewPadAggregator aggregator = new();
        Element source = NewSource("agg-pad-source", Buffers);
        Element sink = ElementFactory.Make("fakesink", "agg-pad-sink")
            ?? throw new InvalidOperationException("fakesink is missing.");

        Assert.True(pipeline.AddMany(source, aggregator, sink));

        using Pad? requested = aggregator.RequestPad(
            ProbeCreateNewPadAggregator.SinkPadTemplate,
            "sink_0",
            null);

        Assert.NotNull(requested);

        ProbeManagedAggregatorPad managed = Assert.IsType<ProbeManagedAggregatorPad>(requested);

        // The request answers the very wrapper the override built, not a
        // second one made for the same instance.
        Assert.Same(Assert.Single(aggregator.CreatedPads), managed);

        // The aggregator holds the pad and the wrapper holds the toggle
        // reference; request_pad handed out one more, which the interned
        // wrapper released again.
        Assert.Equal(2u, RefCountOf(managed.Handle));

        using Pad? sourcePad = source.GetStaticPad("src");

        Assert.NotNull(sourcePad);
        Assert.Equal(PadLinkReturn.Ok, sourcePad.Link(managed));
        Assert.True(aggregator.Link(sink));

        using Bus bus = pipeline.GetBus();

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, BusTimeout);

            Assert.NotNull(message);
            Assert.Equal(MessageType.Eos, message.Type);

            // A flush is what reaches the flush slot of the pad, and it runs on
            // the thread that sends the events.
            Assert.True(managed.SendEvent(Event.NewFlushStart()));
            Assert.True(managed.SendEvent(Event.NewFlushStop(true)));

            _output.WriteLine(FormattableString.Invariant(
                $"pad: flushed={managed.Flushed}, skipped={managed.Skipped}"));

            // skip_buffer is not reached by plain streaming through an
            // aggregator with a single sink pad, so the flush slot is what this
            // asserts; both are declared and both are dispatched the same way.
            Assert.True(managed.Flushed > 0, "The flush override of the managed pad did not run.");
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// A construction property the class does not have is refused by name,
    /// before anything is built.
    /// </summary>
    [Fact]
    public void AnUnknownConstructionPropertyIsRefused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            static () => ProbeMistakenWrapper.Registration.NewInstance(
                new Dictionary<string, object?> { ["not-a-property"] = 1 }));

        _output.WriteLine(error.Message);
        Assert.Contains("not-a-property", error.Message, StringComparison.Ordinal);
        Assert.Contains("is not a property", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A property that cannot be written is refused as well: giving it a value
    /// at construction would be dropped silently by GObject.
    /// </summary>
    [Fact]
    public void ANonWritableConstructionPropertyIsRefused()
    {
        // GstPad:caps is a property of the class and is readable only.
        ArgumentException error = Assert.Throws<ArgumentException>(
            static () => ProbeManagedPad.Registration.NewInstance(
                new Dictionary<string, object?>
                {
                    ["direction"] = PadDirection.Sink,
                    ["caps"] = null,
                }));

        _output.WriteLine(error.Message);
        Assert.Contains("caps", error.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be written", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>CreateWrapper</c> that ignores its arguments is caught: the wrapper
    /// it built belongs to another instance, so the fabrication refuses it, the
    /// wrapper it made is disposed and the instance that was to be wrapped is
    /// left exactly as it was found.
    /// </summary>
    [Fact]
    public void AWrapperBuiltForAnotherInstanceIsRefused()
    {
        // The arguments carry the instance g_object_new made and nothing has
        // wrapped it yet, which is the state the fabrication meets.
        nint handle = ProbeMistakenWrapper.Registration.NewInstance().Handle;

        Assert.Equal(1u, RefCountOf(handle));

        try
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => GObjectObject.FromNative<Element>(handle, Transfer.None));

            _output.WriteLine(error.Message);
            Assert.Contains("CreateWrapper", error.Message, StringComparison.Ordinal);

            // The reference the fabrication took is gone with the wrapper it
            // was refused for: what is left is the floating reference of
            // g_object_new.
            Assert.Equal(1u, RefCountOf(handle));
        }
        finally
        {
            _ = GObjectNative.ObjectRefSink(handle);
            GObjectNative.ObjectUnref(handle);
        }
    }

    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));

    private static Element NewSource(string name, int buffers)
    {
        Element source = ElementFactory.Make("fakesrc", name)
            ?? throw new InvalidOperationException("fakesrc is missing.");

        source.SetProperty("num-buffers", buffers);
        source.SetProperty("sizetype", 2);
        source.SetProperty("filltype", 2);
        return source;
    }

    /// <summary>
    /// Collects the ancestor wrappers the registry reports for one native type
    /// while it is subscribed.
    /// </summary>
    private sealed class FallbackWatch : IDisposable
    {
        private readonly List<TypeFallback> _reported = [];

        private readonly GType _type;

        internal FallbackWatch(GType type)
        {
            _type = type;
            TypeRegistry.Fallback += Collect;
        }

        internal TypeFallback[] Reported
        {
            get
            {
                lock (_reported)
                {
                    return _reported.ToArray();
                }
            }
        }

        public void Dispose() => TypeRegistry.Fallback -= Collect;

        private void Collect(TypeFallback fallback)
        {
            if (fallback.InstanceType != _type)
            {
                return;
            }

            lock (_reported)
            {
                _reported.Add(fallback);
            }
        }
    }
}
