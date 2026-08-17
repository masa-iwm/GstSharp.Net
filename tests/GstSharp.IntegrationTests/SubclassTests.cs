using System.Runtime.CompilerServices;
using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;
using GObjectObject = Gst.GObject.Object;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The end to end proof of stage 0 of <c>docs/subclassing.md</c>: a
/// <c>GstElement</c> subclass that is registered from C#, whose
/// <c>change_state</c> slot GStreamer calls into managed code, and whose
/// managed state lives as long as the native object does.
/// </summary>
/// <remarks>
/// Nothing here is version dependent. The class struct offsets the runtime
/// writes into have been frozen ABI since well before the 1.24 floor, so the
/// tests carry no <see cref="RequiresGStreamerFactAttribute"/>.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class SubclassTests
{
    private static readonly ClockTime StateTimeout = ClockTime.FromSeconds(5);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The managed type is a real <c>GType</c>: it is registered under the name
    /// the descriptor asked for and it derives from <c>GstElement</c>.
    /// </summary>
    [Fact]
    public void TheManagedTypeIsRegisteredUnderItsOwnName()
    {
        GType type = ProbeElement.RegisteredType;
        GType element = new(Element.GetGType());

        _output.WriteLine($"registered: {type.Name} ({type.Value}), parent {type.Parent.Name}");

        Assert.True(type.IsValid);
        Assert.Equal(ProbeElement.GTypeName, type.Name);
        Assert.Equal(element, type.Parent);
        Assert.True(type.IsA(element));
        Assert.Equal(type, GType.FromName(ProbeElement.GTypeName));
    }

    /// <summary>
    /// Registering a name that is taken fails loudly. A double registration is
    /// a caller bug, and GObject's own answer to it is a warning and an invalid
    /// type, which is not something a binding should pass on.
    /// </summary>
    [Fact]
    public void RegisteringTheSameNameTwiceIsRefused()
    {
        // Resolves the first registration, so that the name is taken.
        Assert.True(ProbeElement.RegisteredType.IsValid);

        SubclassDescriptor duplicate = new(new GType(Element.GetGType()), ProbeElement.GTypeName, []);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => SubclassRegistry.Register(duplicate));

        Assert.Contains(ProbeElement.GTypeName, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Deriving a managed subclass from another managed subclass is refused at
    /// registration. The single level chain-up would resolve the parent class
    /// to a class whose slot is the shared trampoline and recurse for ever, so
    /// the restriction of §4.4 is enforced rather than documented.
    /// </summary>
    [Fact]
    public void DerivingFromAManagedSubclassIsRefused()
    {
        SubclassDescriptor nested = new(ProbeElement.RegisteredType, "GstSharpTestNestedProbeElement", []);

        NotSupportedException error =
            Assert.Throws<NotSupportedException>(() => SubclassRegistry.Register(nested));

        Assert.Contains(ProbeElement.GTypeName, error.Message, StringComparison.Ordinal);

        // The refusal has to leave nothing behind: the name is still free.
        Assert.False(GType.FromName("GstSharpTestNestedProbeElement").IsValid);
    }

    /// <summary>
    /// Constructing the managed subclass needs no change to the wrapper
    /// constructor: <c>g_object_new</c> hands out a floating reference, the
    /// existing constructor sinks it, and the wrapper ends up owning exactly
    /// one reference and being the interned one.
    /// </summary>
    [Fact]
    public void ConstructionSinksTheFloatingReferenceAndInternsTheWrapper()
    {
        using ProbeElement probe = new();
        nint handle = probe.Handle;

        _output.WriteLine(FormattableString.Invariant(
            $"instance: 0x{handle:x} type={probe.NativeType.Name} refcount={RefCountOf(handle)}"));

        Assert.Equal(ProbeElement.RegisteredType, probe.NativeType);
        Assert.Equal(0, GObjectNative.ObjectIsFloating(handle));

        // The GObject header is frozen ABI: the class pointer at 0, the
        // reference count at 8. One reference, and it is the wrapper's.
        Assert.Equal(1u, RefCountOf(handle));
        Assert.Same(probe, GObjectObject.TryGetInterned(handle));
    }

    /// <summary>
    /// The override runs for every transition of a NULL to PAUSED and back
    /// round trip, and the chain-up performs the transition for real: the
    /// element ends up in the state it was asked for.
    /// </summary>
    [Fact]
    public void TheOverrideSeesEveryTransitionAndTheChainUpPerformsIt()
    {
        using ProbeElement probe = new();

        Assert.Equal(StateChangeReturn.Success, probe.SetState(State.Ready));
        Assert.Equal([StateChange.NullToReady], probe.Transitions);
        Assert.Equal(State.Ready, StateOf(probe));

        probe.ClearTransitions();
        Assert.Equal(StateChangeReturn.Success, probe.SetState(State.Paused));
        Assert.Equal([StateChange.ReadyToPaused], probe.Transitions);
        Assert.Equal(State.Paused, StateOf(probe));

        // Going all the way down is two transitions in one call, which is what
        // shows that the trampoline is reached per transition rather than per
        // gst_element_set_state.
        probe.ClearTransitions();
        Assert.Equal(StateChangeReturn.Success, probe.SetState(State.Null));
        Assert.Equal([StateChange.PausedToReady, StateChange.ReadyToNull], probe.Transitions);
        Assert.Equal(State.Null, StateOf(probe));
    }

    /// <summary>
    /// The same round trip driven by a real pipeline rather than by the test:
    /// the transitions arrive because <c>GstBin</c> walks its children, through
    /// the vtable, with no managed code in between.
    /// </summary>
    [Fact]
    public void ThePipelineDrivesTheOverrideThroughTheVtable()
    {
        using Pipeline pipeline = Pipeline.New("subclass-host");
        using ProbeElement probe = new();

        Assert.True(pipeline.Add(probe));

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Ready));
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Paused));
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.GetState(out _, out _, StateTimeout));

            _output.WriteLine($"transitions: {string.Join(", ", probe.Transitions)}");

            Assert.Equal([StateChange.NullToReady, StateChange.ReadyToPaused], probe.Transitions);
            Assert.Equal(State.Paused, StateOf(probe));

            probe.ClearTransitions();
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Null));
            Assert.Equal([StateChange.PausedToReady, StateChange.ReadyToNull], probe.Transitions);
        }
        finally
        {
            pipeline.SetState(State.Null);
            pipeline.Remove(probe);
        }
    }

    /// <summary>
    /// An exception in the override is reported through the trap and mapped to
    /// the error value of this slot. It does not unwind into the native frame,
    /// and it does not chain up afterwards: the element stays where it was.
    /// </summary>
    [Fact]
    public void AnExceptionInTheOverrideBecomesAFailedTransition()
    {
        using ProbeElement probe = new() { ThrowOnChangeState = true };

        List<Exception> reported = [];
        void Collect(Exception exception) => reported.Add(exception);

        ExceptionTrap.UnhandledException += Collect;
        try
        {
            Assert.Equal(StateChangeReturn.Failure, probe.SetState(State.Ready));
        }
        finally
        {
            ExceptionTrap.UnhandledException -= Collect;
        }

        Assert.Equal([StateChange.NullToReady], probe.Transitions);

        Exception trapped = Assert.Single(reported);
        _output.WriteLine($"trapped: {trapped.GetType().Name}: {trapped.Message}");
        Assert.IsType<InvalidOperationException>(trapped);

        // No chain-up ran after the throw, so nothing performed the transition.
        Assert.Equal(State.Null, StateOf(probe));
    }

    /// <summary>
    /// Managed state of a subclass survives while native code holds the object,
    /// with nothing keeping the wrapper alive from managed code. This is the
    /// scenario the toggle references exist for, and subclassing inherits it
    /// unchanged.
    /// </summary>
    [Fact]
    public void ManagedStateSurvivesWhileOnlyTheBinHoldsTheElement()
    {
        Bin bin = Bin.New("subclass-keeper");
        nint handle = AddProbeTo(bin);

        // Every managed reference to the wrapper is gone now; the bin's native
        // reference is the only thing left, and the toggle reference is what
        // has to turn that into a strong managed reference.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GObjectObject.DrainPendingReleases();

        ProbeElement survivor = Assert.IsType<ProbeElement>(GObjectObject.TryGetInterned(handle));

        _output.WriteLine($"survivor: tag=\"{survivor.Tag}\" transitions={survivor.Transitions.Count}");

        Assert.Equal("kept across a collection", survivor.Tag);
        Assert.Equal([StateChange.NullToReady], survivor.Transitions);

        // The same instance keeps counting, which is what proves that the
        // wrapper was not rebuilt behind the test's back.
        Assert.Equal(StateChangeReturn.Success, survivor.SetState(State.Null));
        Assert.Equal([StateChange.NullToReady, StateChange.ReadyToNull], survivor.Transitions);

        bin.SetState(State.Null);
        Assert.True(bin.Remove(survivor));
        survivor.Dispose();
        bin.Dispose();
    }

    /// <summary>
    /// A vfunc that fires while no wrapper is interned chains up, so the
    /// element behaves as if the slot had not been overridden at all.
    /// </summary>
    /// <remarks>
    /// This is the construction window of §5.2 and §4.1. <c>GstElement</c>
    /// happens to fire no vfunc between <c>g_object_new</c> allocating the
    /// instance and the wrapper constructor interning it, so the window cannot
    /// be observed from inside <c>new ProbeElement()</c>; the test reaches the
    /// identical no-wrapper path by driving an instance that was never wrapped,
    /// which is also the shape a native factory would produce.
    /// </remarks>
    [Fact]
    public void AnElementWithNoWrapperChainsUpInsteadOfDispatching()
    {
        nint handle = SubclassRegistry.NewInstance(ProbeElement.RegisteredType).Handle;
        GObjectNative.ObjectRefSink(handle);

        try
        {
            Assert.Null(GObjectObject.TryGetInterned(handle));

            int result = TestNatives.ElementSetState(handle, (int)State.Ready);

            _output.WriteLine(FormattableString.Invariant(
                $"unwrapped instance: set_state -> {(StateChangeReturn)result}, state {RawStateOf(handle)}"));

            // The parent implementation ran: the transition succeeded and the
            // element really is in READY.
            Assert.Equal(StateChangeReturn.Success, (StateChangeReturn)result);
            Assert.Equal(State.Ready, RawStateOf(handle));

            // And nothing fabricated a wrapper on the way, which is the rule
            // that keeps a second toggle reference from ever being installed.
            Assert.Null(GObjectObject.TryGetInterned(handle));
        }
        finally
        {
            // An element that is finalised outside NULL is a g_critical.
            TestNatives.ElementSetState(handle, (int)State.Null);
            TestNatives.ObjectUnref(handle);
        }
    }

    /// <summary>
    /// Creates a probe, records one transition on it and hands it to a bin,
    /// returning only its handle.
    /// </summary>
    /// <param name="bin">The bin that takes the element over.</param>
    /// <returns>The native element, which keeps nothing alive by itself.</returns>
    /// <remarks>
    /// The wrapper is created in a frame of its own so that no local of the
    /// test frame keeps it alive; a debug build extends the lifetime of a local
    /// to the end of its method, which would make the collection below prove
    /// nothing.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static nint AddProbeTo(Bin bin)
    {
        ProbeElement probe = new() { Tag = "kept across a collection" };

        Assert.Equal(StateChangeReturn.Success, probe.SetState(State.Ready));
        Assert.True(bin.Add(probe));

        return probe.Handle;
    }

    /// <summary>Reads the reference count out of the <c>GObject</c> header.</summary>
    /// <param name="handle">The object to inspect.</param>
    /// <returns>The current count.</returns>
    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));

    /// <summary>Reads the settled state of an element.</summary>
    /// <param name="element">The element to inspect.</param>
    /// <returns>The current state.</returns>
    private static State StateOf(Element element)
    {
        element.GetState(out State state, out _, StateTimeout);
        return state;
    }

    /// <summary>Reads the settled state of an element that has no wrapper.</summary>
    /// <param name="handle">The element to inspect.</param>
    /// <returns>The current state.</returns>
    private static State RawStateOf(nint handle)
    {
        int state;
        int pending;
        TestNatives.ElementGetState(handle, &state, &pending, (ulong)StateTimeout.Nanoseconds);
        return (State)state;
    }
}
