using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.RtspServer;

/// <content>
/// The one call of this module that the generator would have written wrongly:
/// the mount of a media factory, whose C half keeps the reference it is handed
/// but whose caller still needs the wrapper it handed over.
/// </content>
/// <remarks>
/// Its <c>factory</c> parameter is <c>transfer-ownership="full"</c>, and the
/// generated shape for that is a consuming member — one that disposes the
/// wrapper it passed. See
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#calls-that-consume-their-argument">Calls that consume their argument</see>
/// and the exception this file is,
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#rtsp-server">RTSP server</see>.
/// </remarks>
public unsafe partial class RTSPMountPoints
{
    /// <summary>
    /// Mounts a media factory at a path, so that a client asking for that path
    /// is served the media the factory builds.
    /// </summary>
    /// <param name="path">
    /// The mount point, which the library requires to begin with <c>/</c> — for
    /// example <c>/test</c>, reached as <c>rtsp://host:port/test</c>. A factory
    /// already mounted at this exact path is replaced.
    /// </param>
    /// <param name="factory">
    /// The factory to mount. <b>It stays the caller's.</b> The mount points take
    /// a reference of their own, so this wrapper is usable after the call and
    /// the handlers it has connected to
    /// <see cref="RTSPMediaFactory.MediaConfigure"/> and
    /// <see cref="RTSPMediaFactory.MediaConstructed"/> keep firing for every
    /// media the mount builds.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_rtsp_mount_points_add_factory</c>, and it is written by
    /// hand because <b>it is the one place in this module where a
    /// <c>transfer-ownership="full"</c> GObject argument does not consume its
    /// wrapper</b> — the second such place in the binding, after
    /// <c>new Play(renderer)</c>. The usual rule — the call is handed a reference minted for
    /// it and the argument is disposed when the member returns — is wrong here
    /// for one reason: a media factory is the hook point of a server.
    /// <see cref="Gst.GObject.Object.Dispose()"/> runs <c>DisconnectAll</c>, so
    /// the consuming shape would silently strip the <c>media-constructed</c>
    /// handler that the <c>test-launch</c> arrangement connects immediately
    /// before this call — or the <c>media-configure</c> one connected the same
    /// way — and the server would then build unconfigured media.
    /// </para>
    /// <para>
    /// The native reference count is left exactly where the C call leaves it
    /// all the same. Exactly one reference is minted here and handed over: the
    /// C stores the pointer it is given in a bare field of the mount item
    /// (<c>rtsp-mount-points.c:358</c>) and releases it once in
    /// <c>data_item_free</c> (<c>:71</c>), which runs when the path is unmounted
    /// by <see cref="RemoveFactory"/>, replaced, or the mount points are
    /// finalised. The reference this wrapper holds is untouched and goes away
    /// with the wrapper, as it does for a borrowing call.
    /// </para>
    /// <code>
    /// // Not a using: the field outlives the method, because the mount keeps
    /// // calling the handler below for as long as the server runs.
    /// _factory = RTSPMediaFactory.New();
    /// _factory.SetLaunch("( audiotestsrc ! audioconvert ! rtpL16pay name=pay0 pt=96 )");
    /// _factory.MediaConfigure += (_, e) => Configure(e.Object);
    /// mounts.AddFactory("/test", _factory);
    /// </code>
    /// <para>
    /// Disposing the factory afterwards is a caller's choice and not a
    /// requirement of this call, but it is a strong one: a GObject wrapper
    /// stands for the object across the whole process, so disposing it takes
    /// the handlers off again while the mount keeps serving. Keep the wrapper
    /// for as long as the mount is expected to call back into managed code.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="path"/> or <paramref name="factory"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> does not begin with <c>/</c>. The C function
    /// answers such a path with a <c>g_return_if_fail</c>
    /// (<c>rtsp-mount-points.c:354</c>), which returns before it has taken the
    /// factory, so the check has to happen on this side of the call for the
    /// reference not to be minted and then dropped by nobody.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper was disposed, or <paramref name="factory"/> was.
    /// </exception>
    public void AddFactory(string path, Gst.RtspServer.RTSPMediaFactory factory)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(factory);

        // gst_rtsp_mount_points_add_factory refuses anything else before it
        // reaches the factory (rtsp-mount-points.c:354, g_return_if_fail on
        // path != NULL && path[0] == '/'), and a refusal that early would leave
        // the reference minted below with no owner at all.
        if (path.Length == 0 || path[0] != '/')
        {
            throw new ArgumentException("A mount point has to begin with '/'.", nameof(path));
        }

        // Both handles are read before anything is referenced, so that a
        // disposed wrapper throws without leaking the reference of the other.
        nint mounts = Handle;
        nint owned = factory.Handle;

        System.Span<byte> pathBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
        using Gst.Interop.Utf8Scope pathScope = Gst.Interop.GMarshal.StackUtf8(path, pathBuffer);

        // The one reference the mount item keeps. Not two: the wrapper's own
        // reference is not handed over, which is what leaves it usable.
        GObjectNative.ObjectRef(owned);

        GstRtspMountPointsAddFactory(mounts, pathScope.Pointer, owned);

        // The handles were read before the call, so nothing keeps either
        // wrapper alive across it on its own.
        System.GC.KeepAlive(this);
        System.GC.KeepAlive(factory);
    }

    /// <summary>The <c>gst_rtsp_mount_points_add_factory</c> entry point.</summary>
    [LibraryImport("GstRtspServer", EntryPoint = "gst_rtsp_mount_points_add_factory")]
    private static partial void GstRtspMountPointsAddFactory(nint mounts, byte* path, nint factory);
}
