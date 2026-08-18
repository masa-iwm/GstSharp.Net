using Gst;
using Gst.Controller;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Object = Gst.GObject.Object;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstSharp.Net.Controller</c> module against the library that is
/// installed: control points drive a bound property, the interpolation mode
/// decides what happens between them, and the binding the module builds is an
/// ordinary <see cref="Gst.ControlBinding"/> that the core binding already
/// knows.
/// </summary>
/// <remarks>
/// <para>
/// The module is the worked example of
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/modules.md">docs/modules.md</see>:
/// it is written entirely against the public surface of <c>GstSharp.Net</c>,
/// with no <c>InternalsVisibleTo</c> in either direction. That it compiles is
/// what certifies the SPI; what these tests add is that it also works.
/// </para>
/// <para>
/// <c>volume</c> is the element under test because its <c>volume</c> property
/// is a double that is writable, controllable and not construct-only, which is
/// what a control binding needs, and because it ships in
/// <c>gst-plugins-base</c>, which every leg of the build installs.
/// <c>videotestsrc</c>, out of the same plugin set, is the element the ARGB
/// binding needs: its <c>foreground-color</c> is a controllable <c>guint</c>
/// holding a big-endian ARGB colour, and a <c>guint</c> is the only thing that
/// binding accepts.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ControllerModuleTests
{
    /// <summary>
    /// An absolute binding passes the values of the source through, so the
    /// property reads exactly what the control points say and, between them,
    /// exactly what the interpolation makes of them.
    /// </summary>
    [Fact]
    public void AnAbsoluteBindingDrivesTheBoundProperty()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "absolute"));

        InterpolationControlSource source = InterpolationControlSource.New();
        source.Mode = InterpolationMode.Linear;

        Assert.True(source.Set(ClockTime.Zero, 0.2));
        Assert.True(source.Set(ClockTime.FromSeconds(1), 0.8));
        Assert.True(element.AddControlBinding(DirectControlBinding.NewAbsolute(element, "volume", source)));

        Assert.Equal(0.2, VolumeAt(element, ClockTime.Zero), 6);
        Assert.Equal(0.5, VolumeAt(element, ClockTime.FromMilliseconds(500)), 6);
        Assert.Equal(0.8, VolumeAt(element, ClockTime.FromSeconds(1)), 6);
    }

    /// <summary>
    /// A plain binding reads the values of the source as a fraction of the
    /// range the property declares. <c>volume</c> runs from 0 to 10, so the
    /// same numbers come out ten times as large as they do above.
    /// </summary>
    [Fact]
    public void APlainBindingMapsOntoTheRangeOfTheProperty()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "scaled"));

        InterpolationControlSource source = InterpolationControlSource.New();
        source.Mode = InterpolationMode.Linear;

        Assert.True(source.Set(ClockTime.Zero, 0.2));
        Assert.True(source.Set(ClockTime.FromSeconds(1), 0.8));
        Assert.True(element.AddControlBinding(DirectControlBinding.New(element, "volume", source)));

        Assert.Equal(2.0, VolumeAt(element, ClockTime.Zero), 6);
        Assert.Equal(5.0, VolumeAt(element, ClockTime.FromMilliseconds(500)), 6);
        Assert.Equal(8.0, VolumeAt(element, ClockTime.FromSeconds(1)), 6);
    }

    /// <summary>
    /// The mode is what happens between two control points: none holds the
    /// previous value, linear draws a line. A fresh source is in
    /// <see cref="InterpolationMode.None"/>.
    /// </summary>
    [Fact]
    public void TheModeDecidesWhatHappensBetweenControlPoints()
    {
        InterpolationControlSource source = InterpolationControlSource.New();
        Assert.Equal(InterpolationMode.None, source.Mode);

        Assert.True(source.Set(ClockTime.Zero, 0.0));
        Assert.True(source.Set(ClockTime.FromSeconds(1), 1.0));

        Assert.True(source.TryGetValue(ClockTime.FromMilliseconds(500), out double stepped));
        Assert.Equal(0.0, stepped, 6);

        source.Mode = InterpolationMode.Linear;
        Assert.Equal(InterpolationMode.Linear, source.Mode);

        Assert.True(source.TryGetValue(ClockTime.FromMilliseconds(500), out double interpolated));
        Assert.Equal(0.5, interpolated, 6);
    }

    /// <summary>
    /// Control points can be taken away again, one at a time or all at once,
    /// and a source without any answers nothing.
    /// </summary>
    [Fact]
    public void ControlPointsCanBeRemoved()
    {
        InterpolationControlSource source = InterpolationControlSource.New();

        Assert.True(source.Set(ClockTime.Zero, 0.0));
        Assert.True(source.Set(ClockTime.FromSeconds(1), 1.0));
        Assert.Equal(2, source.Count);

        Assert.True(source.Unset(ClockTime.FromSeconds(1)));
        Assert.False(source.Unset(ClockTime.FromSeconds(1)));
        Assert.Equal(1, source.Count);

        source.UnsetAll();
        Assert.Equal(0, source.Count);
        Assert.False(source.TryGetValue(ClockTime.Zero, out _));
    }

    /// <summary>
    /// The module builds no wrapper of its own for
    /// <c>GstDirectControlBinding</c>, so what it hands back is the
    /// <see cref="Gst.ControlBinding"/> of the core binding: the same interned
    /// instance comes back out of the element, and the members the core binding
    /// already has work on it.
    /// </summary>
    [Fact]
    public void TheBindingIsTheControlBindingOfTheCoreBinding()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "attached"));

        InterpolationControlSource source = InterpolationControlSource.New();
        Assert.True(source.Set(ClockTime.Zero, 0.5));

        Gst.ControlBinding binding = DirectControlBinding.New(element, "volume", source);
        Assert.True(element.AddControlBinding(binding));

        // GObject wrappers are interned, so the lookup hands the same one back.
        Assert.Same(binding, element.GetControlBinding("volume"));
        Assert.True(element.HasActiveControlBindings());

        binding.SetDisabled(true);
        Assert.True(binding.IsDisabled());
        Assert.False(element.HasActiveControlBindings());
    }

    /// <summary>
    /// The type registry answers with the wrapper of the module, which is what
    /// <see cref="InterpolationControlSource.New"/> goes through: it does not
    /// call the constructor, it asks
    /// <see cref="Object.FromNative{T}(nint, Transfer)"/> for the wrapper of the
    /// handle.
    /// </summary>
    [Fact]
    public void TheRegistryBuildsTheWrapperOfTheModule()
    {
        InterpolationControlSource source = InterpolationControlSource.New();

        Assert.IsType<InterpolationControlSource>(source);
        Assert.Same(source, Object.FromNative(source.Handle, Transfer.None));
    }

    /// <summary>
    /// The library the module registered is loaded out of the installation the
    /// core libraries were pinned to, which is the guarantee that keeps one
    /// process on one GStreamer.
    /// </summary>
    /// <remarks>
    /// Only Windows can turn a module handle back into a path, so that is where
    /// the directories are compared; everywhere else the assertion is that the
    /// registered name resolved at all, which the call above proves.
    /// </remarks>
    [Fact]
    public void TheRegisteredLibraryIsLoadedFromThePinnedInstallation()
    {
        // Reaching for the module resolves GstController through the loader.
        _ = InterpolationControlSource.New();

        string? controller = NativeLoader.GetLoadedModulePath("GstController");

        if (!OperatingSystem.IsWindows())
        {
            Assert.Null(controller);
            return;
        }

        string? core = NativeLoader.GetLoadedModulePath("Gst");

        Assert.NotNull(core);
        Assert.NotNull(controller);
        Assert.Equal(
            Path.GetDirectoryName(core),
            Path.GetDirectoryName(controller),
            ignoreCase: true);
    }

    /// <summary>
    /// The LFO source is a waveform rather than a set of control points: the
    /// five properties describe it, and it answers at every timestamp. A sine of
    /// two periods per second, swinging by 0.5 around 0.5, is at its offset at
    /// the start of the period, at the top a quarter later, back at the offset
    /// after half of it and at the bottom after three quarters.
    /// </summary>
    [Fact]
    public void AnLFOControlSourceAnswersTheWaveformItWasGiven()
    {
        LFOControlSource source = LFOControlSource.New();

        source.Waveform = LFOWaveform.Sine;
        source.Frequency = 2.0;
        source.Amplitude = 0.5;
        source.Offset = 0.5;
        source.Timeshift = ClockTime.Zero;

        Assert.Equal(LFOWaveform.Sine, source.Waveform);
        Assert.Equal(2.0, source.Frequency, 6);
        Assert.Equal(0.5, source.Amplitude, 6);
        Assert.Equal(0.5, source.Offset, 6);
        Assert.Equal(ClockTime.Zero, source.Timeshift);

        // The period is half a second, so the quarters are at 125 ms.
        Assert.Equal(0.5, ValueAt(source, ClockTime.Zero), 6);
        Assert.Equal(1.0, ValueAt(source, ClockTime.FromMilliseconds(125)), 6);
        Assert.Equal(0.5, ValueAt(source, ClockTime.FromMilliseconds(250)), 6);
        Assert.Equal(0.0, ValueAt(source, ClockTime.FromMilliseconds(375)), 6);
        Assert.Equal(0.5, ValueAt(source, ClockTime.FromMilliseconds(500)), 6);
    }

    /// <summary>
    /// The waveform is a wave and not a curve through points, so it is defined
    /// before, between and after everything, and the shape is what the waveform
    /// says: a square holds one half of the period below the offset and the
    /// other above it.
    /// </summary>
    [Fact]
    public void AnLFOControlSourceIsDefinedEverywhereAndFollowsItsWaveform()
    {
        LFOControlSource source = LFOControlSource.New();
        source.Waveform = LFOWaveform.Square;
        source.Frequency = 1.0;
        source.Amplitude = 0.5;
        source.Offset = 0.5;

        Assert.Equal(LFOWaveform.Square, source.Waveform);

        Assert.Equal(0.0, ValueAt(source, ClockTime.Zero), 6);
        Assert.Equal(0.0, ValueAt(source, ClockTime.FromMilliseconds(499)), 6);
        Assert.Equal(1.0, ValueAt(source, ClockTime.FromMilliseconds(500)), 6);
        Assert.Equal(1.0, ValueAt(source, ClockTime.FromMilliseconds(999)), 6);

        // The next period is the same one again, hours later as much as
        // milliseconds later.
        Assert.Equal(0.0, ValueAt(source, ClockTime.FromSeconds(3600)), 6);
    }

    /// <summary>
    /// The timeshift moves the waveform to the right: the value at the shift is
    /// the value the unshifted wave had at zero, and zero itself is a quarter
    /// period back into the previous period.
    /// </summary>
    [Fact]
    public void TheTimeshiftMovesTheWaveformToTheRight()
    {
        LFOControlSource source = LFOControlSource.New();
        source.Waveform = LFOWaveform.Sine;
        source.Frequency = 2.0;
        source.Amplitude = 0.5;
        source.Offset = 0.5;
        source.Timeshift = ClockTime.FromMilliseconds(125);

        Assert.Equal(ClockTime.FromMilliseconds(125), source.Timeshift);

        // What used to be at 0 is now at 125 ms, and what is at 0 now is what
        // the unshifted wave answered a quarter period earlier: the bottom.
        Assert.Equal(0.5, ValueAt(source, ClockTime.FromMilliseconds(125)), 6);
        Assert.Equal(1.0, ValueAt(source, ClockTime.FromMilliseconds(250)), 6);
        Assert.Equal(0.0, ValueAt(source, ClockTime.Zero), 6);
    }

    /// <summary>
    /// The acceptance for the LFO source: an absolute direct binding walks a
    /// real property of a real element along the wave. The binding takes any
    /// <see cref="Gst.ControlSource"/>, which is what an
    /// <see cref="LFOControlSource"/> is — it does not derive from
    /// <see cref="TimedValueControlSource"/>, because the native type does not
    /// either.
    /// </summary>
    [Fact]
    public void AnLFOControlSourceDrivesABoundProperty()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "oscillating"));

        LFOControlSource source = LFOControlSource.New();
        source.Waveform = LFOWaveform.Sine;
        source.Frequency = 2.0;
        source.Amplitude = 0.5;
        source.Offset = 0.5;

        Gst.ControlSource asControlSource = source;
        Assert.True(element.AddControlBinding(
            DirectControlBinding.NewAbsolute(element, "volume", asControlSource)));

        Assert.Equal(0.5, VolumeAt(element, ClockTime.Zero), 6);
        Assert.Equal(1.0, VolumeAt(element, ClockTime.FromMilliseconds(125)), 6);
        Assert.Equal(0.5, VolumeAt(element, ClockTime.FromMilliseconds(250)), 6);
        Assert.Equal(0.0, VolumeAt(element, ClockTime.FromMilliseconds(375)), 6);
    }

    /// <summary>
    /// The trigger source answers at its control points and nowhere else: a
    /// timestamp between two of them has no value at all, which is what makes it
    /// a source of one-shot events rather than of a curve.
    /// </summary>
    [Fact]
    public void ATriggerControlSourceAnswersOnlyAtItsControlPoints()
    {
        TriggerControlSource source = TriggerControlSource.New();

        Assert.Equal(0L, source.Tolerance);
        Assert.True(source.Set(ClockTime.FromMilliseconds(100), 0.25));
        Assert.True(source.Set(ClockTime.FromSeconds(1), 0.75));
        Assert.Equal(2, source.Count);

        Assert.True(source.TryGetValue(ClockTime.FromMilliseconds(100), out double first));
        Assert.Equal(0.25, first, 6);
        Assert.True(source.TryGetValue(ClockTime.FromSeconds(1), out double second));
        Assert.Equal(0.75, second, 6);

        // One nanosecond off is off, with the tolerance at zero.
        Assert.False(source.TryGetValue(ClockTime.FromNanoseconds(100_000_001), out _));
        Assert.False(source.TryGetValue(ClockTime.FromMilliseconds(500), out _));

        // Before the first control point there is nothing to trigger.
        Assert.False(source.TryGetValue(ClockTime.Zero, out _));

        // A trigger is a timed value source, so the inherited surface is the
        // same one an interpolation source has.
        Assert.True(source.Unset(ClockTime.FromSeconds(1)));
        Assert.Equal(1, source.Count);
        source.UnsetAll();
        Assert.Equal(0, source.Count);
        Assert.False(source.TryGetValue(ClockTime.FromMilliseconds(100), out _));
    }

    /// <summary>
    /// The tolerance widens each control point into a window, and the window
    /// reaches forward as well as back: a timestamp shortly <em>before</em> a
    /// control point is answered with the value of that coming point, so a
    /// trigger fires early rather than late. A timestamp before the first
    /// control point is never answered, however close it is, because the search
    /// starts at the last point at or before the timestamp and there is none.
    /// </summary>
    [Fact]
    public void TheToleranceOfATriggerFiresEarlyAndNeverBeforeTheFirstPoint()
    {
        TriggerControlSource source = TriggerControlSource.New();

        source.Tolerance = (long)ClockTime.FromMilliseconds(10).Nanoseconds;
        Assert.Equal(10_000_000L, source.Tolerance);

        Assert.True(source.Set(ClockTime.FromMilliseconds(100), 0.25));
        Assert.True(source.Set(ClockTime.FromSeconds(1), 0.75));

        // Just after a control point: that point.
        Assert.True(source.TryGetValue(ClockTime.FromMilliseconds(105), out double late));
        Assert.Equal(0.25, late, 6);

        // Just before the next one: the next one, not the previous.
        Assert.True(source.TryGetValue(ClockTime.FromMilliseconds(995), out double early));
        Assert.Equal(0.75, early, 6);

        // Outside every window there is still nothing.
        Assert.False(source.TryGetValue(ClockTime.FromMilliseconds(500), out _));

        // And the window of the first control point does not reach backwards
        // past itself, tolerance or no tolerance.
        Assert.False(source.TryGetValue(ClockTime.FromMilliseconds(95), out _));
    }

    /// <summary>
    /// A property bound to a trigger source moves at the control points and is
    /// left completely alone between them — the binding reports that it had
    /// nothing to write, and the property keeps what it last got.
    /// </summary>
    [Fact]
    public void ATriggerLeavesABoundPropertyAloneBetweenItsControlPoints()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "triggered"));

        TriggerControlSource source = TriggerControlSource.New();
        Assert.True(source.Set(ClockTime.FromMilliseconds(100), 0.25));
        Assert.True(source.Set(ClockTime.FromSeconds(1), 0.75));

        Assert.True(element.AddControlBinding(DirectControlBinding.NewAbsolute(element, "volume", source)));

        Assert.Equal(0.25, VolumeAt(element, ClockTime.FromMilliseconds(100)), 6);

        // No value at 500 ms: the synchronisation answers false and the volume
        // stays where the last trigger put it.
        Assert.False(element.SyncValues(ClockTime.FromMilliseconds(500)));
        Assert.Equal(0.25, element.GetProperty<double>("volume"), 6);

        Assert.Equal(0.75, VolumeAt(element, ClockTime.FromSeconds(1)), 6);
    }

    /// <summary>
    /// The ARGB binding drives one packed <c>guint</c> property from four
    /// sources, one per channel: each value is clamped to <c>[0, 1]</c>, scaled
    /// to a byte and truncated, and the four bytes are packed alpha first.
    /// </summary>
    [Fact]
    public void AnArgbBindingPacksFourSourcesIntoOneColour()
    {
        using Element element =
            Assert.IsAssignableFrom<Element>(ElementFactory.Make("videotestsrc", "coloured"));

        Gst.ControlBinding binding = ARGBControlBinding.New(
            element,
            "foreground-color",
            Constant(1.0),
            Constant(0.75),
            Constant(0.5),
            Constant(0.25));

        Assert.True(element.AddControlBinding(binding));
        Assert.Same(binding, element.GetControlBinding("foreground-color"));

        Assert.True(element.SyncValues(ClockTime.Zero));

        // 1.0 -> 255, 0.75 -> 191, 0.5 -> 127 (127.5 truncated), 0.25 -> 63.
        Assert.Equal(0xFFBF7F3Fu, element.GetProperty<uint>("foreground-color"));
    }

    /// <summary>
    /// A proxy binding has no control source: it looks the control binding of
    /// another property of another object up, by name and on every request, and
    /// forwards to it.
    /// </summary>
    /// <remarks>
    /// <b>The forwarding includes the object that is written.</b> The reference
    /// binding is invoked with the reference object, so synchronising the
    /// proxying element moves the property of the referenced element and leaves
    /// its own alone. That is what <c>gst_proxy_control_binding_sync_values</c>
    /// does, and it is what makes the binding useful to an element that
    /// re-exports a property of something it owns: controlling either end ends
    /// up in the same place.
    /// </remarks>
    [Fact]
    public void AProxyBindingForwardsToTheBindingOfTheReferenceObject()
    {
        using Element referenced = Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "referenced"));
        using Element proxying = Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "proxying"));

        InterpolationControlSource source = InterpolationControlSource.New();
        source.Mode = InterpolationMode.Linear;
        Assert.True(source.Set(ClockTime.Zero, 0.2));
        Assert.True(source.Set(ClockTime.FromSeconds(1), 0.8));
        Assert.True(referenced.AddControlBinding(
            DirectControlBinding.NewAbsolute(referenced, "volume", source)));

        Gst.ControlBinding proxy = ProxyControlBinding.New(proxying, "volume", referenced, "volume");
        Assert.True(proxying.AddControlBinding(proxy));

        // Both elements start at the default volume of the element.
        Assert.Equal(1.0, proxying.GetProperty<double>("volume"), 6);
        Assert.Equal(1.0, referenced.GetProperty<double>("volume"), 6);

        Assert.True(proxying.SyncValues(ClockTime.FromMilliseconds(500)));

        // The referenced element is the one that moved.
        Assert.Equal(0.5, referenced.GetProperty<double>("volume"), 6);
        Assert.Equal(1.0, proxying.GetProperty<double>("volume"), 6);

        GC.KeepAlive(referenced);
    }

    /// <summary>
    /// A proxy binding that finds no binding behind it does nothing and says so
    /// quietly: the reference object is held weakly and looked up per request,
    /// so an object without a binding under that name is not an error.
    /// </summary>
    [Fact]
    public void AProxyBindingWithoutABindingBehindItDoesNothing()
    {
        using Element referenced = Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "unbound"));
        using Element proxying = Assert.IsAssignableFrom<Element>(ElementFactory.Make("volume", "proxying-nothing"));

        Assert.True(proxying.AddControlBinding(
            ProxyControlBinding.New(proxying, "volume", referenced, "volume")));

        Assert.True(proxying.SyncValues(ClockTime.FromMilliseconds(500)));

        Assert.Equal(1.0, proxying.GetProperty<double>("volume"), 6);
        Assert.Equal(1.0, referenced.GetProperty<double>("volume"), 6);
    }

    /// <summary>
    /// Every control source of the module has an entry in the type table, so the
    /// registry hands its own wrapper back, and the hierarchy of the wrappers is
    /// the hierarchy of the native types.
    /// </summary>
    [Fact]
    public void TheRegistryBuildsTheWrapperOfEveryControlSourceOfTheModule()
    {
        LFOControlSource lfo = LFOControlSource.New();
        TriggerControlSource trigger = TriggerControlSource.New();

        Assert.IsType<LFOControlSource>(lfo);
        Assert.IsType<TriggerControlSource>(trigger);
        Assert.Same(lfo, Object.FromNative(lfo.Handle, Transfer.None));
        Assert.Same(trigger, Object.FromNative(trigger.Handle, Transfer.None));

        // GstLFOControlSource derives straight from GstControlSource;
        // GstTriggerControlSource derives from GstTimedValueControlSource.
        Assert.IsAssignableFrom<Gst.ControlSource>(lfo);
        Assert.IsNotAssignableFrom<TimedValueControlSource>(lfo);
        Assert.IsAssignableFrom<TimedValueControlSource>(trigger);
    }

    /// <summary>
    /// Builds a control source that answers the same value at every timestamp
    /// from zero on, which is what a channel of an ARGB binding needs when it
    /// should simply hold a colour.
    /// </summary>
    /// <param name="value">The value the source answers.</param>
    /// <returns>The control source.</returns>
    private static InterpolationControlSource Constant(double value)
    {
        InterpolationControlSource source = InterpolationControlSource.New();

        // The default mode holds the value of the last control point, so one
        // control point at zero is a constant.
        Assert.True(source.Set(ClockTime.Zero, value));
        return source;
    }

    /// <summary>
    /// Reads a control source at one timestamp and fails the test when it has no
    /// value there.
    /// </summary>
    /// <param name="source">The source to evaluate.</param>
    /// <param name="time">The timestamp to evaluate at.</param>
    /// <returns>The raw value of the source.</returns>
    private static double ValueAt(LFOControlSource source, ClockTime time)
    {
        Assert.True(source.TryGetValue(time, out double value));
        return value;
    }

    /// <summary>
    /// Synchronises the controlled properties of an element to one timestamp
    /// and reads the one under test back.
    /// </summary>
    /// <param name="element">The element whose <c>volume</c> is bound.</param>
    /// <param name="time">The stream time to evaluate the binding at.</param>
    /// <returns>The value the binding wrote.</returns>
    private static double VolumeAt(Element element, ClockTime time)
    {
        Assert.True(element.SyncValues(time));

        using Value value = element.GetProperty("volume");
        return value.GetDouble();
    }
}
