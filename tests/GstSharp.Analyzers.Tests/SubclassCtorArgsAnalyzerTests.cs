using Gst.Analyzers;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GstSharp.Analyzers.Tests;

/// <summary>
/// GST0005: the factory of <c>IManagedSubclass&lt;TSelf&gt;</c> has to build its
/// wrapper from the arguments it was handed.
/// </summary>
public sealed class SubclassCtorArgsAnalyzerTests
{
    /// <remarks>
    /// The factory is a static abstract interface member, which the netstandard2.0
    /// reference assemblies of the other rules cannot express, so these snippets
    /// compile against .NET 8.0 and carry the interface stub of their own.
    /// </remarks>
    private static Task VerifyAsync(string source) =>
        AnalyzerVerifier<SubclassCtorArgsAnalyzer>.VerifyAsync(
            ReferenceAssemblies.Net.Net80,
            GstStubs.SubclassFactorySource,
            source);

    [Fact]
    public Task BlockBodyIgnoringArgs_IsReported() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.GObject.Object, Gst.GObject.IManagedSubclass<Managed>
            {
                public static Managed {|GST0005:CreateWrapper|}(Gst.GObject.SubclassCtorArgs args)
                {
                    return new Managed();
                }
            }
            """);

    [Fact]
    public Task ExpressionBodyIgnoringArgs_IsReported() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.GObject.Object, Gst.GObject.IManagedSubclass<Managed>
            {
                public static Managed {|GST0005:CreateWrapper|}(Gst.GObject.SubclassCtorArgs args) => new();
            }
            """);

    [Fact]
    public Task ExplicitImplementationIgnoringArgs_IsReported() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.GObject.Object, Gst.GObject.IManagedSubclass<Managed>
            {
                static Managed Gst.GObject.IManagedSubclass<Managed>.{|GST0005:CreateWrapper|}(
                    Gst.GObject.SubclassCtorArgs args) => new Managed();
            }
            """);

    [Fact]
    public Task ArgsPassedToTheConstructor_IsSilent() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.GObject.Object, Gst.GObject.IManagedSubclass<Managed>
            {
                private Managed(Gst.GObject.SubclassCtorArgs args)
                {
                }

                public static Managed CreateWrapper(Gst.GObject.SubclassCtorArgs args) => new Managed(args);
            }
            """);

    [Fact]
    public Task ArgsReadThroughAMember_IsSilent() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.GObject.Object, Gst.GObject.IManagedSubclass<Managed>
            {
                public static Managed CreateWrapper(Gst.GObject.SubclassCtorArgs args)
                {
                    System.Console.WriteLine(args.Handle);
                    return new Managed();
                }
            }
            """);

    [Fact]
    public Task ArgsForwardedFromALocal_IsSilent() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.GObject.Object, Gst.GObject.IManagedSubclass<Managed>
            {
                private Managed(Gst.GObject.SubclassCtorArgs args)
                {
                }

                public static Managed CreateWrapper(Gst.GObject.SubclassCtorArgs args)
                {
                    Gst.GObject.SubclassCtorArgs copy = args;
                    return new Managed(copy);
                }
            }
            """);

    [Fact]
    public Task CreateWrapperOutsideTheInterface_IsSilent() =>
        VerifyAsync("""
            internal sealed class Unrelated
            {
                public static Unrelated CreateWrapper(Gst.GObject.SubclassCtorArgs args) => new Unrelated();
            }
            """);

    [Fact]
    public Task InstanceCreateWrapper_IsSilent() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.GObject.Object, Gst.GObject.IManagedSubclass<Managed>
            {
                public static Managed CreateWrapper(Gst.GObject.SubclassCtorArgs args) => new Managed(args);

                private Managed(Gst.GObject.SubclassCtorArgs args)
                {
                }

                public Managed CreateWrapper() => this;
            }
            """);
}
