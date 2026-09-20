using Gst;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The managed <c>GstDeviceProvider</c> subclass: the <c>start</c> and
/// <c>stop</c> slots a provider takes over, the devices it announces while it
/// runs, and what a declared <c>start</c> slot answers when the subclass does
/// not override it.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class ManagedDeviceProviderTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public ManagedDeviceProviderTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The path the slice exists for: both overrides run, in the order the
    /// caller drives them, and the provider reports itself as started in
    /// between.
    /// </summary>
    [Fact]
    public void AManagedProviderReachesItsStartAndStopOverrides()
    {
        using ProbeDeviceProvider provider = new();

        // Before the provider ever ran, the accessor takes the probe path,
        // which a managed provider leaves NULL: the guard in
        // gst_device_provider_get_devices makes that an empty answer.
        Assert.Empty(provider.GetDevices());
        Assert.False(provider.IsStarted());

        Assert.True(provider.Start());

        try
        {
            Assert.Equal(1, provider.Started);
            Assert.True(provider.IsStarted());
        }
        finally
        {
            provider.Stop();
        }

        Assert.Equal(1, provider.Stopped);
        Assert.False(provider.IsStarted());
    }

    /// <summary>
    /// A provider that announces a device from its <c>start</c> override: the
    /// device is part of what the provider lists while it runs, and the
    /// withdrawal from <c>stop</c> takes it off the list again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The device is one the test project mints itself, so the fact runs on a
    /// machine with no hardware at all: <see cref="ProbeDevice"/> is a managed
    /// <c>Gst.Device</c> subclass built through the generated
    /// <c>DefineSubclass</c>, which is what lets a provider announce a device
    /// that describes nothing.
    /// </para>
    /// <para>
    /// The withdrawal is asserted through the <c>removed</c> signal of the
    /// device, which is the one thing <c>gst_device_provider_device_remove</c>
    /// does that the base class does not do by itself: stop clears whatever is
    /// left of the list with <c>gst_object_unparent</c> and no signal
    /// (<c>gstdeviceprovider.c:536-539</c>), so an empty list after
    /// <c>Stop()</c> — and an unparented device — would stay true with the
    /// <c>DeviceRemove</c> call taken out of the override.
    /// </para>
    /// </remarks>
    [Fact]
    public void AManagedProviderAnnouncesTheDevicesOfItsStartOverride()
    {
        using ProbeDevice device = ProbeDevice.New("Probe device");
        using ProbeDeviceProvider provider = new();
        provider.Announce.Add(device);

        int removals = 0;
        void OnRemoved(object? sender, EventArgs args) =>
            Interlocked.Increment(ref removals);

        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        // An exception out of either override is reported and swallowed by the
        // trampoline, so what the trap saw is what says the announcement ran
        // the way the assertions below read it.
        device.Removed += OnRemoved;
        ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            using Bus bus = provider.GetBus();

            Assert.True(provider.Start());

            try
            {
                IReadOnlyList<Device> listed = provider.GetDevices();

                // Wrappers are interned, so the listed device is the very
                // object that was announced - and disposing it here would take
                // the device the provider still holds away from it.
                Assert.Same(device, Assert.Single(listed));
                Assert.Same(provider, device.Parent);
                Assert.Equal(0, Volatile.Read(ref removals));

                using Message? added = bus.Pop();
                Assert.NotNull(added);
                Assert.Equal(MessageType.DeviceAdded, added.Type);
            }
            finally
            {
                provider.Stop();
            }

            Assert.Empty(provider.GetDevices());
            Assert.Equal(1, Volatile.Read(ref removals));
            Assert.Null(device.Parent);
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
            device.Removed -= OnRemoved;
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// A <c>start</c> override that refuses: <c>Start()</c> answers false, the
    /// provider is not started, and the <c>stop</c> override the provider
    /// declares beside it never runs, because the base class calls no stop for
    /// a provider whose use count never left zero.
    /// </summary>
    [Fact]
    public void AStartOverrideThatAnswersFalseRefusesToStart()
    {
        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        using ProbeRefusingDeviceProvider provider = new();
        ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            Assert.False(provider.Start());
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        Assert.Equal(1, provider.Started);
        Assert.False(provider.IsStarted());
        Assert.Equal(0, provider.Stopped);

        // A refusal is an answer, not a failure: nothing was reported.
        Assert.Empty(failures);
    }

    /// <summary>
    /// A <c>start</c> override that throws: the trampoline reports the
    /// exception and answers the default of the slot, which is false, so the
    /// provider refuses to start the same way.
    /// </summary>
    [Fact]
    public void AStartOverrideThatThrowsRefusesToStart()
    {
        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        using ProbeRefusingDeviceProvider provider = new() { Throws = true };
        ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            Assert.False(provider.Start());
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        Assert.Equal(1, provider.Started);
        Assert.False(provider.IsStarted());
        Assert.Equal(0, provider.Stopped);

        Exception reported = Assert.Single(failures);
        Assert.IsType<InvalidOperationException>(reported);
        Assert.Equal(ProbeRefusingDeviceProvider.Excuse, reported.Message);
    }

    /// <summary>
    /// A subclass that declares the <c>start</c> slot without overriding it:
    /// the trampoline reaches the base <c>OnStart</c>, which chains up into a
    /// parent class that has no <c>start</c> at all, and the answer is the
    /// "nothing below refuses" of the chain-up rather than a call through a
    /// NULL slot.
    /// </summary>
    [Fact]
    public void ADeclaredStartSlotWithoutAnOverrideAnswersTrue()
    {
        using ProbeChainUpDeviceProvider provider = new();

        Assert.True(provider.Start());

        try
        {
            Assert.True(provider.IsStarted());
        }
        finally
        {
            provider.Stop();
        }

        Assert.False(provider.IsStarted());
    }

    /// <summary>
    /// A registration that takes over <c>stop</c> alone is refused before it
    /// takes the type name: it would leave <c>klass-&gt;start</c> NULL, and the
    /// fallback the C takes for that is an unguarded <c>klass-&gt;probe</c>
    /// call a managed provider has nothing to answer with.
    /// </summary>
    [Fact]
    public void AProviderThatDeclaresStopAloneIsRefused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => DeviceProvider.DefineSubclass(
                "GstSharpTestStopOnlyDeviceProvider",
                null,
                DeviceProvider.StopOverride));

        _output.WriteLine($"refused: {error.Message}");

        Assert.Contains("StartOverride", error.Message, StringComparison.Ordinal);
        Assert.False(Gst.GObject.GType.FromName("GstSharpTestStopOnlyDeviceProvider").IsValid);
    }
}
