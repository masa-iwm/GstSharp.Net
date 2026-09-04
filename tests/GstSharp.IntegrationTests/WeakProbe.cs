using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Watches one <c>GObject</c> without holding a reference of it and says
/// whether it was freed.
/// </summary>
/// <remarks>
/// A reference count cannot answer "was it freed": reading the count of an
/// object that is gone is undefined, and reading the count of one that is still
/// there says nothing about who else is holding it. The weak notification of
/// GObject is the only answer that is safe to read, and it is what the tests of
/// a wrapper that was disposed need.
/// </remarks>
internal static unsafe partial class WeakProbe
{
    private static int _freed;

    /// <summary>Gets how many of the watched objects were freed since the arming.</summary>
    internal static int Freed => Volatile.Read(ref _freed);

    /// <summary>
    /// Starts watching an object, and forgets whatever was watched before.
    /// </summary>
    /// <param name="handle">The object to watch, which must still be alive.</param>
    /// <remarks>
    /// The notification is never removed: the tests here watch objects that are
    /// meant to be freed before the test ends, and a notification that never
    /// arrives is exactly the failure being looked for.
    /// </remarks>
    internal static void Arm(nint handle)
    {
        Volatile.Write(ref _freed, 0);
        ObjectWeakRef(handle, (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnFreed, nint.Zero);
    }

    /// <summary>
    /// Asks to be told when an object is destroyed, without holding a reference
    /// of its own.
    /// </summary>
    /// <param name="instance">The object to watch.</param>
    /// <param name="notify">
    /// A <c>GWeakNotify</c>, called as <c>notify(userData, object)</c> when the
    /// last reference of the object goes away.
    /// </param>
    /// <param name="userData">The state handed to the notification.</param>
    [LibraryImport("GObject", EntryPoint = "g_object_weak_ref")]
    private static partial void ObjectWeakRef(nint instance, nint notify, nint userData);

    /// <summary>Counts one freed object.</summary>
    /// <param name="userData">Unused; the count is a static field.</param>
    /// <param name="where">
    /// Where the object was. It must not be touched: the notification runs
    /// while the object is being torn down.
    /// </param>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnFreed(nint userData, nint where) => Interlocked.Increment(ref _freed);
}
