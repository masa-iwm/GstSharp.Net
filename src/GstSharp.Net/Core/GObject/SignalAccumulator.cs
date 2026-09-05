using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// How the values several handlers of one emission return are folded into the
/// one value the emission answers.
/// </summary>
/// <remarks>
/// The two named kinds are the accumulators GLib itself exports; a signal that
/// wants another rule has to be defined in C. An accumulator also decides
/// whether the emission continues: both of these stop it as soon as they have
/// their answer, which is what makes a "handled" signal work.
/// </remarks>
public enum SignalAccumulator
{
    /// <summary>
    /// No accumulator: every handler runs and the value of the last one wins.
    /// This is the only choice for a signal that returns nothing.
    /// </summary>
    None = 0,

    /// <summary>
    /// <c>g_signal_accumulator_true_handled</c>: the first handler that returns
    /// <see langword="true"/> stops the emission, and the emission answers
    /// <see langword="true"/>. The signal has to return
    /// <see cref="GType.Boolean"/>.
    /// </summary>
    TrueHandled = 1,

    /// <summary>
    /// <c>g_signal_accumulator_first_wins</c>: the first handler to return at
    /// all stops the emission, and its value is the answer.
    /// </summary>
    FirstWins = 2,
}

/// <summary>
/// The addresses of the two accumulators GLib exports.
/// </summary>
/// <remarks>
/// They are function pointers rather than imports because
/// <c>g_signal_newv</c> is given the address of one, not a call to it. The
/// module is resolved through the loader of this binding, so an accumulator
/// comes out of the same library every other entry point does.
/// </remarks>
internal static class SignalAccumulators
{
    private static readonly Lazy<nint> TrueHandledPointer = new(() => Resolve("g_signal_accumulator_true_handled"));

    private static readonly Lazy<nint> FirstWinsPointer = new(() => Resolve("g_signal_accumulator_first_wins"));

    /// <summary>
    /// Resolves the address <c>g_signal_newv</c> is given for one kind.
    /// </summary>
    /// <param name="accumulator">The kind to resolve.</param>
    /// <returns>The address, or <see cref="nint.Zero"/> for <see cref="SignalAccumulator.None"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="accumulator"/> is not one of the kinds.</exception>
    internal static nint AddressOf(SignalAccumulator accumulator) => accumulator switch
    {
        SignalAccumulator.None => nint.Zero,
        SignalAccumulator.TrueHandled => TrueHandledPointer.Value,
        SignalAccumulator.FirstWins => FirstWinsPointer.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(accumulator)),
    };

    private static nint Resolve(string symbol)
    {
        nint module = NativeLoader.Load("GObject");

        if (!NativeLibrary.TryGetExport(module, symbol, out nint address))
        {
            throw new InvalidOperationException(
                $"The running GObject does not export '{symbol}', so the accumulator cannot be used.");
        }

        return address;
    }
}
