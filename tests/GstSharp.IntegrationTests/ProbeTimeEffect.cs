using GES;
using Gst;
using Gst.GObject;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESBaseEffect</c> that makes itself a time effect: it builds a
/// <c>videorate</c>, registers its <c>rate</c> as the time property, and
/// translates times through it the way the library's own rate effects do.
/// </summary>
/// <remarks>
/// <para>
/// The registration lives in the <c>create_element</c> override because that is
/// the one place every instance passes through before anything can add it to a
/// clip: <c>set_asset</c> runs the slot once per instance
/// (<c>ges-track-element.c:295</c>, <c>:1008-1009</c>, <c>:1026-1028</c>) while
/// the effect has no parent and <c>has-internal-source</c> is still the class
/// default, FALSE (<c>:330-333</c>). A copy the library makes - a split, a
/// paste, a second track - is a fresh extraction
/// (<c>ges-timeline-element.c:1676</c>) that carries neither the registration
/// nor the functions, which are private to the instance
/// (<c>ges-base-effect.c:104-111</c>); it runs the override again instead.
/// </para>
/// <para>
/// The element is a bin that ends in a <c>capsfilter</c> named
/// <c>___ges__effectcapsfilter</c>, the shape the library builds for its own
/// video effects. The frame positioner of 1.28 looks that name up on the first
/// time effect of the video track (<c>ges-clip.c:1942</c>,
/// <c>gstframepositioner.c:394</c>, <c>:566-571</c>); a bare element costs a
/// critical and a <c>GST_ERROR</c> per lookup (<c>gstbin.c:4386</c>).
/// </para>
/// <para>
/// Both functions are static and read only their arguments. An instance method
/// would root the wrapper through the <c>GCHandle</c> that keeps the functions
/// alive (<c>CallbackHandle.cs:107-111</c>) until the effect is disposed
/// explicitly.
/// </para>
/// </remarks>
internal sealed class ProbeTimeEffect : GES.BaseEffect, IManagedSubclass<ProbeTimeEffect>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesTimeEffect";

    /// <summary>The child property registered as the time property.</summary>
    internal const string ChildPropertyName = "rate";

    /// <summary>The name the frame positioner of 1.28 looks the capsfilter up by.</summary>
    internal const string CapsFilterName = "___ges__effectcapsfilter";

    /// <summary>The bin the override builds.</summary>
    internal const string BinDescription = "videorate ! capsfilter name=" + CapsFilterName;

    private static readonly SubclassType Definition = DefineSubclass<ProbeTimeEffect>(
        GTypeName,
        null,
        CreateElementOverride);

    private static int _createElementCalls;

    private static int _sawNoParent;

    private static int _sawNoInternalSource;

    private static int _registeredTimeProperty;

    private static int _setTranslationFuncs;

    private static long _lastSeenRate = BitConverter.DoubleToInt64Bits(double.NaN);

    private static int _translationCalls;

    private ProbeTimeEffect(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the effect.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets how often the <c>create_element</c> override ran.</summary>
    internal static int CreateElementCalls => Volatile.Read(ref _createElementCalls);

    /// <summary>Gets whether the effect had no parent when the override last ran.</summary>
    internal static bool SawNoParent => Volatile.Read(ref _sawNoParent) != 0;

    /// <summary>Gets whether the effect had no internal source when the override last ran.</summary>
    internal static bool SawNoInternalSource => Volatile.Read(ref _sawNoInternalSource) != 0;

    /// <summary>Gets what <see cref="BaseEffect.RegisterTimeProperty"/> last answered.</summary>
    internal static bool RegisteredTimeProperty => Volatile.Read(ref _registeredTimeProperty) != 0;

    /// <summary>Gets what <see cref="BaseEffect.SetTimeTranslationFuncs"/> last answered.</summary>
    internal static bool SetTranslationFuncs => Volatile.Read(ref _setTranslationFuncs) != 0;

    /// <summary>Gets the rate the last translation call was handed.</summary>
    internal static double LastSeenRate => BitConverter.Int64BitsToDouble(Volatile.Read(ref _lastSeenRate));

    /// <summary>Gets how often a translation function ran.</summary>
    internal static int TranslationCalls => Volatile.Read(ref _translationCalls);

    /// <summary>Forgets what the previous test observed.</summary>
    internal static void Reset()
    {
        Volatile.Write(ref _createElementCalls, 0);
        Volatile.Write(ref _sawNoParent, 0);
        Volatile.Write(ref _sawNoInternalSource, 0);
        Volatile.Write(ref _registeredTimeProperty, 0);
        Volatile.Write(ref _setTranslationFuncs, 0);
        Volatile.Write(ref _lastSeenRate, BitConverter.DoubleToInt64Bits(double.NaN));
        Volatile.Write(ref _translationCalls, 0);
    }

    /// <summary>Extracts a video instance from the asset of the type.</summary>
    /// <returns>The effect, whose override has run.</returns>
    /// <remarks>
    /// The asset is cached per type, so the track type set here sticks for the
    /// rest of the process.
    /// </remarks>
    internal static ProbeTimeEffect New()
    {
        Asset asset = Asset.Request(Definition.GType, null)
            ?? throw new InvalidOperationException("The effect asset could not be requested.");

        TrackElementAsset trackAsset = (TrackElementAsset)asset;
        trackAsset.SetTrackType(TrackType.Video);

        return trackAsset.Extract<ProbeTimeEffect>();
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeTimeEffect CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override Element OnCreateElement()
    {
        _ = Interlocked.Increment(ref _createElementCalls);
        Volatile.Write(ref _sawNoParent, Parent is null ? 1 : 0);
        Volatile.Write(ref _sawNoInternalSource, HasInternalSource() ? 0 : 1);

        Bin bin = Gst.Global.ParseBinFromDescription(BinDescription, true);

        // The blacklist matches factory names (ges-track-element.c:1098-1101).
        // It is load-bearing on the 1.24 floor, which lacks the ___ges__ name
        // skip of 1.28 (ges-track-element.c:1183).
        AddChildrenProps(bin, null, ["capsfilter"], null);

        Volatile.Write(ref _registeredTimeProperty, RegisterTimeProperty(ChildPropertyName) ? 1 : 0);
        Volatile.Write(ref _setTranslationFuncs, SetTimeTranslationFuncs(SourceToSink, SinkToSource) ? 1 : 0);

        return bin;
    }

    /// <summary>Translates a time from the source side to the sink side: <c>time * rate</c>.</summary>
    /// <remarks>The arithmetic and the cast are those of <c>ges-effect.c:274-290</c>.</remarks>
    private static ClockTime SourceToSink(BaseEffect effect, ClockTime time, IReadOnlyDictionary<string, Value> values)
    {
        double rate = Record(values);

        if (time.Nanoseconds == 0)
        {
            return ClockTime.Zero;
        }

        if (rate == 0.0)
        {
            return ClockTime.Zero;
        }

        return new ClockTime((ulong)(time.Nanoseconds * rate));
    }

    /// <summary>Translates a time from the sink side to the source side: <c>time / rate</c>.</summary>
    /// <remarks>The arithmetic and the cast are those of <c>ges-effect.c:292-306</c>.</remarks>
    private static ClockTime SinkToSource(BaseEffect effect, ClockTime time, IReadOnlyDictionary<string, Value> values)
    {
        double rate = Record(values);

        if (time.Nanoseconds == 0)
        {
            return ClockTime.Zero;
        }

        if (rate == 0.0)
        {
            return ClockTime.None;
        }

        return new ClockTime((ulong)(time.Nanoseconds / rate));
    }

    /// <summary>Reads the rate a translation call was handed and records the call.</summary>
    private static double Record(IReadOnlyDictionary<string, Value> values)
    {
        double rate = values[ChildPropertyName].GetDouble();
        Volatile.Write(ref _lastSeenRate, BitConverter.DoubleToInt64Bits(rate));
        _ = Interlocked.Increment(ref _translationCalls);
        return rate;
    }
}
