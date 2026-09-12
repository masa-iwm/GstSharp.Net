using Gst;
using Gst.GObject;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <see cref="Message.NewPropertyNotify"/> against the library that is
/// installed: the C adopts the <c>GValue</c> it is handed, and this is where
/// the copy the binding hands it instead shows.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class MessageNewPropertyNotifyTests
{
    /// <summary>
    /// The name and the value survive the round trip, and the value the caller
    /// passed is still its own afterwards: it reads the same number, and
    /// disposing it is still the caller's job.
    /// </summary>
    [Fact]
    public void AValueRoundTripsAndStaysTheCallersOwn()
    {
        using Element source = ElementFactory.Make("fakesrc", "notify-source")
            ?? throw new InvalidOperationException("fakesrc is part of gst-plugins-core.");

        using Value value = Value.New(GType.Int);
        value.SetInt(1234);

        using (Message message = Message.NewPropertyNotify(source, "num-buffers", value))
        {
            (Gst.Object? notified, string name, Value parsed) = message.ParsePropertyNotify();
            using (parsed)
            {
                Assert.Same(source, notified);
                Assert.Equal("num-buffers", name);
                Assert.Equal(GType.Int, parsed.Type);
                Assert.Equal(1234, parsed.GetInt());
            }
        }

        // The copy is what the message consumed, so the value of the caller is
        // still initialised and still holds what it held.
        Assert.Equal(GType.Int, value.Type);
        Assert.Equal(1234, value.GetInt());
    }

    /// <summary>
    /// The copy the constructor hands the library is a copy of the contents as
    /// well: a value that holds an object leaves the message owning a
    /// reference of its own, which it releases when it is disposed, while the
    /// caller keeps the reference its own value took.
    /// </summary>
    /// <remarks>
    /// This is what an <c>int</c> cannot measure. A shallow hand-over of the
    /// caller's storage would read the same number back and would show here as
    /// a reference count that does not rise while the message lives and falls
    /// below the baseline when it is disposed.
    /// </remarks>
    [Fact]
    public unsafe void TheValueTheMessageCarriesHoldsAReferenceOfItsOwn()
    {
        using Element source = ElementFactory.Make("fakesrc", "notify-source-object-value")
            ?? throw new InvalidOperationException("fakesrc is part of gst-plugins-core.");

        // A second element, so that the reference gst_message_new_custom takes
        // on the source of the message is not part of what is measured.
        using Element carried = ElementFactory.Make("fakesink", "notify-carried-object")
            ?? throw new InvalidOperationException("fakesink is part of gst-plugins-core.");

        uint baseline = RefCountOf(carried.Handle);

        using Value value = Value.New(GType.Object);
        value.SetObject(carried);

        uint held = RefCountOf(carried.Handle);
        Assert.Equal(baseline + 1, held);

        Message message = Message.NewPropertyNotify(source, "name", value);
        try
        {
            Assert.Equal(held + 1, RefCountOf(carried.Handle));
        }
        finally
        {
            message.Dispose();
        }

        // The message released its own reference and nothing else: what is
        // left is the one the caller's value holds.
        Assert.Equal(held, RefCountOf(carried.Handle));
        Assert.Same(carried, value.GetObject());
    }

    /// <summary>
    /// A notification without a value parses back as the empty value, which is
    /// what the C reports when the structure carries no property-value field.
    /// </summary>
    [Fact]
    public void ANotificationWithoutAValueParsesBackAsTheEmptyValue()
    {
        using Element source = ElementFactory.Make("fakesrc", "notify-source-without-value")
            ?? throw new InvalidOperationException("fakesrc is part of gst-plugins-core.");

        using Message message = Message.NewPropertyNotify(source, "num-buffers", value: null);

        (Gst.Object? notified, string name, Value parsed) = message.ParsePropertyNotify();
        using (parsed)
        {
            Assert.Same(source, notified);
            Assert.Equal("num-buffers", name);
            Assert.Equal(GType.Invalid, parsed.Type);
        }
    }

    /// <summary>
    /// The empty value has no type to copy, and the member says so before it
    /// reaches the library rather than letting <c>g_value_init</c> raise a
    /// critical.
    /// </summary>
    [Fact]
    public void TheEmptyValueIsRefused()
    {
        using Element source = ElementFactory.Make("fakesrc", "notify-source-empty-value")
            ?? throw new InvalidOperationException("fakesrc is part of gst-plugins-core.");

        Value empty = default;

        Assert.Throws<ArgumentException>(
            () => Message.NewPropertyNotify(source, "num-buffers", empty));
    }

    /// <summary>
    /// Reads <c>ref_count</c> out of a <c>GObject</c>, which follows the
    /// pointer to its class.
    /// </summary>
    /// <param name="handle">The object to read.</param>
    /// <returns>The reference count at that moment.</returns>
    private static unsafe uint RefCountOf(nint handle) => *(uint*)((byte*)handle + sizeof(nint));
}
