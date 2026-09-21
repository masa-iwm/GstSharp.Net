using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GES;

/// <summary>
/// The function <see cref="GES.MetaContainerExtensions.Foreach"/> calls for
/// every metadata field of a container.
/// </summary>
/// <param name="container">
/// The container the walk was started on, which is the very object it was
/// called with.
/// </param>
/// <param name="key">The key of the field.</param>
/// <param name="value">
/// The value stored under the key. The view is valid for the length of the
/// invocation only, so read what is needed out of it before returning.
/// </param>
/// <remarks>
/// <para>
/// The container is not locked while the walk runs, so the function must not
/// register, set or remove metadata of that container
/// (<c>ges-meta-container.c:185-202</c>).
/// </para>
/// <para>
/// An exception that leaves the function is reported through
/// <see cref="Gst.Interop.ExceptionTrap"/> and the walk goes on, because the C
/// callback has no way of ending it.
/// </para>
/// </remarks>
public delegate void MetaForeachFunc(GES.IMetaContainer container, string key, Gst.GObject.ValueView value);

/// <content>
/// The walk over the metadata of a container, which the generator cannot emit.
/// </content>
public static unsafe partial class MetaContainerExtensions
{
    /// <summary>
    /// Calls a function for every metadata field of a container.
    /// </summary>
    /// <param name="container">The container whose metadata is walked.</param>
    /// <param name="func">The function to call for each field.</param>
    /// <remarks>
    /// <para>
    /// This is <c>ges_meta_container_foreach</c>, written by hand because its
    /// callback is handed the container as an interface pointer and the
    /// generator has no projection of an interface typed value. The C hands the
    /// receiver of the walk back (<c>ges-meta-container.c:197</c>), so the
    /// trampoline hands the managed receiver over instead of wrapping the
    /// pointer a second time.
    /// </para>
    /// <para>
    /// The function is called on the calling thread and every call it gets has
    /// happened before this method returns; nothing is kept of it afterwards.
    /// </para>
    /// <para>
    /// An exception thrown by the function is reported through
    /// <see cref="Gst.Interop.ExceptionTrap"/> rather than thrown here, because
    /// it would otherwise unwind through the native frames of the walk.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="container"/> or <paramref name="func"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The wrapper of <paramref name="container"/> was disposed.
    /// </exception>
    public static void Foreach(this GES.IMetaContainer container, GES.MetaForeachFunc func)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(func);
        nint instanceHandle = container.Handle;
        Gst.Interop.CallbackHandle funcState =
            Gst.Interop.CallbackHandle.Alloc(new MetaForeachState(container, func));
        try
        {
            GesMetaContainerForeach(instanceHandle, MetaForeachTrampoline.Pointer, funcState.UserData);
            System.GC.KeepAlive(container);
        }
        finally
        {
            funcState.Free();
        }
    }

    /// <summary>The state one walk over the metadata of a container carries.</summary>
    /// <remarks>
    /// The container is carried along so that the function is handed the very
    /// object the walk was started on rather than a second wrapper of it.
    /// </remarks>
    private sealed class MetaForeachState
    {
        internal MetaForeachState(GES.IMetaContainer container, GES.MetaForeachFunc function)
        {
            Container = container;
            Function = function;
        }

        /// <summary>Gets the container the walk was started on.</summary>
        internal GES.IMetaContainer Container { get; }

        /// <summary>Gets the function to call for each field.</summary>
        internal GES.MetaForeachFunc Function { get; }
    }

    /// <summary>The native entry point of <see cref="GES.MetaForeachFunc"/>.</summary>
    internal static class MetaForeachTrampoline
    {
        /// <summary>Gets the address that is handed to native code.</summary>
        internal static nint Pointer =>
            (nint)(delegate* unmanaged[Cdecl]<nint, byte*, Gst.GObject.GValueNative*, nint, void>)&Invoke;

        /// <summary>Runs the managed function on one metadata field.</summary>
        /// <param name="container">The container that is walked, which the state already carries.</param>
        /// <param name="key">The key of the field.</param>
        /// <param name="value">The value of the field, borrowed for the invocation.</param>
        /// <param name="userData">The <c>GCHandle</c> of the state of the walk.</param>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Invoke(nint container, byte* key, Gst.GObject.GValueNative* value, nint userData)
        {
            _ = container;

            try
            {
                if (Gst.Interop.CallbackHandle.GetState<MetaForeachState>(userData) is not { } state)
                {
                    return;
                }

                string keyValue = Gst.Interop.GMarshal.PtrToStringUtf8((nint)key)
                    ?? throw new InvalidOperationException("GESMetaForeachFunc passed no key.");
                Gst.GObject.ValueView valueValue = value != null
                    ? new Gst.GObject.ValueView(ref *value)
                    : throw new InvalidOperationException("GESMetaForeachFunc passed no value.");
                state.Function(state.Container, keyValue, valueValue);
            }
            catch (Exception exception)
            {
                Gst.Interop.ExceptionTrap.Report(exception);
            }
        }
    }

    /// <summary>The <c>ges_meta_container_foreach</c> entry point.</summary>
    [LibraryImport("GES", EntryPoint = "ges_meta_container_foreach")]
    private static partial void GesMetaContainerForeach(nint container, nint func, nint userData);
}
