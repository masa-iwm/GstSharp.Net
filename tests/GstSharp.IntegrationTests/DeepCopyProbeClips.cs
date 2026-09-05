using Gst.GObject;
using Gst.Interop;

namespace GstSharp.IntegrationTests;

/// <summary>
/// What a <c>deep_copy</c> override observed about the copy it was handed.
/// </summary>
/// <param name="CopyIsManaged">Whether the copy arrived as the managed type.</param>
/// <param name="Tag">What the installed property held when the slot ran.</param>
/// <param name="IsFloating">Whether the copy was still floating.</param>
/// <param name="RefCount">The reference count of the copy at that moment.</param>
internal sealed record DeepCopyObservation(bool CopyIsManaged, string? Tag, bool IsFloating, uint RefCount);

/// <summary>
/// A managed <c>GESSourceClip</c> defined with a wrapper factory, which is what
/// lets the copy the base class creates be resolved as the managed type. It
/// carries one installed property, which <c>ges_timeline_element_copy</c> copies
/// by itself before the slot runs, and one field outside the property system,
/// which only the override copies.
/// </summary>
internal sealed unsafe class DeepCopyProbeClip : GES.SourceClip, IManagedSubclass<DeepCopyProbeClip>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestDeepCopyProbeClip";

    /// <summary>The identifier of the <c>probe-tag</c> property.</summary>
    internal const uint TagId = 1;

    private static readonly ParamSpecString TagSpec = ParamSpecString.New(
        "probe-tag",
        "Probe tag",
        "A string ges_timeline_element_copy carries over by itself",
        null,
        ParamFlags.Readable | ParamFlags.Writable);

    private static readonly SubclassType Definition = DefineSubclass<DeepCopyProbeClip>(
        GTypeName,
        ConfigureClass,
        DeepCopyOverride,
        SetPropertyOverride,
        GetPropertyOverride);

    private string? _tag;

    private DeepCopyProbeClip(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the clip.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets what every override observed, in order.</summary>
    internal static List<DeepCopyObservation> Observations { get; } = [];

    /// <summary>Gets or sets the state that lives outside the property system.</summary>
    internal string? Note { get; set; }

    /// <summary>Gets what the last write of <c>probe-tag</c> stored.</summary>
    internal string? Tag => _tag;

    /// <summary>Builds a clip out of an asset for its own type.</summary>
    /// <returns>The new clip, which has an asset.</returns>
    internal static DeepCopyProbeClip New()
    {
        GES.Asset asset = GES.Asset.Request(Definition.GType, null)
            ?? throw new InvalidOperationException("The clip asset could not be requested.");

        return asset.Extract<DeepCopyProbeClip>();
    }

    /// <summary>Forgets what the previous test observed.</summary>
    internal static void Reset() => Observations.Clear();

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static DeepCopyProbeClip CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override void OnDeepCopy(GES.TimelineElement copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        // The copy still belongs to the caller here: it is floating, and the
        // wrapper took a reference of its own without sinking that one.
        Observations.Add(new DeepCopyObservation(
            copy is DeepCopyProbeClip,
            copy.GetProperty<string>("probe-tag"),
            GObjectNative.ObjectIsFloating(copy.Handle) != 0,
            *(uint*)(copy.Handle + sizeof(nint))));

        ChainUpDeepCopy(copy);

        // The property system carried the tag over by itself; the note lives
        // outside it, which is what this slot is for.
        if (copy is DeepCopyProbeClip managed)
        {
            managed.Note = Note;
        }
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

/// <summary>
/// The same clip defined without a wrapper factory. The non-generic
/// <c>DefineSubclass</c> registers no way of building the wrapper of an instance
/// native code created, so neither an extracted instance nor the copy the base
/// class makes is ever this type: both are wrapped as the closest registered
/// ancestor, and the override below can never run.
/// </summary>
internal sealed class PlainDeepCopyClip : GES.SourceClip
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestPlainDeepCopyClip";

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        static _ => { },
        DeepCopyOverride);

    private PlainDeepCopyClip(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets how many times the override ran.</summary>
    internal static int Calls { get; private set; }

    /// <summary>Gets the registration of the clip.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>
    /// Builds an instance out of an asset for its own type. It comes back as a
    /// <see cref="GES.SourceClip"/>, because the type states no wrapper.
    /// </summary>
    /// <returns>The new clip, which has an asset.</returns>
    internal static GES.SourceClip New()
    {
        GES.Asset asset = GES.Asset.Request(Definition.GType, null)
            ?? throw new InvalidOperationException("The clip asset could not be requested.");

        return asset.Extract<GES.SourceClip>();
    }

    /// <summary>Forgets what the previous test observed.</summary>
    internal static void Reset() => Calls = 0;

    /// <inheritdoc/>
    protected override void OnDeepCopy(GES.TimelineElement copy)
    {
        Calls++;
        ChainUpDeepCopy(copy);
    }
}
