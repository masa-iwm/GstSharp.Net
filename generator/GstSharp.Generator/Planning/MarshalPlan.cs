using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Planning;

/// <summary>
/// The C# shape a callable is emitted in.
/// </summary>
internal enum CallableForm
{
    /// <summary>An instance method; the gir instance parameter becomes <c>this</c>.</summary>
    InstanceMethod,

    /// <summary>A static method.</summary>
    StaticMethod,

    /// <summary>A static factory method that wraps a gir constructor.</summary>
    Constructor,

    /// <summary>An extension method; the instance is the first parameter.</summary>
    ExtensionMethod,
}

/// <summary>
/// How one argument crosses the interop boundary.
/// </summary>
internal enum ArgumentKind
{
    /// <summary>The instance the method is called on.</summary>
    Instance,

    /// <summary>
    /// The instance a method of a value projected structure is called on. The
    /// C function takes a pointer to the structure and the public surface has
    /// no wrapper to read a handle out of, so the address is the address of
    /// <c>this</c>, pinned by a <c>fixed</c> scope that wraps the call. The
    /// argument is never visible, and the member is <c>readonly</c> when the
    /// gir spells the instance <c>const</c>.
    /// </summary>
    ValueInstance,

    /// <summary>Passed through unchanged.</summary>
    Value,

    /// <summary>A <c>gboolean</c>.</summary>
    Boolean,

    /// <summary>A generated enumeration or bitfield.</summary>
    Enumeration,

    /// <summary>A blittable value with a wrapper on the public surface, such as <c>GstClockTime</c>.</summary>
    Wrapper,

    /// <summary>An untyped pointer, passed as a native integer.</summary>
    Pointer,

    /// <summary>A string the callee only reads.</summary>
    Utf8,

    /// <summary>A string the callee takes ownership of.</summary>
    Utf8Owned,

    /// <summary>An instance behind a handle.</summary>
    Handle,

    /// <summary>
    /// An instance behind a handle whose ownership the callee takes
    /// (<c>transfer-ownership="full"</c> on an <c>in</c> parameter). The call
    /// is handed a value minted for it — a reference for a mini object or a
    /// GObject, a copy for a boxed value; which one is stated by
    /// <see cref="ArgumentPlan.ConsumedFamily"/> — and the wrapper is disposed
    /// when the member returns, whatever the call answered.
    /// </summary>
    ConsumedHandle,

    /// <summary>
    /// A boxed record the callee fills through storage the caller provides
    /// (<c>caller-allocates="1"</c> on an <c>out</c> parameter). The binding
    /// allocates that storage from the zero argument constructor of the record
    /// named by <see cref="ArgumentPlan.StorageFactory"/>, hands the call the
    /// bare pointer, and wraps it afterwards: the caller owns the wrapper and
    /// disposes it. A callee that answers a <c>gboolean</c> filled nothing when
    /// it answered false, so the storage is freed again and the parameter is
    /// <see langword="null"/>.
    /// </summary>
    CallerAllocatedBoxed,

    /// <summary>
    /// A <c>GValue</c>, passed as a pointer to storage the caller owns. The
    /// public surface takes the runtime <c>Gst.GObject.Value</c> struct by
    /// <c>in</c>, <c>ref</c> or <c>out</c>; nothing is allocated for the call
    /// and nothing is disposed after it. A returned <c>GValue</c> travels as a
    /// bare pointer and becomes a value of the caller's own: copied when it is
    /// borrowed, adopted — contents moved, shell freed — when the call
    /// transfers it.
    /// </summary>
    GValue,

    /// <summary>
    /// A <c>GValue</c> a callback is <em>handed</em>, as a pointer into storage
    /// that the caller of the callback owns and keeps. It is projected onto a
    /// view rather than onto <c>Gst.GObject.Value</c>, which owns its payload
    /// and could not wrap one: a <c>const GValue*</c> becomes
    /// <c>Gst.GObject.ValueView</c> and a writable <c>GValue*</c> becomes
    /// <c>Gst.GObject.ValueRef</c>. Both are <c>ref struct</c>s, so the compiler
    /// keeps them from outliving the invocation they arrived on — which is the
    /// contract: the item <c>gst_iterator_fold</c> hands out is a stack
    /// <c>GValue</c> that is reset after every call. The trampoline builds the
    /// view and does nothing afterwards; no ownership crosses either way.
    /// </summary>
    BorrowedGValue,

    /// <summary>
    /// A <c>GError</c> the callee only borrows, projected onto
    /// <c>Gst.GLib.GException</c>. An <c>in</c> parameter is built into a
    /// temporary native error for the duration of the call; a borrowed return
    /// is read into a managed exception value and never freed.
    /// </summary>
    GError,

    /// <summary>
    /// A <c>GDate</c>, projected onto <c>System.DateOnly</c>. Nothing is
    /// wrapped: an <c>in</c> parameter is built into a temporary the scope
    /// around the call frees, and an <c>out</c> parameter is read out of the
    /// value the call allocated and freed again, which makes the public
    /// parameter a <c>DateOnly?</c> — a call may answer <c>true</c> and leave
    /// no date behind.
    /// </summary>
    Date,

    /// <summary>A blittable structure, passed by value or through a pointer.</summary>
    PlainStruct,

    /// <summary>A C array the callee only reads, passed as a span.</summary>
    Span,

    /// <summary>
    /// A fixed size array the callee writes into storage the caller provides,
    /// passed as an <c>out</c> parameter of the inline storage type named by
    /// <see cref="ArgumentPlan.FixedArray"/>.
    /// </summary>
    FixedArrayOut,

    /// <summary>A C array that the call produces.</summary>
    ArrayOut,

    /// <summary>A <c>NULL</c> terminated array of strings.</summary>
    Strv,

    /// <summary>
    /// A <c>GList</c> or a <c>GSList</c> that a call is given, built out of an
    /// <c>IEnumerable</c> of the element type. It has exactly two shapes, which
    /// <see cref="ArgumentPlan.Transfer"/> tells apart. A <em>borrowed</em>
    /// list (<c>none</c>) is built into a scope that releases the spine, and
    /// everything that was allocated for it, when the call returns. A
    /// <em>consumed</em> list (<c>full</c>) is built with one value minted per
    /// element and handed over: the callee owns the spine and the minted values
    /// from the moment the call is made, and nothing releases either
    /// afterwards. The element projection is carried by
    /// <see cref="ArgumentPlan.ElementKind"/> and
    /// <see cref="ArgumentPlan.Flavor"/>, and
    /// <see cref="ArgumentPlan.IsSinglyLinked"/> says which of the two GLib
    /// list types the spine is.
    /// </summary>
    ListIn,

    /// <summary>
    /// A <c>GList</c> that a call returned, materialized into a read only list.
    /// The element projection is carried by <see cref="ReturnPlan.ElementKind"/>
    /// and <see cref="ReturnPlan.Flavor"/>.
    /// </summary>
    GListReturn,

    /// <summary>A managed callback handed to native code.</summary>
    Callback,

    /// <summary>The user data of a callback; never visible.</summary>
    UserData,

    /// <summary>The <c>GDestroyNotify</c> of a callback; never visible.</summary>
    DestroyNotify,

    /// <summary>The length of an array; never visible.</summary>
    ArrayLength,

    /// <summary>The <c>GError</c> of a callable that throws; never visible.</summary>
    Error,

    /// <summary>Nothing at all, for a <c>void</c> return.</summary>
    Void,
}

/// <summary>
/// How an argument is passed in C#.
/// </summary>
internal enum ArgumentDirection
{
    /// <summary>By value.</summary>
    In,

    /// <summary>As an <c>out</c> parameter.</summary>
    Out,

    /// <summary>As a <c>ref</c> parameter.</summary>
    Ref,
}

/// <summary>
/// The kind of wrapper a handle is turned into.
/// </summary>
internal enum HandleFlavor
{
    /// <summary>Not a handle.</summary>
    None,

    /// <summary>A <c>GObject</c>, wrapped through <c>Gst.GObject.Object.FromNative</c>.</summary>
    GObject,

    /// <summary>A mini object or a boxed type, which has a typed <c>FromNative</c>.</summary>
    Wrapper,

    /// <summary>An opaque record, whose wrapper is a bare pointer holder.</summary>
    Opaque,

    /// <summary>
    /// A <c>GParamSpec</c>, wrapped by the hand written
    /// <c>Gst.GObject.ParamSpec</c>. It is not a <c>GObject</c> and has no
    /// generated wrapper, so it is constructed directly; under
    /// <c>transfer-ownership="none"</c> the constructor takes a reference of
    /// its own, which makes the wrapper owned by whoever created it.
    /// </summary>
    ParamSpec,
}

/// <summary>
/// What a consuming call is handed for an argument it takes over, which the
/// wrapper family of the argument decides.
/// </summary>
internal enum ConsumedFamily
{
    /// <summary>The argument is not consumed.</summary>
    None,

    /// <summary>A mini object; the call is handed a reference of its own.</summary>
    MiniObject,

    /// <summary>
    /// A boxed value; the call is handed a copy, because a boxed value has no
    /// reference count to raise — the copy is what a reference is there.
    /// </summary>
    Boxed,

    /// <summary>A <c>GObject</c>; the call is handed a reference of its own.</summary>
    GObject,
}

/// <summary>
/// How a call takes over the reference of the instance it is called on, which
/// is the shape of the <c>make_writable</c> family and of the conversions that
/// hand a replacement back.
/// </summary>
/// <remarks>
/// <para>
/// The gir spells both the same way — the instance is
/// <c>transfer-ownership="full"</c> and the return is an owned value of the
/// type of the instance — and the C implementations differ in what a caller
/// does with the answer, so the binding tells them apart by name. A
/// <c>_make_writable</c> is the identity of the object it is called on and the
/// wrapper follows it (<see cref="InPlace"/>); everything else is a conversion
/// whose result is a second value (<see cref="Minted"/>).
/// </para>
/// <para>
/// <see cref="None"/> covers every other member, including one that consumes
/// its instance in a shape neither rule matches: that one stays rejected under
/// <c>SkipReason.InstanceTransferFull</c>, so a future symbol of an unforeseen
/// shape is reported rather than emitted.
/// </para>
/// </remarks>
internal enum InstanceConsumption
{
    /// <summary>The call borrows the instance, which is what nearly every member does.</summary>
    None,

    /// <summary>
    /// The call consumes the reference of the wrapper and hands one back that
    /// stands for the same logical object. The wrapper gives up its handle,
    /// adopts the answer and returns itself, so the member reads as the C
    /// idiom <c>caps = gst_caps_make_writable (caps)</c> written as
    /// <c>caps.MakeWritable()</c>.
    /// </summary>
    InPlace,

    /// <summary>
    /// The call consumes a reference and produces a new value. The binding
    /// mints the reference it is handed, so the wrapper the member was called
    /// on is untouched and the result is a wrapper of its own.
    /// </summary>
    Minted,
}

/// <summary>
/// The inline storage of a fixed size array, emitted as an
/// <c>[InlineArray]</c> struct beside the declaration that needs it.
/// </summary>
/// <param name="TypeName">The simple C# name of the storage type.</param>
/// <param name="ElementTypeName">The C# type of one element.</param>
/// <param name="Length">The number of elements.</param>
/// <remarks>
/// Two declarations use it. A fixed size <em>field</em> of a generated
/// structure is spelled with one, and so is a <em>parameter</em> that the
/// callee fills with a fixed number of elements; both are nested in the type
/// that declares them and are named after the field or the parameter with an
/// <c>Array</c> suffix, so that the storage a caller has to allocate says how
/// large it is.
/// </remarks>
internal sealed record InlineArrayInfo(string TypeName, string ElementTypeName, int Length);

/// <summary>
/// The zero argument constructor a caller allocated boxed out parameter takes
/// its storage from.
/// </summary>
/// <param name="EntryPoint">The <c>c:identifier</c> of the constructor.</param>
/// <param name="NativeName">The name of its <c>LibraryImport</c> declaration.</param>
/// <param name="Library">The logical native library that exports it.</param>
/// <remarks>
/// The record the callee fills is sized and zeroed by the library that declares
/// it, which is the only allocation whose size and whose matching free the
/// binding can be sure of: a mirror of the C structure could be truncated, and
/// a record that frees with <c>g_slice_free</c> may not be handed storage from
/// <c>g_malloc0</c>. The library is carried here because the constructor rarely
/// belongs to the module that declares the member —
/// <c>gst_allocation_params_new</c> is exported by <c>Gst</c> and called from
/// members of <c>GstBase</c>, <c>GstAudio</c> and <c>GstVideo</c>.
/// </remarks>
internal sealed record BoxedStorageFactory(string EntryPoint, string NativeName, string Library);

/// <summary>
/// One argument of a planned callable, visible or hidden.
/// </summary>
/// <remarks>
/// A record rather than a class, so that a planner can hand a finished plan
/// back with one part restated: the gir documentation of a parameter is
/// attached that way, once, at the tail of <c>PlanParameter</c>, instead of by
/// every branch that builds a plan. Identity still decides which argument an
/// array length belongs to, and every place that asks reads it through
/// <c>ReferenceEquals</c>.
/// </remarks>
internal sealed record ArgumentPlan
{
    /// <summary>Gets the gir parameter, or <see langword="null"/> for the instance.</summary>
    internal GirParameter? Source { get; init; }

    /// <summary>Gets how the argument is marshalled.</summary>
    internal required ArgumentKind Kind { get; init; }

    /// <summary>Gets the C# name of the parameter, which is also the base name of its locals.</summary>
    internal required string Name { get; init; }

    /// <summary>Gets the C# type on the public surface.</summary>
    internal string PublicType { get; init; } = string.Empty;

    /// <summary>Gets the C# type in the <c>LibraryImport</c> signature.</summary>
    internal required string RawType { get; init; }

    /// <summary>Gets how the argument is passed in C#.</summary>
    internal ArgumentDirection Direction { get; init; } = ArgumentDirection.In;

    /// <summary>Gets a value indicating whether the argument is absent from the public signature.</summary>
    internal bool IsHidden { get; init; }

    /// <summary>Gets a value indicating whether the argument accepts a null value.</summary>
    internal bool IsNullable { get; init; }

    /// <summary>Gets the ownership transfer of the argument.</summary>
    internal GirTransfer Transfer { get; init; }

    /// <summary>Gets the wrapper flavour of a handle.</summary>
    internal HandleFlavor Flavor { get; init; }

    /// <summary>
    /// Gets the static class whose <c>ToNative</c> and <c>FromNative</c> convert
    /// an <see cref="ArgumentKind.Enumeration"/> whose native numbers are not
    /// the ones of the gir. <see langword="null"/> when the value crosses as a
    /// cast, which is every enumeration but the ones the runtime translates.
    /// </summary>
    internal string? EnumConverter { get; init; }

    /// <summary>
    /// Gets what a <see cref="ArgumentKind.ConsumedHandle"/> argument hands the
    /// call: a reference for a mini object or a GObject, a copy for a boxed
    /// value. <see cref="ConsumedFamily.None"/> for every other kind.
    /// </summary>
    internal ConsumedFamily ConsumedFamily { get; init; }

    /// <summary>
    /// Gets the constructor a <see cref="ArgumentKind.CallerAllocatedBoxed"/>
    /// argument takes its storage from. <see langword="null"/> for every other
    /// kind.
    /// </summary>
    internal BoxedStorageFactory? StorageFactory { get; init; }

    /// <summary>
    /// Gets a value indicating whether the overlays redirected the parameter
    /// from a caller allocated out onto an ordinary <c>in</c> handle, which
    /// makes it the destination the C function works on rather than a result.
    /// </summary>
    internal bool IsRedirectedDestination { get; init; }

    /// <summary>Gets the element type of an array, on the public surface.</summary>
    internal string? ElementType { get; init; }

    /// <summary>
    /// Gets how one element of a container is marshalled. Only
    /// <see cref="ArgumentKind.ListIn"/> sets it, to
    /// <see cref="ArgumentKind.Handle"/> or <see cref="ArgumentKind.Utf8"/>.
    /// </summary>
    internal ArgumentKind ElementKind { get; init; }

    /// <summary>
    /// Gets a value indicating whether a list argument is a <c>GSList</c>
    /// rather than a <c>GList</c>. The two differ in nothing but the functions
    /// that build and release the spine, which the factory is told by a flag.
    /// </summary>
    internal bool IsSinglyLinked { get; init; }

    /// <summary>Gets the index of the argument that carries the length of this array.</summary>
    internal int? LengthArgument { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="LengthArgument"/> was named
    /// by an <c>arrayOverrides</c> entry rather than by the gir.
    /// </summary>
    /// <remarks>
    /// The documentation of the parameter reads it: a length the gir states is
    /// visible in the C declaration a caller can look up, while one the
    /// overlays supply is a fact about the C implementation that only the
    /// generated documentation can carry.
    /// </remarks>
    internal bool LengthIsOverridden { get; init; }

    /// <summary>
    /// Gets the number of elements the C declaration sizes this array at, when
    /// it sizes it at all.
    /// </summary>
    /// <remarks>
    /// It is the other half of <see cref="LengthArgument"/> and never appears
    /// beside it: a block of a size the declaration fixes carries no count of
    /// its own, so the member states the length in its guard and in its
    /// documentation instead of passing it.
    /// </remarks>
    internal int? FixedLength { get; init; }

    /// <summary>
    /// Gets the inline storage type of a <see cref="ArgumentKind.FixedArrayOut"/>
    /// argument, which the declaring type declares beside its members.
    /// </summary>
    internal InlineArrayInfo? FixedArray { get; init; }

    /// <summary>
    /// Gets the index of the argument a hidden argument belongs to: the array
    /// of a length, or the callback of a user data pointer and of a destroy
    /// notification. <c>-1</c> stands for the return value.
    /// </summary>
    internal int? OwnerArgument { get; init; }

    /// <summary>Gets the delegate type of a callback argument.</summary>
    internal string? DelegateType { get; init; }

    /// <summary>Gets the trampoline holder of a callback argument.</summary>
    internal string? TrampolineType { get; init; }

    /// <summary>
    /// Gets the storage slot of the instance a callback that carries no
    /// <c>user_data</c> of its own is filed under, which the call site writes
    /// before it hands the callback over.
    /// </summary>
    internal string? InstanceSlot { get; init; }

    /// <summary>Gets the lifetime of a callback argument.</summary>
    internal GirScope Scope { get; init; }

    /// <summary>Gets the gir documentation of the parameter.</summary>
    internal string? Doc { get; init; }
}

/// <summary>
/// The return value of a planned callable.
/// </summary>
internal sealed class ReturnPlan
{
    /// <summary>Gets how the value is marshalled.</summary>
    internal required ArgumentKind Kind { get; init; }

    /// <summary>Gets the C# type on the public surface.</summary>
    internal required string PublicType { get; init; }

    /// <summary>Gets the C# type in the <c>LibraryImport</c> signature.</summary>
    internal required string RawType { get; init; }

    /// <summary>Gets the ownership transfer of the value.</summary>
    internal GirTransfer Transfer { get; init; }

    /// <summary>Gets a value indicating whether the value may be null.</summary>
    internal bool IsNullable { get; init; }

    /// <summary>Gets the wrapper flavour of a handle.</summary>
    internal HandleFlavor Flavor { get; init; }

    /// <summary>
    /// Gets the static class whose <c>ToNative</c> and <c>FromNative</c> convert
    /// an <see cref="ArgumentKind.Enumeration"/> whose native numbers are not
    /// the ones of the gir; see <see cref="ArgumentPlan.EnumConverter"/>.
    /// </summary>
    internal string? EnumConverter { get; init; }

    /// <summary>Gets the element type of an array, on the public surface.</summary>
    internal string? ElementType { get; init; }

    /// <summary>
    /// Gets how one element of a container is marshalled. Only
    /// <see cref="ArgumentKind.GListReturn"/> sets it, to
    /// <see cref="ArgumentKind.Handle"/> or <see cref="ArgumentKind.Utf8"/>.
    /// </summary>
    internal ArgumentKind ElementKind { get; init; }

    /// <summary>Gets the index of the argument that carries the length of the returned array.</summary>
    internal int? LengthArgument { get; init; }

    /// <summary>
    /// Gets the number of elements the C declaration sizes the returned array
    /// at, when no argument carries the count.
    /// </summary>
    internal int? FixedLength { get; init; }

    /// <summary>Gets the gir documentation of the return value.</summary>
    internal string? Doc { get; init; }

    /// <summary>Gets a value indicating whether the callable returns nothing.</summary>
    internal bool IsVoid => Kind == ArgumentKind.Void;
}

/// <summary>
/// Everything the emitters need to write one callable: the public signature,
/// the marshalling of each argument and the raw entry point behind it.
/// </summary>
internal sealed class MarshalPlan
{
    /// <summary>Gets the gir callable the plan was built from.</summary>
    internal required GirCallable Callable { get; init; }

    /// <summary>Gets the C# shape of the member.</summary>
    internal required CallableForm Form { get; init; }

    /// <summary>Gets the C# name of the member.</summary>
    internal required string Name { get; init; }

    /// <summary>Gets the <c>c:identifier</c> of the native entry point.</summary>
    internal required string EntryPoint { get; init; }

    /// <summary>Gets the name of the generated <c>LibraryImport</c> declaration.</summary>
    internal required string NativeName { get; init; }

    /// <summary>Gets the arguments, in gir order, including the hidden ones.</summary>
    internal required IReadOnlyList<ArgumentPlan> Arguments { get; init; }

    /// <summary>Gets the return value.</summary>
    internal required ReturnPlan Return { get; init; }

    /// <summary>Gets a value indicating whether the callable reports errors through a <c>GError</c>.</summary>
    internal bool Throws { get; init; }

    /// <summary>
    /// Gets the message of the <c>[Obsolete]</c> attribute the overlays put on
    /// the member, or <see langword="null"/> when they put none there.
    /// </summary>
    /// <remarks>
    /// It replaces the attribute the gir deprecation of the callable would
    /// write, because a member carries at most one of them.
    /// </remarks>
    internal string? ObsoleteMessage { get; init; }

    /// <summary>
    /// Gets the hand written sentence the documentation of the member carries,
    /// for a part of its contract that neither the gir nor the marshalling
    /// states, or <see langword="null"/> when it carries none.
    /// </summary>
    internal string? DocNote { get; init; }

    /// <summary>Gets the C# type of the instance, for an extension method.</summary>
    internal string? InstanceType { get; init; }

    /// <summary>
    /// Gets how the call takes over the reference of its instance.
    /// </summary>
    internal InstanceConsumption InstanceConsumption { get; init; }

    /// <summary>
    /// Gets a value indicating whether a wrapper of the declaring type can be
    /// one that borrows what it stands for rather than owning it.
    /// </summary>
    /// <remarks>
    /// Only a mini object wrapper can: it has the borrow constructor an in
    /// place vfunc override needs, while a boxed wrapper owns its value from
    /// the moment it exists. What reads this is the documentation of a call
    /// that takes the reference of its instance over, which refuses a borrow
    /// and says so.
    /// </remarks>
    internal bool InstanceIsBorrowable { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the member overrides a member of
    /// <c>object</c>. Only <c>ToString</c> is ever overridden, and the emitters
    /// decide that after the plan was built, when they know the members of the
    /// declaring type.
    /// </summary>
    internal bool IsOverride { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the member hides one that a
    /// generated base class already carries, as <c>gst_pipeline_new</c> hides
    /// the factory of <c>GstBin</c>. Hiding is intended there, and saying so is
    /// what keeps the compiler quiet.
    /// </summary>
    internal bool IsNew { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a <see langword="null"/> return
    /// is handed out as the empty string, which makes the member non-nullable.
    /// </summary>
    /// <remarks>
    /// Only the <c>ToString</c> of a value projected structure sets it. The C
    /// function answers <c>NULL</c> for a structure it cannot describe — the
    /// <c>default</c> of the struct is exactly such a value — and
    /// <c>object.ToString</c> is the one member a caller may reach on any
    /// instance, so it must not throw and it must not hand out
    /// <see langword="null"/> from a signature that is not nullable.
    /// </remarks>
    internal bool ReturnsEmptyOnNull { get; set; }
}
