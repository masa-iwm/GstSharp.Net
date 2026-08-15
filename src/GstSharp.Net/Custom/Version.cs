using System.Globalization;
using Gst.Interop;

namespace Gst;

/// <summary>
/// The version of the GStreamer library that is loaded into the process.
/// </summary>
/// <remarks>
/// The value that <c>GstSharp.NativeVersion</c> hands out is read from the
/// native library with <c>gst_version</c>, and its <see cref="Description"/>
/// is the string that <c>gst_version_string</c> returned. A version that is
/// constructed by hand has no such string and formats its own instead.
/// </remarks>
public readonly struct Version : IEquatable<Version>
{
    private readonly string? _description;

    /// <summary>
    /// Initialises a version from its parts.
    /// </summary>
    /// <param name="major">The major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <param name="micro">The micro version.</param>
    /// <param name="nano">
    /// The nano version: 0 for a release, 1 for a git build and 2 or more for a
    /// prerelease.
    /// </param>
    public Version(uint major, uint minor, uint micro, uint nano)
        : this(major, minor, micro, nano, description: null)
    {
    }

    private Version(uint major, uint minor, uint micro, uint nano, string? description)
    {
        Major = major;
        Minor = minor;
        Micro = micro;
        Nano = nano;
        _description = description;
    }

    /// <summary>Gets the major version.</summary>
    public uint Major { get; }

    /// <summary>Gets the minor version.</summary>
    public uint Minor { get; }

    /// <summary>Gets the micro version.</summary>
    public uint Micro { get; }

    /// <summary>
    /// Gets the nano version: 0 for a release, 1 for a git build and 2 or more
    /// for a prerelease.
    /// </summary>
    public uint Nano { get; }

    /// <summary>
    /// Gets the human readable version string, for example
    /// <c>GStreamer 1.28.6</c>.
    /// </summary>
    public string Description => _description ?? Format(Major, Minor, Micro, Nano);

    /// <summary>Compares two versions for equality.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when both are the same version.</returns>
    public static bool operator ==(Version left, Version right) => left.Equals(right);

    /// <summary>Compares two versions for inequality.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><see langword="true"/> when both are different versions.</returns>
    public static bool operator !=(Version left, Version right) => !left.Equals(right);

    /// <summary>
    /// Reads the version of the loaded native library. This loads it if it is
    /// not loaded yet.
    /// </summary>
    /// <returns>The version, with the description of the library itself.</returns>
    internal static Version FromNative()
    {
        GstNative.GetVersion(out uint major, out uint minor, out uint micro, out uint nano);
        string? description = GMarshal.PtrToStringUtf8AndFree(GstNative.VersionString());
        return new Version(major, minor, micro, nano, description);
    }

    /// <summary>
    /// Compares this version with another one. <see cref="Description"/> takes
    /// no part in the comparison, because it is derived from the numbers.
    /// </summary>
    /// <param name="other">The version to compare with.</param>
    /// <returns><see langword="true"/> when both are the same version.</returns>
    public bool Equals(Version other) =>
        Major == other.Major && Minor == other.Minor && Micro == other.Micro && Nano == other.Nano;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Version other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Micro, Nano);

    /// <summary>
    /// Formats the version as <c>major.minor.micro</c>, with the nano version
    /// appended when it is not a release.
    /// </summary>
    /// <returns>The formatted version.</returns>
    public override string ToString() =>
        Nano == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Micro}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Micro}.{Nano}");

    private static string Format(uint major, uint minor, uint micro, uint nano)
    {
        // The same shape that gst_version_string produces.
        string suffix = nano switch
        {
            0 => string.Empty,
            1 => " (GIT)",
            _ => " (prerelease)",
        };

        return string.Create(CultureInfo.InvariantCulture, $"GStreamer {major}.{minor}.{micro}{suffix}");
    }
}
