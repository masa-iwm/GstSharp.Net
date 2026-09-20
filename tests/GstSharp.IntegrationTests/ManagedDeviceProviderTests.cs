using Gst;
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

        Assert.Equal(1, provider.Started);
        Assert.True(provider.IsStarted());

        provider.Stop();

        Assert.Equal(1, provider.Stopped);
        Assert.False(provider.IsStarted());
    }

    /// <summary>
    /// A provider that announces a device from its <c>start</c> override: the
    /// device is part of what the provider lists while it runs, and the
    /// withdrawal from <c>stop</c> takes it off the list again.
    /// </summary>
    /// <remarks>
    /// The device is borrowed from whatever provider this machine has, because
    /// <c>GstDevice</c> is abstract and only a plugin creates one. A machine
    /// with no device at all is a valid one; the test says so and stops.
    /// </remarks>
    [Fact]
    public void AManagedProviderAnnouncesTheDevicesOfItsStartOverride()
    {
        using Device? borrowed = BorrowDevice();
        if (borrowed is null)
        {
            _output.WriteLine("No device provider of this machine lists a device; nothing to announce.");
            return;
        }

        using ProbeDeviceProvider provider = new();
        provider.Announce.Add(borrowed);

        Assert.True(provider.Start());

        IReadOnlyList<Device> listed = provider.GetDevices();
        Assert.Single(listed);
        Assert.Equal(borrowed.Handle, listed[0].Handle);
        foreach (Device device in listed)
        {
            device.Dispose();
        }

        provider.Stop();

        Assert.Empty(provider.GetDevices());
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
        Assert.True(provider.IsStarted());

        provider.Stop();

        Assert.False(provider.IsStarted());
    }

    private static Device? BorrowDevice()
    {
        IReadOnlyList<DeviceProviderFactory> factories =
            DeviceProviderFactory.ListGetDeviceProviders(Rank.None);

        try
        {
            foreach (DeviceProviderFactory factory in factories)
            {
                using DeviceProvider? provider = factory.Get();
                if (provider is null || !provider.Start())
                {
                    continue;
                }

                try
                {
                    Device? first = null;
                    foreach (Device device in provider.GetDevices())
                    {
                        if (first is null)
                        {
                            first = device;
                            continue;
                        }

                        device.Dispose();
                    }

                    if (first is not null)
                    {
                        return first;
                    }
                }
                finally
                {
                    provider.Stop();
                }
            }
        }
        finally
        {
            foreach (DeviceProviderFactory factory in factories)
            {
                factory.Dispose();
            }
        }

        return null;
    }
}
