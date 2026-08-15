extern alias gstsharp;

using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Loads the native GStreamer libraries once for the whole test assembly.
/// </summary>
/// <remarks>
/// <para>
/// These tests need a real GStreamer installation. When none is found, the
/// constructor throws, and xunit reports that failure on every test of the
/// collection: a missing or broken installation is a red suite, never a silent
/// skip, because the whole point of the suite is to compare the binding against
/// the library that is actually installed.
/// </para>
/// <para>
/// The fixture also registers the native resolver for this assembly, so that
/// the <see cref="TestNatives"/> imports of the tests themselves resolve
/// through the same installation as the ones of the binding.
/// </para>
/// </remarks>
public sealed class GstFixture
{
    /// <summary>
    /// Initialises GStreamer and hooks the imports of the test assembly up to
    /// the native loader.
    /// </summary>
    public GstFixture()
    {
        // The default options search the machine for an installation; see
        // GstSharpOptions for the ways to point it somewhere else.
        gstsharp::GstSharp.Initialize();

        NativeLoader.EnsureRegistered(typeof(GstFixture).Assembly);
    }
}

/// <summary>
/// The collection that every test of this assembly belongs to.
/// </summary>
/// <remarks>
/// GStreamer is global process state and is initialised exactly once, so the
/// tests share a single fixture and do not run in parallel with each other.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GstCollection : ICollectionFixture<GstFixture>
{
    /// <summary>The name of the collection.</summary>
    public const string Name = "gstreamer";
}
