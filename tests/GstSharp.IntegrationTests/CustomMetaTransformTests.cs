using System.Runtime.CompilerServices;
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

    /// <summary>
    /// A registration the library refuses releases the state of the transform
    /// function that was allocated for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refusal is a name that is already registered: the first call takes
    /// the implementation name and the API type derived from it, and
    /// <c>gst_meta_api_type_register</c> answers <c>G_TYPE_INVALID</c> for the
    /// second, which leaves through the exit that answers nothing without ever
    /// having stored the destroy notification of the transform function.
    /// </para>
    /// <para>
    /// <b>This test is noisy on purpose.</b> Registering an existing GType name
    /// makes GLib print a warning, and the invalid type it then answers makes
    /// the tag annotation that follows print a second one. Both are the library
    /// reporting the refusal this test is about; neither ends the process, and
    /// the assertions below are what the test measures.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARefusedCustomMetaRegistrationReleasesTheStateOfItsTransform()
    {
        const string Name = "GstSharpDuplicateRegistrationMeta";

        // The name and the API type derived from it are taken from here on, and
        // a custom meta registration lives for the rest of the process.
        Assert.NotNull(Meta.RegisterCustomSimple(Name));

        WeakReference state = RegisterOverAnExistingName(Name);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(state.IsAlive);
    }

    /// <summary>
    /// Registers a custom meta under a name that is already taken and reports
    /// what is left over.
    /// </summary>
    /// <param name="name">The name that is already registered.</param>
    /// <returns>A weak reference to the transform function.</returns>
    /// <remarks>
    /// The transform is created in a frame of its own and captures a local, so
    /// that neither the test frame nor the delegate cache of the compiler keeps
    /// it alive when the collection above decides whether anything else did.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterOverAnExistingName(string name)
    {
        int calls = 0;
        CustomMetaTransformFunction transform = (transbuf, meta, source, type, data) =>
        {
            calls++;
            return false;
        };

        WeakReference weak = new(transform);

        Assert.Throws<InvalidOperationException>(
            () => Meta.RegisterCustom(name, ["memory"], transform));

        Assert.Equal(0, calls);
        return weak;
    }
}
