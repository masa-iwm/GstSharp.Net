using Gst;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The nullable callback parameter against the running library.
/// <c>gst_meta_register_custom</c> annotates its <c>transform_func</c>
/// <c>(nullable)</c> and documents what it does without one: "When
/// @transform_func is %NULL, the meta and its backing #GstStructure will always
/// be copied when the transform operation is copy, other operations are
/// discarded, copy regions are ignored."
/// </summary>
/// <remarks>
/// <para>
/// The fallback lives in the private <c>custom_transform_func</c> of
/// <c>gstmeta.c</c>, which branches on the stored function pointer and copies
/// the structure when there is none. That branch is the reason the binding has
/// to hand the library the null pointer rather than a trampoline with no
/// delegate behind it: a trampoline would be a function pointer, the branch
/// would take it, and the trampoline would find no state and answer false —
/// which is a meta that is silently dropped on every copy instead of one that
/// is carried along.
/// </para>
/// <para>
/// The two tests are the two sides of that branch, and each registers a name of
/// its own, because a custom meta registration is process global and lives for
/// the rest of the run.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class CustomMetaTransformTests
{
    /// <summary>
    /// A registration with no transform is accepted, and the meta it describes
    /// survives a buffer copy through the fallback of the library.
    /// </summary>
    [Fact]
    public void ACustomMetaRegisteredWithoutATransformIsCopiedWithTheBuffer()
    {
        const string Name = "GstSharpNullTransformMeta";

        MetaInfo info = Meta.RegisterCustom(Name, ["memory"], null);

        Assert.NotNull(info);

        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));

        Assert.NotNull(buffer.AddCustomMeta(Name));

        using Buffer copy = Assert.IsType<Buffer>(buffer.Copy());

        // The fallback ran: the copy carries the meta, which is what the
        // library does only when the stored function pointer is NULL.
        Assert.NotNull(copy.GetCustomMeta(Name));
    }

    /// <summary>
    /// The other side of the same branch: a transform that is given is the one
    /// the library calls, and its answer decides whether the copy carries the
    /// meta.
    /// </summary>
    [Fact]
    public void ACustomMetaTransformThatWasGivenIsTheOneTheLibraryCalls()
    {
        const string Name = "GstSharpRefusingTransformMeta";
        int calls = 0;

        MetaInfo info = Meta.RegisterCustom(
            Name,
            ["memory"],
            (transbuf, meta, source, type, data) =>
            {
                calls++;
                return false;
            });

        Assert.NotNull(info);

        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));

        Assert.NotNull(buffer.AddCustomMeta(Name));

        using Buffer copy = Assert.IsType<Buffer>(buffer.Copy());

        Assert.Equal(1, calls);
        Assert.Null(copy.GetCustomMeta(Name));
    }
}
