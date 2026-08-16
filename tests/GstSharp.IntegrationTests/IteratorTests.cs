using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Reading a <c>GstIterator</c> against the library that is installed: the
/// elements of a bin and the pads of an element come back in order, typed, and
/// as the wrappers every other lookup hands out.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_iterator_next</c> fills a <c>GValue</c> the caller allocates and
/// answers with a status, which is a shape the emitter refuses, so the
/// generated <c>Gst.Iterator</c> carries only <c>Copy</c>, <c>Push</c> and
/// <c>Resync</c>. Thirteen public methods return one of these, and without a
/// hand written read every one of them is a dead end. This is where the read
/// is measured.
/// </para>
/// <para>
/// The <c>GST_ITERATOR_RESYNC</c> path is not covered here: it needs another
/// thread to modify the bin inside the window between two <c>next</c> calls,
/// which cannot be forced without a hook into the library. What it does is
/// documented on <c>Gst.Iterator.Items</c> — the walk starts over, and because
/// nothing is handed out before the end there is nothing to take back.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class IteratorTests
{
    [Fact]
    public void TheElementsOfABinComeBackInOrder()
    {
        using Bin bin = Bin.New("iterated");
        using Element first = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesrc", "one"));
        using Element second = Assert.IsAssignableFrom<Element>(ElementFactory.Make("identity", "two"));
        using Element third = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "three"));

        Assert.True(bin.AddMany(first, second, third));

        using Iterator iterator = bin.IterateElements();
        IReadOnlyList<Element> elements = iterator.Items<Element>();

        // A bin keeps its children most recently added first, which is the
        // order gst_bin_iterate_elements walks the list in.
        Assert.Equal(
            new[] { "three", "two", "one" },
            elements.Select(static element => element.Name).ToArray());

        // The wrappers are the interned ones: walking a collection is a lookup
        // like any other, so it hands back the instances the test already
        // holds rather than building second ones.
        Assert.Same(third, elements[0]);
        Assert.Same(second, elements[1]);
        Assert.Same(first, elements[2]);

        Assert.True(bin.RemoveMany(first, second, third));
    }

    [Fact]
    public void AnIteratorIsACursorAndIsSpentOnceItIsRead()
    {
        using Bin bin = Bin.New("spent");
        using Element only = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesrc", "only"));

        Assert.True(bin.Add(only));

        using Iterator iterator = bin.IterateElements();

        Assert.Single(iterator.Items<Element>());

        // The walk left the cursor at the end, so a second one reads nothing.
        Assert.Empty(iterator.Items<Element>());

        // Resync rewinds it, which is the supported way of reading it again.
        iterator.Resync();
        Assert.Single(iterator.Items<Element>());

        Assert.True(bin.Remove(only));
    }

    [Fact]
    public void AnEmptyCollectionYieldsAnEmptyList()
    {
        using Bin bin = Bin.New("empty");
        using Iterator iterator = bin.IterateElements();

        Assert.Empty(iterator.Items());
    }

    [Fact]
    public void ThePadsOfAnElementAreReadTheSameWay()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("identity", "pads"));

        using Iterator all = element.IteratePads();
        IReadOnlyList<Pad> pads = all.Items<Pad>();

        Assert.Equal(2, pads.Count);
        Assert.Contains(pads, static pad => string.Equals(pad.Name, "sink", StringComparison.Ordinal));
        Assert.Contains(pads, static pad => string.Equals(pad.Name, "src", StringComparison.Ordinal));

        using Iterator sinks = element.IterateSinkPads();
        Pad sink = Assert.Single(sinks.Items<Pad>());
        Assert.Equal("sink", sink.Name);

        // The untyped overload is the same walk, and every pad is an object.
        using Iterator again = element.IteratePads();
        Assert.Equal(2, again.Items().Count);
    }

    [Fact]
    public void AWrongItemTypeIsReportedRatherThanCastLater()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("identity", "mistyped"));
        using Iterator pads = element.IteratePads();

        // The items are pads, so asking for bins says so at the read.
        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => pads.Items<Bin>());

        Assert.Contains("is not a Bin", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIteratorOverSomethingOtherThanObjectsIsRefused()
    {
        // gst_format_iterate_definitions yields boxed GstFormatDefinition
        // values rather than objects, and reading one as an object would read
        // the wrong half of the GValue. It is the one producer of that kind.
        using Iterator definitions = FormatExtensions.IterateDefinitions();

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => definitions.Items());

        Assert.Contains("which is not a GObject", error.Message, StringComparison.Ordinal);
    }
}
