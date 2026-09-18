using Gst;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The ordering question <see cref="Version"/> answers, which is pure
/// arithmetic on the four parts and needs no native library.
/// </summary>
/// <remarks>
/// <see cref="Version.IsAtLeast(uint, uint, uint)"/> is what a runtime gate of
/// the binding asks — "does the loaded library carry the fix this member needs"
/// — so what matters is that it compares the three released parts
/// lexicographically rather than one part at a time, and that the nano version,
/// which says whether a release is a release at all, takes no part in it.
/// </remarks>
public sealed class VersionTests
{
    [Fact]
    public void AVersionIsAtLeastItself()
    {
        Assert.True(new Gst.Version(1, 26, 10, 0).IsAtLeast(1, 26, 10));
    }

    [Fact]
    public void ALowerMicroOfTheSameMinorIsNotAtLeast()
    {
        Assert.False(new Gst.Version(1, 26, 9, 0).IsAtLeast(1, 26, 10));
    }

    [Fact]
    public void AHigherMinorWithALowerMicroIsAtLeast()
    {
        // The comparison is lexicographic, not per part: 1.27.1 is newer than
        // 1.26.10 although its micro version is the smaller number.
        Assert.True(new Gst.Version(1, 27, 1, 0).IsAtLeast(1, 26, 10));
    }

    [Fact]
    public void ALowerMinorWithAHigherMicroIsNotAtLeast()
    {
        Assert.False(new Gst.Version(1, 26, 11, 0).IsAtLeast(1, 27, 50));
    }

    [Fact]
    public void ALowerMajorIsNotAtLeast()
    {
        Assert.False(new Gst.Version(0, 99, 99, 0).IsAtLeast(1, 0, 0));
    }

    [Fact]
    public void AHigherMajorIsAtLeastWhateverTheOtherPartsSay()
    {
        Assert.True(new Gst.Version(2, 0, 0, 0).IsAtLeast(1, 27, 50));
    }

    [Fact]
    public void TheNanoVersionTakesNoPartInTheComparison()
    {
        // A git build of 1.27.1 and a prerelease of it answer as the release
        // does, which is what GST_CHECK_VERSION answers at compile time.
        Assert.True(new Gst.Version(1, 27, 1, 1).IsAtLeast(1, 27, 1));
        Assert.True(new Gst.Version(1, 27, 1, 2).IsAtLeast(1, 27, 1));
        Assert.False(new Gst.Version(1, 27, 1, 1).IsAtLeast(1, 27, 2));
    }

    [Fact]
    public void TheMicroVersionDefaultsToZero()
    {
        Assert.True(new Gst.Version(1, 28, 0, 0).IsAtLeast(1, 28));
        Assert.True(new Gst.Version(1, 28, 6, 0).IsAtLeast(1, 28));
        Assert.False(new Gst.Version(1, 26, 11, 0).IsAtLeast(1, 28));
    }

    [Fact]
    public void TheGateOfAddStreamReadsTheSameOnEveryReleaseItWasWrittenFor()
    {
        // The truth table of the hand rolled comparison this replaced, kept
        // here because it is the one gate of the binding that a wrong answer
        // would turn into a use after free: the reference is present from
        // 1.26.10 on the 1.26 branch and from 1.27.50 on.
        Assert.False(AddStreamReturnsItsOwnReference(new Gst.Version(1, 24, 13, 0)));
        Assert.False(AddStreamReturnsItsOwnReference(new Gst.Version(1, 26, 9, 0)));
        Assert.True(AddStreamReturnsItsOwnReference(new Gst.Version(1, 26, 10, 0)));
        Assert.False(AddStreamReturnsItsOwnReference(new Gst.Version(1, 27, 1, 0)));
        Assert.True(AddStreamReturnsItsOwnReference(new Gst.Version(1, 27, 50, 0)));
        Assert.True(AddStreamReturnsItsOwnReference(new Gst.Version(1, 28, 0, 0)));
    }

    private static bool AddStreamReturnsItsOwnReference(Gst.Version version) =>
        version.IsAtLeast(1, 27, 50)
            || (version.Major == 1 && version.Minor == 26 && version.IsAtLeast(1, 26, 10));
}
