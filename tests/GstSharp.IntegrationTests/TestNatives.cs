using System.Runtime.InteropServices;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The entry points of <c>libgstreamer-1.0</c> that the tests need and the
/// binding does not offer yet.
/// </summary>
/// <remarks>
/// Everything that the binding does expose is used through the binding, so that
/// the tests exercise the product code path. What is left here is buffer
/// construction, which the function emitter does not cover yet, and
/// <c>gst_parse_launch</c>, which is only used as a source of a <c>GError</c>.
/// </remarks>
internal static unsafe partial class TestNatives
{
    /// <summary>Creates an empty buffer.</summary>
    /// <returns>The new buffer, owned by the caller.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_new")]
    internal static partial nint BufferNew();

    /// <summary>Creates a buffer with preallocated memory.</summary>
    /// <param name="allocator">The allocator to use, or <c>0</c> for the default one.</param>
    /// <param name="size">The number of bytes to allocate.</param>
    /// <param name="parameters">The allocation parameters, or <c>0</c> for the defaults.</param>
    /// <returns>The new buffer, owned by the caller.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_new_allocate")]
    internal static partial nint BufferNewAllocate(nint allocator, nuint size, nint parameters);

    /// <summary>Reads the size of the memory of a buffer.</summary>
    /// <param name="buffer">The buffer to measure.</param>
    /// <returns>The number of bytes in the buffer.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_get_size")]
    internal static partial nuint BufferGetSize(nint buffer);

    /// <summary>Creates a sample out of a buffer and its caps.</summary>
    /// <param name="buffer">The buffer of the sample, or <c>0</c>.</param>
    /// <param name="caps">The caps of the sample, or <c>0</c>.</param>
    /// <param name="segment">The segment of the sample, or <c>0</c>.</param>
    /// <param name="info">The info structure of the sample, or <c>0</c>.</param>
    /// <returns>The new sample, owned by the caller.</returns>
    /// <remarks>
    /// The sample takes a reference of the buffer and copies the caps; the
    /// generator does not emit constructors of mini objects yet, so the tests
    /// that need one call the C function.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_sample_new")]
    internal static partial nint SampleNew(nint buffer, nint caps, nint segment, nint info);

    /// <summary>Builds a pipeline from its textual description.</summary>
    /// <param name="description">The description, as UTF-8.</param>
    /// <param name="error">Receives the <c>GError</c> of a failed call.</param>
    /// <returns>The pipeline, or <c>0</c> when the description was rejected.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_parse_launch")]
    internal static partial nint ParseLaunch(byte* description, nint* error);

    /// <summary>Pulls a sample out of an <c>appsink</c>, or gives up after a timeout.</summary>
    /// <param name="appsink">The sink to pull from.</param>
    /// <param name="timeout">How long to wait, in nanoseconds.</param>
    /// <returns>The sample, owned by the caller, or <c>0</c>.</returns>
    /// <remarks>
    /// The C call is the yardstick the emission of the action signal of the
    /// same name is compared against, ownership included.
    /// </remarks>
    [LibraryImport("GstApp", EntryPoint = "gst_app_sink_try_pull_sample")]
    internal static partial nint AppSinkTryPullSample(nint appsink, ulong timeout);

    /// <summary>Releases a reference of a <c>GstObject</c>.</summary>
    /// <param name="obj">The object to release.</param>
    [LibraryImport("Gst", EntryPoint = "gst_object_unref")]
    internal static partial void ObjectUnref(nint obj);
}
