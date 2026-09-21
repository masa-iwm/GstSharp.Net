using System.Runtime.InteropServices;
using Gst;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand-written <c>probe</c> slot of a managed <c>GstDeviceProvider</c>:
/// what the list the override answers carries when a C caller takes it over,
/// what the forward members of the binding see afterwards, and what the two
/// callers of the slot do with the devices.
/// </summary>
/// <remarks>
/// The arithmetic is the subject, so the slot is called the way C calls it as
/// well as through the binding. A managed device stands at one reference when
/// its wrapper built it; the hand-out adds one per answered device, which is
/// what <c>transfer full</c> on the elements means, and every count below is
/// read off that.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe partial class ManagedDeviceProviderProbeTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public ManagedDeviceProviderProbeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The list a C caller receives: one node per answered device, one added
    /// reference per node, and nothing floating. Releasing what the caller owns
    /// puts every device back where it was, and the wrapper the provider kept
    /// is still usable.
    /// </summary>
    [Fact]
    public void ProbeHandsAConsumingCallerOneReferencePerDevice()
    {
        using ProbeOnlyDeviceProvider provider = new();

        nint head = DeviceProviderGetDevices(provider.Handle);
        try
        {
            nint[] items = GListMarshal.Collect(head);
            Assert.Equal(2, items.Length);

            ProbeDevice fresh = Assert.IsType<ProbeDevice>(provider.Fresh);
            Assert.Equal(new[] { provider.Kept.Handle, fresh.Handle }, items);

            foreach (nint item in items)
            {
                Assert.Equal(0, GObjectNative.ObjectIsFloating(item));
                Assert.Equal(2u, RefCountOf(item));
            }

            foreach (nint item in items)
            {
                ObjectUnref(item);
            }

            Assert.Equal(1u, RefCountOf(provider.Kept.Handle));
            Assert.Equal(1u, RefCountOf(fresh.Handle));
            fresh.Dispose();
        }
        finally
        {
            ListFree(head);
        }

        Assert.Equal("Kept probe device", provider.Kept.DisplayName);
    }

    /// <summary>
    /// The forward member adopts the one reference the caller was given onto
    /// the live wrapper, so what it hands back is the very object the override
    /// answered and the count is back at one.
    /// </summary>
    [Fact]
    public void GetDevicesAnswersTheWrappersTheOverrideAnswered()
    {
        using ProbeOnlyDeviceProvider provider = new();

        IReadOnlyList<Device> first = provider.GetDevices();
        ProbeDevice firstFresh = Assert.IsType<ProbeDevice>(provider.Fresh);
        Assert.Equal(2, first.Count);
        Assert.Same(provider.Kept, first[0]);
        Assert.Same(firstFresh, first[1]);
        Assert.Equal(1u, RefCountOf(provider.Kept.Handle));
        Assert.Equal(1u, RefCountOf(firstFresh.Handle));

        IReadOnlyList<Device> second = provider.GetDevices();
        ProbeDevice secondFresh = Assert.IsType<ProbeDevice>(provider.Fresh);
        Assert.Same(provider.Kept, second[0]);
        Assert.Same(secondFresh, second[1]);
        Assert.NotSame(firstFresh, secondFresh);
        Assert.Equal(1u, RefCountOf(provider.Kept.Handle));
        Assert.Equal(1u, RefCountOf(secondFresh.Handle));

        firstFresh.Dispose();
        secondFresh.Dispose();
    }

    /// <summary>
    /// The start path of a provider that declares no <c>start</c> slot: every
    /// answered device is announced as if <c>DeviceAdd</c> had been called for
    /// it, which parents it to the provider, and <c>Stop()</c> unparents it
    /// again.
    /// </summary>
    [Fact]
    public void StartAnnouncesEveryDeviceProbeAnswered()
    {
        using ProbeOnlyDeviceProvider provider = new();
        using Bus bus = provider.GetBus();

        Assert.True(provider.Start());
        ProbeDevice fresh = Assert.IsType<ProbeDevice>(provider.Fresh);

        try
        {
            Assert.Equal(1, provider.Probed);
            Assert.Same(provider, provider.Kept.Parent);
            Assert.Same(provider, fresh.Parent);

            // A posted message holds a reference of its own for as long as it
            // is alive (gstdeviceprovider.c:647-648), so the count only settles
            // on the wrapper and the provider once it is gone.
            using (Message? added = bus.Pop())
            {
                Assert.NotNull(added);
                Assert.Equal(MessageType.DeviceAdded, added.Type);
                Assert.Equal(3u, RefCountOf(provider.Kept.Handle));
            }

            using (Message? added = bus.Pop())
            {
                Assert.NotNull(added);
                Assert.Equal(MessageType.DeviceAdded, added.Type);
            }

            Assert.Equal(2u, RefCountOf(provider.Kept.Handle));
            Assert.Equal(2u, RefCountOf(fresh.Handle));
        }
        finally
        {
            provider.Stop();
        }

        Assert.Equal(1u, RefCountOf(provider.Kept.Handle));
        Assert.Equal(1u, RefCountOf(fresh.Handle));
        Assert.Null(provider.Kept.Parent);
        fresh.Dispose();
    }

    /// <summary>
    /// An override that throws: the trap sees the exception, the slot answers
    /// no devices, and the start the slot was reached from still succeeds.
    /// </summary>
    [Fact]
    public void AProbeOverrideThatThrowsAnswersNoDevices()
    {
        using ProbeOnlyDeviceProvider provider = new() { Throws = true };

        List<Exception> failures = Watch(() =>
        {
            Assert.Empty(provider.GetDevices());
            Assert.True(provider.Start());
            provider.Stop();
        });

        Assert.Equal(2, failures.Count);
        Assert.All(failures, static failure => Assert.IsType<InvalidOperationException>(failure));
        Assert.All(failures, static failure => Assert.Equal(ProbeOnlyDeviceProvider.Excuse, failure.Message));
    }

    /// <summary>
    /// An override that answers a list with an empty entry: the hand-out
    /// refuses it, and it refuses it before it has referenced anything, so the
    /// device that was in the same list is left exactly as it was.
    /// </summary>
    [Fact]
    public void AProbeOverrideThatAnswersANullEntryReferencesNothing()
    {
        using ProbeOnlyDeviceProvider provider = new() { AnswersANullEntry = true };

        List<Exception> failures = Watch(() => Assert.Empty(provider.GetDevices()));

        Exception reported = Assert.Single(failures);
        Assert.IsType<InvalidOperationException>(reported);
        _output.WriteLine($"refused: {reported.Message}");
        Assert.Contains("OnProbe", reported.Message, StringComparison.Ordinal);
        Assert.Contains("probe", reported.Message, StringComparison.Ordinal);
        Assert.Equal(1u, RefCountOf(provider.Kept.Handle));
    }

    /// <summary>
    /// The registration: either slot is accepted alone, and a provider that
    /// declares neither is refused with a message that names both.
    /// </summary>
    [Fact]
    public void EitherSlotAloneRegistersAndNeitherIsRefused()
    {
        using ProbeOnlyDeviceProvider probeOnly = new();
        using ProbeChainUpDeviceProvider startOnly = new();

        Assert.True(Gst.GObject.GType.FromName(ProbeOnlyDeviceProvider.GTypeName).IsValid);
        Assert.True(Gst.GObject.GType.FromName(ProbeChainUpDeviceProvider.GTypeName).IsValid);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => DeviceProvider.DefineSubclass(
                "GstSharpTestNeitherSlotDeviceProvider",
                null,
                DeviceProvider.StopOverride));

        Assert.Contains("StartOverride", error.Message, StringComparison.Ordinal);
        Assert.Contains("ProbeOverride", error.Message, StringComparison.Ordinal);
        Assert.False(Gst.GObject.GType.FromName("GstSharpTestNeitherSlotDeviceProvider").IsValid);
    }

    /// <summary>
    /// The same device answered across a start, a stop and a second start: it
    /// is announced both times and ends at the count it began with, which is
    /// what says the device was unparented in between rather than refused as an
    /// already parented one.
    /// </summary>
    [Fact]
    public void AKeptDeviceSurvivesASecondStart()
    {
        using ProbeOnlyDeviceProvider provider = new() { AnswersTheKeptDeviceAlone = true };
        using Bus bus = provider.GetBus();

        for (int round = 0; round < 2; round++)
        {
            Assert.True(provider.Start());

            using (Message? added = bus.Pop())
            {
                Assert.NotNull(added);
                Assert.Equal(MessageType.DeviceAdded, added.Type);
            }

            Assert.Equal(2u, RefCountOf(provider.Kept.Handle));
            Assert.Same(provider, provider.Kept.Parent);

            provider.Stop();

            Assert.Equal(1u, RefCountOf(provider.Kept.Handle));
            Assert.Null(provider.Kept.Parent);
        }

        Assert.Equal(2, provider.Probed);
    }

    /// <summary>
    /// The same device twice in one answer: both occurrences mint a reference,
    /// the second <c>device_add</c> is the one that finds a parent already and
    /// is refused, and the count still settles where one announcement leaves
    /// it.
    /// </summary>
    [Fact]
    public void TheSameDeviceTwiceIsAnnouncedOnce()
    {
        using ProbeOnlyDeviceProvider provider = new() { AnswersTheKeptDeviceTwice = true };
        using Bus bus = provider.GetBus();

        Assert.True(provider.Start());

        try
        {
            using (Message? added = bus.Pop())
            {
                Assert.NotNull(added);
                Assert.Equal(MessageType.DeviceAdded, added.Type);
            }

            Assert.Null(bus.Pop());
            Assert.Equal(2u, RefCountOf(provider.Kept.Handle));
            Assert.Same(provider.Kept, Assert.Single(provider.GetDevices()));
        }
        finally
        {
            provider.Stop();
        }

        Assert.Equal(1u, RefCountOf(provider.Kept.Handle));
    }

    /// <summary>
    /// The two callers of the slot: a provider that is not started probes on
    /// every listing, and a started one answers the list it built while it
    /// started without probing again.
    /// </summary>
    [Fact]
    public void AStartedProviderListsWithoutProbingAgain()
    {
        using ProbeOnlyDeviceProvider provider = new() { AnswersTheKeptDeviceAlone = true };
        using Bus bus = provider.GetBus();

        Assert.Same(provider.Kept, Assert.Single(provider.GetDevices()));
        Assert.Equal(1, provider.Probed);
        Assert.Equal(1u, RefCountOf(provider.Kept.Handle));

        Assert.True(provider.Start());

        try
        {
            int probed = provider.Probed;
            using (Message? added = bus.Pop())
            {
                Assert.NotNull(added);
            }

            Assert.Same(provider.Kept, Assert.Single(provider.GetDevices()));
            Assert.Equal(probed, provider.Probed);
            Assert.Equal(2u, RefCountOf(provider.Kept.Handle));
        }
        finally
        {
            provider.Stop();
        }

        Assert.Equal(1u, RefCountOf(provider.Kept.Handle));
    }

    /// <summary>
    /// An override that chains up: no class below a managed provider implements
    /// <c>probe</c>, so the chain-up reads a NULL slot and answers no devices,
    /// which both callers of the slot take as the empty answer rather than as a
    /// failure.
    /// </summary>
    [Fact]
    public void AProbeOverrideThatChainsUpAnswersNoDevices()
    {
        using ProbeOnlyDeviceProvider provider = new() { ChainsUp = true };
        using Bus bus = provider.GetBus();

        // The trap listens throughout: a ChainUpProbe() that threw would be
        // reported there and answer no devices as well, which is the same
        // reading as the empty answer this test is about.
        List<Exception> failures = Watch(() =>
        {
            Assert.Empty(provider.GetDevices());
            Assert.Equal(1, provider.Probed);
            Assert.Null(provider.Fresh);

            // The same answer as a C caller sees it: NULL is the empty list.
            Assert.Equal(nint.Zero, DeviceProviderGetDevices(provider.Handle));
            Assert.Equal(2, provider.Probed);

            Assert.True(provider.Start());

            try
            {
                Assert.Equal(3, provider.Probed);
                Assert.Null(bus.Pop());
            }
            finally
            {
                provider.Stop();
            }
        });

        Assert.Empty(failures);
    }

    /// <summary>Runs an action with the exception trap listening.</summary>
    /// <param name="action">What to run.</param>
    /// <returns>What the trap reported while it ran.</returns>
    private static List<Exception> Watch(Action action)
    {
        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        ExceptionTrap.UnhandledException += OnFailure;
        try
        {
            action();
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        return failures;
    }

    /// <summary>Reads the reference count of a <c>GObject</c>.</summary>
    /// <param name="handle">The instance to measure.</param>
    /// <returns>The count, which the field behind the class pointer holds.</returns>
    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));

    /// <summary>
    /// Calls the accessor the way a C caller does, which is what the forward
    /// member cannot be measured against: it adopts the reference the list
    /// carries, and the state of the answer before that is what this pins.
    /// </summary>
    /// <param name="provider">The provider to ask.</param>
    /// <returns>The list, which the caller owns together with one reference per node.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_device_provider_get_devices")]
    private static partial nint DeviceProviderGetDevices(nint provider);

    /// <summary>Releases one reference of a <c>GObject</c>.</summary>
    /// <param name="instance">The instance to release.</param>
    [LibraryImport("GObject", EntryPoint = "g_object_unref")]
    private static partial void ObjectUnref(nint instance);

    /// <summary>Releases the nodes of a <c>GList</c>, and nothing else.</summary>
    /// <param name="list">The first node.</param>
    [LibraryImport("GLib", EntryPoint = "g_list_free")]
    private static partial void ListFree(nint list);
}
