using System.Runtime.InteropServices;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstPoll</c> entry points the poll descriptor tests need and the
/// binding does not offer.
/// </summary>
/// <remarks>
/// <c>gst_poll_new</c> is bound by hand as <c>Gst.Poll.New</c>, which is what
/// the tests of the binding use; the width probe imports it here instead,
/// because what it measures is the block the library writes into and it holds
/// the raw handle to do so. <c>gst_poll_get_read_gpollfd</c> is imported a
/// second time here, beside the member that binds it, so that the width probe
/// can watch the library write into a block of its own and check where the
/// bytes landed.
/// </remarks>
internal static unsafe partial class PollNatives
{
    /// <summary>Creates a poll set.</summary>
    /// <param name="controllable">Non-zero when the set can be controlled.</param>
    /// <returns>The new set, which the caller frees.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_poll_new")]
    internal static partial nint New(int controllable);

    /// <summary>Fills a <c>GPollFD</c> with the reading half of a set.</summary>
    /// <param name="set">The set to read.</param>
    /// <param name="fd">The block to fill.</param>
    [LibraryImport("Gst", EntryPoint = "gst_poll_get_read_gpollfd")]
    internal static partial void GetReadGpollfd(nint set, byte* fd);
}
