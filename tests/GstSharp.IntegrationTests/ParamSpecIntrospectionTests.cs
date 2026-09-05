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

        // Neither has a type that could not carry a signal in the first place:
        // g_signal_list_ids refuses one that is neither instantiatable nor an
        // interface, so it is never asked.
        Assert.Empty(SignalQuery.List(GType.Int));

        // An enumeration is registered and still carries none, which only says
        // something once the type really is there to be asked about.
        GType format = GType.FromName("GstFormat");
        Assert.True(format.IsValid);
        Assert.Empty(SignalQuery.List(format));
    }

    /// <summary>
    /// An interface lists the signals it declares even though it has no class:
    /// its default vtable is built first, which is what runs the
    /// <c>base_init</c> the registration lives in.
    /// </summary>
    [Fact]
    public void AnInterfaceListsTheSignalsItDeclares()
    {
        SignalQuery[] childProxy = SignalQuery.List(GType.FromName("GstChildProxy"));

        Assert.Contains(childProxy, signal => signal.Name == "child-added");
        Assert.Contains(childProxy, signal => signal.Name == "child-removed");
    }

    /// <summary>
    /// The range and the default of an unsigned property of a real element are
    /// the ones the plugin installed, which is what <c>gst-inspect-1.0</c>
    /// prints for it.
    /// </summary>
    [Fact]
    public void AnUnsignedPropertyCarriesTheRangeThePluginInstalled()
    {
        using Element identity = Assert.IsAssignableFrom<Element>(ElementFactory.Make("identity", "described-uint"));

        using ParamSpec? found = identity.FindProperty("sleep-time");
        ParamSpecUInt spec = Assert.IsType<ParamSpecUInt>(found);

        // gstidentity.c: g_param_spec_uint (0, G_MAXUINT, DEFAULT_SLEEP_TIME).
        Assert.Equal(GType.UInt, spec.ValueType);
        Assert.Equal(0u, spec.Minimum);
        Assert.Equal(uint.MaxValue, spec.Maximum);
        Assert.Equal(0u, spec.Default);
        Assert.InRange(spec.Default, spec.Minimum, spec.Maximum);
        Assert.Equal(0u, spec.DefaultValue.GetUInt());
    }

    /// <summary>
    /// A property whose values are nanoseconds is a 64 bit unsigned one, and
    /// its default is a real quantity rather than zero.
    /// </summary>
    [Fact]
    public void ATimePropertyCarriesItsDefaultAsAQuantity()
    {
        using Element queue = Assert.IsAssignableFrom<Element>(ElementFactory.Make("queue", "described-uint64"));

        using ParamSpec? found = queue.FindProperty("max-size-time");
        ParamSpecUInt64 spec = Assert.IsType<ParamSpecUInt64>(found);

        // gstqueue.c: g_param_spec_uint64 (0, G_MAXUINT64, DEFAULT_MAX_SIZE_TIME),
        // and DEFAULT_MAX_SIZE_TIME is GST_SECOND.
        Assert.Equal(GType.UInt64, spec.ValueType);
        Assert.Equal(0uL, spec.Minimum);
        Assert.Equal(ulong.MaxValue, spec.Maximum);
        Assert.Equal(1_000_000_000uL, spec.Default);
        Assert.InRange(spec.Default, spec.Minimum, spec.Maximum);
        Assert.Equal(1_000_000_000uL, spec.DefaultValue.GetUInt64());
    }

    /// <summary>
    /// A signed property of a source element reads the same way, which is what
    /// makes the three kinds interchangeable to a caller that pattern matches.
    /// </summary>
    [Fact]
    public void ASignedPropertyOfASourceCarriesItsRange()
    {
        using Element source = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesrc", "described-int"));

        using ParamSpec? found = source.FindProperty("datarate");
        ParamSpecInt spec = Assert.IsType<ParamSpecInt>(found);

        // gstfakesrc.c: g_param_spec_int (0, G_MAXINT, DEFAULT_DATARATE).
        Assert.Equal(GType.Int, spec.ValueType);
        Assert.Equal(0, spec.Minimum);
        Assert.Equal(int.MaxValue, spec.Maximum);
        Assert.Equal(0, spec.Default);
        Assert.InRange(spec.Default, spec.Minimum, spec.Maximum);
    }

    /// <summary>
    /// A class the binding declares no derived class for is handed out as
    /// <see cref="ParamSpec"/> itself rather than refused.
    /// </summary>
    [Fact]
    public void AnUnknownClassIsWrappedInTheBaseClass()
    {
        // GParamOverride is a class the binding declares no wrapper for: it
        // stands for another specification rather than describing values of its
        // own. A pointer specification, which used to stand here, now has a
        // class of its own and is checked as that below.
        nint nativePointer = ParamSpecNatives.Pointer(
            "an-opaque-pointer",
            "Opaque",
            "A pointer nothing describes",
            ParamSpecNatives.ReadWrite);

        using ParamSpec pointer = ParamSpec.FromNative(nativePointer, Transfer.None);

        nint native = ParamSpecNatives.Override("an-unknown-class", pointer.Handle);
        using ParamSpec spec = ParamSpec.FromNative(native, Transfer.None);

        Assert.Equal(typeof(ParamSpec), spec.GetType());
        Assert.Equal("GParamOverride", spec.NativeType.Name);

        Assert.IsType<ParamSpecPointer>(pointer);
        Assert.Equal(GType.Pointer, pointer.ValueType);
    }

    /// <summary>
    /// Wrapping the null pointer is refused: unlike an object, a specification
    /// is never optional where one is wrapped.
    /// </summary>
    [Fact]
    public void WrappingNothingIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ParamSpec.FromNative(nint.Zero, Transfer.None));

    /// <summary>
    /// An override stands for the specification it was built over, which is the
    /// one case where <see cref="ParamSpec.RedirectTarget"/> answers.
    /// </summary>
    [Fact]
    public void AnOverrideRedirectsToWhatItStandsFor()
    {
        nint nativeOverridden = ParamSpecNatives.String(
            "overridden-name",
            "Overridden",
            "The specification the override stands for",
            "a default",
            ParamSpecNatives.ReadWrite);

        // Both constructors hand out a floating specification, and FromNative
        // sinks it: each wrapper owns exactly one reference. g_param_spec_override
        // takes a reference of its own on what it overrides, so disposing the
        // wrapper of the original does not invalidate the redirect target.
        using ParamSpec overridden = ParamSpec.FromNative(nativeOverridden, Transfer.None);

        nint nativeOverride = ParamSpecNatives.Override("overriding-name", overridden.Handle);
        using ParamSpec spec = ParamSpec.FromNative(nativeOverride, Transfer.None);

        Assert.Equal("GParamOverride", spec.NativeType.Name);
        Assert.Equal("overriding-name", spec.Name);

        // The redirect target is a wrapper of its own, holding its own
        // reference, so it is disposed here.
        using ParamSpec? target = spec.RedirectTarget;
        ParamSpecString redirected = Assert.IsType<ParamSpecString>(target);

        Assert.Equal(overridden.Handle, redirected.Handle);
        Assert.Equal("overridden-name", redirected.Name);
        Assert.Equal("a default", redirected.Default);

        // The value type of an override is the one of what it stands for, and
        // its nickname falls back to that of the target.
        Assert.Equal(GType.String, spec.ValueType);
        Assert.Equal("Overridden", spec.Nick);
        Assert.Null(overridden.RedirectTarget);
    }
}
