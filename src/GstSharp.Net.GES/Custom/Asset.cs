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
    /// <c>GESUriClip</c> in particular, <see cref="GES.UriClipAsset.NewAsync(string, CancellationToken)"/>
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
    /// context on, so the application needs no main loop of its own. An
    /// application that has one may say so through
    /// <see cref="GstSharp.GioAsyncContext"/>, in which case both halves run on
    /// the context it named instead. Either way, continuations of the returned
    /// task never run there; they are scheduled, so nothing user code does can
    /// stall the loop. See <c>docs/gio-async.md</c>.
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
    /// <paramref name="id"/> contains a null character, or it is
    /// <see langword="null"/> while <paramref name="extractableType"/> is a
    /// <c>GESEffect</c> or a <c>GESEffectClip</c> type, whose identifier is the
    /// bin description and which the library reads without a check
    /// (<c>ges-effect-asset.c:390-391</c>, <c>ges-asset.c:752-766</c>).
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
        CancellationToken cancellationToken = default)
    {
        ThrowIfIdIsRequired(extractableType, id, nameof(id));
        return new RequestState(extractableType, id, cancellationToken).Start();
    }

    /// <summary>
    /// Requests an asset with the given properties, watching a
    /// <c>GCancellable</c> the caller already holds.
    /// </summary>
    /// <param name="extractableType">
    /// The <c>GESExtractable</c> type the asset produces.
    /// </param>
    /// <param name="id">
    /// The identifier of the asset, or <see langword="null"/> for the standard
    /// identifier of the type — read the warning on
    /// <see cref="RequestAsync(Gst.GObject.GType, string, CancellationToken)"/>
    /// before passing <see langword="null"/>.
    /// </param>
    /// <param name="cancellable">
    /// The <c>GCancellable</c> the request watches. It is <em>borrowed</em>: the
    /// binding
    /// takes a reference of its own for the duration of the request and
    /// releases that reference when the request completes, but it never
    /// cancels, resets or disposes the object. Cancelling the request is the
    /// caller's own <see cref="Gst.Gio.Cancellable.Cancel"/>.
    /// </param>
    /// <returns>The requested asset.</returns>
    /// <remarks>
    /// <para>
    /// This overload is for a caller who already has a <c>GCancellable</c> —
    /// one that other Gio work of the application shares, say. A caller who has
    /// a <see cref="CancellationToken"/> instead should use
    /// <see cref="RequestAsync(Gst.GObject.GType, string, CancellationToken)"/>,
    /// which builds and owns the <c>GCancellable</c> itself.
    /// </para>
    /// <para>
    /// Gio's rule about the object applies unchanged: a <c>GCancellable</c>
    /// that has been cancelled is not reused for a new operation, because every
    /// operation that watches it fails immediately. There is a
    /// <see cref="Gst.Gio.Cancellable.Reset"/>, but it may only be called when
    /// no operation is running; a fresh <see cref="Gst.Gio.Cancellable.New"/>
    /// per operation is the simpler shape. Handing in one that is already
    /// cancelled is well defined rather than an error: the callback still runs,
    /// with <c>G_IO_ERROR_CANCELLED</c>, so the task is cancelled.
    /// </para>
    /// <para>
    /// Everything else — the initialisation that must have run, the dispatcher
    /// thread, the ownership of the result — is as
    /// <see cref="RequestAsync(Gst.GObject.GType, string, CancellationToken)"/>
    /// documents it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cancellable"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> contains a null character, or it is
    /// <see langword="null"/> while <paramref name="extractableType"/> is a
    /// <c>GESEffect</c> or a <c>GESEffectClip</c> type, as
    /// <see cref="RequestAsync(Gst.GObject.GType, string, CancellationToken)"/>
    /// documents.
    /// </exception>
    /// <exception cref="Gst.GLib.GException">
    /// The asset could not be built.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The request was cancelled. It carries no token: what cancelled it is the
    /// caller's <c>GCancellable</c>, which is not a
    /// <see cref="CancellationToken"/>.
    /// </exception>
    public static Task<GES.Asset> RequestAsync(
        Gst.GObject.GType extractableType,
        string? id,
        Gst.Gio.Cancellable cancellable)
    {
        ArgumentNullException.ThrowIfNull(cancellable);
        ThrowIfIdIsRequired(extractableType, id, nameof(id));
        return new RequestState(extractableType, id, cancellable).Start();
    }

    /// <summary>
    /// Refuses a <see langword="null"/> identifier for an extractable type whose
    /// <c>check_id</c> reads the identifier through, which the editing services
    /// do not survive.
    /// </summary>
    /// <param name="extractableType">The type the asset is requested for.</param>
    /// <param name="id">The identifier the caller gave.</param>
    /// <param name="paramName">The name of the parameter that carried it.</param>
    /// <remarks>
    /// <para>
    /// Every request runs <c>_check_and_update_parameters</c> before anything
    /// else (<c>ges-asset.c:1263</c>, <c>:1422</c>, <c>:1533</c>), which calls
    /// the <c>check_id</c> of the extractable interface with the identifier as
    /// it was given. A <c>GESEffect</c> type splits it without a check and reads
    /// the first token out of the <see langword="null"/> the split answers
    /// (<c>ges-effect-asset.c:390-391</c>), and a <c>GESEffectClip</c> type
    /// answers <c>g_strdup (NULL)</c>, which sends the request down the wrong-id
    /// path where the identifier becomes the key of a <c>g_str_hash</c> table
    /// (<c>ges-asset.c:752-766</c>). Both dereference a null pointer inside the
    /// library, so the check has to be here rather than after the call. The
    /// native <c>GESEffect</c> and <c>GESEffectClip</c> carry the same
    /// <c>check_id</c> as a managed subtype does, so neither is exempt.
    /// </para>
    /// <para>
    /// Every other extractable type is left alone: the default <c>check_id</c>
    /// answers the name of the type for a <see langword="null"/> identifier
    /// (<c>ges-extractable.c:59-63</c>), which is the documented spelling for a
    /// source, a clip and a direct <c>GESBaseEffect</c> subtype.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="extractableType"/> is a <c>GESEffect</c> or a
    /// <c>GESEffectClip</c> type and <paramref name="id"/> is
    /// <see langword="null"/>.
    /// </exception>
    internal static void ThrowIfIdIsRequired(
        Gst.GObject.GType extractableType,
        string? id,
        string paramName)
    {
        if (id is not null)
        {
            return;
        }

        if (extractableType.IsA(new Gst.GObject.GType(GES.Effect.GetGType())))
        {
            throw new ArgumentException(
                "An asset of " + extractableType.Name + " cannot be requested with a null id: the id of "
                + "a GESEffect type is the bin description the effect is built from, and the "
                + "library reads it without a check. Pass \"video <description>\" or "
                + "\"audio <description>\".",
                paramName);
        }

        if (extractableType.IsA(new Gst.GObject.GType(GES.EffectClip.GetGType())))
        {
            throw new ArgumentException(
                "An asset of " + extractableType.Name + " cannot be requested with a null id: the id of "
                + "a GESEffectClip type is the bin description of its halves, and the library "
                + "reads it without a check. Pass "
                + "\"audio <description> ||video <description>\", either half of which may be "
                + "absent, or \"\" for neither.",
                paramName);
        }
    }

    /// <summary>
    /// Extracts the object the asset describes, which is a new instance of the
    /// extractable type of the asset.
    /// </summary>
    /// <typeparam name="T">
    /// The wrapper type the result is wanted as, which has to be the managed
    /// type of the extractable type of the asset or one of its base classes.
    /// </typeparam>
    /// <returns>The extracted object, which the caller owns.</returns>
    /// <remarks>
    /// <para>
    /// This is the second half of the contract that builds a
    /// <see cref="GES.TrackElement"/> of a managed type: request an asset for
    /// the <c>GType</c> of the subclass and extract it. The default extraction
    /// of the editing services is <c>g_object_new_with_properties</c> on the
    /// extractable type followed by <c>ges_extractable_set_asset</c>
    /// (<c>ges-asset.c:1588-1606</c>), and that second call is what gives a
    /// track element its <c>nleobject</c>. An element built with <c>new</c>
    /// instead has no asset and no <c>nleobject</c>: a layer that is asked to
    /// add a clip with such a child removes the child again, and copying it —
    /// which is what splitting and pasting do — asserts inside the library.
    /// So an override of <c>GES.Clip.OnCreateTrackElement</c> answers what this
    /// extracted, and nothing else:
    /// </para>
    /// <code>
    /// GES.Asset asset = GES.Asset.Request(MySource.Registration.GType, null)!;
    /// MySource child = asset.Extract&lt;MySource&gt;();
    /// </code>
    /// <para>
    /// The <see langword="null"/> id above is the spelling for a source or a
    /// clip. A <see cref="GES.Effect"/> or <see cref="GES.EffectClip"/> type
    /// takes its bin description as the id instead, and for one of those a
    /// <see langword="null"/> id would be fatal rather than merely wrong: the
    /// library dereferences it (<c>ges-effect-asset.c:390-391</c>,
    /// <c>ges-asset.c:752-766</c>), which is why the request refuses it with an
    /// <see cref="ArgumentException"/> before the call.
    /// </para>
    /// <para>
    /// The library hands the instance back <em>floating</em>. The wrapper sinks
    /// it and owns the one reference there is, so the child must not be
    /// disposed before the slot that answers it returns: whoever consumes it —
    /// <c>ges_container_add</c> — takes a reference of its own only then.
    /// </para>
    /// </remarks>
    /// <exception cref="Gst.GLib.GException">
    /// The asset could not be extracted.
    /// </exception>
    /// <exception cref="InvalidCastException">
    /// The extracted object is not a <typeparamref name="T"/>. The wrapper of
    /// the object is disposed before this is thrown, because nothing else ever
    /// held it.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The library answered nothing and reported no error.
    /// </exception>
    public T Extract<T>()
        where T : Gst.GObject.Object
    {
        nint errorNative = 0;
        nint extracted = GesAssetExtract(Handle, &errorNative);
        Gst.GLib.GException.ThrowIfSet(ref errorNative);

        // The instance is floating on arrival, so Transfer.None is what settles
        // it: the registry sinks the reference and hands it to the wrapper.
        Gst.GObject.Object? wrapper =
            Gst.GObject.Object.FromNative(extracted, Gst.Interop.Transfer.None);

        if (wrapper is null)
        {
            throw new InvalidOperationException("ges_asset_extract returned no object.");
        }

        if (wrapper is T typed)
        {
            return typed;
        }

        // The wrapper holds the only reference to an object no caller asked
        // for, which is the one case where disposing a wrapper is right.
        Type actual = wrapper.GetType();
        wrapper.Dispose();

        throw new InvalidCastException(
            FormattableString.Invariant(
                $"The asset extracted a {actual} rather than a {typeof(T)}."));
    }

    /// <summary>The <c>ges_asset_extract</c> entry point.</summary>
    [LibraryImport("GES", EntryPoint = "ges_asset_extract")]
    private static partial nint GesAssetExtract(nint self, nint* error);

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
    /// The state of one <see cref="RequestAsync(Gst.GObject.GType, string, CancellationToken)"/>.
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

        internal RequestState(Gst.GObject.GType extractableType, string? id, Gst.Gio.Cancellable cancellable)
            : base(owner: null, cancellable)
        {
            // Same reasoning as the constructor above: the type is unwrapped
            // and the identifier is copied on the calling thread.
            _extractableType = extractableType.Value;
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
