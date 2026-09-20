using System.Runtime.InteropServices;

namespace Gst.GLib;

/// <summary>The native layout of <c>GMutex</c>.</summary>
/// <remarks>
/// <para>
/// GLib declares it a <c>union { gpointer p; guint i[2]; }</c>
/// (<c>glib/gthread.h</c>), so it is one pointer wide and pointer aligned on
/// every 64 bit target of this binding. Only the storage matters here: nothing
/// managed ever locks one, and the blob exists so that a mirror of a structure
/// that embeds a lock lays the fields behind it out where the C compiler put
/// them.
/// </para>
/// <para>
/// The member is spelled as a pointer rather than as bytes on purpose: a
/// <c>fixed byte[8]</c> would be aligned on 1, and a mirror that embedded it
/// would silently shift every field behind it.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct MutexRaw
{
    /// <summary>The <c>p</c> member of the union.</summary>
    internal nint Pointer;
}

/// <summary>The native layout of <c>GCond</c>.</summary>
/// <remarks>
/// GLib declares it a <c>struct { gpointer p; guint i[2]; }</c>
/// (<c>glib/gthread.h</c>): 16 bytes, pointer aligned, on every 64 bit target.
/// See <see cref="MutexRaw"/> for why the members are spelled out rather than
/// laid out as bytes.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct CondRaw
{
    /// <summary>The <c>p</c> field.</summary>
    internal nint Pointer;

    /// <summary>The first of the two <c>i</c> fields.</summary>
    internal uint First;

    /// <summary>The second of the two <c>i</c> fields.</summary>
    internal uint Second;
}

/// <summary>The native layout of <c>GRecMutex</c>.</summary>
/// <remarks>
/// The same shape as <see cref="CondRaw"/>: GLib declares every one of
/// <c>GCond</c>, <c>GRecMutex</c> and <c>GRWLock</c> as
/// <c>struct { gpointer p; guint i[2]; }</c> (<c>glib/gthread.h</c>). They are
/// separate types here so that a mirror names the lock it embeds.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct RecMutexRaw
{
    /// <summary>The <c>p</c> field.</summary>
    internal nint Pointer;

    /// <summary>The first of the two <c>i</c> fields.</summary>
    internal uint First;

    /// <summary>The second of the two <c>i</c> fields.</summary>
    internal uint Second;
}

/// <summary>The native layout of <c>GRWLock</c>.</summary>
/// <remarks>
/// The same shape as <see cref="CondRaw"/>; see <see cref="RecMutexRaw"/>.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct RWLockRaw
{
    /// <summary>The <c>p</c> field.</summary>
    internal nint Pointer;

    /// <summary>The first of the two <c>i</c> fields.</summary>
    internal uint First;

    /// <summary>The second of the two <c>i</c> fields.</summary>
    internal uint Second;
}
