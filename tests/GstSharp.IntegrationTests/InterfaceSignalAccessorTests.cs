using Gst;
using Gst.Video;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The signal accessors of a gir interface reached through the view that
/// <see cref="Gst.GObject.Object.As{T}"/> hands out for an element whose
/// wrapper class does not declare the interface.
/// </summary>
/// <remarks>
/// The view is not an object wrapper, so the accessors resolve it to the
/// wrapper it was taken from. Each <c>As&lt;T&gt;()</c> call makes a new view,
/// which is why a handler added through one view is removed through another.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class InterfaceSignalAccessorTests
{
    /// <summary>
    /// <c>videobalance</c> wraps as a video filter, so its colour balance is
    /// reached through a view. A handler added through one view fires once for
    /// a value that changed, and removing it through a second view stops it.
    /// </summary>
    [RequiresElementFact("videobalance")]
    public void AColorBalanceViewConnectsAndDisconnectsTheValueChangedSignal()
    {
        GstVideo.Initialize();
        using Element element = ElementFactory.Make("videobalance", null)
            ?? throw new InvalidOperationException("videobalance is part of the good plugins.");

        IColorBalance first = element.As<IColorBalance>()
            ?? throw new InvalidOperationException("videobalance implements GstColorBalance.");
        Assert.False(first is Gst.GObject.Object);
        ColorBalanceChannel hue = first.ListChannels().Single(channel => channel.Label == "HUE");

        List<(string? Label, int Value)> announced = [];
        void OnValueChanged(object? sender, ColorBalanceExtensions.ValueChangedSignalArgs args) =>
            announced.Add((args.Channel.Label, args.Value));

        first.AddValueChangedHandler(OnValueChanged);
        first.SetValue(hue, 250);

        Assert.Equal([("HUE", 250)], announced);

        IColorBalance second = element.As<IColorBalance>()
            ?? throw new InvalidOperationException("videobalance implements GstColorBalance.");
        Assert.NotSame(first, second);
        second.RemoveValueChangedHandler(OnValueChanged);

        // videobalance announces a value only when it changes, so the second
        // write uses a different one: a handler still connected would see it.
        first.SetValue(hue, -250);

        Assert.Equal(-250, first.GetValue(hue));
        Assert.Equal([("HUE", 250)], announced);
    }

    /// <summary>
    /// <c>compositor</c> wraps as a video aggregator, so its child proxy is
    /// reached through a view. Its <c>request_new_pad</c> announces the new
    /// pad as a child, so a handler added through the view sees a requested
    /// sink pad, and removing it through a second view stops it.
    /// </summary>
    [RequiresElementFact("compositor")]
    public void AChildProxyViewConnectsAndDisconnectsTheChildAddedSignal()
    {
        GstVideo.Initialize();
        using Element element = ElementFactory.Make("compositor", null)
            ?? throw new InvalidOperationException("compositor is part of the base plugins.");

        IChildProxy first = element.As<IChildProxy>()
            ?? throw new InvalidOperationException("compositor implements GstChildProxy.");
        Assert.False(first is Gst.GObject.Object);

        List<string?> added = [];
        void OnChildAdded(object? sender, ChildProxyExtensions.ChildAddedSignalArgs args) => added.Add(args.Name);

        first.AddChildAddedHandler(OnChildAdded);
        using Pad pad = element.RequestPadSimple("sink_%u")
            ?? throw new InvalidOperationException("compositor hands out sink pads on request.");

        Assert.Equal([pad.Name], added);

        IChildProxy second = element.As<IChildProxy>()
            ?? throw new InvalidOperationException("compositor implements GstChildProxy.");
        second.RemoveChildAddedHandler(OnChildAdded);

        using Pad other = element.RequestPadSimple("sink_%u")
            ?? throw new InvalidOperationException("compositor hands out sink pads on request.");

        Assert.Single(added);

        element.ReleaseRequestPad(other);
        element.ReleaseRequestPad(pad);
    }

    /// <summary>A null receiver is refused before anything reaches the library.</summary>
    [Fact]
    public void ANullReceiverIsRefused()
    {
        IColorBalance balance = null!;
        IChildProxy proxy = null!;

        Assert.Throws<ArgumentNullException>(
            "self",
            () => balance.AddValueChangedHandler((_, _) => { }));
        Assert.Throws<ArgumentNullException>(
            "self",
            () => balance.RemoveValueChangedHandler((_, _) => { }));
        Assert.Throws<ArgumentNullException>(
            "self",
            () => proxy.AddChildAddedHandler((_, _) => { }));
    }
}
