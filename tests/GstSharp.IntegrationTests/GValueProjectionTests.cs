using Gst;
using Gst.Controller;
using Gst.GObject;
using Xunit;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The generated <c>GValue</c> projection against the installed library: the
/// <c>in</c>, <c>ref</c> and <c>out</c> parameter shapes, the copied and the
/// adopted return, and the empty value guard.
/// </summary>
/// <remarks>
/// <para>
/// Every member called here is available since GStreamer 1.0, so the tests run
/// on the 1.24 floor of the Linux leg; the 1.26 <c>id_str</c> members and the
/// 1.28 <c>gst_value_hash</c> are deliberately not exercised.
/// </para>
/// <para>
/// The fraction round trip is the scenario the projection exists for: a
/// <c>framerate</c> field of a caps has no typed structure accessor, so the
/// hand written <see cref="Structure.GetValue"/> and the generated
/// <c>Global.ValueGetFraction*</c> family are the only way to read one.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GValueProjectionTests
{
    /// <summary>
    /// A framerate is read out of parsed caps as a value, unpacked with the
    /// generated fraction readers, rewritten in place through the <c>ref</c>
    /// shape, and stored back through the hand written writer.
    /// </summary>
    [Fact]
    public void AFramerateRoundTripsThroughTheFractionMembers()
    {
        using Caps? caps = Caps.FromString("video/x-raw, framerate=(fraction)30/1");
        Assert.NotNull(caps);

        using Structure structure = caps.GetStructure(0);

        Value framerate = structure.GetValue("framerate");
        try
        {
            Assert.False(framerate.IsEmpty);
            Assert.Equal(30, Global.ValueGetFractionNumerator(in framerate));
            Assert.Equal(1, Global.ValueGetFractionDenominator(in framerate));

            // The ref shape writes into the caller's own storage: the value
            // was initialized as a fraction by the read above, which is the
            // contract gst_value_set_fraction asserts.
            Global.ValueSetFraction(ref framerate, 15, 2);
            Assert.Equal(15, Global.ValueGetFractionNumerator(in framerate));
            Assert.Equal(2, Global.ValueGetFractionDenominator(in framerate));

            structure.SetValue("framerate", framerate);
        }
        finally
        {
            framerate.Dispose();
        }

        using Value read = structure.GetValue("framerate");
        Assert.Equal(15, Global.ValueGetFractionNumerator(in read));
        Assert.Equal(2, Global.ValueGetFractionDenominator(in read));
    }

    /// <summary>
    /// <c>gst_value_deserialize</c> reads the type of its destination to pick
    /// the parser, which is why the overlays turn its annotated out into the
    /// <c>ref</c> shape: a zeroed destination would fail before parsing
    /// anything.
    /// </summary>
    [Fact]
    public void DeserializeParsesIntoThePreInitializedDestination()
    {
        Value number = Value.New(GType.Int);
        try
        {
            Assert.True(Global.ValueDeserialize(ref number, "42"));
            Assert.Equal(42, number.GetInt());

            // A refused parse leaves the destination what it was.
            Assert.False(Global.ValueDeserialize(ref number, "not-a-number"));
            Assert.Equal(GType.Int, number.Type);
        }
        finally
        {
            number.Dispose();
        }
    }

    /// <summary>
    /// The out shape: the member zeroes the caller's storage and the callee
    /// fills it on success. On the refusal path the storage stays untouched,
    /// which reads as the empty value, and disposing it is a no-op.
    /// </summary>
    [Fact]
    public void IntersectFillsItsDestinationOnSuccessAndLeavesItEmptyOnFailure()
    {
        using Value one = Value.CreateFor(1, GType.Int);
        using Value alsoOne = Value.CreateFor(1, GType.Int);
        using Value two = Value.CreateFor(2, GType.Int);

        Assert.True(Global.ValueIntersect(out Value hit, in one, in alsoOne));
        try
        {
            Assert.Equal(GType.Int, hit.Type);
            Assert.Equal(1, hit.GetInt());
        }
        finally
        {
            hit.Dispose();
        }

        Assert.False(Global.ValueIntersect(out Value miss, in one, in two));
        Assert.True(miss.IsEmpty);
        miss.Dispose();
    }

    /// <summary>
    /// The child proxy pair reaches a property of a named child through the
    /// bin that carries it: the setter is the read-only <c>in</c> shape, the
    /// getter the caller allocated out.
    /// </summary>
    /// <remarks>
    /// The extension methods are called through their static class:
    /// <see cref="Gst.GObject.Object"/> declares instance members named
    /// <c>GetProperty</c> and <c>SetProperty</c> for the properties of the
    /// object itself, and those would win overload resolution for the setter.
    /// </remarks>
    [Fact]
    public void ChildProxyReadsAndWritesThePropertyOfANamedChild()
    {
        using Bin bin = Bin.New("gvalue-proxy-bin");
        using Element child = Assert.IsAssignableFrom<Element>(ElementFactory.Make("identity", "kid"));
        Assert.True(bin.Add(child));

        ChildProxyExtensions.GetProperty(bin, "kid::sync", out Value initial);
        try
        {
            Assert.Equal(GType.Boolean, initial.Type);
            Assert.False(initial.GetBoolean());
        }
        finally
        {
            initial.Dispose();
        }

        using (Value enabled = Value.CreateFor(true, GType.Boolean))
        {
            ChildProxyExtensions.SetProperty(bin, "kid::sync", enabled);

            // The callee copied what it kept: the caller's value is intact.
            Assert.True(enabled.GetBoolean());
        }

        ChildProxyExtensions.GetProperty(bin, "kid::sync", out Value read);
        try
        {
            Assert.True(read.GetBoolean());
        }
        finally
        {
            read.Dispose();
        }
    }

    /// <summary>
    /// The adopted return: <c>gst_object_get_value</c> hands over a
    /// <c>g_new0</c> allocated value, whose contents
    /// <see cref="Value.TakeOwnership"/> moves into the caller's own before
    /// freeing the shell — and answers <c>NULL</c>, the empty value, for a
    /// property that carries no control binding.
    /// </summary>
    [Fact]
    public void AControlledPropertyIsReadAsAnAdoptedValue()
    {
        using Element element =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "gvalue-owner"));

        // No binding yet: NULL comes back and reads as the empty value.
        using (Value unbound = element.GetValue("volume", ClockTime.Zero))
        {
            Assert.True(unbound.IsEmpty);
        }

        InterpolationControlSource source = InterpolationControlSource.New();
        Assert.True(source.Set(ClockTime.Zero, 0.25));
        Assert.True(element.AddControlBinding(DirectControlBinding.NewAbsolute(element, "volume", source)));

        using (Value bound = element.GetValue("volume", ClockTime.Zero))
        {
            Assert.False(bound.IsEmpty);
            Assert.Equal(GType.Double, bound.Type);
            Assert.Equal(0.25, bound.GetDouble(), 6);
        }

        // The same shape on the binding itself, which is the other adopted
        // return of the core module.
        ControlBinding? binding = element.GetControlBinding("volume");
        Assert.NotNull(binding);
        using (Value fromBinding = binding.GetValue(ClockTime.Zero))
        {
            Assert.False(fromBinding.IsEmpty);
            Assert.Equal(0.25, fromBinding.GetDouble(), 6);
        }
    }

    /// <summary>
    /// The guard of the read-only shape: an empty value has no type for the
    /// call to read, and the C side would answer it with a critical warning
    /// and a meaningless result.
    /// </summary>
    [Fact]
    public void AnEmptyValueIsRefusedByTheInShape()
    {
        Value empty = default;

        ArgumentException failure =
            Assert.Throws<ArgumentException>(() => Global.ValueIsFixed(in empty));
        Assert.Equal("value", failure.ParamName);
    }
}
