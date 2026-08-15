namespace Gst;

public partial class Bin
{
    /// <summary>
    /// Adds several elements to the bin, in one call.
    /// </summary>
    /// <param name="elements">
    /// The elements to add. An empty array adds nothing and succeeds.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every element was added. The elements are
    /// added in order and the call stops at the first one the bin refuses,
    /// typically because it already has a parent, so a failure leaves the
    /// elements before it in the bin.
    /// </returns>
    /// <remarks>
    /// Each element is added by <see cref="Add(Gst.Element)"/>: the bin becomes
    /// the parent of the element and takes a reference of its own, next to the
    /// one the wrapper holds.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="elements"/>, or one of its entries, is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">A wrapper was disposed.</exception>
    public bool AddMany(params Element[] elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        foreach (Element element in elements)
        {
            ArgumentNullException.ThrowIfNull(element, nameof(elements));

            if (!Add(element))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Removes several elements from the bin, in one call.
    /// </summary>
    /// <param name="elements">
    /// The elements to remove. An empty array removes nothing and succeeds.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every element was removed. The call stops at
    /// the first element the bin refuses to remove.
    /// </returns>
    /// <remarks>
    /// Each element is removed by <see cref="Remove(Gst.Element)"/>, which
    /// releases the reference the bin took when it became the parent. The
    /// wrapper keeps its own reference, so the element stays usable.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="elements"/>, or one of its entries, is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">A wrapper was disposed.</exception>
    public bool RemoveMany(params Element[] elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        foreach (Element element in elements)
        {
            ArgumentNullException.ThrowIfNull(element, nameof(elements));

            if (!Remove(element))
            {
                return false;
            }
        }

        return true;
    }
}
