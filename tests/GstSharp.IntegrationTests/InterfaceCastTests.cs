using System;
using Gst;
using Gst.Audio;
using Gst.GObject;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <see cref="Object.As{T}"/> against the installed library: an element whose
/// wrapper class says nothing about the GObject interfaces of its native type
/// is still usable through them.
/// </summary>
/// <remarks>
/// The <c>volume</c> element implements <c>GstStreamVolume</c>, while its
/// wrapper is the closest registered ancestor the binding knows
/// (<c>GstAudioFilter</c>), so nothing but the run time type check can find the
/// interface. <c>GstAudio.Initialize()</c> has to run first: an interface of a
/// module that was never initialised is not registered.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class InterfaceCastTests
{
    /// <summary>An interface that no module registers.</summary>
    private interface IUnregistered
    {
    }

    [RequiresElementFact("volume")]
    public void AVolumeElementIsAStreamVolume()
    {
        GstAudio.Initialize();
        using Element element = ElementFactory.Make("volume", null)!;

        IStreamVolume? volume = element.As<IStreamVolume>();

        Assert.NotNull(volume);
        Assert.Equal(1.0, volume.GetVolume(StreamVolumeFormat.Linear), 6);
        Assert.False(volume.GetMute());
        volume.SetMute(true);
        Assert.True(volume.GetMute());
    }

    [RequiresElementFact("volume")]
    public void TheViewSharesTheHandleOfTheWrapperAndItsLifetime()
    {
        GstAudio.Initialize();
        Element element = ElementFactory.Make("volume", null)!;
        IStreamVolume volume = element.As<IStreamVolume>()!;

        Assert.Equal(element.Handle, volume.Handle);

        element.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = volume.Handle);
    }

    [RequiresElementFact("capsfilter")]
    public void AnElementThatDoesNotImplementTheInterfaceIsNull()
    {
        GstAudio.Initialize();
        using Element element = ElementFactory.Make("capsfilter", null)!;

        Assert.Null(element.As<IStreamVolume>());
    }

    [Fact]
    public void AWrapperThatDeclaresTheInterfaceAnswersOnItsOwn()
    {
        using Bin bin = Bin.New("cast-bin");

        Assert.Same(bin, bin.As<IChildProxy>());
    }

    [Fact]
    public void AClassArgumentIsTheCastOfTheWrapperItself()
    {
        using Bin bin = Bin.New("cast-bin");

        Assert.Same(bin, bin.As<Element>());
        Assert.Null(bin.As<Pipeline>());
    }

    [Fact]
    public void AnInterfaceOfNoInitialisedModuleThrows()
    {
        using Bin bin = Bin.New("cast-bin");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => bin.As<IUnregistered>());

        Assert.Contains("Initialize", error.Message, StringComparison.Ordinal);
    }
}
