using Gst;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Covers <see cref="Global.ValueSerialize"/>: the printer that turns a
/// <c>GValue</c> of a type only known at run time into the text a pipeline
/// description would carry.
/// </summary>
/// <remarks>
/// The interesting cases are the ones GObject alone cannot answer — a fraction
/// is a GStreamer type — and the ones where the text is not the obvious one: a
/// string that holds a space comes back quoted and escaped, which is checked by
/// reading it back rather than by asserting an escape form.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ValueSerializeTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public ValueSerializeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The plain types write themselves the way a structure carries them.
    /// </summary>
    [Fact]
    public void PlainValuesSerializeToTheirTextForm()
    {
        using Value number = Value.New(GType.Int);
        number.SetInt(42);
        Assert.Equal("42", Global.ValueSerialize(number));

        using Value text = Value.New(GType.String);
        text.SetString("hello");
        Assert.Equal("hello", Global.ValueSerialize(text));

        using Value flag = Value.New(GType.Boolean);
        flag.SetBoolean(true);
        Assert.Equal("true", Global.ValueSerialize(flag));
    }

    /// <summary>
    /// A fraction is a GStreamer type with no GObject transform to a string, so
    /// it exercises the serialisation table rather than the fallback. The value
    /// comes out of a structure, which is where a caller meets one.
    /// </summary>
    [Fact]
    public void GStreamerTypesSerializeThroughTheirOwnTable()
    {
        using Structure structure = Assert.IsAssignableFrom<Structure>(
            Structure.NewFromString("test, framerate=(fraction)30/1;"));

        using Value framerate = structure.GetValue("framerate");
        Assert.False(framerate.IsEmpty);
        Assert.Equal("30/1", Global.ValueSerialize(framerate));

        using Structure range = Assert.IsAssignableFrom<Structure>(
            Structure.NewFromString("test, width=(int)[ 1, 10 ];"));

        using Value width = range.GetValue("width");
        _output.WriteLine($"width={Global.ValueSerialize(width)}");
        Assert.Equal("[ 1, 10 ]", Global.ValueSerialize(width));
    }

    /// <summary>
    /// A string that holds a space is written quoted and escaped, which is
    /// checked by parsing the text back into a structure: the round trip is the
    /// property that matters, and it does not depend on which characters the
    /// installed version chooses to escape.
    /// </summary>
    [Fact]
    public void AStringWithASpaceSurvivesTheRoundTrip()
    {
        using Value text = Value.New(GType.String);
        text.SetString("foo bar");

        string? serialized = Global.ValueSerialize(text);
        Assert.NotNull(serialized);
        _output.WriteLine($"serialized={serialized}");

        using Structure structure = Assert.IsAssignableFrom<Structure>(
            Structure.NewFromString($"test, text={serialized};"));

        Assert.Equal("foo bar", structure.GetString("text"));
    }

    /// <summary>
    /// A type that nothing can write produces no text rather than an exception:
    /// a printer that meets an object property says so and moves on.
    /// </summary>
    [Fact]
    public void AnUnwritableTypeProducesNull()
    {
        using Value pointer = Value.New(GType.Pointer);
        pointer.SetPointer(nint.Zero);

        Assert.Null(Global.ValueSerialize(pointer));
    }

    /// <summary>
    /// An uninitialised value has no type to write it as. C answers that with
    /// an assertion failure on the console, which is not an answer an
    /// application can act on.
    /// </summary>
    [Fact]
    public void AnEmptyValueIsRejected()
    {
        Value empty = default;

        Assert.True(empty.IsEmpty);
        Assert.Throws<ArgumentException>(() => Global.ValueSerialize(empty));
    }
}
