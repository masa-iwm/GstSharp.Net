using System.Linq;
using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Reading a property description in full: the derived class of the
/// specification, the range and the default it carries, the table of an
/// enumeration, and the interfaces and signals of a type. Together they are
/// what a tool needs to print an element the way <c>gst-inspect-1.0</c> does.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class ParamSpecIntrospectionTests
{
    /// <summary>
    /// The specification of an integer property comes back as
    /// <see cref="ParamSpecInt"/>, which is what makes the range readable
    /// without a cast the caller has to justify.
    /// </summary>
    [Fact]
    public void AnIntegerPropertyIsDescribedByItsDerivedClass()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "described"));

        using ParamSpec? found = sink.FindProperty("num-buffers");
        ParamSpecInt spec = Assert.IsType<ParamSpecInt>(found);

        Assert.Equal("num-buffers", spec.Name);
        Assert.Equal(-1, spec.Minimum);
        Assert.Equal(int.MaxValue, spec.Maximum);
        Assert.Equal(-1, spec.Default);

        Assert.NotEmpty(spec.Nick);
        Assert.False(string.IsNullOrEmpty(spec.Blurb));

        // owner_type names the class that installed the property, which is
        // fakesink itself here and GstObject for the name below.
        Assert.Equal("GstFakeSink", spec.OwnerType.Name);
        Assert.Equal("GParamInt", spec.NativeType.Name);
        Assert.Equal(GType.Int, spec.ValueType);
        Assert.Equal(-1, spec.DefaultValue.GetInt());

        // Only an override stands for another specification.
        Assert.Null(spec.RedirectTarget);
    }

    /// <summary>
    /// A string property whose default is the null pointer answers
    /// <see langword="null"/> rather than an empty string, which is what the
    /// name of every <c>GstObject</c> has.
    /// </summary>
    [Fact]
    public void AStringPropertyWithoutADefaultAnswersNull()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "unnamed-default"));

        using ParamSpec? found = sink.FindProperty("name");
        ParamSpecString spec = Assert.IsType<ParamSpecString>(found);

        Assert.Null(spec.Default);
        Assert.Equal("GstObject", spec.OwnerType.Name);
    }

    /// <summary>
    /// An enumeration property carries the table of its type, and the members
    /// have a nickname of their own next to the name C declares them under.
    /// </summary>
    [Fact]
    public void AnEnumerationPropertyListsTheMembersOfItsType()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "enumerated"));

        using ParamSpec? found = sink.FindProperty("state-error");
        ParamSpecEnum spec = Assert.IsType<ParamSpecEnum>(found);

        EnumValue[] members = spec.Values;
        Assert.NotEmpty(members);
        Assert.Contains(members, member => member.Nick == "none");

        EnumValue none = members.Single(member => member.Nick == "none");
        Assert.Equal(spec.Default, none.Value);
        Assert.NotEqual(none.Nick, none.Name);
    }

    /// <summary>
    /// A flags type is read the same way, and its members are combinations of
    /// bits rather than one bit each.
    /// </summary>
    [Fact]
    public void AFlagsTypeListsItsMembers()
    {
        FlagsValue[] members = GType.FromName("GstSeekFlags").GetFlagsValues();

        Assert.NotEmpty(members);
        Assert.Contains(members, member => member.Nick == "flush");

        FlagsValue flush = members.Single(member => member.Nick == "flush");
        Assert.Equal((uint)SeekFlags.Flush, flush.Value);
        Assert.NotEqual(flush.Nick, flush.Name);
    }

    /// <summary>
    /// Asking a type that is neither an enumeration nor a set of flags for a
    /// table is a mistake about the type rather than about an argument.
    /// </summary>
    [Fact]
    public void ATypeThatIsNotAnEnumerationRefusesToListMembers()
    {
        Assert.Throws<InvalidOperationException>(() => GType.Int.GetEnumValues());
        Assert.Throws<InvalidOperationException>(() => GType.FromName("GstState").GetFlagsValues());
    }

    /// <summary>
    /// The public constructor still wraps whatever it is given in the base
    /// class, which is what keeps the code that was written against it working.
    /// </summary>
    [Fact]
    public void ThePublicConstructorWrapsInTheBaseClass()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "base-wrapped"));

        using ParamSpec? found = sink.FindProperty("num-buffers");
        Assert.NotNull(found);

        using ParamSpec bare = new(found.Handle, Transfer.None);
        Assert.Equal(typeof(ParamSpec), bare.GetType());
        Assert.Equal("num-buffers", bare.Name);
    }

    /// <summary>
    /// A property that cannot be read still has a default, which is the value
    /// a tool prints for it. The specification is built here because no core
    /// element declares a write only property.
    /// </summary>
    [Fact]
    public void AWriteOnlyPropertyStillHasADefault()
    {
        const uint writable = 2;

        using ParamSpec spec = ParamSpec.FromNative(
            ParamSpecNatives.Int("write-only", "Write only", "Written and never read", 0, 10, 4, writable),
            Transfer.None);

        Assert.Equal(ParamFlags.Writable, spec.Flags);
        Assert.Equal(4, Assert.IsType<ParamSpecInt>(spec).Default);
        Assert.Equal(4, spec.DefaultValue.GetInt());
        Assert.Equal("Write only", spec.Nick);
        Assert.Equal("Written and never read", spec.Blurb);
    }

    /// <summary>
    /// Listing every property of an element hands out the derived classes too,
    /// so a caller can pattern match over the whole list.
    /// </summary>
    [Fact]
    public void ListingPropertiesHandsOutTheDerivedClasses()
    {
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "listed"));

        ParamSpec[] specifications = sink.ListProperties();

        try
        {
            Assert.Contains(specifications, spec => spec is ParamSpecInt { Name: "num-buffers" });
            Assert.Contains(specifications, spec => spec is ParamSpecBoolean { Name: "sync" });
        }
        finally
        {
            foreach (ParamSpec spec in specifications)
            {
                spec.Dispose();
            }
        }
    }

    /// <summary>
    /// The interfaces of a type include the ones it inherited, and a type that
    /// has no instances has none at all.
    /// </summary>
    [Fact]
    public void ATypeListsTheInterfacesItImplements()
    {
        GType[] interfaces = GType.FromName("GstBin").GetInterfaces();

        Assert.Contains(interfaces, type => type.Name == "GstChildProxy");
        Assert.All(interfaces, type => Assert.True(type.IsInterface));

        Assert.Empty(GType.Int.GetInterfaces());
        Assert.False(GType.Int.IsInterface);
        Assert.True(GType.FromName("GstChildProxy").IsInterface);
    }

    /// <summary>
    /// A type lists its own signals and not those of its ancestors, which is
    /// why a caller that wants them all walks the parents itself.
    /// </summary>
    [Fact]
    public void ATypeListsItsOwnSignals()
    {
        // The type of an element exists once its plugin has been loaded, which
        // making one is the shortest way to arrange.
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "signalled"));

        SignalQuery[] bin = SignalQuery.List(GType.FromName("GstBin"));
        Assert.Contains(bin, signal => signal.Name == "element-added");
        Assert.DoesNotContain(bin, signal => signal.Name == "pad-added");

        SignalQuery[] element = SignalQuery.List(GType.FromName("GstElement"));
        Assert.Contains(element, signal => signal.Name == "pad-added");

        SignalQuery[] fakeSink = SignalQuery.List(GType.FromName("GstFakeSink"));
        Assert.Contains(fakeSink, signal => signal.Name == "handoff");

        // An invalid type has nothing to list and is not an error.
        Assert.Empty(SignalQuery.List(GType.Invalid));
    }
}
