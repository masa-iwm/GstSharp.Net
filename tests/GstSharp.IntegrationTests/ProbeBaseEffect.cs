using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESBaseEffect</c>, which is the shape that has to build its own
/// element: no class between <c>GESTrackElement</c> and here implements
/// <c>create_element</c>.
/// </summary>
/// <remarks>
/// <para>
/// The library guards the NULL slot (<c>ges-track-element.c:1024</c>), so a
/// subclass that declares nothing is not a crash — it is an <c>nleoperation</c>
/// with nothing inside it, which passes no data. The override is what makes the
/// effect an effect, and <c>ChainUpCreateElement</c> would throw here.
/// </para>
/// <para>
/// Nothing tells such a type which track it belongs to either. The track type is
/// set once on the asset, which <c>set_asset</c> copies into every instance that
/// is still unknown, the copies native code extracts included
/// (<c>ges-track-element.c:286-290</c>). Its asset id may be <see langword="null"/>:
/// <c>GESBaseEffect</c> is not <c>GESEffect</c> and has no description to parse.
/// </para>
/// </remarks>
internal sealed class ProbeBaseEffect : GES.BaseEffect, IManagedSubclass<ProbeBaseEffect>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesBaseEffect";

    private static readonly SubclassType Definition = DefineSubclass<ProbeBaseEffect>(
        GTypeName,
        null,
        CreateElementOverride);

    private static int _createElementCalls;

    private ProbeBaseEffect(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the effect.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets how often the <c>create_element</c> override ran.</summary>
    internal static int CreateElementCalls => Volatile.Read(ref _createElementCalls);

    /// <summary>Forgets what the previous test observed.</summary>
    internal static void Reset() => Volatile.Write(ref _createElementCalls, 0);

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeBaseEffect CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override Gst.Element OnCreateElement()
    {
        _ = Interlocked.Increment(ref _createElementCalls);

        return Gst.ElementFactory.Make("identity", null)
            ?? throw new InvalidOperationException("identity is not installed.");
    }
}
