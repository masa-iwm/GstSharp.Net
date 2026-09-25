using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst.Base;

/// <summary>
/// The function <see cref="Gst.Base.BaseGlobal.TypeFindHelperGetRange"/> and
/// <see cref="Gst.Base.BaseGlobal.TypeFindHelperGetRangeFull"/> read the data
/// to identify through.
/// </summary>
/// <param name="obj">The object the typefinding was started with, which is the very instance the member was called with.</param>
/// <param name="parent">The parent the typefinding was started with, which is the very instance the member was called with, or <see langword="null"/>.</param>
/// <param name="offset">The offset of the first byte asked for.</param>
/// <param name="length">The number of bytes asked for.</param>
/// <param name="buffer">
/// The buffer that holds the bytes, or <see langword="null"/>. The binding
/// hands the helper a reference of its own and releases the wrapper after the
/// function returns; the helper releases its reference before the member
/// returns, so the function must not keep or reuse the buffer.
/// </param>
/// <returns>
/// <see cref="Gst.FlowReturn.Ok"/> when <paramref name="buffer"/> holds the
/// bytes, <see cref="Gst.FlowReturn.Eos"/> for a range past the end of the
/// data, or another value that ends the typefinding.
/// </returns>
/// <remarks>
/// <para>
/// The function runs synchronously on the thread that called the member, and
/// every call it gets has happened before the member returns; nothing is kept
/// of it afterwards.
/// </para>
/// <para>
/// The helper asks for at least 4096 bytes at a time
/// (<c>gsttypefindhelper.c:144</c>) and drops, without an error, a buffer that
/// is shorter than what the typefinder wanted or whose
/// <see cref="Gst.Buffer.Offset"/> is neither <c>GST_BUFFER_OFFSET_NONE</c>
/// (<see cref="ulong.MaxValue"/>) nor equal to <paramref name="offset"/>
/// (<c>gsttypefindhelper.c:165-181</c>). A function that reads with a shifted
/// offset has to set <c>Offset</c> back to <paramref name="offset"/>, as
/// <c>gsttagdemux.c</c> does.
/// </para>
/// <para>
/// <see cref="Gst.FlowReturn.Eos"/> is a soft answer: the helper goes on with
/// the next typefinder. Every other value but <see cref="Gst.FlowReturn.Ok"/>
/// ends the typefinding with no caps. <see cref="Gst.FlowReturn.Ok"/> together
/// with a <see langword="null"/> <paramref name="buffer"/> is answered as
/// <see cref="Gst.FlowReturn.Error"/> by the binding, because the helper reads
/// the buffer of an <c>Ok</c> answer without checking it
/// (<c>gsttypefindhelper.c:167-168</c>). A buffer handed out with a value
/// other than <see cref="Gst.FlowReturn.Ok"/> is released and never reaches the
/// helper.
/// </para>
/// <para>
/// An exception that leaves the function is reported through
/// <see cref="Gst.Interop.ExceptionTrap"/> and answered with
/// <see cref="Gst.FlowReturn.Error"/>.
/// </para>
/// </remarks>
public delegate Gst.FlowReturn TypeFindHelperGetRangeFunction(
    Gst.Object obj, Gst.Object? parent, ulong offset, uint length, out Gst.Buffer? buffer);

/// <content>
/// The typefinding over a range reading function, which the generator cannot
/// emit because the callback hands a buffer out and carries no closure.
/// </content>
/// <remarks>
/// <para>
/// The C callback type has no user data (<c>gsttypefindhelper.h:107-111</c>),
/// and the helper only ever calls it synchronously from inside the call
/// (<c>gsttypefindhelper.c:143-145</c>). The managed function is therefore
/// handed to the trampoline through a thread static slot that each call saves
/// and restores, which makes the members re-entrant (a function may start
/// another typefinding) and callable from any thread.
/// </para>
/// </remarks>
public static unsafe partial class BaseGlobal
{
    /// <summary>
    /// Finds the type of the data read through a range reading function.
    /// </summary>
    /// <param name="obj">The object the typefinding is for, handed back to <paramref name="func"/>.</param>
    /// <param name="parent">The parent of <paramref name="obj"/>, handed back to <paramref name="func"/>, or <see langword="null"/>.</param>
    /// <param name="func">The function that reads the data; see <see cref="Gst.Base.TypeFindHelperGetRangeFunction"/>.</param>
    /// <param name="size">The length of the data in bytes.</param>
    /// <param name="extension">The extension of the data, or <see langword="null"/>; typefinders that claim it are tried first.</param>
    /// <param name="prob">The probability of the caps that were found.</param>
    /// <returns>The caps of the data, or <see langword="null"/> when no type was found.</returns>
    /// <remarks>
    /// This is <c>gst_type_find_helper_get_range</c>, written by hand; the
    /// notes on <see cref="Gst.Base.TypeFindHelperGetRangeFunction"/> say how
    /// the function is called.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="obj"/> or <paramref name="func"/> is <see langword="null"/>.
    /// </exception>
    public static Gst.Caps? TypeFindHelperGetRange(
        Gst.Object obj, Gst.Object? parent, Gst.Base.TypeFindHelperGetRangeFunction func,
        ulong size, string? extension, out Gst.TypeFindProbability prob)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(func);
        System.Span<byte> extensionBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
        using Gst.Interop.Utf8Scope extensionScope = Gst.Interop.GMarshal.StackUtf8(extension, extensionBuffer);
        int probNative = default;
        nint nativeResult;

        TypeFindGetRangeTrampoline.Scope? previous = TypeFindGetRangeTrampoline.Current;
        TypeFindGetRangeTrampoline.Current = new TypeFindGetRangeTrampoline.Scope(func, obj, parent);
        try
        {
            nativeResult = GstTypeFindHelperGetRange(
                obj.Handle, parent is null ? 0 : parent.Handle, TypeFindGetRangeTrampoline.Pointer,
                size, extensionScope.Pointer, &probNative);
        }
        finally
        {
            TypeFindGetRangeTrampoline.Current = previous;
        }

        System.GC.KeepAlive(obj);
        System.GC.KeepAlive(parent);
        prob = (Gst.TypeFindProbability)probNative;
        return Gst.Caps.FromNative(nativeResult, Gst.Interop.Transfer.Full);
    }

    /// <summary>
    /// Finds the type of the data read through a range reading function, and
    /// says why the typefinding ended.
    /// </summary>
    /// <param name="obj">The object the typefinding is for, handed back to <paramref name="func"/>.</param>
    /// <param name="parent">The parent of <paramref name="obj"/>, handed back to <paramref name="func"/>, or <see langword="null"/>.</param>
    /// <param name="func">The function that reads the data; see <see cref="Gst.Base.TypeFindHelperGetRangeFunction"/>.</param>
    /// <param name="size">The length of the data in bytes.</param>
    /// <param name="extension">The extension of the data, or <see langword="null"/>; typefinders that claim it are tried first.</param>
    /// <param name="caps">The caps of the data, or <see langword="null"/> when no type was found.</param>
    /// <param name="prob">The probability of <paramref name="caps"/>.</param>
    /// <returns>
    /// <see cref="Gst.FlowReturn.Ok"/>, or the value of <paramref name="func"/>
    /// that ended the typefinding.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_type_find_helper_get_range_full</c>, written by hand; the
    /// notes on <see cref="Gst.Base.TypeFindHelperGetRangeFunction"/> say how
    /// the function is called.
    /// </para>
    /// <para>
    /// A value other than <see cref="Gst.FlowReturn.Ok"/> can come back
    /// together with non-null <paramref name="caps"/>: the C stores the caps
    /// before it turns a trailing <c>Eos</c> into <c>Error</c>
    /// (<c>gsttypefindhelper.c:443-455</c>), so <paramref name="caps"/> is
    /// always handed out and always has to be disposed.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="obj"/> or <paramref name="func"/> is <see langword="null"/>.
    /// </exception>
    public static Gst.FlowReturn TypeFindHelperGetRangeFull(
        Gst.Object obj, Gst.Object? parent, Gst.Base.TypeFindHelperGetRangeFunction func,
        ulong size, string? extension, out Gst.Caps? caps, out Gst.TypeFindProbability prob)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(func);
        System.Span<byte> extensionBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
        using Gst.Interop.Utf8Scope extensionScope = Gst.Interop.GMarshal.StackUtf8(extension, extensionBuffer);
        nint capsNative = 0;
        int probNative = 0;
        int ret;

        TypeFindGetRangeTrampoline.Scope? previous = TypeFindGetRangeTrampoline.Current;
        TypeFindGetRangeTrampoline.Current = new TypeFindGetRangeTrampoline.Scope(func, obj, parent);
        try
        {
            ret = GstTypeFindHelperGetRangeFull(
                obj.Handle, parent is null ? 0 : parent.Handle, TypeFindGetRangeTrampoline.Pointer,
                size, extensionScope.Pointer, &capsNative, &probNative);
        }
        finally
        {
            TypeFindGetRangeTrampoline.Current = previous;
        }

        System.GC.KeepAlive(obj);
        System.GC.KeepAlive(parent);

        // Taken before the return value is looked at: a non-OK answer can
        // carry caps (gsttypefindhelper.c:449-455).
        caps = Gst.Caps.FromNative(capsNative, Gst.Interop.Transfer.Full);
        prob = (Gst.TypeFindProbability)probNative;
        return (Gst.FlowReturn)ret;
    }

    /// <summary>The native entry point of <see cref="Gst.Base.TypeFindHelperGetRangeFunction"/>.</summary>
    internal static class TypeFindGetRangeTrampoline
    {
        /// <summary>
        /// The typefinding the current thread is running, or
        /// <see langword="null"/> when it runs none.
        /// </summary>
        [ThreadStatic]
        private static Scope? current;

        /// <summary>Gets the address that is handed to native code.</summary>
        internal static nint Pointer =>
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, ulong, uint, nint*, int>)&Invoke;

        /// <summary>
        /// Gets or sets the typefinding the current thread is running.
        /// </summary>
        internal static Scope? Current
        {
            get => current;
            set => current = value;
        }

        /// <summary>Runs the managed function on one range.</summary>
        /// <param name="obj">The object of the typefinding, which the scope already carries.</param>
        /// <param name="parent">The parent of the typefinding, which the scope already carries.</param>
        /// <param name="offset">The offset of the first byte asked for.</param>
        /// <param name="length">The number of bytes asked for.</param>
        /// <param name="buffer">Where the buffer of an <c>Ok</c> answer is stored, with one reference.</param>
        /// <returns>The <c>GstFlowReturn</c> of the function.</returns>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Invoke(nint obj, nint parent, ulong offset, uint length, nint* buffer)
        {
            // Nothing is written to *buffer on a path that does not answer Ok:
            // the helper never reads it then (gsttypefindhelper.c:147-148).
            try
            {
                if (current is not { } scope)
                {
                    // A typefinder that hopped to a thread of its own, or a
                    // call that nothing on this thread started.
                    return (int)Gst.FlowReturn.Error;
                }

                Debug.Assert(obj == scope.Obj.Handle, "The helper handed back another object.");
                Debug.Assert(parent == (scope.Parent is null ? 0 : scope.Parent.Handle), "The helper handed back another parent.");

                Gst.Buffer? value = null;
                try
                {
                    Gst.FlowReturn result = scope.Function(scope.Obj, scope.Parent, offset, length, out value);
                    if (result != Gst.FlowReturn.Ok)
                    {
                        // The wrapper is released below and no reference was
                        // minted, so the buffer does not outlive the call.
                        return (int)result;
                    }

                    if (value is null)
                    {
                        // The helper dereferences the buffer of an Ok answer
                        // without a check (gsttypefindhelper.c:167-168).
                        return (int)Gst.FlowReturn.Error;
                    }

                    nint handle = value.Handle;
                    Gst.GstNative.MiniObjectRef(handle);
                    *buffer = handle;
                    return (int)Gst.FlowReturn.Ok;
                }
                finally
                {
                    value?.Dispose();
                }
            }
            catch (Exception exception)
            {
                Gst.Interop.ExceptionTrap.Report(exception);
                return (int)Gst.FlowReturn.Error;
            }
        }

        /// <summary>One typefinding a thread is running.</summary>
        /// <remarks>
        /// The caller's own wrappers are carried along so that the function is
        /// handed the very instances the member was called with. The scope
        /// lives for the length of the call only.
        /// </remarks>
        internal sealed class Scope
        {
            internal Scope(Gst.Base.TypeFindHelperGetRangeFunction function, Gst.Object obj, Gst.Object? parent)
            {
                Function = function;
                Obj = obj;
                Parent = parent;
            }

            /// <summary>Gets the function that reads the data.</summary>
            internal Gst.Base.TypeFindHelperGetRangeFunction Function { get; }

            /// <summary>Gets the object the typefinding was started with.</summary>
            internal Gst.Object Obj { get; }

            /// <summary>Gets the parent the typefinding was started with.</summary>
            internal Gst.Object? Parent { get; }
        }
    }

    /// <summary>The <c>gst_type_find_helper_get_range</c> entry point.</summary>
    [LibraryImport("GstBase", EntryPoint = "gst_type_find_helper_get_range")]
    private static partial nint GstTypeFindHelperGetRange(nint obj, nint parent, nint func, ulong size, byte* extension, int* prob);

    /// <summary>The <c>gst_type_find_helper_get_range_full</c> entry point.</summary>
    [LibraryImport("GstBase", EntryPoint = "gst_type_find_helper_get_range_full")]
    private static partial int GstTypeFindHelperGetRangeFull(nint obj, nint parent, nint func, ulong size, byte* extension, nint* caps, int* prob);
}
