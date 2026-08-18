extern alias gstsharp;

using Gst;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The three states of <see cref="GstSharpOptions.SkipNativeInit"/> and the
/// conflict rule that reads them.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here initialises anything: the rule is a comparison of two booleans,
/// which is why it can be lifted out of <c>GstSharp.Initialize</c> and measured
/// in the native free suite. What it stands in for is a second
/// <c>GstSharp.Initialize</c> on a process whose first call skipped
/// <c>gst_init</c>.
/// </para>
/// <para>
/// Every other option says "no preference" with a <see langword="null"/>. A
/// <see cref="bool"/> has none, so a fresh options object handed to a second
/// call used to read as a demand that <c>gst_init</c> run after all, and the
/// call failed for a preference its caller never expressed.
/// </para>
/// </remarks>
public class GstSharpOptionsTests
{
    [Fact]
    public void AFreshObjectStatesNoPreference()
    {
        GstSharpOptions options = new();

        Assert.False(options.SkipNativeInit);
        Assert.False(options.SkipNativeInitSpecified);
    }

    [Fact]
    public void AssigningTheDefaultStillStatesAPreference()
    {
        GstSharpOptions options = new() { SkipNativeInit = false };

        Assert.False(options.SkipNativeInit);
        Assert.True(options.SkipNativeInitSpecified);
    }

    [Fact]
    public void AssigningTrueStatesAPreference()
    {
        GstSharpOptions options = new() { SkipNativeInit = true };

        Assert.True(options.SkipNativeInit);
        Assert.True(options.SkipNativeInitSpecified);
    }

    /// <summary>
    /// A second call that only wants to state another option hands in a fresh
    /// object, which says nothing about <c>gst_init</c> and must not conflict.
    /// </summary>
    [Fact]
    public void FreshOptionsDoNotConflictWithASkippedInitialisation()
    {
        Assert.False(
            gstsharp::GstSharp.ConflictsWithAppliedSkipNativeInit(new GstSharpOptions(), applied: true));
    }

    /// <summary>
    /// Asking for <c>gst_init</c> after it was suppressed is the conflict the
    /// check exists for: the native libraries are loaded and nothing can undo
    /// that the initialisation was skipped.
    /// </summary>
    [Fact]
    public void AskingForTheInitialisationThatWasSkippedConflicts()
    {
        Assert.True(
            gstsharp::GstSharp.ConflictsWithAppliedSkipNativeInit(
                new GstSharpOptions { SkipNativeInit = false },
                applied: true));
    }

    [Fact]
    public void AskingForWhatWasDoneDoesNotConflict()
    {
        Assert.False(
            gstsharp::GstSharp.ConflictsWithAppliedSkipNativeInit(
                new GstSharpOptions { SkipNativeInit = true },
                applied: true));
    }

    /// <summary>
    /// Nothing conflicts with a first call that did initialise GStreamer:
    /// asking for it again is asking for what was done, and asking to skip it
    /// is a preference about a step that is already behind the process.
    /// </summary>
    [Fact]
    public void NothingConflictsWithAnInitialisationThatRan()
    {
        Assert.False(
            gstsharp::GstSharp.ConflictsWithAppliedSkipNativeInit(new GstSharpOptions(), applied: false));
        Assert.False(
            gstsharp::GstSharp.ConflictsWithAppliedSkipNativeInit(
                new GstSharpOptions { SkipNativeInit = false },
                applied: false));
        Assert.False(
            gstsharp::GstSharp.ConflictsWithAppliedSkipNativeInit(
                new GstSharpOptions { SkipNativeInit = true },
                applied: false));
    }
}
