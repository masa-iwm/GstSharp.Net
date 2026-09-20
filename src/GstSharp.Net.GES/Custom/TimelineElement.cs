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
    /// <c>GError</c>; this one answers only whether the write succeeded, so a
    /// failure is told apart here by looking the name up afterwards: a name
    /// that exists was refused, and one that does not is the unknown-property
    /// case. Only the <c>_full</c> sibling carries the reason behind a
    /// refusal.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="propertyName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// No child of this element has such a property, or
    /// <paramref name="value"/> holds nothing.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A child has the property, but the write was refused.
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

        // FALSE stands for two different things, and the call does not say
        // which: the name matched no child property at all, or it matched one
        // and the write was refused — which is what an OnSetChildPropertyFull
        // override does when it answers false. The lookup here is the very one
        // the native call makes first, so it settles that short of a concurrent
        // change of the children: a name it finds was refused, not missing.
        // Both out parameters are optional and the C only takes a reference
        // when one is given, so passing neither asks the question without
        // fabricating a wrapper for a child this call has no use for.
        bool exists = found == 0 && GesTimelineElementLookupChild(self, scope.Pointer, null, null) != 0;

        GC.KeepAlive(this);

        if (found == 0)
        {
            if (exists)
            {
                throw new InvalidOperationException(
                    $"The \"{propertyName}\" child property of {NativeType.Name} exists, but the write was " +
                    "refused. SetChildPropertyFull reports the reason a refusal carries.");
            }

            throw new ArgumentException(
                $"No child of {NativeType.Name} has a \"{propertyName}\" property. " +
                "A clip has children once it is in a layer of a timeline.",
                nameof(propertyName));
        }
    }

    /// <summary>
    /// Gets the declaration of <c>GESTimelineElement.set_child_property_full</c>, for a
    /// subclass that overrides <see cref="OnSetChildPropertyFull"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slot carries a <c>GError**</c>, which is a shape the generator does
    /// not project, so the override, its chain-up and its trampoline are written
    /// by hand here and are otherwise the generated ones of every other slot of
    /// this class.
    /// </para>
    /// <para>
    /// <b>Installing it takes over every child property write.</b> GES calls
    /// this slot whenever one is asked for and only reaches
    /// <c>set_child_property</c> through the implementation below it
    /// (ges-timeline-element.c:828-829), so an override that does not chain up
    /// stops <see cref="OnSetChildProperty"/> from running at all.
    /// </para>
    /// </remarks>
    public static Gst.GObject.VfuncOverride SetChildPropertyFullOverride { get; } = new(
        &GetGType,
        GES.TimelineElementClassRaw.SetChildPropertyFullOffset,
        (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, Gst.GObject.GValueNative*, nint*, int>)&SetChildPropertyFullTrampoline);

    /// <summary>
    /// Writes a property of one of the children of this element, with a reason
    /// when the write is refused.
    /// </summary>
    /// <param name="child">
    /// The <c>child</c> argument.
    /// The element lends this for the duration of the call. Keeping the wrapper is
    /// safe: a GObject wrapper is interned and its reference outlives the call.
    /// </param>
    /// <param name="pspec">
    /// The <c>pspec</c> argument.
    /// The caller lends this for the duration of the call: the wrapper takes a
    /// reference of its own and gives it back when the override returns, so keep
    /// nothing beyond the call — re-wrap it with ParamSpec.FromNative(pspec.Handle,
    /// Transfer.None) to hold one afterwards.
    /// </param>
    /// <param name="value">
    /// The <c>value</c> argument.
    /// The view points at storage the caller of the slot owns and is only valid
    /// while the call runs; ToValue() copies what it holds. The value may arrive as
    /// a string for a specification of another type — the by name setters go
    /// through gst_util_set_object_arg — so read its Type before a typed getter, or
    /// chain up, which handles that case.
    /// </param>
    /// <param name="error">
    /// Receives the reason a refusal carries, or <see langword="null"/> when the
    /// refusal has none. A refusal without a reason is what GES itself answers
    /// on several of its own paths, so it is a legal answer rather than an
    /// omission; a reason that is to reach the caller needs a domain, which only
    /// the <c>GException(Quark, int, string)</c> constructor gives it.
    /// </param>
    /// <returns>Whether the property was written.</returns>
    /// <remarks>
    /// <para>
    /// The implementation here chains up, which is what keeps
    /// <see cref="OnSetChildProperty"/> reachable: the default implementation of
    /// this slot in GES is the call to that one.
    /// </para>
    /// <para>
    /// An exception that leaves this override is reported through the exception
    /// trap and the write is refused, the way every other slot of the binding
    /// answers a managed exception. A <see cref="Gst.GLib.GException"/> is
    /// forwarded as the reason of that refusal on top of being reported, under
    /// the same rule a returned one is written under; any other exception
    /// leaves the error of the caller untouched. Nothing is synthesised in its
    /// place: the slot documents its error as optionally set, and every caller
    /// in the editing services tolerates a refusal that carries none.
    /// </para>
    /// </remarks>
    protected virtual bool OnSetChildPropertyFull(
        Gst.GObject.Object child,
        Gst.GObject.ParamSpec pspec,
        Gst.GObject.ValueView value,
        out Gst.GLib.GException? error) =>
        ChainUpSetChildPropertyFull(child, pspec, value, out error);

    /// <summary>Runs the implementation of <c>set_child_property_full</c> below the managed override.</summary>
    /// <param name="child">The child that carries the property.</param>
    /// <param name="pspec">The specification of the property.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="error">Receives the reason of a refusal, or <see langword="null"/>.</param>
    /// <returns>Whether the property was written.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    protected bool ChainUpSetChildPropertyFull(
        Gst.GObject.Object child,
        Gst.GObject.ParamSpec pspec,
        Gst.GObject.ValueView value,
        out Gst.GLib.GException? error)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(pspec);
        using Gst.GObject.Value valueCopy = value.ToValue();
        Gst.GObject.GValueNative* valueNative = &valueCopy.NativeValue;
        nint errorNative = nint.Zero;
        bool result = ChainUpSetChildPropertyFull(Handle, child.Handle, pspec.Handle, valueNative, &errorNative);
        GC.KeepAlive(this);
        GC.KeepAlive(child);
        GC.KeepAlive(pspec);

        // The error of the implementation below belongs to this call: it is read
        // into a value of its own and released here, so that what the override
        // above gives back outlives the frame the pointer lived in.
        error = Gst.GLib.GException.FromBorrowed(errorNative);
        if (errorNative != nint.Zero)
        {
            GLibNative.ErrorFree(errorNative);
        }

        return result;
    }

    private static bool ChainUpSetChildPropertyFull(
        nint self,
        nint child,
        nint pspec,
        Gst.GObject.GValueNative* value,
        nint* error)
    {
        delegate* unmanaged[Cdecl]<nint, nint, nint, Gst.GObject.GValueNative*, nint*, int> slot =
            (delegate* unmanaged[Cdecl]<nint, nint, nint, Gst.GObject.GValueNative*, nint*, int>)
                ParentClassOf(self)->SetChildPropertyFull;

        if (slot is null)
        {
            throw new InvalidOperationException(
                "TimelineElement.set_child_property_full has no parent implementation; override OnSetChildPropertyFull.");
        }

        return slot(self, child, pspec, value, error) != 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int SetChildPropertyFullTrampoline(
        nint self,
        nint child,
        nint pspec,
        Gst.GObject.GValueNative* value,
        nint* error)
    {
        try
        {
            if (Gst.GObject.Object.TryGetOrFabricate(self) is not TimelineElement managed)
            {
                return (ChainUpSetChildPropertyFull(self, child, pspec, value, error)) ? 1 : 0;
            }

            Gst.GObject.Object? childValue = Gst.GObject.Object.FromNative<Gst.GObject.Object>(child, Transfer.None);
            using Gst.GObject.ParamSpec? pspecValue = pspec == nint.Zero ? null : Gst.GObject.ParamSpec.FromNative(pspec, Transfer.None);
            Gst.GObject.ValueView valueValue = value != null
                ? new Gst.GObject.ValueView(ref *value)
                : throw new InvalidOperationException("set_child_property_full passed no value.");

            if (managed.OnSetChildPropertyFull(childValue!, pspecValue!, valueValue, out Gst.GLib.GException? failure))
            {
                return 1;
            }

            WriteRefusal(error, failure);
            return 0;
        }
        catch (Gst.GLib.GException refusal)
        {
            // A throw is still a bug in the override — the slot answers a
            // refusal, it does not raise one — so it is reported the way any
            // other escaping exception is. The error it carries is a reason
            // the caller can read, though, so it is forwarded under the same
            // rule a returned one is written under rather than dropped.
            ExceptionTrap.Report(refusal);
            WriteRefusal(error, refusal);
            return 0;
        }
        catch (Exception exception)
        {
            ExceptionTrap.Report(exception);
            return default;
        }
    }

    /// <summary>
    /// Writes the reason of a refusal, unless there is no room for one.
    /// </summary>
    /// <param name="error">The <c>GError**</c> the caller passed, which may be null.</param>
    /// <param name="failure">What the override reported, or null.</param>
    /// <remarks>
    /// The pointer is shared across the children a write walks
    /// (ges-timeline-element.c:855), so an error that is already there is left
    /// alone: g_set_error warns and drops the second one, and the first refusal
    /// is the one that describes the write. An error GLib would refuse — no
    /// domain, no message, or a message with an embedded null — is dropped as
    /// well, since a refusal that carries nothing is a legal answer of this slot
    /// and a null error is not.
    /// </remarks>
    private static void WriteRefusal(nint* error, Gst.GLib.GException? failure)
    {
        if (error is null || *error != nint.Zero || failure is null)
        {
            return;
        }

        if (failure.Domain.Value == 0
            || string.IsNullOrEmpty(failure.Message)
            || failure.Message.Contains('\0', StringComparison.Ordinal))
        {
            return;
        }

        nint text = GMarshal.StringToUtf8Ptr(failure.Message);
        try
        {
            *error = GLibNative.ErrorNewLiteral(failure.Domain.Value, failure.Code, (byte*)text);
        }
        finally
        {
            GMarshal.Free(text);
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
