using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The detach of a lent opaque wrapper, without a library to lend one: the
/// wrapper of an opaque record holds nothing but a pointer, so the state the
/// detach leaves behind can be asserted on a wrapper a test built itself.
/// </summary>
public sealed class LentOpaqueRecordTests
{
    /// <summary>
    /// A wrapper the trampoline detached says so on every member, with the
    /// exception the borrowed mini objects answer, rather than reading an
    /// address that means nothing after the call.
    /// </summary>
    [Fact]
    public void ADetachedWrapperThrowsOnEveryMember()
    {
        Gst.Meta item = new(0x1000);

        item.Detach();

        _ = Assert.Throws<ObjectDisposedException>(() => item.RequireHandle());
        _ = Assert.Throws<ObjectDisposedException>(() => item.Info);
    }

    /// <summary>
    /// Before the detach the wrapper answers the pointer it was built with, so
    /// what the test above measures is the detach and not a wrapper that never
    /// held anything.
    /// </summary>
    [Fact]
    public void AWrapperAnswersItsPointerUntilItIsDetached()
    {
        Gst.Meta item = new(0x1000);

        Assert.Equal(0x1000, item.RequireHandle());
    }
}
