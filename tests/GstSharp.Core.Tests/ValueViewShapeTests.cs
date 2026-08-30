using Gst.GObject;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The shape of the two <c>GValue</c> views, which is what makes them safe to
/// hand to a callback at all.
/// </summary>
/// <remarks>
/// <para>
/// A view points at a <c>GValue</c> that somebody else owns and that goes away
/// when the invocation the view arrived on returns — the item
/// <c>gst_iterator_fold</c> hands out is a stack value that is reset after every
/// call, and a <c>GstStructure</c> field is gone with the structure. Nothing at
/// run time can catch a handler that stored one; the guarantee is the
/// <c>ref struct</c> shape, which the compiler enforces at every use site.
/// </para>
/// <para>
/// That makes it a fact of the type rather than of any one test, and it costs
/// nothing to assert here, where no GStreamer installation is needed: a
/// refactor that turned either into an ordinary struct would compile, would pass
/// every behavioural test, and would hand applications a dangling pointer.
/// </para>
/// </remarks>
public class ValueViewShapeTests
{
    [Theory]
    [InlineData(typeof(ValueView))]
    [InlineData(typeof(ValueRef))]
    public void AViewCannotEscapeTheCallItArrivedOn(Type view)
    {
        Assert.True(view.IsByRefLike);
        Assert.True(view.IsValueType);
    }

    [Theory]
    [InlineData(typeof(ValueView))]
    [InlineData(typeof(ValueRef))]
    public void AViewOwnsNothingAndIsNotDisposed(Type view)
    {
        // There is no payload to release: the value belongs to whoever handed
        // the view over. An IDisposable here would invite a `using`, and
        // disposing a value a GstStructure still holds is how a field is
        // corrupted.
        Assert.DoesNotContain(typeof(IDisposable), view.GetInterfaces());

        // C# 13 lets a ref struct implement an interface, so the absence of
        // IDisposable is no longer the absence of every interface. A view
        // implements none: an interface is a way to hand one to code that only
        // sees the interface, which is the escape the ref struct shape exists
        // to refuse.
        Assert.Empty(view.GetInterfaces());
        Assert.Null(view.GetMethod("Dispose", Type.EmptyTypes));
        Assert.Null(view.GetMethod("Unset", Type.EmptyTypes));
    }

    [Fact]
    public void AnUninitialisedValueSaysThatRatherThanNamingATypeItDoesNotHave()
    {
        // The seed gst_iterator_fold is handed can arrive uninitialised, and
        // "a value that holds a invalid cannot be given a gint" reads as a type
        // mismatch when the value simply has no type yet. The guard answers
        // before it consults the type at all, which is also why this needs no
        // GStreamer: nothing native is called on the way to the throw.
        static void WriteToAnUninitialisedValue()
        {
            GValueNative native = default;
            ValueRef value = new(ref native);
            value.SetInt(1);
        }

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(WriteToAnUninitialisedValue);

        Assert.Equal(
            "The value is not initialized; initialize it before the call.",
            failure.Message);
    }

    [Fact]
    public void TheReadOnlyViewOffersNoWriter()
    {
        // ValueView is what a `const GValue*` argument becomes, and the C
        // contract of every one of them says the callback may not modify the
        // value. The absence of the setters is that contract.
        foreach (System.Reflection.MethodInfo method in typeof(ValueView).GetMethods())
        {
            Assert.False(
                method.Name.StartsWith("Set", StringComparison.Ordinal),
                $"ValueView declares the writer {method.Name}.");
        }
    }

    [Fact]
    public void TheWritableViewOffersTheSameReadersAsTheReadOnlyOne()
    {
        // A handler that is given the writable view must not have to convert it
        // to read what it is about to change, so the two carry the same readers
        // under the same names.
        HashSet<string> readers = [];
        foreach (System.Reflection.MethodInfo method in typeof(ValueView).GetMethods())
        {
            if (method.DeclaringType == typeof(ValueView))
            {
                readers.Add(method.Name);
            }
        }

        HashSet<string> writable = [];
        foreach (System.Reflection.MethodInfo method in typeof(ValueRef).GetMethods())
        {
            if (method.DeclaringType == typeof(ValueRef))
            {
                writable.Add(method.Name);
            }
        }

        Assert.Empty(readers.Except(writable));
        Assert.Contains("AsView", writable);
        Assert.Contains("ToValue", readers);
    }
}
