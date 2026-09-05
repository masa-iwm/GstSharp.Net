using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed element that installs a property of a name its parent already
/// uses, which GObject allows and this binding therefore has to allow too.
/// </summary>
/// <remarks>
/// It stands on its own rather than joining <see cref="ProbePropertyElement"/>
/// because shadowing <c>name</c> really does take it over: there is no chain up
/// out of a property slot, so <c>GstObject</c> never sees the value again and
/// the element has no name as far as GStreamer is concerned. That is the point
/// of the test, and it would be a trap for every other test on the same type.
/// </remarks>
internal sealed class ProbeShadowNameElement : Element, IManagedSubclass<ProbeShadowNameElement>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestShadowNameElement";

    /// <summary>The identifier of the shadowing <c>name</c> property.</summary>
    internal const uint NameId = 1;

    private static readonly ParamSpecString NameSpec = ParamSpecString.New(
        "name",
        "Name",
        "A property that shadows the one of GstObject",
        null,
        ParamFlags.Readable | ParamFlags.Writable);

    private static readonly SubclassType Definition = DefineSubclass<ProbeShadowNameElement>(
        GTypeName,
        ConfigureClass,
        SetPropertyOverride,
        GetPropertyOverride);

    private string? _shadowed;

    private ProbeShadowNameElement(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Creates one instance.</summary>
    public ProbeShadowNameElement()
        : this(Definition.NewInstance())
    {
    }

    /// <summary>Gets the type the element is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets the specification that was installed.</summary>
    internal static ParamSpecString SpecOfName => NameSpec;

    /// <summary>Gets what the shadowing property stored.</summary>
    internal string? Shadowed => _shadowed;

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeShadowNameElement CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override void OnSetProperty(uint propertyId, ValueView value, ParamSpec pspec)
    {
        if (propertyId == NameId)
        {
            _shadowed = value.GetString();
            return;
        }

        base.OnSetProperty(propertyId, value, pspec);
    }

    /// <inheritdoc/>
    protected override void OnGetProperty(uint propertyId, ValueRef value, ParamSpec pspec)
    {
        if (propertyId == NameId)
        {
            value.SetString(_shadowed);
            return;
        }

        base.OnGetProperty(propertyId, value, pspec);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe shadow name element",
            "Testing",
            "A managed element that redefines a property of its parent",
            "GstSharp.Net integration tests");

        config.InstallProperty(NameId, NameSpec);
    }
}
