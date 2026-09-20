using System.Runtime.InteropServices;
using Gst;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The managed <c>GstDevice</c> subclass: the four construct only properties it
/// is built with, the two slots it takes over, and the state the element of a
/// <c>create_element</c> override is in when the caller of the slot gets it.
/// </summary>
/// <remarks>
/// <para>
/// The last one is the whole reason the slot needed a return mode of its own.
/// <c>gst_device_create_element</c> hands the answer of the slot on exactly as it
/// came and raises a critical for one that is not floating
/// (<c>gstdevice.c:217-225</c>); its callers own that reference and are free to
/// drop it with a bare unref, which <c>gst-device-monitor.c:200,205</c> does.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe partial class ManagedDeviceTests
{
    /// <summary>
    /// The construct only half: all four properties of a <c>GstDevice</c> are
    /// <c>CONSTRUCT_ONLY</c>, so the dictionary overload of
    /// <c>NewInstance</c> is the only thing that can write them, and all four
    /// read back off the instance it built.
    /// </summary>
    [Fact]
    public void AManagedDeviceCarriesTheConstructOnlyPropertiesItWasBuiltWith()
    {
        using ProbeDevice device = ProbeDevice.New("Construct probe");

        Assert.Equal("Construct probe", device.DisplayName);
        Assert.Equal("Test/Probe", device.DeviceClass);

        using Caps? caps = device.GetCaps();
        Assert.NotNull(caps);
        Assert.Equal(ProbeDevice.CapsDescription, caps.ToString());

        using Structure? properties = device.GetProperties();
        Assert.NotNull(properties);
        Assert.Equal(ProbeDevice.PropertiesName, properties.GetName());
    }

    /// <summary>
    /// The raw slot call, which is what a C caller of
    /// <c>gst_device_create_element</c> sees: the answer is floating, it carries
    /// one reference more than the wrapper of the override holds, and no
    /// critical is logged for it.
    /// </summary>
    [Fact]
    public void TheRawSlotAnswersAFloatingElementWithoutACritical()
    {
        Assert.True(InitializeLogProbe.IsInstalled);

        using ProbeDevice device = ProbeDevice.New("Floating probe");

        nint answer = nint.Zero;
        IReadOnlyList<string> logged = InitializeLogProbe.CaptureWhile(
            () => answer = DeviceCreateElement(device.Handle, null));

        try
        {
            Assert.NotEqual(nint.Zero, answer);
            Assert.Equal(1, device.Created);

            // Asserted before the state of the answer, because this is the
            // assertion a broken hand out reaches first: the C raises the
            // critical while the call is still inside the capture.
            Assert.DoesNotContain(
                logged,
                static message => message.Contains("should be floating", StringComparison.Ordinal));

            // The trampoline minted the reference the caller now owns and put
            // the flag back on, so the object carries the wrapper's reference
            // and the caller's.
            Assert.NotEqual(0, ObjectIsFloating(answer));
            Assert.Equal(2u, RefCountOf(answer));

            Element kept = Assert.IsAssignableFrom<Element>(device.Answered);
            Assert.Equal(kept.Handle, answer);
        }
        finally
        {
            // What gst-device-monitor.c:200 does with an answer it decided not
            // to use. Guarded, because a failure of the first assertion would
            // otherwise unref nothing and add a critical of its own to the
            // noise the failure is read through.
            if (answer != nint.Zero)
            {
                ObjectUnref(answer);
            }
        }
    }

    /// <summary>
    /// The bare unref of the raw answer, which is the drop a caller that never
    /// sinks performs: the wrapper of the override keeps the reference it owns
    /// and stays usable afterwards.
    /// </summary>
    [Fact]
    public void ABareUnrefOfTheRawAnswerLeavesTheWrapperUsable()
    {
        using ProbeDevice device = ProbeDevice.New("Unref probe");

        nint answer = DeviceCreateElement(device.Handle, "unreffed");
        Assert.NotEqual(nint.Zero, answer);

        Element kept = Assert.IsAssignableFrom<Element>(device.Answered);
        Assert.Equal(2u, RefCountOf(answer));

        ObjectUnref(answer);

        Assert.Equal(1u, RefCountOf(kept.Handle));
        Assert.Equal("unreffed", kept.Name);
    }

    /// <summary>
    /// The forward call from managed code: the wrapper the override answered is
    /// the one the caller gets back, the floating reference the trampoline minted
    /// is settled on the way through, and what is left is the one reference the
    /// wrapper owns.
    /// </summary>
    [Fact]
    public void TheForwardCallHandsBackTheWrapperTheOverrideAnswered()
    {
        using ProbeDevice device = ProbeDevice.New("Forward probe");

        Element? element = device.CreateElement("forwarded");

        Assert.NotNull(element);
        Assert.Same(device.Answered, element);
        Assert.Equal(1, device.Created);

        // Settled: the reference the caller of the slot was handed has been
        // sunk and given back, and the flag is gone with it.
        Assert.Equal(0, ObjectIsFloating(element.Handle));
        Assert.Equal(1u, RefCountOf(element.Handle));
        Assert.Equal("forwarded", element.Name);
    }

    /// <summary>
    /// An override that answers nothing, which <c>gst_device_create_element</c>
    /// allows: the forward call answers null and nothing is logged for it.
    /// </summary>
    [Fact]
    public void AnOverrideThatAnswersNothingIsNotACritical()
    {
        Assert.True(InitializeLogProbe.IsInstalled);

        using ProbeDevice device = ProbeDevice.New("Empty probe");
        device.AnswersNothing = true;

        Element? element = null;
        IReadOnlyList<string> logged = InitializeLogProbe.CaptureWhile(
            () => element = device.CreateElement(null));

        Assert.Null(element);
        Assert.Equal(1, device.Created);
        Assert.DoesNotContain(
            logged,
            static message => message.Contains("should be floating", StringComparison.Ordinal));
    }

    /// <summary>
    /// The other slot: the override is reached through the forward call and its
    /// answer is what the caller reads.
    /// </summary>
    [Fact]
    public void TheReconfigureOverrideIsReachedThroughTheForwardCall()
    {
        using ProbeDevice device = ProbeDevice.New("Reconfigure probe");
        using Element element = ElementFactory.Make("identity", "reconfigured")
            ?? throw new InvalidOperationException("The identity element is not installed.");

        Assert.True(device.ReconfigureElement(element));
        Assert.Equal(1, device.Reconfigured);

        device.Reconfigures = false;
        Assert.False(device.ReconfigureElement(element));
        Assert.Equal(2, device.Reconfigured);
    }

    /// <summary>
    /// A subclass that declares both slots and overrides neither: every call
    /// goes through the generated chain-up onto a parent class that leaves the
    /// slots NULL, and the answers are the ones the C gives for an empty slot
    /// rather than an exception out of the chain-up.
    /// </summary>
    [Fact]
    public void ADeviceThatOverridesNeitherSlotAnswersTheDefaults()
    {
        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        using ProbeChainUpDevice device = ProbeChainUpDevice.New();
        using Element element = ElementFactory.Make("identity", "untouched")
            ?? throw new InvalidOperationException("The identity element is not installed.");

        ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            Assert.Null(device.CreateElement(null));
            Assert.False(device.ReconfigureElement(element));
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        Assert.Empty(failures);
    }

    /// <summary>Reads the reference count of a <c>GObject</c>.</summary>
    /// <param name="handle">The instance to measure.</param>
    /// <returns>The count, which the field behind the class pointer holds.</returns>
    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));

    /// <summary>
    /// Calls the <c>create_element</c> slot the way a C caller does, which is
    /// what the forward member of the binding cannot be measured against: it
    /// settles the floating reference the slot hands out, and the state of the
    /// answer before that is what this pins.
    /// </summary>
    /// <param name="device">The device to ask.</param>
    /// <param name="name">The name of the element, or <c>null</c>.</param>
    /// <returns>The element, floating and owned by the caller.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_device_create_element", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint DeviceCreateElement(nint device, string? name);

    /// <summary>Tests whether an object still carries its floating reference.</summary>
    /// <param name="instance">The instance to test.</param>
    /// <returns>Non zero when the object is floating.</returns>
    [LibraryImport("GObject", EntryPoint = "g_object_is_floating")]
    private static partial int ObjectIsFloating(nint instance);

    /// <summary>Releases one reference of an object without sinking it first.</summary>
    /// <param name="instance">The instance to release.</param>
    [LibraryImport("GObject", EntryPoint = "g_object_unref")]
    private static partial void ObjectUnref(nint instance);
}
