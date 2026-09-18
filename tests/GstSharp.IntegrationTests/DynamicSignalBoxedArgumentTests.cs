using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A boxed argument of a signal connected by name arrives as the wrapper of its
/// type, borrowed for the length of the call.
/// </summary>
/// <remarks>
/// <para>
/// The signals the binding cares about here — the <c>handle-request</c>,
/// <c>on-sdp</c> and <c>before-send</c> of <c>rtspsrc</c> — need a server on the
/// network, so the argument is carried by a signal the managed
/// <see cref="ProbeSignalElement"/> defines instead: the marshalling path is
/// the one every dynamically connected handler walks, whoever emitted.
/// </para>
/// <para>
/// What is asserted is pointer identity and disposal, never a reference count:
/// a signal that does not declare its argument static scope has GLib collect a
/// copy of it, and that copy is itself a reference, so counts say nothing about
/// whether the binding took one.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class DynamicSignalBoxedArgumentTests
{
    private static nint _rawArgument;

    /// <summary>
    /// A <c>GstStructure</c> argument arrives as a <see cref="Structure"/>, and
    /// what the handler writes into it is read back by the class handler, which
    /// runs after it on the very same value.
    /// </summary>
    [Fact]
    public void ARegisteredBoxedArgumentArrivesAsItsWrapper()
    {
        Assert.True(ProbeSignalElement.IsRegistered);
        ProbeSignalElement.Reset();

        using Element made = ElementFactory.Make(ProbeSignalElement.FactoryName, "boxed-argument")
            ?? throw new InvalidOperationException("The probe factory is missing.");

        string? name = null;
        string? read = null;
        int calls = 0;

        ulong handler = made.ConnectSignal(ProbeSignalElement.BoxedSignal, (sender, args) =>
        {
            _ = sender;

            _ = Interlocked.Increment(ref calls);

            if (args.Length > 0 && args[0] is Structure structure)
            {
                name = structure.GetName();
                read = structure.GetString("carried");

                // The emitter reads this back where the signal lends its
                // argument, which is what the class handler stands in for.
                using Value written = Value.CreateFor("seen", GType.String);
                structure.SetValue(ProbeSignalElement.BoxedFieldName, written);
            }

            return null;
        });

        try
        {
            using Structure sent = Structure.NewEmpty("gstsharp-boxed-argument");
            using Value carried = Value.CreateFor("hello", GType.String);

            sent.SetValue("carried", carried);

            _ = made.EmitSignal(ProbeSignalElement.BoxedSignal, sent);
        }
        finally
        {
            made.RemoveHandler(handler);
        }

        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.Equal("gstsharp-boxed-argument", name);
        Assert.Equal("hello", read);

        // The class handler saw the write of the connected one, so both were
        // handed the same value rather than a copy each.
        Assert.Equal("seen", ProbeSignalElement.BoxedClassHandlerReadBack);
    }

    /// <summary>
    /// The wrapper belongs to the emission: it is disposed when the handler
    /// returns, and a handler that kept it is left with nothing.
    /// </summary>
    [Fact]
    public void TheWrapperOfABoxedArgumentIsDisposedAfterTheHandler()
    {
        Assert.True(ProbeSignalElement.IsRegistered);
        ProbeSignalElement.Reset();

        using Element made = ElementFactory.Make(ProbeSignalElement.FactoryName, "boxed-disposal")
            ?? throw new InvalidOperationException("The probe factory is missing.");

        Structure? kept = null;
        bool usableInsideTheHandler = false;
        Structure? copied = null;

        ulong handler = made.ConnectSignal(ProbeSignalElement.BoxedSignal, (sender, args) =>
        {
            _ = sender;

            if (args.Length > 0 && args[0] is Structure structure)
            {
                usableInsideTheHandler = structure.Handle != nint.Zero;

                // Keeping the argument means copying it, exactly as it does for
                // a caps.
                copied = structure.Copy();
                kept = structure;
            }

            return null;
        });

        try
        {
            using Structure sent = Structure.NewEmpty("gstsharp-boxed-disposal");

            _ = made.EmitSignal(ProbeSignalElement.BoxedSignal, sent);
        }
        finally
        {
            made.RemoveHandler(handler);
        }

        Assert.True(usableInsideTheHandler);
        Assert.NotNull(kept);
        _ = Assert.Throws<ObjectDisposedException>(() => _ = kept.Handle);

        using (copied)
        {
            Assert.NotNull(copied);
            Assert.Equal("gstsharp-boxed-disposal", copied.GetName());
        }
    }

    /// <summary>
    /// The wrapper holds the pointer the emission carries: the raw C callback
    /// connected beside the managed handler sees the same address.
    /// </summary>
    /// <remarks>
    /// The static-scope route, where the emitter reads its own value back after
    /// the emission, needs an RTSP server on the network. Pointer identity
    /// against the pointer the C marshaller is given is what stands in for it,
    /// and it is the same claim: the wrapper is the emission's value, not a copy
    /// of it.
    /// </remarks>
    [Fact]
    public void TheWrapperCarriesThePointerTheEmissionCarries()
    {
        Assert.True(ProbeSignalElement.IsRegistered);
        ProbeSignalElement.Reset();
        Volatile.Write(ref _rawArgument, nint.Zero);

        using Element made = ElementFactory.Make(ProbeSignalElement.FactoryName, "boxed-identity")
            ?? throw new InvalidOperationException("The probe factory is missing.");

        nint wrapped = nint.Zero;

        // The raw callback is connected first, so it runs first and records the
        // pointer before the managed handler is given its wrapper.
        CULong raw = ConnectRaw(made.Handle, ProbeSignalElement.BoxedSignal);

        ulong handler = made.ConnectSignal(ProbeSignalElement.BoxedSignal, (sender, args) =>
        {
            _ = sender;

            if (args.Length > 0 && args[0] is Structure structure)
            {
                wrapped = structure.Handle;
            }

            return null;
        });

        try
        {
            using Structure sent = Structure.NewEmpty("gstsharp-boxed-identity");

            _ = made.EmitSignal(ProbeSignalElement.BoxedSignal, sent);
        }
        finally
        {
            made.RemoveHandler(handler);
            TestNatives.SignalHandlerDisconnect(made.Handle, raw);
        }

        nint carried = Volatile.Read(ref _rawArgument);

        Assert.NotEqual(nint.Zero, carried);
        Assert.Equal(carried, wrapped);
        Assert.Equal(carried, ProbeSignalElement.BoxedClassHandlerPointer);
    }

    /// <summary>
    /// A mini object argument is borrowed as well: the wrapper holds the very
    /// caps the emission carries.
    /// </summary>
    /// <remarks>
    /// The boxed copy function of a mini object is <c>gst_mini_object_ref</c>,
    /// so the value the emission collected is the caps this test built, and
    /// pointer identity against it is readable from here. No reference count is
    /// asserted: the collected reference is one of its own.
    /// </remarks>
    [Fact]
    public void AMiniObjectArgumentIsBorrowedByPointer()
    {
        Assert.True(ProbeSignalElement.IsRegistered);
        ProbeSignalElement.Reset();

        using Element made = ElementFactory.Make(ProbeSignalElement.FactoryName, "mini-object-argument")
            ?? throw new InvalidOperationException("The probe factory is missing.");

        using Caps sent = Assert.IsType<Caps>(Caps.FromString("audio/x-raw, rate=(int)48000"));

        nint wrapped = nint.Zero;
        string? described = null;
        Caps? kept = null;

        ulong handler = made.ConnectSignal(ProbeSignalElement.MiniObjectSignal, (sender, args) =>
        {
            _ = sender;

            if (args.Length > 0 && args[0] is Caps caps)
            {
                wrapped = caps.Handle;
                described = caps.ToString();
                kept = caps;
            }

            return null;
        });

        try
        {
            _ = made.EmitSignal(ProbeSignalElement.MiniObjectSignal, sent);
        }
        finally
        {
            made.RemoveHandler(handler);
        }

        Assert.Equal(sent.Handle, wrapped);
        Assert.Equal("audio/x-raw, rate=(int)48000", described);

        // Borrowed the same way a boxed value is: the wrapper is detached when
        // the handler returns, and the caps themselves are untouched.
        Assert.NotNull(kept);
        _ = Assert.Throws<ObjectDisposedException>(() => _ = kept.Handle);
        Assert.Equal("audio/x-raw, rate=(int)48000", sent.ToString());
    }

    /// <summary>
    /// A boxed type no module of this binding wraps still arrives as its raw
    /// handle, which is what the documented fallback is.
    /// </summary>
    /// <remarks>
    /// The type chosen is <c>G_TYPE_BYTES</c>: <c>GBytes</c> is a boxed type of
    /// GLib, this binding describes no GLib types in its type registry at all,
    /// and the test asserts that premise rather than assuming it.
    /// </remarks>
    [Fact]
    public void AnUnregisteredBoxedArgumentStaysARawHandle()
    {
        Assert.True(ProbeSignalElement.IsRegistered);
        ProbeSignalElement.Reset();

        GType bytesType = new(TestNatives.BytesGetType());
        Assert.True(bytesType.IsValid);

        using Element made = ElementFactory.Make(ProbeSignalElement.FactoryName, "unbound-boxed")
            ?? throw new InvalidOperationException("The probe factory is missing.");

        ReadOnlySpan<byte> payload = "gstsharp"u8;
        nint bytes;

        fixed (byte* first = payload)
        {
            bytes = TestNatives.BytesNew(first, (nuint)payload.Length);
        }

        Assert.NotEqual(nint.Zero, bytes);

        // The premise of the test: nothing registered a wrapper for this type,
        // so there is none to build and none to borrow.
        Assert.False(TypeRegistry.TryCreateWrapper(bytesType, bytes, Transfer.None, out object? none));
        Assert.Null(none);
        Assert.False(TypeRegistry.TryCreateBorrowedWrapper(bytesType, bytes, out object? neither));
        Assert.Null(neither);

        object? seen = null;
        int calls = 0;

        ulong handler = made.ConnectSignal(ProbeSignalElement.UnboundBoxedSignal, (sender, args) =>
        {
            _ = sender;

            _ = Interlocked.Increment(ref calls);
            seen = args.Length > 0 ? args[0] : null;
            return null;
        });

        try
        {
            _ = made.EmitSignal(ProbeSignalElement.UnboundBoxedSignal, bytes);
        }
        finally
        {
            made.RemoveHandler(handler);
            TestNatives.BytesUnref(bytes);
        }

        Assert.Equal(1, Volatile.Read(ref calls));

        // The copy function of a GBytes is g_bytes_ref, so the handle the
        // emission carries is the one this test made.
        nint handle = Assert.IsType<nint>(seen);
        Assert.Equal(bytes, handle);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnRawBoxedArgument(nint instance, nint argument, nint userData)
    {
        _ = instance;
        _ = userData;
        Volatile.Write(ref _rawArgument, argument);
    }

    /// <summary>
    /// Connects the raw C callback above, which is the only way to see the
    /// pointer an emission carries without going through the binding.
    /// </summary>
    /// <param name="instance">The instance to connect to.</param>
    /// <param name="signal">The name of the signal.</param>
    /// <returns>The identifier of the handler.</returns>
    private static CULong ConnectRaw(nint instance, string signal)
    {
        Span<byte> name = stackalloc byte[64];
        int written = Encoding.UTF8.GetBytes(signal, name);
        name[written] = 0;

        fixed (byte* first = name)
        {
            return TestNatives.SignalConnectData(
                instance,
                first,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnRawBoxedArgument,
                nint.Zero,
                nint.Zero,
                0);
        }
    }
}
