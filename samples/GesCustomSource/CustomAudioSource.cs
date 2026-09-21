// A managed GESAudioSource that answers the element behind it and that watches
// every child property write reaching it, which is the second half of the
// timeline this sample builds.
using Gst.GObject;

/// <summary>
/// A managed <c>GESAudioSource</c> whose <c>create_source</c> override answers
/// an <c>audiotestsrc</c>, and whose <c>set_child_property_full</c> override
/// sees every write of a child property of that element.
/// </summary>
/// <remarks>
/// <para>
/// As for the video source beside it, nothing in this sample constructs one:
/// the editing services build it through <c>ges_asset_extract</c> when the clip
/// is added to a layer, so <see cref="CreateWrapper"/> is what says how the
/// wrapper of such an instance is made. See <c>docs/subclassing.md</c> §11.
/// </para>
/// <para>
/// The properties of the element a source is made of are not child properties
/// by themselves: <c>ges_audio_source_create_element</c> registers the volume
/// and the converter of the bin it builds and nothing of the sub element
/// (ges-audio-source.c:159-163), so the override registers the one it wants to
/// show the same way <c>GESAudioTestSource</c> does
/// (ges-audio-test-source.c:114).
/// </para>
/// <para>
/// Installing <c>set_child_property_full</c> takes over every child property
/// write of this element: the <c>set_child_property</c> slot below is only
/// reached through it, so the override chains up for every write it lets
/// through.
/// </para>
/// </remarks>
internal sealed class CustomAudioSource : GES.AudioSource, IManagedSubclass<CustomAudioSource>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpSampleGesAudioSource";

    /// <summary>The child property of the inner element the sample writes.</summary>
    internal const string ToneProperty = "freq";

    /// <summary>The highest tone the override lets through, in hertz.</summary>
    internal const double HighestTone = 2000.0;

    private static readonly SubclassType Definition = DefineSubclass<CustomAudioSource>(
        GTypeName,
        null,
        CreateSourceOverride,
        SetChildPropertyFullOverride);

    private readonly List<string> _observedWrites = [];

    private CustomAudioSource(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the source.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets the factory name of the element the override built.</summary>
    internal string? BuiltElement { get; private set; }

    /// <summary>Gets the names of the child properties the override saw written.</summary>
    internal IReadOnlyList<string> ObservedWrites => _observedWrites;

    /// <summary>Gets what the override refused the last time it refused a write.</summary>
    internal string? LastRefusal { get; private set; }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static CustomAudioSource CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override Gst.Element OnCreateSource()
    {
        // No ancestor implements this slot for an audio source, so there is
        // nothing to chain up to: the override answers an element or the
        // binding substitutes an identity for it.
        Gst.Element source = Gst.ElementFactory.Make("audiotestsrc", null)
            ?? throw new InvalidOperationException(
                "audiotestsrc is not installed. Install the base plugins of GStreamer.");

        // The whitelist is what turns one property of the sub element into a
        // child property of this track element. It is registered here, before
        // the element is handed over, which is where GESAudioTestSource
        // registers its own.
        AddChildrenProps(source, null, null, [ToneProperty]);

        BuiltElement = source.Name;
        return source;
    }

    /// <inheritdoc/>
    protected override bool OnSetChildPropertyFull(
        Gst.GObject.Object child,
        ParamSpec pspec,
        ValueView value,
        out Gst.GLib.GException? error)
    {
        _observedWrites.Add(pspec.Name ?? "?");

        // A tone the sample decided against: the refusal carries a reason, so
        // the caller of the full member is told why rather than only that.
        if (string.Equals(pspec.Name, ToneProperty, StringComparison.Ordinal)
            && value.Type == Gst.GObject.GType.Double
            && value.GetDouble() > HighestTone)
        {
            LastRefusal = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{value.GetDouble():F0} Hz is above the {HighestTone:F0} Hz this source allows.");

            error = new Gst.GLib.GException(
                Gst.CoreErrorExtensions.Quark(),
                (int)Gst.CoreError.Failed,
                LastRefusal);
            return false;
        }

        // Chaining up is what keeps the set_child_property slot, and with it
        // the write itself, reachable.
        return ChainUpSetChildPropertyFull(child, pspec, value, out error);
    }
}
