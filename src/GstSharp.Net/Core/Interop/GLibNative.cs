using System.Runtime.InteropServices;

namespace Gst.Interop;

/// <summary>
/// Raw entry points of <c>libglib-2.0</c> that the runtime needs.
/// </summary>
/// <remarks>
/// Every signature is blittable: strings are passed as <c>byte*</c> and encoded
/// by <see cref="GMarshal"/>, and <c>gboolean</c> is an <see cref="int"/>.
/// </remarks>
internal static unsafe partial class GLibNative
{
    /// <summary>Default priority of a GLib source (<c>G_PRIORITY_DEFAULT</c>).</summary>
    internal const int PriorityDefault = 0;

    /// <summary>Priority of a high priority idle source (<c>G_PRIORITY_HIGH_IDLE</c>).</summary>
    internal const int PriorityHighIdle = 100;

    /// <summary>Priority of a plain idle source (<c>G_PRIORITY_DEFAULT_IDLE</c>).</summary>
    internal const int PriorityDefaultIdle = 200;

    [LibraryImport("GLib", EntryPoint = "g_free")]
    internal static partial void Free(nint memory);

    [LibraryImport("GLib", EntryPoint = "g_malloc0")]
    internal static partial nint Malloc0(nuint size);

    [LibraryImport("GLib", EntryPoint = "g_strdup")]
    internal static partial nint StrDup(byte* text);

    [LibraryImport("GLib", EntryPoint = "g_strfreev")]
    internal static partial void StrFreeV(nint strv);

    [LibraryImport("GLib", EntryPoint = "g_quark_from_string")]
    internal static partial uint QuarkFromString(byte* text);

    [LibraryImport("GLib", EntryPoint = "g_quark_to_string")]
    internal static partial nint QuarkToString(uint quark);

    [LibraryImport("GLib", EntryPoint = "g_error_new_literal")]
    internal static partial nint ErrorNewLiteral(uint domain, int code, byte* message);

    [LibraryImport("GLib", EntryPoint = "g_error_free")]
    internal static partial void ErrorFree(nint error);

    [LibraryImport("GLib", EntryPoint = "g_main_context_default")]
    internal static partial nint MainContextDefault();

    [LibraryImport("GLib", EntryPoint = "g_main_context_get_thread_default")]
    internal static partial nint MainContextGetThreadDefault();

    [LibraryImport("GLib", EntryPoint = "g_main_context_new")]
    internal static partial nint MainContextNew();

    [LibraryImport("GLib", EntryPoint = "g_main_context_ref")]
    internal static partial nint MainContextRef(nint context);

    [LibraryImport("GLib", EntryPoint = "g_main_context_unref")]
    internal static partial void MainContextUnref(nint context);

    [LibraryImport("GLib", EntryPoint = "g_main_context_iteration")]
    internal static partial int MainContextIteration(nint context, int mayBlock);

    [LibraryImport("GLib", EntryPoint = "g_main_context_pending")]
    internal static partial int MainContextPending(nint context);

    [LibraryImport("GLib", EntryPoint = "g_main_context_is_owner")]
    internal static partial int MainContextIsOwner(nint context);

    [LibraryImport("GLib", EntryPoint = "g_main_context_push_thread_default")]
    internal static partial void MainContextPushThreadDefault(nint context);

    [LibraryImport("GLib", EntryPoint = "g_main_context_pop_thread_default")]
    internal static partial void MainContextPopThreadDefault(nint context);

    [LibraryImport("GLib", EntryPoint = "g_main_context_invoke_full")]
    internal static partial void MainContextInvokeFull(
        nint context,
        int priority,
        delegate* unmanaged[Cdecl]<nint, int> function,
        nint data,
        delegate* unmanaged[Cdecl]<nint, void> notify);

    [LibraryImport("GLib", EntryPoint = "g_idle_source_new")]
    internal static partial nint IdleSourceNew();

    [LibraryImport("GLib", EntryPoint = "g_source_set_priority")]
    internal static partial void SourceSetPriority(nint source, int priority);

    [LibraryImport("GLib", EntryPoint = "g_source_set_callback")]
    internal static partial void SourceSetCallback(
        nint source,
        delegate* unmanaged[Cdecl]<nint, int> function,
        nint data,
        delegate* unmanaged[Cdecl]<nint, void> notify);

    [LibraryImport("GLib", EntryPoint = "g_source_attach")]
    internal static partial uint SourceAttach(nint source, nint context);

    [LibraryImport("GLib", EntryPoint = "g_source_unref")]
    internal static partial void SourceUnref(nint source);

    [LibraryImport("GLib", EntryPoint = "g_idle_add_full")]
    internal static partial uint IdleAddFull(
        int priority,
        delegate* unmanaged[Cdecl]<nint, int> function,
        nint data,
        delegate* unmanaged[Cdecl]<nint, void> notify);

    [LibraryImport("GLib", EntryPoint = "g_main_loop_new")]
    internal static partial nint MainLoopNew(nint context, int isRunning);

    [LibraryImport("GLib", EntryPoint = "g_main_loop_ref")]
    internal static partial nint MainLoopRef(nint loop);

    [LibraryImport("GLib", EntryPoint = "g_main_loop_unref")]
    internal static partial void MainLoopUnref(nint loop);

    [LibraryImport("GLib", EntryPoint = "g_main_loop_run")]
    internal static partial void MainLoopRun(nint loop);

    [LibraryImport("GLib", EntryPoint = "g_main_loop_quit")]
    internal static partial void MainLoopQuit(nint loop);

    [LibraryImport("GLib", EntryPoint = "g_main_loop_is_running")]
    internal static partial int MainLoopIsRunning(nint loop);

    [LibraryImport("GLib", EntryPoint = "g_main_loop_get_context")]
    internal static partial nint MainLoopGetContext(nint loop);

    /// <summary>Allocates an empty, growable byte array.</summary>
    /// <returns>The array, which the caller frees with <see cref="ByteArrayFree"/>.</returns>
    [LibraryImport("GLib", EntryPoint = "g_byte_array_new")]
    internal static partial nint ByteArrayNew();

    /// <summary>Releases a byte array.</summary>
    /// <param name="array">The array to release.</param>
    /// <param name="freeSegment">
    /// Non zero to release the bytes as well, zero to hand them to the caller.
    /// </param>
    /// <returns>
    /// The bytes when <paramref name="freeSegment"/> is zero, and <c>0</c>
    /// otherwise.
    /// </returns>
    [LibraryImport("GLib", EntryPoint = "g_byte_array_free")]
    internal static partial nint ByteArrayFree(nint array, int freeSegment);
}

/// <summary>The native layout of <c>GByteArray</c>.</summary>
/// <remarks>
/// The mirror is only ever read through a pointer into memory that GLib owns;
/// it is never allocated, assigned or copied. Both fields are public in the C
/// header, which is what makes reading them the supported way of getting at
/// what a <c>GByteArray</c> holds.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GByteArrayRaw
{
    /// <summary>The <c>data</c> field: the bytes, or <c>0</c> while empty.</summary>
    internal nint Data;

    /// <summary>The <c>len</c> field: how many bytes are in the array.</summary>
    internal uint Len;
}
