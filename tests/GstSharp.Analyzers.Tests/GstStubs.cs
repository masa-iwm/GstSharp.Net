namespace GstSharp.Analyzers.Tests;

/// <summary>
/// Minimal stand-ins for the product types the rules match on.
/// </summary>
/// <remarks>
/// The analyzers match by metadata name and never reference the product
/// assemblies, so the tests do not need them either. Reproducing the shapes here
/// keeps the test compilations tiny and independent of the binding build.
/// </remarks>
internal static class GstStubs
{
    /// <summary>The stub compilation unit added to every test.</summary>
    internal const string Source = """
        namespace Gst
        {
            public abstract class MiniObject : System.IDisposable
            {
                public void Dispose()
                {
                }
            }

            public enum MapFlags
            {
                Read = 1,
                Write = 2,
            }

            public sealed class Sample : MiniObject
            {
                public Buffer Buffer => null;

                public Caps Caps => null;
            }

            public sealed class Caps : MiniObject
            {
            }

            public sealed class Buffer : MiniObject
            {
                public MapScope Map(MapFlags flags) => default;

                public ref struct MapScope
                {
                    public int Size => 0;

                    public void Dispose()
                    {
                    }
                }
            }

            public sealed class Structure : GObject.Boxed
            {
            }

            public sealed class AppSink
            {
                public Sample PullSample() => null;

                public Sample TryPullSample(ulong timeout) => null;

                public Buffer PullBuffer() => null;
            }

            namespace GObject
            {
                public abstract class Boxed : System.IDisposable
                {
                    public void Dispose()
                    {
                    }
                }

                public readonly struct VfuncOverride
                {
                }

                public sealed class ClassConfig
                {
                }

                public sealed class SubclassType
                {
                }

                /// <summary>A stand-in for a property specification.</summary>
                public sealed class ParamSpec
                {
                }

                /// <summary>A stand-in for the read-only view of a value.</summary>
                public readonly ref struct ValueView
                {
                }

                /// <summary>A stand-in for the writable view of a value.</summary>
                public ref struct ValueRef
                {
                }

                /// <summary>
                /// A stand-in for the wrapped GObject base class, carrying the
                /// property slots every subclassable class inherits.
                /// </summary>
                public class Object
                {
                    public static VfuncOverride SetPropertyOverride { get; } = default;

                    public static VfuncOverride GetPropertyOverride { get; } = default;

                    protected virtual void OnSetProperty(uint propertyId, ValueView value, ParamSpec pspec)
                    {
                    }

                    protected virtual void OnGetProperty(uint propertyId, ValueRef value, ParamSpec pspec)
                    {
                    }
                }

                /// <summary>
                /// A stand-in for the arguments of an adopting constructor. The
                /// handle is internal in the product too, and the stubs share a
                /// compilation with the snippet, so a snippet may read it.
                /// </summary>
                public readonly struct SubclassCtorArgs
                {
                    internal nint Handle => 0;
                }
            }

        #nullable enable
            /// <summary>
            /// A stand-in for a subclassable base class, shaped like the generated
            /// ".Subclass.cs" partials: a slot declaration property and an "On"
            /// method per vfunc, plus the registration entry point.
            /// </summary>
            public class FakeSrc : GObject.Object
            {
                public static GObject.VfuncOverride XOverride { get; } = default;

                public static GObject.VfuncOverride YOverride { get; } = default;

                public static GObject.SubclassType DefineSubclass(
                    string typeName,
                    System.Action<GObject.ClassConfig>? configureClass,
                    params GObject.VfuncOverride[] overrides) => new GObject.SubclassType();

                protected virtual int OnX() => 0;

                protected virtual int OnY() => 0;
            }
        #nullable restore

            namespace Base
            {
                public sealed class Adapter
                {
                    public MapScope Map(nuint size) => default;

                    public ref struct MapScope
                    {
                        public nuint Size => 0;

                        public void Dispose()
                        {
                        }
                    }
                }
            }
        }
        """;

    /// <summary>
    /// The factory contract, in a compilation unit of its own.
    /// </summary>
    /// <remarks>
    /// A static abstract interface member needs a runtime that supports one, so
    /// the tests that use this stub have to ask for .NET reference assemblies
    /// instead of the netstandard2.0 ones the rest of the stubs compile against.
    /// </remarks>
    internal const string SubclassFactorySource = """
        namespace Gst.GObject
        {
            /// <summary>A stand-in for the factory contract of a managed subclass.</summary>
            /// <typeparam name="TSelf">The subclass itself.</typeparam>
            public interface IManagedSubclass<TSelf>
                where TSelf : Object, IManagedSubclass<TSelf>
            {
                static abstract TSelf CreateWrapper(SubclassCtorArgs args);
            }
        }
        """;
}
