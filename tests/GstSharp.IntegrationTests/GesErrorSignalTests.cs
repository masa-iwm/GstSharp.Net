using GES;
using Gst.GLib;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The three <c>GError</c> shapes of the editing services:
/// <c>GESProject::missing-uri</c>, whose handler answers with an owned string,
/// <c>GESProject::error-loading-asset</c>, whose error the overlays corrected
/// to nullable, and <c>ges_asset_get_error</c>, the one borrowed
/// <c>GError</c> return of the corpus.
/// </summary>
/// <remarks>
/// <para>
/// <c>ges_project_create_asset_sync</c> is what drives all three, and it drives
/// them on the calling thread: a URI that is not there fails with
/// <c>GST_RESOURCE_ERROR_NOT_FOUND</c>, which is the one error
/// <c>ges_project_try_updating_id</c> asks the application about before it
/// gives up. Nothing here needs a main loop or a file on disk.
/// </para>
/// <para>
/// Every member is 1.24 or older, so the file carries no availability gate.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesErrorSignalTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public GesErrorSignalTests(ITestOutputHelper output) => _output = output;

    private static GType UriClipType => GType.FromName("GESUriClip");

    /// <summary>
    /// A handler that answers no URI is the "no answer" contract: the project
    /// stops looking and reports the failure through
    /// <c>error-loading-asset</c>. The handler still saw a real error.
    /// </summary>
    [Fact]
    public void AMissingUriHandlerThatAnswersNothingLetsTheLoadFail()
    {
        GstGES.Initialize();

        using Project project = Project.New(null);

        int calls = 0;
        GException? seenError = null;
        string? seenAssetId = null;

        Project.MissingUriHandler handler = (sender, args) =>
        {
            calls++;
            seenError = args.Error;
            seenAssetId = args.WrongAsset.Id;
            return null;
        };

        int failures = 0;
        GException? loadingError = null;
        void OnErrorLoadingAsset(object? sender, Project.ErrorLoadingAssetSignalArgs args)
        {
            failures++;
            loadingError = args.Error;
        }

        project.MissingUri += handler;
        project.ErrorLoadingAsset += OnErrorLoadingAsset;
        try
        {
            string uri = MissingUri();

            // The member throws: ges_project_create_asset_sync reports the
            // failure through its GError as well as through the signals.
            GException raised = Assert.Throws<GException>(() => project.CreateAssetSync(uri, UriClipType));
            Assert.False(string.IsNullOrWhiteSpace(raised.Message));

            _output.WriteLine(FormattableString.Invariant($"calls={calls} failures={failures}"));

            // How often the editing services report one failure differs
            // between their versions - 1.28.6 asks once and reports twice,
            // because ges_project_try_updating_id sends error-loading-asset
            // on its own path and again from the retry - so the counts are read as "at least once" and what the handler
            // was handed is what the assertions are about.
            Assert.True(calls >= 1, "the missing-uri handler was never asked");
            Assert.NotNull(seenError);
            Assert.NotEqual(Quark.Zero, seenError.Domain);
            Assert.False(string.IsNullOrWhiteSpace(seenError.Message));
            Assert.Equal(uri, seenAssetId);

            _output.WriteLine(FormattableString.Invariant(
                $"missing-uri: domain={seenError.Domain} code={seenError.Code} message={seenError.Message}"));

            // The other signal of the same failure. Its error is nullable only
            // because of the overlay this item ships; on this path it is there.
            Assert.True(failures >= 1, "error-loading-asset was never raised");
            Assert.NotNull(loadingError);
        }
        finally
        {
            project.MissingUri -= handler;
            project.ErrorLoadingAsset -= OnErrorLoadingAsset;
        }
    }

    /// <summary>
    /// A handler that answers a URI is answered: the string it returned is
    /// copied into memory the editing services own and free, and the project
    /// asks again about the identifier the handler named.
    /// </summary>
    [Fact]
    public void AMissingUriHandlerThatAnswersAUriIsUsedForTheReload()
    {
        GstGES.Initialize();

        using Project project = Project.New(null);

        string replacement = MissingUri();
        List<string> asked = [];
        GException? seenError = null;

        Project.MissingUriHandler handler = (sender, args) =>
        {
            asked.Add(args.WrongAsset.Id ?? string.Empty);
            seenError = args.Error;

            // The first question is answered with another URI, which is not
            // there either; the second is not answered at all, so the walk
            // ends rather than repeating for ever.
            return asked.Count == 1 ? replacement : null;
        };

        project.MissingUri += handler;
        try
        {
            string first = MissingUri();
            Assert.Throws<GException>(() => project.CreateAssetSync(first, UriClipType));

            _output.WriteLine(string.Join(" -> ", asked));

            Assert.True(asked.Count >= 2, "the project never asked a second time");
            Assert.Equal(first, asked[0]);

            // What the handler was handed is asserted out here: an assertion
            // that fails inside the trampoline is trapped rather than raised.
            Assert.NotNull(seenError);

            // The identifier of the second question is the string the handler
            // answered the first with, which is the whole of the round trip:
            // the managed string crossed as a copy the library owns and frees.
            Assert.Equal(replacement, asked[1]);
        }
        finally
        {
            project.MissingUri -= handler;
        }
    }

    /// <summary>
    /// The borrowed <c>GError</c> return: an asset that loaded carries none.
    /// </summary>
    [Fact]
    public void AnAssetThatLoadedCarriesNoError()
    {
        GstGES.Initialize();

        // Naming the type is what registers it.
        using (TestClip? clip = TestClip.New())
        {
            Assert.NotNull(clip);
        }

        GType testClip = GType.FromName("GESTestClip");
        Assert.True(testClip.IsValid);

        using Project project = Project.New(null);
        using Asset? asset = project.CreateAssetSync("GESTestClip", testClip);

        Assert.NotNull(asset);
        Assert.Null(asset.GetError());
    }

    private static string MissingUri() =>
        FormattableString.Invariant($"file:///gstsharp-missing-{Guid.NewGuid():N}.mp4");
}
