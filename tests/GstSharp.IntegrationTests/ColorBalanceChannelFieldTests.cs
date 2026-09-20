using System;
using System.Collections.Generic;
using Gst;
using Gst.GObject;
using Gst.Interop;
using Gst.Video;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The three hand bound public fields of a <see cref="ColorBalanceChannel"/>,
/// and the instance layout they are read out of.
/// </summary>
/// <remarks>
/// <c>label</c>, <c>min_value</c> and <c>max_value</c> carry no accessor in C,
/// so the wrapper reads them through a mirror of the instance: the layout of
/// that mirror is what everything here rests on. It is asserted against the
/// offsets of the 1.28 headers, against the instance size the running library
/// reports, and against the channels of two real elements, whose labels and
/// ranges a wrong offset cannot produce.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ColorBalanceChannelFieldTests
{
    /// <summary>
    /// The mirror lays its fields where the C headers put them on a 64 bit
    /// platform, and is as long as the whole instance.
    /// </summary>
    /// <remarks>
    /// The derivation is written out on
    /// <c>Gst.Video.ColorBalanceChannelRaw</c>. The numbers are repeated here
    /// on purpose: the C ABI is the ground truth, so a mirror that drifts has
    /// to fail here rather than quietly agree with itself.
    /// </remarks>
    [Fact]
    public unsafe void TheInstanceIsMirroredWhereTheHeadersPutIt()
    {
        ColorBalanceChannelRaw raw = default;

        Assert.Equal(24L, Offset(&raw, &raw.Label));
        Assert.Equal(32L, Offset(&raw, &raw.MinValue));
        Assert.Equal(36L, Offset(&raw, &raw.MaxValue));
        Assert.Equal(40L, Offset(&raw, &raw.GstReserved));
        Assert.Equal(72, sizeof(ColorBalanceChannelRaw));
    }

    /// <summary>
    /// The installed library allocates an instance of exactly the length the
    /// mirror has, which is what makes the offsets above the ones of the
    /// running <c>GstColorBalanceChannel</c> rather than of a header the
    /// binding was written against.
    /// </summary>
    [Fact]
    public unsafe void TheInstanceSizeMatchesTheRunningLibrary()
    {
        GObjectNative.TypeQuery(ColorBalanceChannel.GetGType(), out GTypeQuery query);

        Assert.Equal((uint)sizeof(ColorBalanceChannelRaw), query.InstanceSize);
    }

    /// <summary>
    /// A bare <c>videobalance</c> lists the four channels its instance init
    /// built, with the labels and the range the plugin gives them. The element
    /// is never taken out of the NULL state: the channels exist from
    /// construction.
    /// </summary>
    /// <remarks>
    /// The channels come out of the list at <see cref="Transfer.None"/> as
    /// interned wrappers that hold their own reference, so none of them is the
    /// caller's to dispose; the element is.
    /// </remarks>
    [RequiresElementFact("videobalance")]
    public void TheChannelsOfAVideoBalanceCarryTheirLabelsAndRanges()
    {
        using Element balance = ElementFactory.Make("videobalance", null)
            ?? throw new InvalidOperationException("videobalance is part of the good plugins.");

        IColorBalance colorBalance = balance.As<IColorBalance>()
            ?? throw new InvalidOperationException("videobalance implements GstColorBalance.");

        IReadOnlyList<ColorBalanceChannel> channels = colorBalance.ListChannels();

        Assert.Equal(
            new[] { "HUE", "SATURATION", "BRIGHTNESS", "CONTRAST" },
            Labels(channels));

        foreach (ColorBalanceChannel channel in channels)
        {
            Assert.Equal(-1000, channel.MinValue);
            Assert.Equal(1000, channel.MaxValue);
        }
    }

    /// <summary>
    /// A bare <c>playbin</c> lists the four proxy channels its sink built, in
    /// the order the sink appends them, and remembers a value written to one of
    /// them. No media is played and no state is changed: the proxies and their
    /// cached values live in the sink from construction.
    /// </summary>
    /// <remarks>
    /// The channel handed to <c>set_value</c> has to be one of the objects the
    /// element itself listed, which is what holding on to the list gives. The
    /// channels are interned wrappers that hold their own reference and are not
    /// the caller's to dispose.
    /// </remarks>
    [RequiresElementFact("playbin")]
    public void APlaybinRemembersAValueWrittenToOneOfItsChannels()
    {
        using Element playbin = ElementFactory.Make("playbin", null)
            ?? throw new InvalidOperationException("playbin is part of the base plugins.");

        IColorBalance colorBalance = playbin.As<IColorBalance>()
            ?? throw new InvalidOperationException("playbin implements GstColorBalance.");

        IReadOnlyList<ColorBalanceChannel> channels = colorBalance.ListChannels();

        Assert.Equal(
            new[] { "CONTRAST", "BRIGHTNESS", "HUE", "SATURATION" },
            Labels(channels));

        foreach (ColorBalanceChannel channel in channels)
        {
            Assert.Equal(-1000, channel.MinValue);
            Assert.Equal(1000, channel.MaxValue);
        }

        ColorBalanceChannel contrast = channels[0];
        colorBalance.SetValue(contrast, 250);

        Assert.Equal(250, colorBalance.GetValue(contrast));
    }

    /// <summary>
    /// A channel made through <see cref="ColorBalanceChannel.New(string, int, int)"/>
    /// carries what it was made with, and the three setters write it again.
    /// </summary>
    /// <remarks>
    /// The label is written twice on purpose: the second write is the one that
    /// has to free the copy the first one made, and a mismatch between the
    /// allocator of the write and the <c>g_free</c> of the C dispose aborts the
    /// process here rather than failing an assertion. The leak of a write that
    /// forgot to free is not observable from managed code; only the pairing is.
    /// </remarks>
    [Fact]
    public void AChannelOfOnesOwnCarriesWhatItWasMadeWith()
    {
        using ColorBalanceChannel channel = ColorBalanceChannel.New("BRIGHTNESS", -1000, 1000);

        Assert.Equal("BRIGHTNESS", channel.Label);
        Assert.Equal(-1000, channel.MinValue);
        Assert.Equal(1000, channel.MaxValue);

        channel.Label = "CONTRAST";
        Assert.Equal("CONTRAST", channel.Label);

        channel.Label = "SATURATION";
        Assert.Equal("SATURATION", channel.Label);

        channel.MinValue = -100;
        channel.MaxValue = 100;

        Assert.Equal(-100, channel.MinValue);
        Assert.Equal(100, channel.MaxValue);
    }

    /// <summary>
    /// A channel an element listed refuses all three setters, while a channel
    /// of one's own takes them
    /// (<see cref="AChannelOfOnesOwnCarriesWhatItWasMadeWith"/>).
    /// </summary>
    /// <remarks>
    /// The element finds its own channel again by the content of the label, so
    /// a write there would disable its <c>set_value</c> and <c>get_value</c>,
    /// abort the process under <c>playsink</c> and free a string the element
    /// may be reading on another thread. The three fields are read back to show
    /// that the refusal happened before anything was written.
    /// </remarks>
    [RequiresElementFact("videobalance")]
    public void AChannelOfAnElementRefusesEverySetter()
    {
        using Element balance = ElementFactory.Make("videobalance", null)
            ?? throw new InvalidOperationException("videobalance is part of the good plugins.");

        IColorBalance colorBalance = balance.As<IColorBalance>()
            ?? throw new InvalidOperationException("videobalance implements GstColorBalance.");

        ColorBalanceChannel listed = colorBalance.ListChannels()[0];

        Assert.Throws<InvalidOperationException>(() => listed.Label = "BRIGHTNESS");
        Assert.Throws<InvalidOperationException>(() => listed.MinValue = 0);
        Assert.Throws<InvalidOperationException>(() => listed.MaxValue = 0);

        Assert.Equal("HUE", listed.Label);
        Assert.Equal(-1000, listed.MinValue);
        Assert.Equal(1000, listed.MaxValue);
    }

    /// <summary>
    /// The hand written factory and setter refuse what they cannot write
    /// rather than leaving a channel that aborts <c>playsink</c> later.
    /// </summary>
    [Fact]
    public void AChannelRefusesALabelThatIsNothingAndARangeTheWrongWayRound()
    {
        Assert.Throws<ArgumentNullException>(() => ColorBalanceChannel.New(null!, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorBalanceChannel.New("HUE", 1, 0));

        using ColorBalanceChannel channel = ColorBalanceChannel.New("HUE", 0, 0);

        Assert.Throws<ArgumentNullException>(() => channel.Label = null);
        Assert.Equal("HUE", channel.Label);
    }

    /// <summary>Reads the label of every channel of a list.</summary>
    /// <param name="channels">The channels to read.</param>
    /// <returns>The labels, in the order the channels were listed in.</returns>
    private static string?[] Labels(IReadOnlyList<ColorBalanceChannel> channels)
    {
        string?[] labels = new string?[channels.Count];

        for (int index = 0; index < channels.Count; index++)
        {
            labels[index] = channels[index].Label;
        }

        return labels;
    }

    /// <summary>Measures where a field of a mirror sits.</summary>
    /// <param name="start">The address of the mirror.</param>
    /// <param name="field">The address of the field.</param>
    /// <returns>The offset of the field, in bytes.</returns>
    private static unsafe long Offset(void* start, void* field) => (byte*)field - (byte*)start;
}
