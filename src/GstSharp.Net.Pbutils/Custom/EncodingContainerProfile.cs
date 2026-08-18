using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Pbutils;

/// <content>
/// The call that fills a container profile, and the first call of the binding
/// that takes a <see cref="Gst.GObject.Object"/> over rather than a mini object
/// or a boxed value.
/// </content>
/// <remarks>
/// Its <c>profile</c> parameter is <c>transfer-ownership="full"</c>, and the
/// generator emits no call that takes a wrapper over. See
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#calls-that-consume-their-argument">Calls that consume their argument</see>.
/// Without it a container profile can be built and never filled, which leaves
/// <c>encodebin</c> — and everything above it — with nothing to encode.
/// </remarks>
public partial class EncodingContainerProfile
{
    /// <summary>
    /// Adds a stream profile to this container, taking the stream profile over.
    /// </summary>
    /// <param name="profile">
    /// The stream profile to add, normally a
    /// <see cref="Gst.Pbutils.EncodingAudioProfile"/> or a
    /// <see cref="Gst.Pbutils.EncodingVideoProfile"/>. The call consumes it:
    /// <paramref name="profile"/> is disposed when this method returns,
    /// whatever the answer is, and using it afterwards throws
    /// <see cref="ObjectDisposedException"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the container took the profile, which is
    /// what the library answers for every profile a caller here can hand it —
    /// including one the container already holds, which is appended a second
    /// time rather than refused.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_encoding_container_profile_add_profile</c>, whose
    /// <c>profile</c> parameter is <c>transfer-ownership="full"</c> — "no copy
    /// of @profile will be made", in the words of its C documentation. It is
    /// written by hand for the reason <see cref="Gst.Element.SendEvent"/> is:
    /// it hands the call a reference of its own and then disposes the wrapper,
    /// which leaves the native reference count exactly where the C call leaves
    /// it.
    /// </para>
    /// <para>
    /// <b>What differs is that a profile is a GObject.</b> A GObject wrapper is
    /// interned and its <see cref="Gst.GObject.Object.Dispose()"/> gives up the
    /// object for the whole process rather than for one holder, so consuming
    /// one is a stronger statement than consuming a mini object: after this call
    /// there is no wrapper for the profile anywhere, and the way back to it is
    /// <see cref="GetProfiles"/>, which builds a fresh one. That is the price of
    /// keeping one ownership rule for consuming calls, and it costs nothing in
    /// the shape this call is used in — a stream profile is built, added, and
    /// never touched again.
    /// </para>
    /// <code>
    /// using Caps format = Caps.FromString("audio/mpeg,mpegversion=4")!;
    /// container.AddProfile(EncodingAudioProfile.New(format, null, null, 0));
    /// </code>
    /// <para>
    /// The wrapper is spent whatever the answer is: the C function hands
    /// nothing back on any path, so there is never anything left for the
    /// wrapper to own. <b>The container does not deduplicate</b> — a profile it
    /// already holds is added again, answered <see langword="true"/>, and shows
    /// up twice in <see cref="GetProfiles"/> — so ask
    /// <see cref="ContainsProfile"/> first when that is a possibility.
    /// <see cref="Gst.GObject.Object.Dispose()"/> is idempotent, so a
    /// <c>using</c> declaration around the profile stays correct.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="profile"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper was disposed, or <paramref name="profile"/> was.
    /// </exception>
    public bool AddProfile(Gst.Pbutils.EncodingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Both handles are read before anything is referenced, so that a
        // disposed wrapper throws without leaking the reference of the other.
        nint container = Handle;
        nint owned = profile.Handle;

        // The reference the call consumes. Without it the wrapper and the
        // container would both own the one reference the wrapper holds.
        GObjectNative.ObjectRef(owned);

        int result = GstEncodingContainerProfileAddProfile(container, owned);

        // The handles were read before the call, so nothing keeps either
        // wrapper alive across it on its own.
        GC.KeepAlive(this);

        // And the reference of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing. It happens on a
        // refusal as well, because the C function offers no way back.
        profile.Dispose();

        return result != 0;
    }

    /// <summary>The <c>gst_encoding_container_profile_add_profile</c> entry point.</summary>
    [LibraryImport("GstPbutils", EntryPoint = "gst_encoding_container_profile_add_profile")]
    private static partial int GstEncodingContainerProfileAddProfile(nint container, nint profile);
}
