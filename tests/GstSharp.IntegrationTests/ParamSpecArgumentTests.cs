using Gst;
using Gst.GObject;
using Xunit;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The members that take or hand out a <c>GParamSpec</c>, against the library
/// that is installed.
/// </summary>
/// <remarks>
/// <para>
/// A parameter specification is neither a <c>GObject</c> nor a boxed record, so
/// it is the one wrapper of the runtime a member constructs rather than takes
/// from a factory. What these tests pin is the ownership doctrine around it:
/// the wrapper of a borrowed specification takes a reference of its own, the
/// wrapper of a transferred one adopts it, and a lookup that finds nothing
/// hands back <see langword="null"/> rather than a wrapper of the null pointer.
/// </para>
/// <para>
/// <c>gst_child_proxy_lookup</c> leaves both of its out parameters untouched
/// when it answers <c>FALSE</c>, so the generated member zeroes the native
/// slots before the call; the miss below is what says that contract holds
/// rather than reading whatever the stack happened to carry.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ParamSpecArgumentTests
{
    /// <summary>
    /// A child proxy lookup that succeeds names the child and the
    /// specification of its property.
    /// </summary>
    [Fact]
    public void AChildProxyLookupFindsTheChildAndItsSpecification()
    {
        using Bin bin = Bin.New("lookup-bin");
        using Element source =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesrc", "looked-up"));

        Assert.True(bin.Add(source));

        Assert.True(bin.Lookup("looked-up::num-buffers", out Gst.GObject.Object? target, out ParamSpec? pspec));

        // The child is handed over with a reference of its own, and its wrapper
        // is the interned one, so it is the same instance the factory answered
        // and is not disposed here.
        Assert.NotNull(target);
        Assert.Same(source, target);

        Assert.NotNull(pspec);

        // The specification belongs to the class of the child and is only
        // borrowed, so the wrapper holds a reference of its own: disposing it
        // is correct and leaves the class untouched.
        using (pspec)
        {
            Assert.Equal("num-buffers", pspec.Name);
            Assert.Equal(GType.Int, pspec.ValueType);
            Assert.Equal(ParamFlags.Readable, pspec.Flags & ParamFlags.Readable);
        }

        // The class still describes the property after the wrapper released
        // its reference.
        Assert.True(bin.Lookup("looked-up::num-buffers", out _, out ParamSpec? again));
        using (again)
        {
            Assert.NotNull(again);
            Assert.Equal("num-buffers", again.Name);
        }
    }

    /// <summary>
    /// A lookup that finds nothing answers <see langword="false"/> and leaves
    /// both out parameters null.
    /// </summary>
    [Fact]
    public void AChildProxyLookupThatMissesAnswersNullForBoth()
    {
        using Bin bin = Bin.New("missing-bin");

        Assert.False(bin.Lookup("no-such-property", out Gst.GObject.Object? target, out ParamSpec? pspec));
        Assert.Null(target);
        Assert.Null(pspec);

        // A child that does not exist takes the other branch of the same
        // function and has to read the same way.
        Assert.False(bin.Lookup("no-such-child::no-such-property", out target, out pspec));
        Assert.Null(target);
        Assert.Null(pspec);
    }

    /// <summary>
    /// A property of the proxy itself is found without a child path, and the
    /// child the lookup names is then the proxy.
    /// </summary>
    [Fact]
    public void AChildProxyLookupWithoutAPathNamesTheProxyItself()
    {
        using Bin bin = Bin.New("self-bin");

        Assert.True(bin.Lookup("async-handling", out Gst.GObject.Object? target, out ParamSpec? pspec));
        Assert.Same(bin, target);

        using (pspec)
        {
            Assert.NotNull(pspec);
            Assert.Equal("async-handling", pspec.Name);
            Assert.Equal(GType.Boolean, pspec.ValueType);
        }
    }

    /// <summary>
    /// Deserializing takes the type from the destination the caller
    /// initialized, which is why the destination is passed by reference rather
    /// than as an out parameter.
    /// </summary>
    [Fact]
    public void ADeserializationWithoutASpecificationReadsTheTypeOfTheDestination()
    {
        // A value that is passed by reference cannot be a `using` variable, so
        // the release is written out; this is the ref shape the overlay
        // correction produces, and it is the pre-initialized type that decides
        // which parser runs.
        Value number = Value.New(GType.Int);

        try
        {
            Assert.True(Global.ValueDeserializeWithPspec(ref number, "42", null));
            Assert.Equal(GType.Int, number.Type);
            Assert.Equal(42, number.GetInt());
        }
        finally
        {
            number.Dispose();
        }
    }

    /// <summary>
    /// The same call with the specification of the property whose value is
    /// being parsed, which is what guides the members of a nested value.
    /// </summary>
    [Fact]
    public void ADeserializationAcceptsTheSpecificationOfTheProperty()
    {
        using Bin bin = Bin.New("deserialize-bin");
        using Element source =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesrc", "parsed"));

        Assert.True(bin.Add(source));
        Assert.True(bin.Lookup("parsed::num-buffers", out _, out ParamSpec? pspec));

        using (pspec)
        {
            Assert.NotNull(pspec);

            Value number = Value.New(pspec.ValueType);

            try
            {
                Assert.True(Global.ValueDeserializeWithPspec(ref number, "17", pspec));
                Assert.Equal(17, number.GetInt());
            }
            finally
            {
                number.Dispose();
            }
        }
    }

    /// <summary>
    /// The default deep notify handler is a call rather than a signal
    /// connection here, and a property it is told to exclude is the path that
    /// prints nothing at all.
    /// </summary>
    [Fact]
    public void TheDefaultDeepNotifyExcludesThePropertiesItIsGiven()
    {
        using Bin bin = Bin.New("notify-bin");
        using Element source =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesrc", "notified"));

        Assert.True(bin.Add(source));
        Assert.True(bin.Lookup("notified::num-buffers", out Gst.GObject.Object? target, out ParamSpec? pspec));

        using (pspec)
        {
            Assert.NotNull(pspec);
            Assert.NotNull(target);

            // Excluded, so the handler returns before it reads the property or
            // writes a line; every argument is borrowed and nothing is released
            // by the call.
            Gst.Object.DefaultDeepNotify(bin, source, pspec, [pspec.Name]);
        }
    }
}
