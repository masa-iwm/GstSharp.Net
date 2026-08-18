using Gst;
using Gst.GObject;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Enumerating the properties of a class and comparing two values, which is
/// what a tool needs before it can print an object as a pipeline description.
/// </summary>
/// <remarks>
/// <see cref="Gst.GObject.Object.ListProperties"/> asks
/// <c>g_object_class_list_properties</c> and
/// <see cref="Global.ValueCompare"/> asks <c>gst_value_compare</c>; together
/// they are what <c>samples/GstDeviceMonitor</c> builds its
/// <c>gst-launch-1.0</c> snippet from.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class PropertyIntrospectionTests
{
    /// <summary>
    /// The properties of an element are listed, and each one says its name, the
    /// type of its values and what may be done with it.
    /// </summary>
    [Fact]
    public void AnElementListsItsProperties()
    {
        using Element filter = Assert.IsAssignableFrom<Element>(ElementFactory.Make("capsfilter", "listed"));

        ParamSpec[] properties = filter.ListProperties();

        try
        {
            Assert.NotEmpty(properties);

            // Every entry is a usable specification rather than a null slot.
            Assert.All(properties, property =>
            {
                Assert.NotEmpty(property.Name);
                Assert.True(property.ValueType.IsValid);
            });

            // "name" comes from GstObject and "caps" from capsfilter itself, so
            // the list covers the whole ancestry and not just the leaf class.
            ParamSpec name = Assert.Single(properties, property => property.Name == "name");
            Assert.Equal(GType.String, name.ValueType);
            Assert.Equal(ParamFlags.Readable, name.Flags & ParamFlags.Readable);
            Assert.Equal(ParamFlags.Writable, name.Flags & ParamFlags.Writable);

            ParamSpec caps = Assert.Single(properties, property => property.Name == "caps");
            Assert.Equal("GstCaps", caps.ValueType.Name);

            // What the list says and what a lookup by name says agree.
            using Value value = filter.GetProperty(caps.Name);
            Assert.Equal(caps.ValueType, value.Type);
        }
        finally
        {
            foreach (ParamSpec property in properties)
            {
                property.Dispose();
            }
        }
    }

    /// <summary>
    /// The flags tell a property that only the constructor may write from one
    /// that can be assigned at any time, which is the distinction a launch line
    /// depends on.
    /// </summary>
    /// <remarks>
    /// A pad is the GObject that has one of each: <c>direction</c> is construct
    /// only, <c>offset</c> is not, and <c>caps</c> is readable and nothing
    /// else.
    /// </remarks>
    [Fact]
    public void TheFlagsDistinguishConstructOnlyProperties()
    {
        using Element filter = Assert.IsAssignableFrom<Element>(ElementFactory.Make("capsfilter", "flagged"));

        // The pad is an interned GObject wrapper and is not disposed here.
        Pad pad = Assert.IsAssignableFrom<Pad>(filter.GetStaticPad("src"));

        ParamSpec[] properties = pad.ListProperties();

        try
        {
            ParamSpec direction = Assert.Single(properties, property => property.Name == "direction");
            Assert.Equal(ParamFlags.ConstructOnly, direction.Flags & ParamFlags.ConstructOnly);

            ParamSpec offset = Assert.Single(properties, property => property.Name == "offset");
            Assert.Equal(ParamFlags.None, offset.Flags & ParamFlags.ConstructOnly);

            ParamSpec caps = Assert.Single(properties, property => property.Name == "caps");
            Assert.Equal(ParamFlags.Readable, caps.Flags & ParamFlags.Readable);
            Assert.Equal(ParamFlags.None, caps.Flags & ParamFlags.Writable);
        }
        finally
        {
            foreach (ParamSpec property in properties)
            {
                property.Dispose();
            }
        }
    }

    /// <summary>
    /// A specification outlives the array it came out of, because the wrapper
    /// holds a reference of its own and only the array is freed.
    /// </summary>
    [Fact]
    public void ASpecificationOutlivesTheListing()
    {
        ParamSpec kept;

        using (Element filter = Assert.IsAssignableFrom<Element>(ElementFactory.Make("capsfilter", "outliving")))
        {
            ParamSpec[] properties = filter.ListProperties();
            kept = Assert.Single(properties, property => property.Name == "caps");

            foreach (ParamSpec property in properties)
            {
                if (!ReferenceEquals(property, kept))
                {
                    property.Dispose();
                }
            }
        }

        using (kept)
        {
            Assert.Equal("caps", kept.Name);
            Assert.Equal("GstCaps", kept.ValueType.Name);
        }
    }

    /// <summary>
    /// Two values of the same type compare as an ordering.
    /// </summary>
    [Fact]
    public void ValuesOfTheSameTypeAreOrdered()
    {
        using Value smaller = Value.New(GType.Int);
        smaller.SetInt(3);

        using Value larger = Value.New(GType.Int);
        larger.SetInt(7);

        using Value same = Value.New(GType.Int);
        same.SetInt(3);

        Assert.Equal(ValueOrder.LessThan, Global.ValueCompare(smaller, larger));
        Assert.Equal(ValueOrder.GreaterThan, Global.ValueCompare(larger, smaller));
        Assert.Equal(ValueOrder.Equal, Global.ValueCompare(smaller, same));
    }

    /// <summary>
    /// Two values that cannot be ordered say so, rather than being squeezed
    /// into one of the three answers an ordering has.
    /// </summary>
    /// <remarks>
    /// Two integer ranges that overlap without containing one another are the
    /// case GStreamer added <c>GST_VALUE_UNORDERED</c> for; two values of
    /// different types are the other one.
    /// </remarks>
    [Fact]
    public void OverlappingRangesAndMixedTypesAreUnordered()
    {
        using Structure ranges =
            Assert.IsType<Structure>(Structure.FromString("t, first=[1,10], second=[5,20]", out string? _));

        using Value first = ranges.GetValue("first");
        using Value second = ranges.GetValue("second");

        Assert.Equal("GstIntRange", first.Type.Name);
        Assert.Equal(ValueOrder.Unordered, Global.ValueCompare(first, second));

        // The same range compares equal to itself, so the answer above is about
        // the overlap and not about the type.
        using Value again = ranges.GetValue("first");
        Assert.Equal(ValueOrder.Equal, Global.ValueCompare(first, again));

        using Value number = Value.New(GType.Int);
        number.SetInt(5);
        Assert.Equal(ValueOrder.Unordered, Global.ValueCompare(first, number));
    }

    /// <summary>
    /// An empty value has no type to compare as, which is an argument error
    /// here rather than an assertion failure on the console.
    /// </summary>
    [Fact]
    public void AnEmptyValueCannotBeCompared()
    {
        using Value number = Value.New(GType.Int);
        number.SetInt(1);

        Value empty = default;

        Assert.Throws<ArgumentException>(() => Global.ValueCompare(empty, number));
        Assert.Throws<ArgumentException>(() => Global.ValueCompare(number, empty));
    }

    /// <summary>
    /// The pair the launch line of <c>gst-device-monitor-1.0</c> rests on: a
    /// property that was written differs from the same property of a bare
    /// element of the same factory, and one that was not does not.
    /// </summary>
    [Fact]
    public void AWrittenPropertyDiffersFromTheFactoryDefault()
    {
        using Element written = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesrc", "written"));
        using Element bare = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesrc", "bare"));

        using (Value assigned = Value.New(GType.Int))
        {
            assigned.SetInt(7);
            written.SetProperty("num-buffers", assigned);
        }

        using Value changed = written.GetProperty("num-buffers");
        using Value untouched = bare.GetProperty("num-buffers");
        Assert.NotEqual(ValueOrder.Equal, Global.ValueCompare(changed, untouched));

        using Value sameOnBoth = written.GetProperty("sizetype");
        using Value bareSizeType = bare.GetProperty("sizetype");
        Assert.Equal(ValueOrder.Equal, Global.ValueCompare(sameOnBoth, bareSizeType));
    }

    /// <summary>
    /// The value-taking writer refuses every misuse the C call would answer
    /// with a console warning and no write: an unknown name, a read only or
    /// construct only property, an empty value, and a pair of types GObject
    /// cannot transform.
    /// </summary>
    /// <remarks>
    /// <c>g_object_set_property</c> is void and has no error channel, so before
    /// these guards each of the calls below succeeded silently and changed
    /// nothing. The transformable write at the end pins the other half of the
    /// contract: what GObject can convert still goes through, exactly as it
    /// always has, so the guard sits on the boundary the C call decides the
    /// write on and not inside it.
    /// </remarks>
    [Fact]
    public void TheValueWriterRefusesWhatNativeWouldSilentlyIgnore()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "guarded"));

        using (Value number = Value.New(GType.Int))
        {
            number.SetInt(1);
            Assert.Throws<ArgumentException>(() => sink.SetProperty("no-such-property", number));
        }

        // A read only property, with a value of exactly the right type: the
        // refusal is about the flags, not the payload.
        using (Value stats = sink.GetProperty("stats"))
        {
            Assert.Throws<ArgumentException>(() => sink.SetProperty("stats", stats));
        }

        // A construct only property, likewise read back from the object itself.
        Pad pad = Assert.IsAssignableFrom<Pad>(sink.GetStaticPad("sink"));
        using (Value direction = pad.GetProperty("direction"))
        {
            Assert.Throws<ArgumentException>(() => pad.SetProperty("direction", direction));
        }

        Value empty = default;
        Assert.Throws<ArgumentException>(() => sink.SetProperty("sync", empty));

        // A string cannot become a boolean, and the message names both sides.
        using (Value text = Value.New(GType.String))
        {
            text.SetString("yes");
            ArgumentException failure = Assert.Throws<ArgumentException>(
                () => sink.SetProperty("sync", text));
            Assert.Contains("gchararray", failure.Message, StringComparison.Ordinal);
            Assert.Contains("gboolean", failure.Message, StringComparison.Ordinal);
        }

        // And the boundary from the other side: an int is transformable into a
        // gint64 property, so the write that always worked still works.
        using (Value offset = Value.New(GType.Int))
        {
            offset.SetInt(25);
            sink.SetProperty("ts-offset", offset);
        }

        Assert.Equal(25L, sink.GetProperty<long>("ts-offset"));
    }
}
