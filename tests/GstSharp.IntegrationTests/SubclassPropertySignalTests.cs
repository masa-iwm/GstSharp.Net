using Gst;
using Gst.GObject;
using Xunit;
using GObjectObject = Gst.GObject.Object;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Properties and signals a managed subclass installs on itself: the property
/// slots answer for the specifications the subclass owns and for nothing else,
/// the notifications come out once, and a signal a subclass defines is emitted
/// and subscribed to like any other.
/// </summary>
/// <remarks>
/// All of it needs a registered <c>GType</c> and a running library, so it is an
/// integration test. What the validations refuse before any native call is
/// pinned here as well, because refusing is only meaningful against the class
/// that would otherwise have been built.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class SubclassPropertySignalTests
{
    private const ParamFlags ReadWrite = ParamFlags.Readable | ParamFlags.Writable;

    /// <summary>
    /// The four kinds of property the probe installs all travel through the
    /// managed slots and come back unchanged.
    /// </summary>
    [Fact]
    public void EveryKindOfInstalledPropertyRoundTrips()
    {
        Assert.True(ProbePropertyElement.IsRegistered);

        using Element made = Made("round-trip");
        ProbePropertyElement probe = Assert.IsType<ProbePropertyElement>(made);
        using Element peer = ElementFactory.Make("fakesink", "peer")
            ?? throw new InvalidOperationException("fakesink is missing.");

        probe.SetProperty("value", 42);
        probe.SetProperty("label", "hello");
        probe.SetProperty("target-state", State.Playing);
        probe.SetProperty("peer", peer);

        Assert.Equal(42, probe.Value);
        Assert.Equal("hello", probe.Label);
        Assert.Equal(State.Playing, probe.TargetState);
        Assert.Same(peer, probe.Peer);

        Assert.Equal(42, probe.GetProperty<int>("value"));
        Assert.Equal("hello", probe.GetProperty<string>("label"));
        Assert.Equal(State.Playing, (State)probe.GetProperty<int>("target-state"));
        Assert.True(probe.GetCalls > 0);
    }

    /// <summary>
    /// A property without <c>EXPLICIT_NOTIFY</c> is notified by GObject once
    /// the setter returns, and one with it is notified by the setter — once
    /// either way.
    /// </summary>
    [Fact]
    public void ANotificationArrivesExactlyOnce()
    {
        using Element made = Made("notify");
        ProbePropertyElement probe = Assert.IsType<ProbePropertyElement>(made);

        int implicitly = 0;
        int explicitly = 0;

        _ = probe.AddNotifyHandler("value", (_, _) => Interlocked.Increment(ref implicitly));
        _ = probe.AddNotifyHandler("counter", (_, _) => Interlocked.Increment(ref explicitly));

        probe.SetProperty("value", 7);
        probe.SetProperty("counter", 3);

        Assert.Equal(7, probe.Value);
        Assert.Equal(3, probe.Counter);
        Assert.Equal(1, Volatile.Read(ref implicitly));
        Assert.Equal(1, Volatile.Read(ref explicitly));
    }

    /// <summary>
    /// A pipeline description names the property, so GStreamer writes it from
    /// inside <c>g_object_new</c> — the first managed code the instance ever
    /// reaches is the property slot, which is where the wrapper gets built.
    /// </summary>
    [Fact]
    public void APipelineDescriptionWritesAPropertyBeforeAnythingElse()
    {
        ProbePropertyElement.Reset();

        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            $"{ProbePropertyElement.FactoryName} name=described value=5 ! fakesink"));

        using Element? found = ((Bin)pipeline).GetByName("described");
        ProbePropertyElement probe = Assert.IsType<ProbePropertyElement>(found);

        Assert.Equal(5, probe.Value);
        Assert.True(probe.LastSetSawWrapper);
        Assert.Equal(1, ProbePropertyElement.WrappersBuilt);
    }

    /// <summary>
    /// A write from another thread is dispatched on that thread: nothing about
    /// the property slots hops to the thread that created the element.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task APropertyIsWrittenOnTheThreadThatWrote()
    {
        using Element made = Made("threaded");
        ProbePropertyElement probe = Assert.IsType<ProbePropertyElement>(made);

        int here = Environment.CurrentManagedThreadId;
        await System.Threading.Tasks.Task.Run(() => probe.SetProperty("value", 11));

        Assert.Equal(11, probe.Value);
        Assert.NotEqual(here, probe.LastSetThreadId);
    }

    /// <summary>
    /// Installing a property leaves the class holding two references and the
    /// runtime one, on top of the one the caller's wrapper has.
    /// </summary>
    [Fact]
    public void AnInstalledSpecificationIsHeldByTheClassAndTheRuntime()
    {
        Assert.True(ProbePropertyElement.IsRegistered);

        Assert.Equal(4u, RefCountOf(ProbePropertyElement.SpecOfValue.Handle));
        Assert.Equal(
            ProbePropertyElement.RegisteredType.Value,
            ProbePropertyElement.SpecOfValue.OwnerType.Value);
    }

    /// <summary>
    /// The wrapper of an element that installed properties still owns the last
    /// reference: nothing the installation kept is attached to the instance.
    /// </summary>
    [Fact]
    public void AnElementWithPropertiesIsFreedWithItsWrapper()
    {
        Element made = Made("freed");
        ProbePropertyElement probe = Assert.IsType<ProbePropertyElement>(made);
        probe.SetProperty("value", 1);

        nint handle = probe.Handle;
        WeakProbe.Arm(handle);
        probe.Dispose();

        Assert.Equal(1, WeakProbe.Freed);
    }

    /// <summary>
    /// A construct property is refused: GObject would deliver it before any
    /// wrapper could exist.
    /// </summary>
    [Fact]
    public void AConstructPropertyIsRefused()
    {
        ArgumentException failure = DefineAndCatch<ArgumentException>(
            "GstSharpTestConstructProperty",
            config =>
            {
                using ParamSpecInt spec = ParamSpecInt.New(
                    "constructed", null, null, 0, 10, 0, ReadWrite | ParamFlags.Construct);

                config.InstallProperty(1, spec);
            });

        Assert.Contains("CONSTRUCT", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Two properties cannot share an identifier.</summary>
    [Fact]
    public void ADuplicateIdentifierIsRefused()
    {
        ArgumentException failure = DefineAndCatch<ArgumentException>(
            "GstSharpTestDuplicateId",
            config =>
            {
                using ParamSpecInt first = ParamSpecInt.New("first", null, null, 0, 10, 0, ReadWrite);
                using ParamSpecInt second = ParamSpecInt.New("second", null, null, 0, 10, 0, ReadWrite);

                config.InstallProperty(1, first);
                config.InstallProperty(1, second);
            });

        Assert.Contains("identifier 1", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Two properties of one class cannot share a name.</summary>
    [Fact]
    public void ADuplicateNameIsRefused()
    {
        ArgumentException failure = DefineAndCatch<ArgumentException>(
            "GstSharpTestDuplicateName",
            config =>
            {
                using ParamSpecInt first = ParamSpecInt.New("twice", null, null, 0, 10, 0, ReadWrite);
                using ParamSpecInt second = ParamSpecInt.New("twice", null, null, 0, 10, 0, ReadWrite);

                config.InstallProperty(1, first);
                config.InstallProperty(2, second);
            });

        Assert.Contains("'twice'", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>One specification cannot be installed on two classes.</summary>
    [Fact]
    public void AnAlreadyInstalledSpecificationIsRefused()
    {
        ArgumentException failure = DefineAndCatch<ArgumentException>(
            "GstSharpTestReusedSpecification",
            config => config.InstallProperty(9, ProbePropertyElement.SpecOfValue));

        Assert.Contains("already been installed", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A property cannot be installed on a class that did not take the matching
    /// slot over, because GObject would answer it out of <c>GObject</c> itself.
    /// </summary>
    [Fact]
    public void APropertyWithoutTheOverrideIsRefused()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            Element.DefineSubclass(
                "GstSharpTestMissingPropertyOverride",
                config =>
                {
                    using ParamSpecInt spec = ParamSpecInt.New("orphan", null, null, 0, 10, 0, ReadWrite);
                    config.InstallProperty(1, spec);
                }));

        InvalidOperationException inner = Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Contains("SetPropertyOverride", inner.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Redefining a property of an ancestor is legal, and the redefinition is
    /// the one that answers.
    /// </summary>
    [Fact]
    public void APropertyOfAnAncestorMayBeRedefined()
    {
        Assert.True(ProbeShadowNameElement.RegisteredType.IsValid);
        Assert.Equal(
            ProbeShadowNameElement.RegisteredType.Value,
            ProbeShadowNameElement.SpecOfName.OwnerType.Value);

        using ProbeShadowNameElement element = new();
        element.SetProperty("name", "shadowed");

        Assert.Equal("shadowed", element.Shadowed);
        Assert.Equal("shadowed", element.GetProperty<string>("name"));
    }

    /// <summary>
    /// Constructing from a dictionary refuses a property the subclass itself
    /// installed: GObject would dispatch it to the managed slot before the
    /// wrapper this very call is building exists.
    /// </summary>
    [Fact]
    public void ConstructingFromADictionaryRefusesAManagedProperty()
    {
        Dictionary<string, object?> properties = new(StringComparer.Ordinal) { ["value"] = 3 };

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => ProbePropertyElement.Registration.NewInstance(properties));

        Assert.Contains("value", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A notification names a property of the object, or it is refused.</summary>
    [Fact]
    public void NotifyingWithAForeignSpecificationIsRefused()
    {
        using Element made = Made("foreign-notify");
        ProbePropertyElement probe = Assert.IsType<ProbePropertyElement>(made);
        using ParamSpecInt foreign = ParamSpecInt.New("foreign", null, null, 0, 10, 0, ReadWrite);

        _ = Assert.Throws<ArgumentException>(() => probe.Notify(foreign));
    }

    /// <summary>
    /// The base implementation of the setter warns about an identifier no
    /// property was installed with, and does nothing else — above all it does
    /// not throw and does not chain up.
    /// </summary>
    [Fact]
    public void AnUnknownIdentifierOnlyWarns()
    {
        using Element made = Made("unknown-id");
        ProbePropertyElement probe = Assert.IsType<ProbePropertyElement>(made);

        probe.WarnForUnknownId(4242, ProbePropertyElement.SpecOfValue);

        probe.SetProperty("value", 6);
        Assert.Equal(6, probe.Value);
    }

    /// <summary>
    /// A signal a managed subclass defined is emitted like any other, the class
    /// handler runs, and the instance it is given is the interned wrapper.
    /// </summary>
    [Fact]
    public void AManagedSignalReachesTheClassHandlerAndTheConnectedOnes()
    {
        Assert.True(ProbeSignalElement.IsRegistered);
        ProbeSignalElement.Reset();

        using Element made = ElementFactory.Make(ProbeSignalElement.FactoryName, "signals")
            ?? throw new InvalidOperationException("The probe factory is missing.");
        ProbeSignalElement probe = Assert.IsType<ProbeSignalElement>(made);

        Assert.NotEqual(0u, ProbeSignalElement.PingSignalId);

        int connected = 0;
        _ = probe.ConnectSignal(
            ProbeSignalElement.PingSignal,
            (_, _) =>
            {
                _ = Interlocked.Increment(ref connected);
                return null;
            });

        _ = probe.EmitSignal(ProbeSignalElement.PingSignal, 7);

        Assert.Equal(1, ProbeSignalElement.ClassHandlerCalls);
        Assert.Same(probe, ProbeSignalElement.ClassHandlerSender);
        Assert.Equal(7, Assert.Single(ProbeSignalElement.ClassHandlerArgs!));
        Assert.Equal(1, Volatile.Read(ref connected));
    }

    /// <summary>
    /// The <c>TrueHandled</c> accumulator stops the emission at the first
    /// handler that says it handled it.
    /// </summary>
    [Fact]
    public void TrueHandledStopsAtTheHandlerThatSaysSo()
    {
        Assert.True(ProbeSignalElement.IsRegistered);

        using Element made = ElementFactory.Make(ProbeSignalElement.FactoryName, "handled")
            ?? throw new InvalidOperationException("The probe factory is missing.");
        GObjectObject probe = Assert.IsType<ProbeSignalElement>(made);

        int second = 0;

        _ = probe.ConnectSignal(ProbeSignalElement.HandledSignal, (_, _) => true);
        _ = probe.ConnectSignal(
            ProbeSignalElement.HandledSignal,
            (_, _) =>
            {
                _ = Interlocked.Increment(ref second);
                return false;
            });

        Assert.True(probe.EmitSignal<bool>(ProbeSignalElement.HandledSignal));
        Assert.Equal(0, Volatile.Read(ref second));
    }

    /// <summary>
    /// The <c>FirstWins</c> accumulator answers the value of the first handler
    /// and stops there.
    /// </summary>
    [Fact]
    public void FirstWinsAnswersTheFirstHandler()
    {
        Assert.True(ProbeSignalElement.IsRegistered);

        using Element made = ElementFactory.Make(ProbeSignalElement.FactoryName, "first-wins")
            ?? throw new InvalidOperationException("The probe factory is missing.");
        GObjectObject probe = Assert.IsType<ProbeSignalElement>(made);

        int second = 0;

        _ = probe.ConnectSignal(ProbeSignalElement.FirstWinsSignal, (_, _) => 11);
        _ = probe.ConnectSignal(
            ProbeSignalElement.FirstWinsSignal,
            (_, _) =>
            {
                _ = Interlocked.Increment(ref second);
                return 22;
            });

        Assert.Equal(11, probe.EmitSignal<int>(ProbeSignalElement.FirstWinsSignal));
        Assert.Equal(0, Volatile.Read(ref second));
    }

    /// <summary>A signal name that GObject would reject is refused first.</summary>
    [Fact]
    public void AnInvalidSignalNameIsRefused()
    {
        ArgumentException failure = DefineAndCatch<ArgumentException>(
            "GstSharpTestInvalidSignalName",
            config => config.AddSignal("not a name", SignalFlags.RunLast, GType.None, []));

        Assert.Contains("not a valid signal name", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A signal name the ancestry already uses is refused.</summary>
    [Fact]
    public void AnExistingSignalNameIsRefused()
    {
        ArgumentException failure = DefineAndCatch<ArgumentException>(
            "GstSharpTestExistingSignalName",
            config => config.AddSignal("pad-added", SignalFlags.RunLast, GType.None, []));

        Assert.Contains("pad-added", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A signal that names no run flag is refused.</summary>
    [Fact]
    public void ASignalWithoutARunFlagIsRefused()
    {
        ArgumentException failure = DefineAndCatch<ArgumentException>(
            "GstSharpTestSignalWithoutRunFlag",
            config => config.AddSignal("gstsharp-nowhere", SignalFlags.NoRecurse, GType.None, []));

        Assert.Contains("RunFirst", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A signal that answers nothing cannot have an accumulator.</summary>
    [Fact]
    public void AnAccumulatorOnASignalThatAnswersNothingIsRefused()
    {
        ArgumentException failure = DefineAndCatch<ArgumentException>(
            "GstSharpTestAccumulatorWithoutReturn",
            config => config.AddSignal(
                "gstsharp-void-accumulated",
                SignalFlags.RunLast,
                GType.None,
                [],
                null,
                SignalAccumulator.FirstWins));

        Assert.Contains("nothing for an accumulator", failure.Message, StringComparison.Ordinal);
    }

    private static Element Made(string name)
    {
        // Touching the registration is what runs the static initialiser of the
        // probe, and the factory name is a constant that would not.
        Assert.True(ProbePropertyElement.IsRegistered);

        return ElementFactory.Make(ProbePropertyElement.FactoryName, name)
            ?? throw new InvalidOperationException("The probe factory is missing.");
    }

    private static TException DefineAndCatch<TException>(string typeName, Action<ClassConfig> configure)
        where TException : Exception
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            Element.DefineSubclass(
                typeName,
                configure,
                GObjectObject.SetPropertyOverride,
                GObjectObject.GetPropertyOverride));

        return Assert.IsType<TException>(failure.InnerException);
    }

    private static unsafe uint RefCountOf(nint handle) => *(uint*)((byte*)handle + (8 * sizeof(nint)));
}
