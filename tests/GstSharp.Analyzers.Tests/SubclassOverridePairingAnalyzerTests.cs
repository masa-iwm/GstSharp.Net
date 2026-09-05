using Gst.Analyzers;
using Xunit;

namespace GstSharp.Analyzers.Tests;

/// <summary>
/// GST0003 and GST0004: the pairing of an <c>On&lt;X&gt;</c> override and the
/// <c>&lt;X&gt;Override</c> slot declaration of the same registration call.
/// </summary>
public sealed class SubclassOverridePairingAnalyzerTests
{
    private static Task VerifyAsync(params string[] sources) =>
        AnalyzerVerifier<SubclassOverridePairingAnalyzer>.VerifyAsync(sources);

    [Fact]
    public Task OverrideWithoutDeclaration_IsReported() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null);

                protected override int {|GST0003:OnX|}() => 1;
            }
            """);

    [Fact]
    public Task DeclarationWithoutOverride_IsReported() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null, {|GST0004:XOverride|});
            }
            """);

    [Fact]
    public Task DeclarationAndOverride_AreSilent() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null, XOverride);

                protected override int OnX() => 1;
            }
            """);

    [Fact]
    public Task NeitherDeclarationNorOverride_IsSilent() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null);
            }
            """);

    [Fact]
    public Task OverridesInALocal_AreSilent() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static Gst.GObject.SubclassType Register()
                {
                    Gst.GObject.VfuncOverride[] slots = new[] { XOverride };
                    return DefineSubclass("managed", null, slots);
                }

                protected override int OnY() => 1;
            }
            """);

    [Fact]
    public Task OverridesFromAHelper_AreSilent() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null, Slots());

                private static Gst.GObject.VfuncOverride[] Slots() => new[] { XOverride };

                protected override int OnY() => 1;
            }
            """);

    [Fact]
    public Task SpreadOverrides_AreSilent() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null, [.. Slots()]);

                private static Gst.GObject.VfuncOverride[] Slots() => new[] { XOverride };

                protected override int OnY() => 1;
            }
            """);

    [Fact]
    public Task OverrideInAnotherPartialDeclaration_IsSilent() =>
        VerifyAsync(
            """
            internal sealed partial class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null, XOverride);
            }
            """,
            """
            internal sealed partial class Managed
            {
                protected override int OnX() => 1;
            }
            """);

    [Fact]
    public Task NestedClass_KeysOnItsOwnMembers() =>
        VerifyAsync("""
            internal class Outer : Gst.FakeSrc
            {
                protected override int OnX() => 1;

                internal sealed class Inner : Gst.FakeSrc
                {
                    private static readonly Gst.GObject.SubclassType Definition =
                        DefineSubclass("inner", null, {|GST0004:XOverride|});
                }
            }
            """);

    [Fact]
    public Task CallFromANonDerivedClass_IsSilent() =>
        VerifyAsync("""
            internal sealed class Registrations
            {
                internal static void ForeignSlot()
                {
                    Gst.FakeSrc.DefineSubclass("foreign", null, Gst.FakeSrc.XOverride);
                }
            }
            """);

    [Fact]
    public Task OnePairedAndOneUnpairedStem_ReportOnlyTheUnpairedOne() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null, XOverride);

                protected override int OnX() => 1;

                protected override int {|GST0003:OnY|}() => 2;
            }
            """);

    [Fact]
    public Task OverrideInAnotherPartialDeclaration_IsReportedThere() =>
        VerifyAsync(
            """
            internal sealed partial class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null);
            }
            """,
            """
            internal sealed partial class Managed
            {
                protected override int {|GST0003:OnX|}() => 1;
            }
            """);

    [Fact]
    public Task ExplicitArrayArgument_IsRead() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass(
                        "managed",
                        null,
                        new Gst.GObject.VfuncOverride[] { XOverride, {|GST0004:YOverride|} });

                protected override int OnX() => 1;
            }
            """);

    [Fact]
    public Task TwoRegistrationCalls_AreBothEvaluated() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType First =
                    DefineSubclass("first", null, XOverride);

                private static readonly Gst.GObject.SubclassType Second =
                    DefineSubclass("second", null, {|GST0004:YOverride|});

                protected override int {|GST0003:OnX|}() => 1;
            }
            """);

    [Fact]
    public Task OverrideOnAnIntermediateClass_IsSilent() =>
        VerifyAsync("""
            internal abstract class Mid : Gst.FakeSrc
            {
                protected override int OnX() => 1;
            }

            internal sealed class Leaf : Mid
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("leaf", null, XOverride);
            }
            """);

    [Fact]
    public Task NoOverrideAnywhereInTheChain_IsReported() =>
        VerifyAsync("""
            internal abstract class Mid : Gst.FakeSrc
            {
            }

            internal sealed class Leaf : Mid
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("leaf", null, {|GST0004:XOverride|});
            }
            """);

    [Fact]
    public Task TypeQualifiedSlotReference_IsRead() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null, {|GST0004:Gst.FakeSrc.XOverride|});
            }
            """);

    [Fact]
    public Task CallInAStaticConstructor_KeysOnTheClass() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition;

                static Managed()
                {
                    Definition = DefineSubclass("managed", null, {|GST0004:XOverride|});
                }
            }
            """);

    [Fact]
    public Task CallInALambda_KeysOnTheClass() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly System.Func<Gst.GObject.SubclassType> Factory =
                    () => DefineSubclass("managed", null, {|GST0004:XOverride|});
            }
            """);

    [Fact]
    public Task CollectionExpression_IsRead() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null, [XOverride, {|GST0004:YOverride|}]);

                protected override int OnX() => 1;
            }
            """);

    [Fact]
    public Task PropertyOverridesAndOverrides_AreSilent() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null, SetPropertyOverride, GetPropertyOverride);

                protected override void OnSetProperty(
                    uint propertyId, Gst.GObject.ValueView value, Gst.GObject.ParamSpec pspec)
                {
                }

                protected override void OnGetProperty(
                    uint propertyId, Gst.GObject.ValueRef value, Gst.GObject.ParamSpec pspec)
                {
                }
            }
            """);

    [Fact]
    public Task OnSetPropertyWithoutDeclaration_IsReported() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null);

                protected override void {|GST0003:OnSetProperty|}(
                    uint propertyId, Gst.GObject.ValueView value, Gst.GObject.ParamSpec pspec)
                {
                }
            }
            """);

    [Fact]
    public Task GetPropertyOverrideWithoutOverride_IsReported() =>
        VerifyAsync("""
            internal sealed class Managed : Gst.FakeSrc
            {
                private static readonly Gst.GObject.SubclassType Definition =
                    DefineSubclass("managed", null, SetPropertyOverride, {|GST0004:GetPropertyOverride|});

                protected override void OnSetProperty(
                    uint propertyId, Gst.GObject.ValueView value, Gst.GObject.ParamSpec pspec)
                {
                }
            }
            """);
}
