using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace GES;

/// <content>
/// The child properties, which the generator skips because they travel in a
/// <c>GValue</c>.
/// </content>
/// <remarks>
/// <para>
/// A timeline element is a facade in front of the <c>GstElement</c>s that do
/// the work — a test clip is an <c>audiotestsrc</c> and a <c>videotestsrc</c>
/// under a pair of track elements — and the editing services expose the
/// properties of those elements as child properties of the timeline element.
/// The pattern, the frequency and the position of a clip are reached this way
/// and no other, so without these two the generated surface of a clip stops at
/// the properties GES itself declares.
/// </para>
/// <para>
/// Both calls are the child property analogue of
/// <see cref="Gst.GObject.Object.GetProperty(string)"/> and
/// <see cref="Gst.GObject.Object.SetProperty(string, in Gst.GObject.Value)"/>,
/// down to the exception that an unknown name raises. The one difference is
/// where the type of the value comes from: the plain property accessor looks
/// the parameter specification up itself and initialises the value from it,
/// while GES does that lookup inside the call and initialises the value on the
/// way through.
/// </para>
/// </remarks>
public abstract unsafe partial class TimelineElement
{
    /// <summary>
    /// Reads a property of one of the children of this element.
    /// </summary>
    /// <param name="propertyName">
    /// The name of the child property, either as <c>prop-name</c> or as
    /// <c>TypeName::prop-name</c> when two children of different types have a
    /// property of the same name.
    /// </param>
    /// <returns>
    /// A copy of the property, which the caller has to dispose. Its
    /// <see cref="Gst.GObject.Value.Type"/> is the type the child declares the
    /// property as.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>ges_timeline_element_get_child_property</c> takes a <c>GValue</c>
    /// that the caller allocates and initialises it from the parameter
    /// specification of the child before copying the property into it, so the
    /// value handed to it here is an empty one and the type of the result is
    /// whatever the child says it is.
    /// </para>
    /// <para>
    /// A clip only has children once it is in a layer of a timeline: that is
    /// when its track elements are created, and they are what carries the
    /// properties. Asking a clip that is not in a layer therefore fails the way
    /// an unknown name does.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="propertyName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No child of this element has such a property.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.GObject.Value GetChildProperty(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        nint self = Handle;

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(propertyName, buffer);

        // An empty value: the call initialises it from the parameter
        // specification of the child it finds, and leaves it untouched when it
        // finds none.
        Gst.GObject.Value value = default;

        // A local lives on the stack, so its address is already fixed.
        int found = GesTimelineElementGetChildProperty(self, scope.Pointer, &value.NativeValue);

        // Handle was the last use of this wrapper.
        GC.KeepAlive(this);

        if (found == 0)
        {
            throw new ArgumentException(
                $"No child of {NativeType.Name} has a \"{propertyName}\" property. " +
                "A clip has children once it is in a layer of a timeline.",
                nameof(propertyName));
        }

        return value;
    }

    /// <summary>
    /// Writes a property of one of the children of this element.
    /// </summary>
    /// <param name="propertyName">
    /// The name of the child property, either as <c>prop-name</c> or as
    /// <c>TypeName::prop-name</c>.
    /// </param>
    /// <param name="value">
    /// The new value. The child takes a copy of it, so the caller keeps and
    /// disposes the one it passed.
    /// </param>
    /// <remarks>
    /// <para>
    /// The value has to hold the type the child declares the property as.
    /// <see cref="GetChildProperty(string)"/> is the way to find out what that
    /// is without knowing the child: the value it returns carries the type, and
    /// a value created with <c>Value.New(read.Type)</c> is one the child
    /// accepts.
    /// </para>
    /// <para>
    /// This is <c>ges_timeline_element_set_child_property</c>. Its
    /// <c>_full</c> sibling reports why a write failed through a
    /// <c>GError</c>; this one answers only whether the property was found,
    /// which is what the exception below says.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="propertyName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// No child of this element has such a property, or
    /// <paramref name="value"/> holds nothing.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetChildProperty(string propertyName, in Gst.GObject.Value value)
    {
        ArgumentNullException.ThrowIfNull(propertyName);

        if (value.IsEmpty)
        {
            throw new ArgumentException(
                "An empty value cannot be written to a child property: it has no type.",
                nameof(value));
        }

        nint self = Handle;

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(propertyName, buffer);

        int found;
        fixed (Gst.GObject.GValueNative* native = &Unsafe.AsRef(in value).NativeValue)
        {
            found = GesTimelineElementSetChildProperty(self, scope.Pointer, native);
        }

        // Handle was the last use of this wrapper.
        GC.KeepAlive(this);

        if (found == 0)
        {
            throw new ArgumentException(
                $"No child of {NativeType.Name} has a \"{propertyName}\" property. " +
                "A clip has children once it is in a layer of a timeline.",
                nameof(propertyName));
        }
    }

    /// <summary>The <c>ges_timeline_element_get_child_property</c> entry point.</summary>
    /// <remarks>
    /// The value travels as a pointer rather than as a <c>ref</c>. A
    /// <c>GValueNative</c> is blittable, but it comes from another assembly
    /// here, and the source generator of <c>LibraryImport</c> will only take a
    /// by-reference parameter of a type it can prove blittable inside the
    /// compilation it runs in — everywhere else it asks for the whole assembly
    /// to disable runtime marshalling, which this repository does not do. A
    /// pointer needs no such proof and marshals identically.
    /// </remarks>
    [LibraryImport("GES", EntryPoint = "ges_timeline_element_get_child_property")]
    private static partial int GesTimelineElementGetChildProperty(
        nint self,
        byte* propertyName,
        Gst.GObject.GValueNative* value);

    /// <summary>The <c>ges_timeline_element_set_child_property</c> entry point.</summary>
    [LibraryImport("GES", EntryPoint = "ges_timeline_element_set_child_property")]
    private static partial int GesTimelineElementSetChildProperty(
        nint self,
        byte* propertyName,
        Gst.GObject.GValueNative* value);
}
