using System.Runtime.InteropServices;
using Gst.Gio;
using Gst.Interop;

namespace GES;

/// <summary>
/// The asynchronous constructor of a URI asset, which the generator does not
/// emit: <c>ges_uri_clip_asset_new</c> takes a <c>GAsyncReadyCallback</c> with
/// <c>scope="async"</c>, and the planner skips that scope by design.
/// </summary>
public unsafe partial class UriClipAsset
{
    /// <summary>
    /// Builds the asset of a media file, and completes when the editing
    /// services have discovered it.
    /// </summary>
    /// <param name="uri">The URI of the file the asset describes.</param>
    /// <param name="cancellationToken">
    /// The token that abandons the request. It is translated into the
    /// <c>GCancellable</c> the operation watches; the binding owns that object
    /// and releases it when the request completes.
    /// </param>
    /// <returns>The asset of <paramref name="uri"/>.</returns>
    /// <remarks>
    /// <para>
    /// This is the call an application should use. Discovering a media file
    /// means running a pipeline over it, so the synchronous
    /// <see cref="RequestSync"/> blocks the calling thread for as long as that
    /// takes.
    /// </para>
    /// <para>
    /// <see cref="GstGES.Initialize"/> must have run before this is called:
    /// the editing services own the discoverer this request goes through.
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
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="uri"/> contains a null character.
    /// </exception>
    /// <exception cref="Gst.GLib.GException">
    /// The file could not be discovered.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The request was cancelled. It carries
    /// <paramref name="cancellationToken"/> when that token is what cancelled
    /// it, and no token when something else did — a cancellation the caller did
    /// not ask for is still a cancellation rather than a fault, so it is not a
    /// <see cref="Gst.GLib.GException"/> either.
    /// </exception>
    public static Task<GES.UriClipAsset> NewAsync(string uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return new NewState(uri, cancellationToken).Start();
    }

    /// <summary>The <c>ges_uri_clip_asset_new</c> entry point.</summary>
    [LibraryImport("GES", EntryPoint = "ges_uri_clip_asset_new")]
    private static partial void GesUriClipAssetNew(
        byte* uri,
        nint cancellable,
        delegate* unmanaged[Cdecl]<nint, nint, nint, void> callback,
        nint userData);

    /// <summary>The <c>ges_uri_clip_asset_finish</c> entry point.</summary>
    [LibraryImport("GES", EntryPoint = "ges_uri_clip_asset_finish")]
    private static partial nint GesUriClipAssetFinish(nint result, nint* error);

    /// <summary>
    /// The state of one <see cref="NewAsync"/>.
    /// </summary>
    /// <remarks>
    /// There is no owner to keep reachable: <c>ges_uri_clip_asset_new</c> is a
    /// constructor of the library rather than a method on an object, so nothing
    /// managed is the source of the request.
    /// </remarks>
    private sealed class NewState : GioAsyncState<GES.UriClipAsset>
    {
        private nint _uri;

        internal NewState(string uri, CancellationToken cancellationToken)
            : base(owner: null, cancellationToken)
        {
            // The request is made on the dispatcher thread, so the URI cannot
            // live on the stack of the caller. Copying it here rather than
            // there is also what keeps the rejection of a string with a null
            // character a synchronous ArgumentException, which is what an
            // argument check should be.
            _uri = Gst.Interop.GMarshal.StringToUtf8Ptr(uri);
        }

        protected override void Invoke(nint cancellable, nint userData) =>
            GesUriClipAssetNew((byte*)_uri, cancellable, GioAsync.Bridge, userData);

        protected override GES.UriClipAsset Finish(nint sourceObject, nint result)
        {
            // ges_uri_clip_asset_finish takes the result alone; the source
            // object the callback carries is the asset of the request, which
            // may have failed to load and must not be used as is.
            nint errorNative = 0;
            nint nativeResult = GesUriClipAssetFinish(result, &errorNative);
            Gst.GLib.GException.ThrowIfSet(ref errorNative);

            return Gst.GObject.Object.FromNative<GES.UriClipAsset>(nativeResult, Gst.Interop.Transfer.Full)
                ?? throw new InvalidOperationException("ges_uri_clip_asset_finish returned no value.");
        }

        internal override void Cleanup()
        {
            Gst.Interop.GMarshal.Free(Interlocked.Exchange(ref _uri, nint.Zero));
            base.Cleanup();
        }
    }
}
