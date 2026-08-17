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
