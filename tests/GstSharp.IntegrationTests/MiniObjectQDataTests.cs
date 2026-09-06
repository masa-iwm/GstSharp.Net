using Gst;
using Gst.GLib;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The quark keyed data of a mini object, against the library that is
/// installed.
/// </summary>
/// <remarks>
/// The binding reads and steals this data but never writes it: the setter
/// takes a destroy notification whose lifetime belongs to whoever stored the
/// pointer, and that is native code. The store below is therefore
/// <see cref="TestNatives.MiniObjectSetQData"/>, standing in for the plugin
/// that would have made the entry, and the pointer it stores is a plain number
/// with no notification attached, so nothing has to be released when the buffer
/// goes.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MiniObjectQDataTests
{
    /// <summary>The pointer the store below puts on the buffer.</summary>
    private static readonly nint Stored = 0x5157;

    /// <summary>
    /// Nothing is stored under a fresh quark, the store is what
    /// <see cref="MiniObject.GetQData"/> reads back, and
    /// <see cref="MiniObject.StealQData"/> answers it once and removes it.
    /// </summary>
    [Fact]
    public void QDataIsReadStolenAndGoneAfterwards()
    {
        using Gst.Buffer buffer = Assert.IsType<Gst.Buffer>(Gst.Buffer.NewAllocate(null, 16, null));

        Quark quark = Quark.FromString("gstsharp-net-qdata-test");
        Assert.NotEqual(Quark.Zero, quark);

        // An empty entry is an ordinary answer, not a failure.
        Assert.Equal(nint.Zero, buffer.GetQData(quark));

        TestNatives.MiniObjectSetQData(buffer.Handle, quark.Value, Stored, nint.Zero);

        Assert.Equal(Stored, buffer.GetQData(quark));

        // Reading does not remove the entry; stealing does, and the caller
        // owns what it was handed afterwards.
        Assert.Equal(Stored, buffer.GetQData(quark));
        Assert.Equal(Stored, buffer.StealQData(quark));

        Assert.Equal(nint.Zero, buffer.GetQData(quark));
        Assert.Equal(nint.Zero, buffer.StealQData(quark));
    }

    /// <summary>
    /// The zero quark is refused by both members rather than handed to the C,
    /// which guards <c>quark &gt; 0</c> and answers NULL with a critical.
    /// </summary>
    /// <remarks>
    /// An empty entry and a quark that names nothing would otherwise be the
    /// same answer, and only one of the two is a programming error.
    /// </remarks>
    [Fact]
    public void TheZeroQuarkIsRefusedByBothMembers()
    {
        using Gst.Buffer buffer = Assert.IsType<Gst.Buffer>(Gst.Buffer.NewAllocate(null, 16, null));

        ArgumentException read = Assert.Throws<ArgumentException>(() => buffer.GetQData(Quark.Zero));
        Assert.Equal("quark", read.ParamName);

        ArgumentException stolen = Assert.Throws<ArgumentException>(() => buffer.StealQData(Quark.Zero));
        Assert.Equal("quark", stolen.ParamName);
    }
}
