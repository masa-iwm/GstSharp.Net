namespace Gst;

public abstract partial class Element
{
    /// <summary>
    /// Links this element to the first of <paramref name="elements"/>, that one
    /// to the next, and so on, so that a whole chain is linked in one call.
    /// </summary>
    /// <param name="elements">
    /// The elements to link, in the order in which the data flows. An empty
    /// chain links nothing and succeeds.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every pair was linked. The chain is linked
    /// left to right and stops at the first pair that GStreamer refuses, so a
    /// failure leaves the pairs before it linked; the caller decides whether to
    /// tear the elements down or to try a different chain.
    /// </returns>
    /// <remarks>
    /// Each pair is linked by <see cref="Link(Gst.Element)"/>, which is what
    /// linking two elements calls directly; this overload only exists to spare
    /// the caller the chain of <c>&amp;&amp;</c>. All elements have to live in
    /// the same bin already.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="elements"/>, or one of its entries, is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">A wrapper was disposed.</exception>
    public bool Link(params Element[] elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        Element source = this;
        foreach (Element sink in elements)
        {
            ArgumentNullException.ThrowIfNull(sink, nameof(elements));

            if (!source.Link(sink))
            {
                return false;
            }

            source = sink;
        }

        return true;
    }

    /// <summary>
    /// Unlinks the chain that <see cref="Link(Gst.Element[])"/> established:
    /// this element from the first of <paramref name="elements"/>, that one
    /// from the next, and so on.
    /// </summary>
    /// <param name="elements">The elements of the chain, in the same order.</param>
    /// <remarks>
    /// Like <see cref="Unlink(Gst.Element)"/>, unlinking a pair that is not
    /// linked does nothing, so this reports no result.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="elements"/>, or one of its entries, is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">A wrapper was disposed.</exception>
    public void Unlink(params Element[] elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        Element source = this;
        foreach (Element sink in elements)
        {
            ArgumentNullException.ThrowIfNull(sink, nameof(elements));

            source.Unlink(sink);
            source = sink;
        }
    }
}
