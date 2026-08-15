using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst.Interop;

/// <summary>
/// Lifetime of the managed state behind a callback that is handed to native
/// code.
/// </summary>
public enum CallbackScope
{
    /// <summary>
    /// The callback is only invoked while the call that receives it is running.
    /// The state can be released as soon as that call returns.
    /// </summary>
    Call,

    /// <summary>
    /// The callback is invoked exactly once, when the asynchronous operation it
    /// was handed to completes. The state is released by that invocation.
    /// </summary>
    Async,

    /// <summary>
    /// The callback can be invoked any number of times until native code runs
    /// the destroy notification that releases the state.
    /// </summary>
    Notified,
}

/// <summary>
/// A managed callback state that is passed to native code as the
/// <c>user_data</c> pointer.
/// </summary>
/// <remarks>
/// The state is kept alive by a <see cref="GCHandle"/>. Native code that owns
/// the callback releases it again through
/// <see cref="DestroyNotify"/> (a <c>GDestroyNotify</c>) or
/// <see cref="ClosureNotify"/> (a <c>GClosureNotify</c>, as used by
/// <c>g_signal_connect_data</c>).
/// </remarks>
public readonly unsafe struct CallbackHandle : IEquatable<CallbackHandle>
{
    private readonly nint _userData;

    private CallbackHandle(nint userData) => _userData = userData;

    /// <summary>
    /// Gets the <c>GDestroyNotify</c> that releases the state of a
    /// <see cref="CallbackHandle"/>.
    /// </summary>
    public static delegate* unmanaged[Cdecl]<nint, void> DestroyNotify => &DestroyNotifyTrampoline;

    /// <summary>
    /// Gets the <c>GClosureNotify</c> that releases the state of a
    /// <see cref="CallbackHandle"/>.
    /// </summary>
    public static delegate* unmanaged[Cdecl]<nint, nint, void> ClosureNotify => &ClosureNotifyTrampoline;

    /// <summary>
    /// Gets the pointer that is passed to native code as <c>user_data</c>.
    /// </summary>
    public nint UserData => _userData;

    /// <summary>
    /// Gets a value indicating whether this handle refers to allocated state.
    /// </summary>
    public bool IsAllocated => _userData != nint.Zero;

    /// <summary>Compares two handles for equality.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true"/> when both refer to the same state.</returns>
    public static bool operator ==(CallbackHandle left, CallbackHandle right) => left.Equals(right);

    /// <summary>Compares two handles for inequality.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true"/> when both refer to different state.</returns>
    public static bool operator !=(CallbackHandle left, CallbackHandle right) => !left.Equals(right);

    /// <summary>
    /// Allocates a handle for <paramref name="state"/>.
    /// </summary>
    /// <param name="state">The managed state of the callback.</param>
    /// <returns>The new handle.</returns>
    public static CallbackHandle Alloc(object state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new CallbackHandle(GCHandle.ToIntPtr(GCHandle.Alloc(state)));
    }

    /// <summary>
    /// Wraps a <c>user_data</c> pointer that was produced by
    /// <see cref="Alloc"/>.
    /// </summary>
    /// <param name="userData">The pointer that native code passed back.</param>
    /// <returns>The handle for <paramref name="userData"/>.</returns>
    public static CallbackHandle FromUserData(nint userData) => new(userData);

    /// <summary>
    /// Reads the state behind a <c>user_data</c> pointer.
    /// </summary>
    /// <typeparam name="T">The expected type of the state.</typeparam>
    /// <param name="userData">The pointer that native code passed back.</param>
    /// <returns>
    /// The state, or <see langword="null"/> when the pointer is
    /// <see cref="nint.Zero"/> or the state has a different type.
    /// </returns>
    public static T? GetState<T>(nint userData)
        where T : class
    {
        if (userData == nint.Zero)
        {
            return null;
        }

        return GCHandle.FromIntPtr(userData).Target as T;
    }

    /// <summary>
    /// Releases the state of this handle.
    /// </summary>
    public void Free()
    {
        if (_userData != nint.Zero)
        {
            GCHandle.FromIntPtr(_userData).Free();
        }
    }

    /// <inheritdoc/>
    public bool Equals(CallbackHandle other) => _userData == other._userData;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CallbackHandle other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _userData.GetHashCode();

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void DestroyNotifyTrampoline(nint userData)
    {
        try
        {
            FromUserData(userData).Free();
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ClosureNotifyTrampoline(nint userData, nint closure)
    {
        _ = closure;

        try
        {
            FromUserData(userData).Free();
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
        }
    }
}
