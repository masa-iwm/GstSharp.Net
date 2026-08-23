using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// What <see cref="Gst.Buffer.ForeachMeta"/> does with the item it just showed.
/// </summary>
/// <remarks>
/// This enumeration is public API from its first release, so its members are
/// never reordered.
/// </remarks>
public enum MetaForeachAction
{
    /// <summary>Leaves the item on the buffer and goes on to the next one.</summary>
    Keep,

    /// <summary>Removes the item from the buffer and goes on to the next one.</summary>
    Remove,

    /// <summary>Ends the walk without visiting the items that are left.</summary>
    Stop,
}

public sealed unsafe partial class Buffer
{
    /// <summary>Removes one metadata item from this buffer.</summary>
    /// <param name="meta">The item to remove.</param>
    /// <returns>
    /// <see langword="true"/> when the item was found on this buffer and freed.
    /// </returns>
    /// <remarks>
    /// On <see langword="true"/> the item is freed before the call returns and
    /// <paramref name="meta"/> is dead: its handle is zeroed and every member of
    /// this binding that reads it throws
    /// <see cref="ObjectDisposedException"/>. <see langword="false"/> leaves the
    /// item alive and is what a shared buffer, an item flagged
    /// <c>GST_META_FLAG_LOCKED</c> and an item of another buffer answer, each of
    /// which also logs a GLib CRITICAL.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="meta"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">
    /// The wrapper was disposed, or the item was already removed from its
    /// buffer.
    /// </exception>
    public bool RemoveMeta(Gst.Meta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        nint metaHandle = meta.RequireHandle();
        nint handle = Handle;

        int nativeResult = BufferNative.RemoveMeta(handle, metaHandle);
        GC.KeepAlive(this);

        if (nativeResult == 0)
        {
            return false;
        }

        // The library unlinked the item, called its free function and g_freed
        // it before it returned, so the wrapper the caller still holds is the
        // one thing left addressing released memory. Zeroing the handle is what
        // turns every later read through it into an exception.
        meta.Handle = 0;
        return true;
    }

    /// <summary>
    /// Walks the metadata of this buffer, removing what the function rejects.
    /// </summary>
    /// <param name="function">
    /// Called once per item with this buffer and the item, and answers what to
    /// do with it.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the walk ran to the end.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The function is called once per item, on the calling thread, before this
    /// call returns, and must not modify the buffer — not through
    /// <see cref="RemoveMeta"/> either. Answer
    /// <see cref="Gst.MetaForeachAction.Remove"/> instead, which is the
    /// sanctioned way of dropping an item mid walk, because the library
    /// captures the successor of an item before it runs the function on it.
    /// </para>
    /// <para>
    /// <see cref="Gst.MetaForeachAction.Remove"/> needs a writable buffer and an
    /// unlocked item; when either fails the library aborts the walk, leaves that
    /// item attached and answers <see langword="false"/> without visiting the
    /// rest, while enumeration alone needs neither. The wrapper of an item whose
    /// removal was refused this way stays alive, because nothing was freed.
    /// <see cref="Gst.MetaForeachAction.Stop"/> ends the walk and also answers
    /// <see langword="false"/>.
    /// </para>
    /// <para>
    /// An exception thrown by the function stops the walk and is re-thrown once
    /// the library has returned.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The function answered a value that is not a
    /// <see cref="Gst.MetaForeachAction"/>, which ends the walk as well.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public bool ForeachMeta(Func<Gst.Buffer, Gst.Meta, Gst.MetaForeachAction> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        nint handle = Handle;

        ForeachMetaState state = new(this, function);
        Gst.Interop.CallbackHandle stateHandle = Gst.Interop.CallbackHandle.Alloc(state);
        int nativeResult;

        try
        {
            nativeResult = BufferNative.ForeachMeta(
                handle,
                ForeachMetaTrampoline.Pointer,
                stateHandle.UserData);
            GC.KeepAlive(this);
        }
        finally
        {
            // The library is done with the state when the call returns, which
            // is what makes this a scope=call callback in the first place.
            stateHandle.Free();
        }

        if (nativeResult == 0 && state.Failure is null && state.PendingRemoval is { } refused)
        {
            // The last thing the function answered was Remove and the walk
            // answered FALSE, which is the one shape an unhonoured removal has:
            // gst_buffer_foreach_meta refuses the removal of an item of a non
            // writable buffer and of a GST_META_FLAG_LOCKED item, and it does
            // so before it calls the free function, so the item is still
            // attached. A removal it does honour frees the item and goes on to
            // the next one, which either reaches the end of the list, answering
            // TRUE, or runs the function once more, which clears this slot.
            refused.Handle = state.PendingRemovalHandle;
        }

        // The walk stopped where the function threw, and this is the first
        // place the exception has a managed caller to reach.
        state.Failure?.Throw();
        return nativeResult != 0;
    }

    /// <summary>Enumerates the metadata attached to this buffer.</summary>
    /// <returns>The items, in the order the buffer holds them.</returns>
    /// <remarks>
    /// The cursor is the metadata item itself, so the buffer must not be
    /// modified while the enumeration is open: removing an item frees the node
    /// the cursor stands on and the next step is a use after free. Use
    /// <see cref="ForeachMeta"/> to remove while walking.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public IEnumerable<Gst.Meta> IterateMeta()
    {
        // The handle is read here rather than in the iterator block, so that a
        // disposed wrapper is reported when the enumerable is asked for and not
        // at the first step of a foreach somewhere else.
        nint handle = Handle;
        return IterateMetaCore(handle);
    }

    /// <summary>Enumerates the metadata of one API.</summary>
    /// <param name="apiType">
    /// The metadata API to enumerate, which is what the
    /// <c>*MetaApiGetType()</c> of the declaring module answers.
    /// </param>
    /// <returns>The items of that API, in the order the buffer holds them.</returns>
    /// <remarks>
    /// The filtering is done in managed code on <see cref="Gst.Meta.ApiType"/>,
    /// which is what <c>gst_buffer_iterate_meta_filtered</c> compares as well.
    /// The remark of <see cref="IterateMeta()"/> about modifying the buffer
    /// applies here too.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public IEnumerable<Gst.Meta> IterateMeta(Gst.GObject.GType apiType)
    {
        nint handle = Handle;
        return IterateMetaCore(handle, apiType);
    }

    private IEnumerable<Gst.Meta> IterateMetaCore(nint handle)
    {
        nint state = 0;
        nint meta;

        while ((meta = BufferNative.IterateMeta(handle, ref state)) != 0)
        {
            yield return new Gst.Meta(meta);
        }

        // The buffer has to outlive the walk: the handle is a plain address and
        // a finalizer that ran in between would free what the cursor walks.
        GC.KeepAlive(this);
    }

    private IEnumerable<Gst.Meta> IterateMetaCore(nint handle, Gst.GObject.GType apiType)
    {
        foreach (Gst.Meta meta in IterateMetaCore(handle))
        {
            if (meta.ApiType == apiType)
            {
                yield return meta;
            }
        }
    }

    /// <summary>The state one <see cref="ForeachMeta"/> walk carries.</summary>
    private sealed class ForeachMetaState
    {
        /// <summary>Initialises the state of one walk.</summary>
        /// <param name="buffer">The buffer that is walked.</param>
        /// <param name="function">The function that decides about each item.</param>
        internal ForeachMetaState(Gst.Buffer buffer, Func<Gst.Buffer, Gst.Meta, Gst.MetaForeachAction> function)
        {
            Buffer = buffer;
            Function = function;
        }

        /// <summary>Gets the buffer that is walked.</summary>
        internal Gst.Buffer Buffer { get; }

        /// <summary>Gets the function that decides about each item.</summary>
        internal Func<Gst.Buffer, Gst.Meta, Gst.MetaForeachAction> Function { get; }

        /// <summary>Gets or sets what the function threw, if anything.</summary>
        internal ExceptionDispatchInfo? Failure { get; set; }

        /// <summary>
        /// Gets or sets the wrapper of the item the last call of the function
        /// asked to remove, as long as the walk has not shown that the library
        /// honoured the removal.
        /// </summary>
        internal Gst.Meta? PendingRemoval { get; set; }

        /// <summary>
        /// Gets or sets the handle <see cref="PendingRemoval"/> carried before
        /// the trampoline zeroed it, which is what a refused removal restores.
        /// </summary>
        internal nint PendingRemovalHandle { get; set; }
    }

    /// <summary>The native entry point of a <see cref="ForeachMeta"/> walk.</summary>
    /// <remarks>
    /// <para>
    /// The trampoline is written by hand because the parameter the library
    /// hands it is a <c>GstMeta**</c> whose only meaningful write is NULL:
    /// <c>gst_buffer_foreach_meta</c> tests the value the callback left behind
    /// against NULL and discards any other pointer, which makes the parameter a
    /// keep, remove or stop decision rather than an inout parameter, and no
    /// argument kind of the generator describes that.
    /// </para>
    /// <para>
    /// It also has two jobs a generated trampoline does not have: it invalidates
    /// the wrapper of an item the walk removed, and it captures an exception the
    /// managed function threw so that <see cref="ForeachMeta"/> can re-throw it
    /// once the library has returned. That is why nothing here reports to
    /// <c>ExceptionTrap</c>: this callback has a managed caller to throw to.
    /// </para>
    /// <para>
    /// The invalidation is provisional, because the library decides after the
    /// callback has returned whether it honours the removal at all. The state
    /// therefore remembers the wrapper and the handle of the last removal, and
    /// <see cref="ForeachMeta"/> restores the handle when the walk turns out to
    /// have refused it.
    /// </para>
    /// </remarks>
    internal static class ForeachMetaTrampoline
    {
        /// <summary>Gets the address that is handed to native code.</summary>
        internal static nint Pointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint*, nint, int>)&Invoke;

        /// <summary>Runs the managed function on one metadata item.</summary>
        /// <param name="buffer">The buffer that is walked.</param>
        /// <param name="meta">The item, and where NULL is written to remove it.</param>
        /// <param name="userData">The <c>GCHandle</c> of the state of the walk.</param>
        /// <returns>Non zero to go on, zero to end the walk.</returns>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Invoke(nint buffer, nint* meta, nint userData)
        {
            if (Gst.Interop.CallbackHandle.GetState<ForeachMetaState>(userData) is not { } state)
            {
                // The state is gone, so nothing can decide anything. Ending the
                // walk is the answer that changes nothing about the buffer.
                return 0;
            }

            // Reaching the callback at all means the library went on past the
            // removal the previous one asked for, which is the only proof the
            // binding gets that the removal was honoured. Clearing the slot here
            // rather than in each branch below is what keeps a Keep, a Stop, a
            // throw and a bad answer from all having to remember to do it.
            state.PendingRemoval = null;
            state.PendingRemovalHandle = 0;

            Gst.Meta? item = meta is null ? null : Gst.Meta.FromNative(*meta);

            if (item is null)
            {
                state.Failure ??= ExceptionDispatchInfo.Capture(
                    new InvalidOperationException("gst_buffer_foreach_meta passed no metadata item."));
                return 0;
            }

            Gst.MetaForeachAction action;

            try
            {
                // The buffer of the state is passed rather than a fresh wrapper
                // over the address, so that the function is handed the very
                // object it called ForeachMeta on.
                action = state.Function(state.Buffer, item);
            }
            catch (Exception exception)
            {
                state.Failure = ExceptionDispatchInfo.Capture(exception);
                return 0;
            }

            switch (action)
            {
                case Gst.MetaForeachAction.Remove:
                    // NULL is the one value the library reads back. The item is
                    // freed before the walk goes on, which is what the wrapper
                    // is invalidated for, but the library may also refuse the
                    // removal, so ForeachMeta is left what it needs to undo the
                    // invalidation.
                    state.PendingRemoval = item;
                    state.PendingRemovalHandle = *meta;
                    *meta = 0;
                    item.Handle = 0;
                    return 1;

                case Gst.MetaForeachAction.Stop:
                    return 0;

                case Gst.MetaForeachAction.Keep:
                    return 1;

                default:
                    // The function answered something no member of the
                    // enumeration names, and guessing on its behalf would drop
                    // or keep an item nobody decided about. Ending the walk and
                    // reporting is the only honest answer.
                    state.Failure ??= ExceptionDispatchInfo.Capture(
                        new ArgumentOutOfRangeException(
                            nameof(action),
                            action,
                            "The ForeachMeta function answered a value that is not a MetaForeachAction."));
                    return 0;
            }
        }
    }
}
