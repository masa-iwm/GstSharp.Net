using Gst;
using Gst.GObject;
using Xunit;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GValue</c> bridge of <see cref="Structure"/>: reading a field as a
/// value, writing one, and reading a boxed field back as the wrapper of the
/// binding.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here needs a plugin, an element or a pipeline, which is deliberate:
/// this is the piece the WebRTC scenario is built on, and that scenario only
/// runs where <c>webrtcbin</c> is installed. The bridge itself is measured on
/// every machine the suite runs on.
/// </para>
/// <para>
/// The three claims are the ones the surface makes. A value read out of a
/// structure is a copy, so it outlives the structure; a value written into one
/// is copied as well, so the caller keeps what it passed; and a boxed field
/// comes back as a wrapper built through the type registry, which is the route
/// that needs no <c>FromNative</c> of another assembly.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class StructureValueTests
{
    /// <summary>
    /// The round trip an application performs: write a field as a value, read
    /// it back, and see the typed getter of the structure agree.
    /// </summary>
    [Fact]
    public void AFieldWrittenAsAValueIsReadBackByBothAccessors()
    {
        using Structure structure = Structure.NewEmpty("application/x-test");

        using (Value number = Value.New(GType.Int))
        {
            number.SetInt(96);
            structure.SetValue("payload", number);

            // The structure took a copy, so the value the caller passed is
            // still its own and is still readable.
            Assert.Equal(96, number.GetInt());
        }

        using (Value text = Value.New(GType.String))
        {
            text.SetString("opus");
            structure.SetValue("encoding-name", text);
        }

        Assert.True(structure.HasField("payload"));
        Assert.True(structure.GetInt("payload", out int payload));
        Assert.Equal(96, payload);
        Assert.Equal("opus", structure.GetString("encoding-name"));

        using Value read = structure.GetValue("payload");
        Assert.False(read.IsEmpty);
        Assert.Equal(GType.Int, read.Type);
        Assert.Equal(96, read.GetInt());
    }

    /// <summary>
    /// The value a read produces is a copy: it stays readable after the
    /// structure it came from is gone.
    /// </summary>
    /// <remarks>
    /// <c>gst_structure_get_value</c> hands out the value the structure owns.
    /// A binding that wrapped that pointer instead of copying it would read
    /// freed memory here, or would unset a value the structure still uses.
    /// </remarks>
    [Fact]
    public void AReadValueOutlivesTheStructureItCameFrom()
    {
        Value read;

        using (Structure structure = Structure.NewEmpty("application/x-test"))
        {
            using Value text = Value.New(GType.String);
            text.SetString("kept");
            structure.SetValue("name", text);

            read = structure.GetValue("name");
        }

        using (read)
        {
            Assert.Equal("kept", read.GetString());
        }
    }

    /// <summary>
    /// A field the structure does not have reads as an empty value rather than
    /// as a failure, the way the generated getters of a structure answer.
    /// </summary>
    [Fact]
    public void AMissingFieldReadsAsAnEmptyValue()
    {
        using Structure structure = Structure.NewEmpty("application/x-test");

        using Value missing = structure.GetValue("nothing-of-the-sort");

        Assert.True(missing.IsEmpty);
        Assert.Equal(GType.Invalid, missing.Type);

        // GetBoxed says the same thing in its own currency.
        Assert.Null(structure.GetBoxed<Structure>("nothing-of-the-sort"));
    }

    /// <summary>
    /// Writing a field that is already there replaces it, including with a
    /// value of another type.
    /// </summary>
    [Fact]
    public void WritingAFieldTwiceReplacesIt()
    {
        using Structure structure = Structure.NewEmpty("application/x-test");

        using (Value first = Value.New(GType.Int))
        {
            first.SetInt(1);
            structure.SetValue("field", first);
        }

        Assert.Equal(1, structure.NFields());

        using (Value second = Value.New(GType.String))
        {
            second.SetString("two");
            structure.SetValue("field", second);
        }

        Assert.Equal(1, structure.NFields());
        Assert.Equal(GType.String, structure.GetFieldType("field"));
        Assert.Equal("two", structure.GetString("field"));
    }

    /// <summary>
    /// The half the WebRTC scenario turns on: a boxed field read back as the
    /// wrapper of its type, built through the type registry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A structure inside a structure is the boxed field that needs no plugin.
    /// It travels the same route a <c>GstWebRTCSessionDescription</c> travels
    /// out of a promise reply: the <c>GType</c> of the value says what the
    /// pointer is, the registry hands back the factory of the wrapper, and the
    /// wrapper adopts a copy of its own.
    /// </para>
    /// <para>
    /// That the copy is independent is what the last assertion says. The inner
    /// structure that was written is modified afterwards and the one that was
    /// read does not change with it.
    /// </para>
    /// </remarks>
    [Fact]
    public void ABoxedFieldIsReadBackAsItsWrapper()
    {
        using Structure structure = Structure.NewEmpty("application/x-test");
        using Structure inner = Structure.NewEmpty("application/x-inner");

        using (Value number = Value.New(GType.Int))
        {
            number.SetInt(7);
            inner.SetValue("count", number);
        }

        using (Value boxed = Value.New(inner.BoxedType))
        {
            // g_value_set_boxed copies, so the structure of the test stays the
            // structure of the test.
            boxed.SetBoxed(inner.Handle);
            structure.SetValue("inner", boxed);
        }

        using Structure? read = structure.GetBoxed<Structure>("inner");

        Assert.NotNull(read);
        Assert.True(read.HasName("application/x-inner"));
        Assert.True(read.GetInt("count", out int count));
        Assert.Equal(7, count);

        // Two copies apart from each other.
        using (Value replacement = Value.New(GType.Int))
        {
            replacement.SetInt(99);
            inner.SetValue("count", replacement);
        }

        Assert.True(read.GetInt("count", out int unchanged));
        Assert.Equal(7, unchanged);
    }

    /// <summary>
    /// A field that holds no boxed value is a cast failure rather than a GLib
    /// warning on the console.
    /// </summary>
    /// <remarks>
    /// <c>g_value_get_boxed</c> on a value of another fundamental type is an
    /// assertion failure inside GLib, which prints and returns null. The
    /// question is therefore asked before the call.
    /// </remarks>
    [Fact]
    public void ANonBoxedFieldRefusesToBeReadAsAWrapper()
    {
        using Structure structure = Structure.NewEmpty("application/x-test");

        using (Value number = Value.New(GType.Int))
        {
            number.SetInt(1);
            structure.SetValue("payload", number);
        }

        InvalidCastException failure =
            Assert.Throws<InvalidCastException>(() => structure.GetBoxed<Structure>("payload"));

        Assert.Contains("gint", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty value has no type, so it cannot be stored: the field would have
    /// nothing to be.
    /// </summary>
    [Fact]
    public void AnEmptyValueCannotBeStored()
    {
        using Structure structure = Structure.NewEmpty("application/x-test");

        Value empty = default;

        Assert.Throws<ArgumentException>(() => structure.SetValue("field", empty));
        Assert.Equal(0, structure.NFields());
    }
}
