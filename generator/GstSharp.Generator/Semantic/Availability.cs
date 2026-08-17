using System.Globalization;
using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Semantic;

/// <summary>
/// Which GStreamer a member needs, for the members that need more than the
/// oldest one the binding supports.
/// </summary>
/// <remarks>
/// <para>
/// The gir writes a <c>version</c> attribute on everything that arrived after
/// the namespace itself, and the binding emits that as a documentation line so
/// that a member which is not in the library on the reader's machine says so
/// before the call fails. The two traps this repository walked into —
/// <c>gst_structure_is_writable</c> and <c>gst_state_get_name</c>, both 1.28
/// entry points — were invisible for exactly the reason that the emitted
/// documentation never named a version.
/// </para>
/// <para>
/// Only what is newer than <see cref="SupportFloor"/> is worth a line. Below
/// the floor the statement is true of every member the binding emits, so it
/// would be noise on thousands of members rather than a warning on the few
/// hundred that carry a risk.
/// </para>
/// </remarks>
internal static class Availability
{
    /// <summary>
    /// The oldest GStreamer the binding supports. A member of this version or
    /// of an older one is available everywhere the binding runs at all, and
    /// says nothing about its version.
    /// </summary>
    /// <remarks>
    /// This is the floor the ABI probes of <c>tests/GstSharp.IntegrationTests</c>
    /// assert against, and the reason the Linux job of the CI is pinned to
    /// <c>ubuntu-24.04</c> rather than to <c>ubuntu-latest</c>: 24.04 ships
    /// GStreamer 1.24, so a member that arrived later fails there and nowhere
    /// else. Raising the floor is a change of that pin first and of this
    /// constant second.
    /// </remarks>
    internal const string SupportFloor = "1.24";

    /// <summary>
    /// Returns the version a member arrived in when that is newer than the
    /// support floor.
    /// </summary>
    /// <param name="node">The gir element the member was emitted from.</param>
    /// <returns>
    /// The <c>version</c> attribute verbatim, or <see langword="null"/> when
    /// there is none, when it is not newer than <see cref="SupportFloor"/>, or
    /// when it is not a version this can compare.
    /// </returns>
    internal static string? SinceVersion(GirNode? node) => SinceVersion(node?.Version);

    /// <summary>
    /// Returns a version when it is newer than the support floor.
    /// </summary>
    /// <param name="version">The <c>version</c> attribute of a gir element.</param>
    /// <returns>
    /// The version verbatim, or <see langword="null"/> when there is none, when
    /// it is not newer than <see cref="SupportFloor"/>, or when it is not a
    /// version this can compare.
    /// </returns>
    /// <remarks>
    /// The comparison is on the major and the minor component alone, so that
    /// the <c>1.24.0</c> spelling of the floor is the floor rather than
    /// something above it. A micro release never adds API, so there is nothing
    /// the third component could say that the second one does not.
    /// </remarks>
    internal static string? SinceVersion(string? version) =>
        IsNewer(version, SupportFloor) ? version : null;

    /// <summary>
    /// Tells whether one gir version is newer than another.
    /// </summary>
    /// <param name="version">The version to test.</param>
    /// <param name="than">The version to test it against.</param>
    /// <returns><see langword="true"/> when <paramref name="version"/> is newer.</returns>
    /// <remarks>
    /// A version that cannot be read is not newer than anything. The gir writes
    /// plain decimal components and nothing else, so anything that does not
    /// parse is a file this generator does not understand, and staying silent
    /// about it is the answer that cannot be wrong in the loud direction.
    /// </remarks>
    internal static bool IsNewer(string? version, string? than)
    {
        if (!TryParse(version, out int major, out int minor))
        {
            return false;
        }

        if (!TryParse(than, out int otherMajor, out int otherMinor))
        {
            return true;
        }

        return major != otherMajor ? major > otherMajor : minor > otherMinor;
    }

    /// <summary>
    /// Reads the major and the minor component of a gir version.
    /// </summary>
    /// <param name="version">The version to read.</param>
    /// <param name="major">The first component.</param>
    /// <param name="minor">The second component, or zero when there is none.</param>
    /// <returns><see langword="true"/> when the version could be read.</returns>
    private static bool TryParse(string? version, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        if (string.IsNullOrEmpty(version))
        {
            return false;
        }

        int separator = version.IndexOf('.', StringComparison.Ordinal);
        string majorText = separator < 0 ? version : version[..separator];

        if (!int.TryParse(majorText, NumberStyles.None, CultureInfo.InvariantCulture, out major))
        {
            return false;
        }

        if (separator < 0)
        {
            return true;
        }

        string rest = version[(separator + 1)..];
        int next = rest.IndexOf('.', StringComparison.Ordinal);
        string minorText = next < 0 ? rest : rest[..next];

        return int.TryParse(minorText, NumberStyles.None, CultureInfo.InvariantCulture, out minor);
    }
}
