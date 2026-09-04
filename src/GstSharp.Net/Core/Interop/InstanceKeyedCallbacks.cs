using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst.Interop;

/// <summary>
/// The managed state of the callbacks whose own C signature carries no
/// <c>user_data</c> parameter, keyed by the instance they were installed on and
/// the storage slot of that instance they occupy.
/// </summary>
/// <remarks>
/// <para>
/// <c>GstPadChainFunction</c> and its ten siblings are called as
/// <c>chainfunc (pad, parent, buffer)</c> (gstpad.c:4605): the
/// <c>user_data</c> the <c>gst_pad_set_*_function_full</c> setter took is kept
/// on the pad and never handed back, so a trampoline cannot recover its state
/// the way <see cref="CallbackHandle.GetState{T}"/> does. It recovers it from
/// here instead, keyed by the instance pointer of its first argument and the
/// name of the slot the setter wrote.
/// </para>
/// <para>
/// The entries are owned by native code, not by this table:
/// <see cref="Install"/> writes the entry before the setter runs, and the
/// setter's own <c>GDestroyNotify</c> - <see cref="DestroyNotify"/> - takes it
/// out again. C replaces a function with notify(old) followed by install(new)
/// and holds no lock while it does (gstpad.c:1820-1835), so the notify of the
/// value being replaced runs after the new entry is already in the table; it
/// therefore removes the key only while it still maps to the very handle being
/// released, which is what <see cref="ConcurrentDictionary{TKey, TValue}.TryRemove(KeyValuePair{TKey, TValue})"/>
/// tests. <c>gst_pad_finalize</c> runs every notify unconditionally
/// (gstpad.c:772-791), so an entry dies exactly when native code says it does.
/// </para>
/// </remarks>
internal static unsafe class InstanceKeyedCallbacks
{
    private static readonly ConcurrentDictionary<InstanceSlot, CallbackHandle> Entries = new();

    /// <summary>
    /// Gets the <c>GDestroyNotify</c> that native code runs when it drops one
    /// of these callbacks.
    /// </summary>
    internal static delegate* unmanaged[Cdecl]<nint, void> DestroyNotify => &DestroyNotifyTrampoline;

    /// <summary>
    /// Records <paramref name="callback"/> as the managed state of one slot of
    /// one instance, replacing whatever the slot held.
    /// </summary>
    /// <param name="instance">The instance the callback is installed on.</param>
    /// <param name="slot">The storage slot of that instance it occupies.</param>
    /// <param name="callback">The managed delegate, or <see langword="null"/> to install nothing.</param>
    /// <returns>
    /// The handle whose <see cref="CallbackHandle.UserData"/> is passed to the
    /// setter, or the default handle when there is no callback to install.
    /// </returns>
    /// <remarks>
    /// The entry is written before the native setter is called, so that a
    /// callback which is invoked from another thread the moment the setter
    /// returns already finds its state. Nothing is recorded for a null
    /// callback: the call site passes the null function pointer along with the
    /// null user data and the null notification, which is how C is told to
    /// unset the slot, and the notification of the value being unset removes
    /// the entry that was there.
    /// </remarks>
    internal static CallbackHandle Install(nint instance, string slot, object? callback)
    {
        if (callback is null)
        {
            return default;
        }

        CallbackHandle handle = CallbackHandle.Alloc(new Entry(instance, slot, callback));
        Entries[new InstanceSlot(instance, slot)] = handle;
        return handle;
    }

    /// <summary>Reads the delegate that one slot of one instance holds.</summary>
    /// <typeparam name="T">The delegate type the slot is expected to hold.</typeparam>
    /// <param name="instance">The instance pointer the trampoline was called with.</param>
    /// <param name="slot">The storage slot the trampoline belongs to.</param>
    /// <returns>
    /// The delegate, or <see langword="null"/> when the slot holds nothing or
    /// holds a delegate of another type.
    /// </returns>
    /// <remarks>
    /// A slot two delegate types share - <c>event</c>, which
    /// <c>gst_pad_set_event_function_full</c> and
    /// <c>gst_pad_set_event_full_function_full</c> both write
    /// (gstpad.c:1933-1937, :1979-1984) - answers null to the trampoline of the
    /// type that is not the one installed, which is the failure value of that
    /// trampoline.
    /// </remarks>
    internal static T? Lookup<T>(nint instance, string slot)
        where T : class =>
        Entries.TryGetValue(new InstanceSlot(instance, slot), out CallbackHandle handle)
            ? CallbackHandle.GetState<Entry>(handle.UserData)?.Callback as T
            : null;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void DestroyNotifyTrampoline(nint userData)
    {
        try
        {
            CallbackHandle handle = CallbackHandle.FromUserData(userData);
            if (CallbackHandle.GetState<Entry>(userData) is { } entry)
            {
                Entries.TryRemove(
                    new KeyValuePair<InstanceSlot, CallbackHandle>(
                        new InstanceSlot(entry.Instance, entry.Slot),
                        handle));
            }

            handle.Free();
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }
    }

    /// <summary>The key of one callback: an instance and one of its slots.</summary>
    /// <param name="Instance">The instance pointer.</param>
    /// <param name="Slot">The slot name.</param>
    private readonly record struct InstanceSlot(nint Instance, string Slot);

    /// <summary>
    /// The state behind the <c>user_data</c> pointer: the delegate, plus the
    /// key it is filed under, which the destroy notification only receives
    /// through here.
    /// </summary>
    /// <param name="instance">The instance the callback is installed on.</param>
    /// <param name="slot">The slot it occupies.</param>
    /// <param name="callback">The managed delegate.</param>
    private sealed class Entry(nint instance, string slot, object callback)
    {
        /// <summary>Gets the instance the callback is installed on.</summary>
        internal nint Instance { get; } = instance;

        /// <summary>Gets the slot it occupies.</summary>
        internal string Slot { get; } = slot;

        /// <summary>Gets the managed delegate.</summary>
        internal object Callback { get; } = callback;
    }
}
