using System.Runtime.InteropServices;
using Gst.GObject;

namespace Gst;

public sealed partial class Iterator
{
    /// <summary>
    /// Walks the iterator to its end and returns everything it yielded.
    /// </summary>
    /// <returns>
    /// The objects the iterator produced, in the order it produced them. An
    /// iterator over an empty collection returns an empty list.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the managed face of <c>gst_iterator_next</c>, which the
    /// generator does not emit: it fills a <c>GValue</c> the caller allocates,
    /// and answers with a status rather than a value. Without it every method
    /// that hands out an iterator — <see cref="Gst.Bin.IterateElements"/>,
    /// <see cref="Gst.Element.IteratePads"/> and their relatives — is a dead
    /// end, because <c>Copy</c>, <c>Push</c> and <c>Resync</c> are all the
    /// generated class carries.
    /// </para>
    /// <para>
    /// <b>The walk is eager, and that is deliberate.</b> A lazy enumerator
    /// would have to hand the first items out before it knows whether the
    /// collection is going to change underneath it, and there would be no way
    /// to take them back. Reading everything first means a concurrent
    /// modification is invisible: the loop starts over from an empty result and
    /// what the caller sees is one consistent snapshot. It is also the shape
    /// every other collection return of this binding has — see
    /// <c>GListMarshal</c> — so a caller reads pads the same way everywhere.
    /// </para>
    /// <para>
    /// <b>A collection that keeps changing keeps restarting.</b> The library
    /// answers <c>GST_ITERATOR_RESYNC</c> when the collection was updated while
    /// the walk was running; this resyncs and starts over, exactly as the loop
    /// in the C documentation does, and it does not give up after a fixed
    /// number of tries. An application that mutates a bin from another thread
    /// without pause can therefore keep this call busy. Serialise the two, or
    /// read the collection when it has settled.
    /// </para>
    /// <para>
    /// <b>The objects are not the caller's to dispose.</b> Every item comes out
    /// of the <c>GValue</c> as a borrow — <c>g_value_get_object</c> adds no
    /// reference — and is wrapped with
    /// <see cref="Gst.Interop.Transfer.None"/>, so the wrapper takes a
    /// reference of its own and is the interned one: two walks of the same
    /// collection hand back the same instances. GObject wrappers are normally
    /// left to the collector, and disposing one means that this process is done
    /// with the object, for every holder at once. Do not dispose what this
    /// returns unless that is what is meant.
    /// </para>
    /// <para>
    /// The iterator itself is a boxed value that the caller owns and has to
    /// dispose; this does not consume it. Walking it a second time yields
    /// nothing, because an iterator is a cursor and this leaves it at its end —
    /// call <see cref="Resync"/> to read it again, or ask the producer for a
    /// fresh one.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The iterator reported an unrecoverable error, or it does not yield
    /// <c>GObject</c> instances. <see cref="Gst.FormatExtensions.IterateDefinitions"/>
    /// is the one producer of the second kind: its items are boxed
    /// <c>GstFormatDefinition</c> values, which this does not read.
    /// </exception>
    public IReadOnlyList<Gst.GObject.Object> Items() => Items<Gst.GObject.Object>();

    /// <summary>
    /// Walks the iterator to its end and returns everything it yielded, typed.
    /// </summary>
    /// <typeparam name="T">
    /// The type the items are expected to have, for example
    /// <see cref="Gst.Element"/> for <see cref="Gst.Bin.IterateElements"/> or
    /// <see cref="Gst.Pad"/> for <see cref="Gst.Element.IteratePads"/>.
    /// </typeparam>
    /// <returns>The objects the iterator produced, in order.</returns>
    /// <remarks>
    /// See <see cref="Items()"/> for how the walk handles a concurrent
    /// modification and who owns the objects that come back. The type argument
    /// is checked per item rather than assumed: an iterator that is documented
    /// to yield one type but hands out another says so here instead of failing
    /// at the first use of the result.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The iterator reported an unrecoverable error, it does not yield
    /// <c>GObject</c> instances, or an item is not a <typeparamref name="T"/>.
    /// </exception>
    public IReadOnlyList<T> Items<T>()
        where T : Gst.GObject.Object
    {
        // The handle is read once, before the walk, so that a disposed wrapper
        // throws before any value exists that would have to be unset.
        nint handle = Handle;

        List<T> items = [];

        // default is G_VALUE_INIT, which is what gst_iterator_next wants of a
        // value it has not typed yet: it initialises the value to the type of
        // the iterator on the first call.
        Value value = default;

        try
        {
            while (true)
            {
                switch (GstIteratorNext(handle, ref value.NativeValue))
                {
                    case IteratorResult.Ok:
                        items.Add(Read<T>(ref value));

                        // The value holds a reference of the item while it is
                        // set, and the next call wants it clear again. Dispose
                        // is g_value_unset and leaves a zeroed value behind,
                        // which the next call re-initialises.
                        value.Dispose();
                        break;

                    case IteratorResult.Resync:
                        // The collection changed while this was reading it, so
                        // everything read so far belongs to a list that no
                        // longer exists. Nothing has been handed out yet, which
                        // is what makes starting over the whole recovery.
                        items.Clear();
                        value.Dispose();
                        Resync();
                        break;

                    case IteratorResult.Done:
                        return items;

                    default:
                        throw new InvalidOperationException(
                            "The iterator reported an unrecoverable error while it was being read.");
                }
            }
        }
        finally
        {
            // Covers the throwing paths and the early return alike; Dispose is
            // idempotent, so the one the loop already did is not undone.
            value.Dispose();

            // The handle was read before the loop, so nothing else keeps this
            // wrapper alive across the calls that used it.
            GC.KeepAlive(this);
        }
    }

    /// <summary>
    /// Reads one item out of the value the iterator filled.
    /// </summary>
    /// <typeparam name="T">The type the item is expected to have.</typeparam>
    /// <param name="value">The value holding the item.</param>
    /// <returns>The wrapper of the item.</returns>
    /// <exception cref="InvalidOperationException">
    /// The value does not hold a <c>GObject</c>, holds nothing, or holds
    /// something that is not a <typeparamref name="T"/>.
    /// </exception>
    private static T Read<T>(ref Value value)
        where T : Gst.GObject.Object
    {
        GType type = value.Type;

        if (!type.IsA(GType.Object))
        {
            throw new InvalidOperationException(
                $"This iterator yields '{type.Name}', which is not a GObject, and reading one as an object "
                + "would read the wrong half of the value. Only iterators over objects can be walked this way.");
        }

        // g_value_get_object borrows, which is what Transfer.None says: the
        // wrapper takes a reference of its own, so unsetting the value below
        // leaves the object alive and the caller with the interned wrapper.
        Gst.GObject.Object item = value.GetObject()
            ?? throw new InvalidOperationException("The iterator produced a null object.");

        return item as T
            ?? throw new InvalidOperationException(
                $"The iterator produced a '{item.GetType().Name}', which is not a {typeof(T).Name}.");
    }

    /// <summary>The <c>gst_iterator_next</c> entry point.</summary>
    /// <remarks>
    /// The value is passed by address and has to be zeroed or typed to the
    /// iterator; <c>GValueNative</c> is the blittable mirror of a
    /// <c>GValue</c>, and <c>GstIteratorResult</c> is a plain 32 bit
    /// enumeration, so the signature needs no marshalling.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_iterator_next")]
    private static partial IteratorResult GstIteratorNext(nint it, ref GValueNative elem);
}
