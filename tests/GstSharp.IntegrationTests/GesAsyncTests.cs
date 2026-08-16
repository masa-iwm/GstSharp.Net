using GES;
using Gst.GLib;
using Gst.GObject;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The asynchronous asset surface of the editing services, and through it the
/// Gio async machinery of the runtime: the dispatcher, the shared
/// <c>GAsyncReadyCallback</c> bridge, the error mapping and the cancellation
/// bridge.
/// </summary>
/// <remarks>
/// <para>
/// The load bearing fact about this class is what it does <em>not</em> do:
/// nothing here builds a <c>GMainLoop</c>, iterates a <c>GMainContext</c>, or
/// installs a synchronization context, and the collection runs its tests one at
/// a time so nothing else does either. A <c>GTask</c> delivers its callback in
/// an iteration of the context that was thread default when it was built, so
/// without the dispatcher of the binding every task below would stay
/// incomplete forever. Every wait therefore carries
/// <see cref="Patience"/>: a dispatcher that never delivers has to fail the
/// suite rather than hang it.
/// </para>
/// <para>
/// The two entry points also split the risk cleanly. The asset request is pure
/// <c>GTask</c>, so it measures the capture and the delivery on their own; the
/// URI asset adds the discoverer of the editing services on top, which runs a
/// pipeline and has a main context story of its own.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesAsyncTests
{
    /// <summary>
    /// How long a request may take before the suite calls it a hang. Every
    /// operation below either resolves out of the asset cache or fails on a
    /// file that is not there, so the real times are milliseconds; the margin
    /// is for a loaded CI machine.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The success path, and the whole point of the dispatcher: a request
    /// completes in a process that iterates no main context of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GESTestClip</c> is the type the rest of the GES suite is built on
    /// because it needs nothing on disk, and the extractable type the answer
    /// comes back carrying is what says the answer is the one that was asked
    /// for.
    /// </para>
    /// <para>
    /// The identifier is spelled out rather than left <see langword="null"/>.
    /// It is the same identifier the editing services would derive from the
    /// type, but <c>ges_asset_request_async</c> in 1.28.6 crashes on a
    /// <c>NULL</c> identifier once the asset is in its cache — see
    /// <see cref="Asset.RequestAsync"/>, which documents the hazard.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAssetRequestCompletesWithNoMainLoopInTheProcess()
    {
        GstGES.Initialize();

        // Naming the type is what registers it; asking for it by name before
        // anything instantiated one would find nothing.
        using (TestClip? clip = TestClip.New())
        {
            Assert.NotNull(clip);
        }

        GType testClip = GType.FromName("GESTestClip");
        Assert.True(testClip.IsValid);

        // The calling thread has pushed no context, which is the situation the
        // dispatcher exists for.
        Assert.Null(MainContext.ThreadDefault);

        Task<Asset> request = Asset.RequestAsync(testClip, "GESTestClip");

        // And the binding did not commandeer the calling thread to get its
        // context in place: the request went to a thread of its own.
        Assert.Null(MainContext.ThreadDefault);

        await Settled(request);

        using Asset asset = await request;

        Assert.Equal(testClip, asset.ExtractableType);
        Assert.Equal("GESTestClip", asset.Id);

        // The state graph of the request is unreachable now. Collecting it must
        // not take the dispatcher, the callback protocol or the wrapper table
        // with it.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Gst.GObject.Object.DrainPendingReleases();
    }

    /// <summary>
    /// The failure path: a URI that resolves to nothing faults the task with
    /// the error the library reported, rather than hanging or cancelling.
    /// </summary>
    /// <remarks>
    /// The name carries a fresh identifier so that the asset cache of the
    /// editing services cannot answer it out of an earlier run of this test in
    /// the same process.
    /// </remarks>
    [Fact]
    public async Task ARequestForAFileThatIsNotThereFaultsWithTheLibraryError()
    {
        GstGES.Initialize();

        string uri = $"file:///nonexistent-{Guid.NewGuid():N}.mp4";

        Task<UriClipAsset> request = UriClipAsset.NewAsync(uri);

        await Settled(request);

        GException error = await Assert.ThrowsAsync<GException>(() => request);

        // A GError that was marshalled rather than invented: it carries a
        // domain the library interned and a message the library wrote.
        Assert.NotEqual(Quark.Zero, error.Domain);
        Assert.False(string.IsNullOrWhiteSpace(error.Domain.ToString()));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    /// <summary>
    /// The cancellation path: a token that is already cancelled when the
    /// request is made cancels the task, and the cancellation carries the
    /// caller's token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pre-cancelled token cancels the private <c>GCancellable</c> before the
    /// operation starts, which Gio documents as harmless — the callback still
    /// runs, with <c>G_IO_ERROR_CANCELLED</c>. That is the mapping under test:
    /// the awaiter has to see a cancellation carrying the caller's token rather
    /// than the <see cref="GException"/> that error code would otherwise
    /// become. The URI request is what makes the assertion about the error
    /// domain and the assertion about the token the same assertion — a token
    /// that was ignored would surface as the "file is not there" fault the test
    /// above measures.
    /// </para>
    /// <para>
    /// It is the URI request rather than the asset request because only one of
    /// them watches the token. An asset request that the cache can answer
    /// straight away hands the asset over regardless of the state of the
    /// <c>GCancellable</c> it was given; the URI request goes through the
    /// asynchronous initialisation of the editing services, which does watch
    /// it. Both are the library's behaviour, not the binding's.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task APreCancelledTokenCancelsTheRequestWithTheCallersToken()
    {
        GstGES.Initialize();

        using CancellationTokenSource source = new();
        await source.CancelAsync();

        string uri = $"file:///nonexistent-{Guid.NewGuid():N}.mp4";

        Task<UriClipAsset> request = UriClipAsset.NewAsync(uri, source.Token);

        await Settled(request);

        Assert.True(request.IsCanceled, $"status={request.Status}, error={request.Exception?.InnerException}");

        OperationCanceledException cancellation =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);

        Assert.Equal(source.Token, cancellation.CancellationToken);
    }

    /// <summary>
    /// Waits for a request to reach any terminal state, and fails the test
    /// instead of hanging it when it reaches none.
    /// </summary>
    /// <param name="request">The task the request handed back.</param>
    /// <returns>The wait.</returns>
    private static async Task Settled(Task request)
    {
        Task finished = await Task.WhenAny(request, Task.Delay(Patience));

        Assert.True(
            ReferenceEquals(finished, request),
            $"The request did not complete within {Patience}. Nothing in this process iterates a main " +
            "context, so this is the Gio async dispatcher failing to deliver the callback.");
    }
}
