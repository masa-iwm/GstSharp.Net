using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst.Pbutils;

/// <summary>
/// Reports the outcome of a plugin installation that
/// <see cref="PbutilsGlobal.InstallPluginsAsync"/> started.
/// </summary>
/// <param name="result">Whether the requested plugins could be installed.</param>
/// <remarks>
/// The function runs on the thread that drives the default main context, once
/// the installer helper has exited. It is invoked exactly once, and only when
/// the call that installed it answered
/// <see cref="InstallPluginsReturn.StartedOk"/>.
/// </remarks>
public delegate void InstallPluginsResultFunc(InstallPluginsReturn result);

public static unsafe partial class PbutilsGlobal
{
    /// <summary>
    /// Asks the installer helper of the platform for a set of missing plugins,
    /// without blocking.
    /// </summary>
    /// <param name="details">
    /// The installer details, typically obtained by calling
    /// <see cref="MissingPluginMessageGetInstallerDetail"/> on the
    /// missing-plugin messages a pipeline posted on its bus.
    /// </param>
    /// <param name="ctx">The context of the request, or <see langword="null"/>.</param>
    /// <param name="func">What to call with the result of the installation.</param>
    /// <returns>Whether an external installer could be started.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_install_plugins_async</c>. It needs a running GLib main
    /// loop, or at least a regular
    /// <c>g_main_context_iteration</c> of the default context: the helper is
    /// watched through a child watch source, and nothing calls
    /// <paramref name="func"/> while nothing iterates the context.
    /// </para>
    /// <para>
    /// <paramref name="func"/> is called exactly once, and only when this
    /// member answers <see cref="InstallPluginsReturn.StartedOk"/>. Every other
    /// return means no helper was started, so the state of the delegate is
    /// released here and the delegate is never called; that is the ordinary
    /// outcome on a platform that ships no installer helper. It is why the
    /// member is written by hand rather than generated.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="details"/> or <paramref name="func"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="details"/> is empty, or one of its elements is
    /// <see langword="null"/> or holds an embedded NUL. The vector handed to
    /// the library is NUL terminated, so a null element would end it early and
    /// silently drop every detail behind it rather than asking for it.
    /// </exception>
    public static InstallPluginsReturn InstallPluginsAsync(
        string[] details,
        InstallPluginsContext? ctx,
        InstallPluginsResultFunc func)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(func);

        // The library reads the details out of a NUL terminated vector, so an
        // empty request asks for nothing and is refused rather than passed on.
        // A null element or one with an embedded NUL would end the vector early
        // and silently drop every detail behind it; the scope below refuses
        // both, and it releases whatever it had already allocated when it does.
        if (details.Length == 0)
        {
            throw new ArgumentException("At least one installer detail is required.", nameof(details));
        }

        nint context = ctx?.Handle ?? 0;

        // The vector owns both allocations it holds and frees them on every
        // path out of this member, including the one an embedded NUL takes.
        using Gst.Interop.StrvScope detailsScope = Gst.Interop.GMarshal.AllocStrv(details);

        // The strings are marshalled first, because an element the vector
        // refuses throws above. The handle is allocated once nothing between it
        // and the call can throw any more, so a refused request cannot leak one.
        Gst.Interop.CallbackHandle state = Gst.Interop.CallbackHandle.Alloc(func);

        InstallPluginsReturn result = InstallPluginsNative.InstallPluginsAsync(
            detailsScope.Pointer,
            context,
            (nint)(delegate* unmanaged[Cdecl]<InstallPluginsReturn, nint, void>)&InstallPluginsResultTrampoline,
            state.UserData);

        GC.KeepAlive(ctx);

        if (result != InstallPluginsReturn.StartedOk)
        {
            // No helper was started, so nothing will ever call the delegate.
            state.Free();
        }

        return result;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void InstallPluginsResultTrampoline(InstallPluginsReturn result, nint userData)
    {
        try
        {
            Gst.Interop.CallbackHandle.GetState<InstallPluginsResultFunc>(userData)?.Invoke(result);
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
        }
        finally
        {
            Gst.Interop.CallbackHandle.FromUserData(userData).Free();
        }
    }
}
