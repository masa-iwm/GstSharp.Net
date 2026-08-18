using System.Runtime.InteropServices;
using Gst.Gio;
using Gst.Interop;

namespace GES;

/// <summary>
/// The asynchronous half of the asset request, which the generator does not
/// emit: <c>ges_asset_request_async</c> takes a <c>GAsyncReadyCallback</c> with
/// <c>scope="async"</c>, and the planner skips that scope by design.
/// </summary>
public unsafe partial class Asset
{
    /// <summary>
    /// Requests an asset with the given properties, and completes when the
    /// editing services have initialised or fetched it.
    /// </summary>
    /// <param name="extractableType">
    /// The <c>GESExtractable</c> type the asset produces, for example the type
    /// of <see cref="GES.UriClip"/>.
    /// </param>
    /// <param name="id">
    /// The identifier of the asset, or <see langword="null"/> when the
    /// extractable type does not parametrise its extraction and the standard
    /// identifier of the type is wanted. Read the warning below before passing
    /// <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// The token that abandons the request. It is translated into the
    /// <c>GCancellable</c> the operation watches; the binding owns that object
    /// and releases it when the request completes.
    /// </param>
    /// <returns>The requested asset.</returns>
    /// <remarks>
    /// <para>
    /// This is the call to use for the asset types that can only be built
    /// asynchronously, <see cref="GES.UriClip"/> among them, where the
    /// synchronous <see cref="Request"/> fails by construction. For a
    /// <c>GESUriClip</c> in particular, <see cref="GES.UriClipAsset.NewAsync"/>
    /// says the same thing with a typed result.
    /// </para>
    /// <para>
    /// <see cref="GstGES.Initialize"/> must have run before this is called:
    /// the editing services register the types this resolves and build the
    /// asset cache it answers out of.
    /// </para>
    /// <para>
    /// The request is made, and its callback runs, on the dispatcher thread of
    /// the binding — a thread the binding owns and iterates a private main
    /// context on, so the application needs no main loop of its own.
    /// Continuations of the returned task never run there; they are scheduled,
    /// so nothing user code does can stall the dispatcher. See
    /// <c>docs/gio-async.md</c>.
    /// </para>
    /// <para>
    /// The returned wrapper is owned: it holds a reference of its own and is
    /// disposed like any other GObject wrapper of the binding.
    /// </para>
    /// <para>
    /// <strong>A <see langword="null"/> <paramref name="id"/> is a hazard on
    /// the library side.</strong> <c>ges_asset_request_async</c> in the
    /// editing services 1.28.6 reads through a null pointer, and takes the
    /// process down, when it is given no identifier <em>and</em> the asset is
    /// already in its cache; the first request for a type succeeds and the
    /// second one crashes. The synchronous <see cref="Request"/> and every
    /// request that names its identifier are unaffected, so pass the
    /// identifier — for a type whose extraction is not parametrised that is
    /// the name of the type, which is what the editing services would have
    /// derived themselves. The binding does not derive it for you: which
    /// identifier a <see langword="null"/> stands for is the extractable
    /// type's own answer, and guessing it here would be wrong for every type
    /// that overrides it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> contains a null character.
    /// </exception>
    /// <exception cref="Gst.GLib.GException">
    /// The asset could not be built.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The request was cancelled. It carries
    /// <paramref name="cancellationToken"/> when that token is what cancelled
    /// it, and no token when something else did — a cancellation the caller did
    /// not ask for is still a cancellation rather than a fault, so it is not a
    /// <see cref="Gst.GLib.GException"/> either.
    /// </exception>
    public static Task<GES.Asset> RequestAsync(
        Gst.GObject.GType extractableType,
        string? id,
        CancellationToken cancellationToken = default) =>
        new RequestState(extractableType, id, cancellationToken).Start();

    /// <summary>The <c>ges_asset_request_async</c> entry point.</summary>
    [LibraryImport("GES", EntryPoint = "ges_asset_request_async")]
    private static partial void GesAssetRequestAsync(
        nuint extractableType,
        byte* id,
        nint cancellable,
        delegate* unmanaged[Cdecl]<nint, nint, nint, void> callback,
        nint userData);

    /// <summary>The <c>ges_asset_request_finish</c> entry point.</summary>
    [LibraryImport("GES", EntryPoint = "ges_asset_request_finish")]
    private static partial nint GesAssetRequestFinish(nint result, nint* error);

    /// <summary>
    /// The state of one <see cref="RequestAsync"/>.
    /// </summary>
    /// <remarks>
    /// There is no owner to keep reachable: <c>ges_asset_request_async</c> is a
    /// function of the library rather than a method on an object, so nothing
    /// managed is the source of the request.
    /// </remarks>
    private sealed class RequestState : GioAsyncState<GES.Asset>
    {
        private readonly nuint _extractableType;

        private nint _id;

        internal RequestState(Gst.GObject.GType extractableType, string? id, CancellationToken cancellationToken)
            : base(owner: null, cancellationToken)
        {
            _extractableType = extractableType.Value;

            // The request is made on the dispatcher thread, so the identifier
            // cannot live on the stack of the caller. Copying it here rather
            // than there is also what keeps the rejection of a string with a
            // null character a synchronous ArgumentException, which is what an
            // argument check should be.
            _id = Gst.Interop.GMarshal.StringToUtf8Ptr(id);
        }

        protected override void Invoke(nint cancellable, nint userData) =>
            GesAssetRequestAsync(_extractableType, (byte*)_id, cancellable, GioAsync.Bridge, userData);

        protected override GES.Asset Finish(nint sourceObject, nint result)
        {
            // ges_asset_request_finish takes the result alone; the source
            // object the callback carries is the asset of the request, which
            // may have failed to load and must not be used as is.
            nint errorNative = 0;
            nint nativeResult = GesAssetRequestFinish(result, &errorNative);
            Gst.GLib.GException.ThrowIfSet(ref errorNative);

            return Gst.GObject.Object.FromNative<GES.Asset>(nativeResult, Gst.Interop.Transfer.Full)
                ?? throw new InvalidOperationException("ges_asset_request_finish returned no value.");
        }

        internal override void Cleanup()
        {
            Gst.Interop.GMarshal.Free(Interlocked.Exchange(ref _id, nint.Zero));
            base.Cleanup();
        }
    }
}
