using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESVideoSource</c> that overrides <c>set_child_property</c> and
/// nothing above it, which is the shape a subclass has when it leaves
/// <c>set_child_property_full</c> to the editing services.
/// </summary>
/// <remarks>
/// It exists beside <see cref="ProbeVideoSource"/>, which takes both slots
/// over, so that the plain one keeps being exercised the way every consumer
/// that never heard of the full slot exercises it: through the implementation
/// GES installs on every class.
/// </remarks>
internal sealed class PlainChildPropertyVideoSource
    : GES.VideoSource, IManagedSubclass<PlainChildPropertyVideoSource>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesPlainChildPropertySource";

    /// <summary>The name of the child property the source registers for itself.</summary>
    internal const string TagName = "plain-tag";

    /// <summary>The identifier of the <c>plain-tag</c> property.</summary>
    internal const uint TagId = 1;

    private static readonly ParamSpecString TagSpec = ParamSpecString.New(
        TagName,
        "Plain tag",
        "A string the child property slot writes and the property slots keep",
        null,
        ParamFlags.Readable | ParamFlags.Writable);

    private static readonly SubclassType Definition = DefineSubclass<PlainChildPropertyVideoSource>(
        GTypeName,
        ConfigureClass,
        CreateSourceOverride,
        SetChildPropertyOverride,
        SetPropertyOverride,
        GetPropertyOverride);

    private readonly List<string> _childPropertyWrites = [];

    private string? _tag;

    private PlainChildPropertyVideoSource(SubclassCtorArgs args)
        : base(args) =>
        _ = AddChildProperty(TagSpec, this);

    /// <summary>Gets what the last write of <c>plain-tag</c> stored.</summary>
    internal string? Tag => _tag;

    /// <summary>Gets the child properties the <c>set_child_property</c> override saw.</summary>
    internal IReadOnlyList<string> ChildPropertyWrites
    {
        get
        {
            lock (_childPropertyWrites)
            {
                return [.. _childPropertyWrites];
            }
        }
    }

    /// <summary>Builds an instance no asset describes.</summary>
    /// <returns>The new source, which has no asset.</returns>
    internal static PlainChildPropertyVideoSource New() => new(Definition.NewInstance());

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static PlainChildPropertyVideoSource CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override Gst.Element OnCreateSource() =>
        Gst.ElementFactory.Make("videotestsrc", null)
            ?? throw new InvalidOperationException("videotestsrc is not installed.");

    /// <inheritdoc/>
    protected override void OnSetChildProperty(Gst.GObject.Object child, ParamSpec pspec, ValueView value)
    {
        lock (_childPropertyWrites)
        {
            _childPropertyWrites.Add(pspec.Name);
        }

        ChainUpSetChildProperty(child, pspec, value);
    }

    /// <inheritdoc/>
    protected override void OnSetProperty(uint propertyId, ValueView value, ParamSpec pspec)
    {
        if (propertyId == TagId)
        {
            _tag = value.GetString();
            return;
        }

        base.OnSetProperty(propertyId, value, pspec);
    }

    /// <inheritdoc/>
    protected override void OnGetProperty(uint propertyId, ValueRef value, ParamSpec pspec)
    {
        if (propertyId == TagId)
        {
            value.SetString(_tag);
            return;
        }

        base.OnGetProperty(propertyId, value, pspec);
    }

    private static void ConfigureClass(ObjectClassConfig config) =>
        config.InstallProperty(TagId, TagSpec);
}
