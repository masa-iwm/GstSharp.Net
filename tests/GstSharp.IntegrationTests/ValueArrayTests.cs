using Gst;
using Gst.GObject;
using Xunit;
using Value = Gst.GObject.Value;

// Gst.ValueArray is the holder of the GST_TYPE_ARRAY functions, a different
// type of the same name in a namespace this file also imports.
using ValueArray = Gst.GObject.ValueArray;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The six <c>GValueArray</c> entry points against the installed library:
/// the structure array and list round trips, the refusal path of the getters,
/// and the object property pair.
/// </summary>
/// <remarks>
/// <para>
/// Every member called here is available since GStreamer 1.12, so the tests
/// run on the 1.24 floor of the Linux leg.
/// </para>
/// <para>
/// The ownership claims of <c>docs/ownership.md</c> are what is exercised: a
/// getter that answers <see langword="true"/> hands the caller a newly
/// allocated array to dispose and one that declines hands out
/// <see langword="null"/>, while a setter copies the contents and leaves the
/// caller's array intact and still the caller's to dispose.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ValueArrayTests
{
    /// <summary>
    /// An array field round trips: two int values go in through
    /// <see cref="Structure.SetArray"/>, the caller keeps its array, and
    /// <see cref="Structure.GetArray"/> hands back a fresh array with copies
    /// of the elements.
    /// </summary>
    [Fact]
    public void AnArrayFieldRoundTripsThroughSetArrayAndGetArray()
    {
        using ValueArray array = ValueArray.New(2);
        using (Value one = Value.CreateFor(1, GType.Int))
        {
            array.Append(one);
        }

        using (Value two = Value.CreateFor(2, GType.Int))
        {
            array.Append(two);
        }

        Assert.Equal(2u, array.Count);

        using Structure structure = Structure.NewEmpty("mark");
        structure.SetArray("values", array);

        // The callee copied what it kept: the caller's array is intact.
        Assert.Equal(2u, array.Count);

        Assert.True(structure.GetArray("values", out ValueArray? read));
        Assert.NotNull(read);
        using (read)
        {
            Assert.Equal(2u, read.Count);

            using Value first = read.Get(0);
            Assert.Equal(GType.Int, first.Type);
            Assert.Equal(1, first.GetInt());

            using Value second = read.Get(1);
            Assert.Equal(2, second.GetInt());
        }
    }

    /// <summary>
    /// A list field round trips the same way through
    /// <see cref="Structure.SetList"/> and <see cref="Structure.GetList"/>,
    /// which speak <c>GST_TYPE_LIST</c> instead of <c>GST_TYPE_ARRAY</c>.
    /// </summary>
    [Fact]
    public void AListFieldRoundTripsThroughSetListAndGetList()
    {
        using ValueArray list = ValueArray.New(1);
        using (Value text = Value.CreateFor("first", GType.String))
        {
            list.Append(text);
        }

        using Structure structure = Structure.NewEmpty("mark");
        structure.SetList("values", list);

        Assert.True(structure.GetList("values", out ValueArray? read));
        Assert.NotNull(read);
        using (read)
        {
            Assert.Equal(1u, read.Count);

            using Value element = read.Get(0);
            Assert.Equal("first", element.GetString());
        }
    }

    /// <summary>
    /// The refusal path: a field that does not exist, and a field that holds
    /// the other of the two container types, both answer
    /// <see langword="false"/> and leave the out parameter
    /// <see langword="null"/> — the C function writes nothing on that path.
    /// </summary>
    [Fact]
    public void AGetterDeclinesWithANullArray()
    {
        using Structure structure = Structure.NewEmpty("mark");

        Assert.False(structure.GetArray("missing", out ValueArray? missing));
        Assert.Null(missing);

        using ValueArray list = ValueArray.New(1);
        using (Value number = Value.CreateFor(3, GType.Int))
        {
            list.Append(number);
        }

        structure.SetList("values", list);

        // The field holds a GST_TYPE_LIST, which GetArray does not convert.
        Assert.False(structure.GetArray("values", out ValueArray? mistyped));
        Assert.Null(mistyped);
    }

    /// <summary>
    /// The object property pair: <c>mix-matrix</c> of <c>audioconvert</c> is
    /// a <c>GST_TYPE_ARRAY</c> property — the kind the pair exists for — and
    /// its default, the empty matrix, round trips as an empty array.
    /// </summary>
    [Fact]
    public void AnObjectArrayPropertyRoundTripsThroughTheUtilPair()
    {
        using Element convert =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("audioconvert", "value-array-convert"));

        using ValueArray matrix = ValueArray.New(0);
        Assert.True(Global.UtilSetObjectArray(convert, "mix-matrix", matrix));

        Assert.True(Global.UtilGetObjectArray(convert, "mix-matrix", out ValueArray? read));
        Assert.NotNull(read);
        using (read)
        {
            Assert.Equal(0u, read.Count);
        }
    }

    /// <summary>
    /// A non-empty matrix: the row is itself a <c>GST_TYPE_ARRAY</c> value,
    /// which the binding reaches by parsing a structure — the same road a
    /// caps field takes — and the round trip hands the one row back.
    /// </summary>
    [Fact]
    public void ANestedRowSurvivesTheObjectArrayRoundTrip()
    {
        using Element convert =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("audioconvert", "value-array-rows"));

        using Structure? parsed = Structure.NewFromString("m, row=<(float)1.0>");
        Assert.NotNull(parsed);

        using ValueArray matrix = ValueArray.New(1);
        using (Value row = parsed.GetValue("row"))
        {
            Assert.False(row.IsEmpty);
            matrix.Append(row);
        }

        Assert.True(Global.UtilSetObjectArray(convert, "mix-matrix", matrix));

        Assert.True(Global.UtilGetObjectArray(convert, "mix-matrix", out ValueArray? read));
        Assert.NotNull(read);
        using (read)
        {
            Assert.Equal(1u, read.Count);
        }
    }

    /// <summary>
    /// The guards of the wrapper itself: an element read past the end throws
    /// instead of copying from the warning path, and appending the empty
    /// value is refused the way every read-only <c>in</c> value is.
    /// </summary>
    [Fact]
    public void TheWrapperGuardsItsOwnEdges()
    {
        using ValueArray array = ValueArray.New(0);

        Assert.Equal(0u, array.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => array.Get(0));

        Value empty = default;
        ArgumentException failure = Assert.Throws<ArgumentException>(() => array.Append(empty));
        Assert.Equal("value", failure.ParamName);
    }
}
