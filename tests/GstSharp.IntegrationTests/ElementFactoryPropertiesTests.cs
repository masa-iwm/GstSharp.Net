using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The two factory calls that carry the properties of the new element with
/// them, measured against the library that is installed.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_element_factory_make_with_properties</c> and
/// <c>gst_element_factory_create_with_properties</c> arrived in 1.20, below the
/// 1.24 floor of this binding, so nothing here is gated on a native version.
/// </para>
/// <para>
/// What the tests measure is the half the C call leaves to the caller: that
/// each value is built at the type its property declares, that a name the
/// element does not carry and a property that cannot be written are refused
/// before the call rather than answered with a message on the console, and that
/// an element the registry does not have is a <see langword="null"/> answer and
/// not a failure. Every property of those tests belongs to <c>queue</c>, a core
/// element, so they need no plugin beyond <c>coreelements</c>.
/// </para>
/// <para>
/// The reason the two members exist at all - a property that can only be given
/// to the constructor - is measured on <c>audiomixer</c>, whose
/// <c>force-live</c> is the nearest such property to the core; that one test is
/// skipped where the base plugins are not installed.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ElementFactoryPropertiesTests
{
    /// <summary>
    /// The properties travel with the construction and are converted against
    /// the type each one declares: <c>max-size-buffers</c> is a <c>guint</c>
    /// and is given a plain <see cref="int"/>, which widens into it without
    /// loss, and <c>leaky</c> is an enumeration the binding does not generate,
    /// which is why it travels as its number.
    /// </summary>
    [Fact]
    public void MakingAnElementWithPropertiesGivesItThoseProperties()
    {
        using Element element = ElementFactory.MakeWithProperties(
            "queue",
            new Dictionary<string, object?>
            {
                ["name"] = "properties-queue",
                ["max-size-buffers"] = 17,
                ["leaky"] = 2,
            })
            ?? throw new InvalidOperationException("queue is a core element and has to exist.");

        Assert.Equal("properties-queue", element.GetProperty<string>("name"));
        Assert.Equal(17u, element.GetProperty<uint>("max-size-buffers"));
        Assert.Equal(2, element.GetProperty<int>("leaky"));
    }

    /// <summary>
    /// An empty dictionary is allowed: it is the null arrays of the C call,
    /// which builds the element with the defaults of its class.
    /// </summary>
    [Fact]
    public void MakingAnElementWithNoPropertyAtAllBuildsIt()
    {
        using Element element = ElementFactory.MakeWithProperties("queue", new Dictionary<string, object?>())
            ?? throw new InvalidOperationException("queue is a core element and has to exist.");

        // The defaults of the class, untouched.
        Assert.Equal(200u, element.GetProperty<uint>("max-size-buffers"));
    }

    /// <summary>
    /// A factory the registry does not have is <see langword="null"/> and not
    /// an exception, the same answer <c>ElementFactory.Make</c> gives: an
    /// application that names an element a plugin set does not carry has to be
    /// able to see that as a value.
    /// </summary>
    [Fact]
    public void MakingAnElementOfAFactoryThatDoesNotExistAnswersNothing()
    {
        Element? element = ElementFactory.MakeWithProperties(
            "gstsharp-no-such-factory",
            new Dictionary<string, object?> { ["name"] = "never" });

        Assert.Null(element);
    }

    /// <summary>
    /// A name the element does not declare is refused before anything is
    /// created. GLib answers one with a message on the console and an element
    /// whose property was silently never written.
    /// </summary>
    /// <remarks>
    /// The good property in front of the bad one is what makes this the leak
    /// test of the family: <c>name</c> is a string, so its value owns a copy of
    /// that string by the time the second name is refused, and the array it was
    /// moved into is what has to unset it on the way out.
    /// </remarks>
    [Fact]
    public void APropertyTheElementDoesNotDeclareIsRefused()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ElementFactory.MakeWithProperties(
                "queue",
                new Dictionary<string, object?>
                {
                    ["name"] = "never-built",
                    ["no-such-property"] = 1,
                }));

        Assert.Equal("properties", exception.ParamName);
        Assert.Contains("no-such-property", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>current-level-buffers</c> reports how full the queue is and is
    /// readable only, which is the other misuse GLib answers with a console
    /// message and a write that never happens.
    /// </summary>
    [Fact]
    public void APropertyThatCannotBeWrittenIsRefused()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ElementFactory.MakeWithProperties(
                "queue",
                new Dictionary<string, object?> { ["current-level-buffers"] = 4 }));

        Assert.Equal("properties", exception.ParamName);
        Assert.Contains("current-level-buffers", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The value has to fit the type the property declares, which is the
    /// conversion contract of <see cref="Gst.GObject.Value.CreateFor"/>: a
    /// string is not a number, and a negative number does not fit a
    /// <c>guint</c>.
    /// </summary>
    /// <param name="content">The value to try to write.</param>
    [Theory]
    [InlineData("seventeen")]
    [InlineData(-1)]
    public void AValueThePropertyCannotHoldIsRefused(object content)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ElementFactory.MakeWithProperties(
                "queue",
                new Dictionary<string, object?> { ["max-size-buffers"] = content }));

        Assert.Contains("max-size-buffers", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The instance call is the same creation reached through a factory the
    /// caller already holds, and the factory survives it: it is borrowed for
    /// the length of the call and nothing about it is consumed.
    /// </summary>
    [Fact]
    public void CreatingAnElementFromAFactoryGivesItItsProperties()
    {
        using ElementFactory factory = ElementFactory.Find("queue")
            ?? throw new InvalidOperationException("queue is a core element and has to exist.");

        using (Element element = factory.CreateWithProperties(
            new Dictionary<string, object?>
            {
                ["name"] = "created-queue",
                ["max-size-bytes"] = 4096u,
            })
            ?? throw new InvalidOperationException("queue is a core element and has to exist."))
        {
            Assert.Equal("created-queue", element.GetProperty<string>("name"));
            Assert.Equal(4096u, element.GetProperty<uint>("max-size-bytes"));
        }

        using Element second = factory.CreateWithProperties(new Dictionary<string, object?>())
            ?? throw new InvalidOperationException("queue is a core element and has to exist.");

        Assert.Equal("queue", factory.Name);
        Assert.NotNull(second.GetProperty<string>("name"));
    }

    /// <summary>
    /// A property that can only be given to the constructor is what the two
    /// members are for, and the one guard
    /// <see cref="Gst.GObject.Object.SetProperty(string, object?)"/> makes that
    /// they deliberately do not repeat. <c>force-live</c> of
    /// <c>GstAudioAggregator</c> is declared
    /// <c>G_PARAM_READWRITE | G_PARAM_CONSTRUCT_ONLY</c> and arrived in 1.22,
    /// below the floor this binding is built against, so what is measured here
    /// is the contrast: the factory writes it while the element is built, and
    /// writing it afterwards is refused.
    /// </summary>
    [RequiresElementFact("audiomixer")]
    public void APropertyThatCanOnlyBeGivenToTheConstructorIsWritten()
    {
        using Element element = ElementFactory.MakeWithProperties(
            "audiomixer",
            new Dictionary<string, object?> { ["force-live"] = true })
            ?? throw new InvalidOperationException("audiomixer was found by the attribute of this test.");

        Assert.True(element.GetProperty<bool>("force-live"));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => element.SetProperty("force-live", false));

        Assert.Equal("name", exception.ParamName);
        Assert.True(element.GetProperty<bool>("force-live"));
    }

    /// <summary>
    /// Neither argument may be <see langword="null"/>: the name of the factory
    /// is what the registry is asked for, and the dictionary is the surface of
    /// the call rather than an optional extra - the empty dictionary is how
    /// "no property" is spelled.
    /// </summary>
    [Fact]
    public void TheArgumentsAreRefusedWhenTheyAreNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => ElementFactory.MakeWithProperties(null!, new Dictionary<string, object?>()));
        Assert.Throws<ArgumentNullException>(() => ElementFactory.MakeWithProperties("queue", null!));

        using ElementFactory factory = ElementFactory.Find("queue")
            ?? throw new InvalidOperationException("queue is a core element and has to exist.");

        Assert.Throws<ArgumentNullException>(() => factory.CreateWithProperties(null!));
    }
}
