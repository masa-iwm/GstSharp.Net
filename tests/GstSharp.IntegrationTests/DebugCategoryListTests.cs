using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The category registry, read through the one singly linked list return of the
/// bound modules.
/// </summary>
/// <remarks>
/// <c>gst_debug_get_all_categories</c> copies the spine of the static registry
/// under the lock that guards it and hands the copy over, so the managed member
/// frees the spine with <c>g_slist_free</c> and borrows the categories. What is
/// asserted here is what that contract promises against the installed library:
/// the snapshot is populated after initialisation, a second one still carries
/// every name the first did, and a category registered in between shows up in
/// the next one. Nothing compares wrappers: the opaque projection mints a fresh
/// wrapper per element per call, and the stability of the native pointer is a C
/// invariant that belongs in the documentation rather than in an assertion.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class DebugCategoryListTests
{
    /// <summary>
    /// The registry is never empty once GStreamer has initialised - it creates
    /// the <c>default</c> category among others - and every element of the
    /// snapshot is a readable category rather than a stale pointer.
    /// </summary>
    [Fact]
    public void TheSnapshotCarriesTheCategoriesTheLibraryRegistered()
    {
        IReadOnlyList<DebugCategory> categories = Global.DebugGetAllCategories();

        Assert.NotEmpty(categories);
        Assert.Contains(categories, category => string.Equals(category.GetName(), "default", StringComparison.Ordinal));
        Assert.All(categories, category => Assert.False(string.IsNullOrEmpty(category.GetName())));
    }

    /// <summary>
    /// A second call still carries everything the first one did. The names are
    /// what is compared, because a category is not reference counted and the
    /// wrapper of one is minted anew on every call; the two sets are compared
    /// by containment rather than for equality, because the registry only ever
    /// grows and any thread of the library may add to it between the calls.
    /// </summary>
    [Fact]
    public void ASecondSnapshotStillCarriesEverythingTheFirstDid()
    {
        HashSet<string> first = NamesOf(Global.DebugGetAllCategories());
        HashSet<string> second = NamesOf(Global.DebugGetAllCategories());

        Assert.Superset(first, second);
    }

    /// <summary>
    /// The snapshot is taken at the call and not cached: a category registered
    /// after one call is in the next.
    /// </summary>
    [Fact]
    public void ACategoryRegisteredAfterwardsIsInTheNextSnapshot()
    {
        string name = "gstsharp-test-listing-" + Guid.NewGuid().ToString("N");
        HashSet<string> before = NamesOf(Global.DebugGetAllCategories());
        Assert.DoesNotContain(name, before);

        DebugCategory registered = DebugCategory.New(name, description: "A category this test registered.");
        Assert.Equal(name, registered.GetName());

        HashSet<string> after = NamesOf(Global.DebugGetAllCategories());
        Assert.Contains(name, after);
        Assert.Superset(before, after);
    }

    private static HashSet<string> NamesOf(IReadOnlyList<DebugCategory> categories) =>
        new(categories.Select(static category => category.GetName()), StringComparer.Ordinal);
}
