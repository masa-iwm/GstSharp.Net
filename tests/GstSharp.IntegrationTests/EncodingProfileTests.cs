using Gst;
using Gst.Pbutils;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <see cref="EncodingContainerProfile.AddProfile"/>, the first call of the
/// binding that takes a <c>GObject</c> over.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_encoding_container_profile_add_profile</c> is
/// <c>transfer-ownership="full"</c> in its profile parameter — "no copy of
/// @profile will be made" — so the binding hands it a reference of its own and
/// disposes the wrapper afterwards, the way the mini object senders do. What
/// differs is the reach of that disposal: a GObject wrapper is interned, so the
/// profile has no wrapper anywhere once the call returns and
/// <see cref="EncodingContainerProfile.GetProfiles"/> is the way back to it.
/// Both halves of that are pinned here.
/// </para>
/// <para>
/// A container profile that is never filled is what an application would have
/// been left with: the whole point of the type is the list of stream profiles
/// it holds, and <c>encodebin</c> reads nothing else.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class EncodingProfileTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public EncodingProfileTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A stream profile that is added is held by the container, and the wrapper
    /// that was handed over owns nothing afterwards.
    /// </summary>
    [Fact]
    public void AContainerTakesTheStreamProfileOver()
    {
        using EncodingContainerProfile container = NewContainer("takes-over");

        EncodingAudioProfile audio = NewAudio();

        Assert.True(container.AddProfile(audio));

        // What the caller had is gone: the call consumed the reference of the
        // wrapper, so the wrapper is disposed and its handle is unreachable.
        Assert.True(audio.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => audio.Handle);

        // Disposing twice does nothing, so a `using` around a profile that ends
        // up being added stays correct.
        audio.Dispose();

        IReadOnlyList<EncodingProfile> profiles = container.GetProfiles();

        Assert.Single(profiles);

        // The way back to the profile is the container, which hands out a fresh
        // wrapper for the object the disposed one stood for.
        EncodingProfile held = profiles[0];

        Assert.False(held.IsDisposed);
        Assert.True(container.ContainsProfile(held));

        using Caps format = held.GetFormat();

        _output.WriteLine($"{container.GetName()} holds {format}");
    }

    /// <summary>
    /// The container does not deduplicate: the very profile it already holds,
    /// handed to it again, is taken again — and that wrapper is spent too.
    /// </summary>
    /// <remarks>
    /// This is the second half of the ownership claim, and the half that a
    /// missing <c>g_object_ref</c> would show up in: the container ends up
    /// holding the same object twice and owning two references to it, so a
    /// binding that had handed over the wrapper's own reference would leave the
    /// list pointing at an object that the disposal of the wrapper released.
    /// </remarks>
    [Fact]
    public void AProfileAddedTwiceIsHeldTwiceAndSpentTwice()
    {
        using EncodingContainerProfile container = NewContainer("twice");

        Assert.True(container.AddProfile(NewAudio()));

        // The very object the container already holds, handed to it again.
        EncodingProfile held = container.GetProfiles()[0];

        Assert.True(container.AddProfile(held));
        Assert.True(held.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => held.Handle);

        IReadOnlyList<EncodingProfile> profiles = container.GetProfiles();

        Assert.Equal(2, profiles.Count);
        Assert.Same(profiles[0], profiles[1]);

        _output.WriteLine(
            FormattableString.Invariant(
                $"the container holds {profiles.Count} entries for one profile"));
    }

    /// <summary>
    /// The guards of the hand written call: a null profile is refused, and so
    /// is one whose wrapper is already spent.
    /// </summary>
    [Fact]
    public void AContainerRefusesWhatItCannotTake()
    {
        using EncodingContainerProfile container = NewContainer("guarded");

        Assert.Throws<ArgumentNullException>(() => container.AddProfile(null!));

        EncodingAudioProfile spent = NewAudio();
        spent.Dispose();

        Assert.Throws<ObjectDisposedException>(() => container.AddProfile(spent));
        Assert.Empty(container.GetProfiles());
    }

    /// <summary>
    /// Builds an empty container profile for an Ogg stream.
    /// </summary>
    /// <param name="name">The name of the profile.</param>
    /// <returns>The profile, which the caller owns.</returns>
    private static EncodingContainerProfile NewContainer(string name)
    {
        using Caps? format = Caps.FromString("application/ogg");

        Assert.NotNull(format);

        return EncodingContainerProfile.New(name, "A profile built by the test suite", format, null);
    }

    /// <summary>
    /// Builds a stream profile for a Vorbis track.
    /// </summary>
    /// <returns>The profile, which the caller hands over.</returns>
    private static EncodingAudioProfile NewAudio()
    {
        using Caps? format = Caps.FromString("audio/x-vorbis");

        Assert.NotNull(format);

        return EncodingAudioProfile.New(format, null, null, 0);
    }
}
