using Gst;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// An application can register a debug category of its own and log through it,
/// so that its own output lands in the same stream, with the same filtering,
/// as the output of GStreamer itself.
/// </summary>
/// <remarks>
/// <para>
/// <c>_gst_debug_category_new</c> is get or create, which is what makes the
/// C macro safe to run from several translation units. That behaviour is
/// asserted here against the real library rather than taken on trust, because
/// it is the reason the binding needs no registration guard of its own.
/// </para>
/// <para>
/// The collecting log function is installed once for the whole process and is
/// never removed: <c>Global.DebugAddLogFunction</c> keeps the
/// <c>user_data</c> pointer of the callback to itself, so
/// <c>gst_debug_remove_log_function_by_data</c> cannot be reached from managed
/// code. It is harmless — the collector filters strictly by the category names
/// of this class, and only those categories have a threshold that lets
/// anything through — but it does mean that the collector must be installed
/// exactly once, which is what the static constructor is for. xunit builds a
/// fresh instance of this class per test method, and installing per instance
/// would install it three times.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class DebugCategoryTests
{
    /// <summary>The category that the logging tests use, and nothing else.</summary>
    private const string LogCategoryName = "gstsharp-test-log";

    /// <summary>The category that the get-or-create test registers twice.</summary>
    private const string SharedCategoryName = "gstsharp-test-shared";

    /// <summary>The category that the property round trip test registers.</summary>
    private const string PropertyCategoryName = "gstsharp-test-properties";

    /// <summary>Guards <see cref="Entries"/> against the logging threads.</summary>
    private static readonly object Sync = new();

    /// <summary>Everything the collector saw, oldest first.</summary>
    private static readonly List<LogEntry> Entries = [];

    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Installs the collecting log function, once per process.
    /// </summary>
    static DebugCategoryTests()
    {
        // The collection fixture has initialised GStreamer by the time xunit
        // builds an instance of this class, so the debug system is up.
        Global.DebugSetActive(true);
        Global.DebugAddLogFunction(Collect);
    }

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public DebugCategoryTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A new category carries the name, the color and the description it was
    /// registered with.
    /// </summary>
    [Fact]
    public void NewRegistersACategoryThatReadsBack()
    {
        DebugCategory category = DebugCategory.New(PropertyCategoryName, color: 1, "A category of the test suite.");

        Assert.Equal(PropertyCategoryName, category.GetName());
        Assert.Equal(1u, category.GetColor());
        Assert.Equal("A category of the test suite.", category.GetDescription());

        // The optional arguments are optional: no color, no description.
        DebugCategory plain = DebugCategory.New(PropertyCategoryName + "-plain");
        Assert.Equal(PropertyCategoryName + "-plain", plain.GetName());
        Assert.Equal(0u, plain.GetColor());
    }

    /// <summary>
    /// Registering a name twice returns the category that is already there. The
    /// second registration is accepted and dropped: the description of the
    /// first one survives.
    /// </summary>
    [Fact]
    public void NewIsGetOrCreate()
    {
        DebugCategory first = DebugCategory.New(SharedCategoryName, color: 2, "The first description.");
        DebugCategory second = DebugCategory.New(SharedCategoryName, color: 4, "The second description.");

        _output.WriteLine(FormattableString.Invariant(
            $"first=0x{first.Handle:x} second=0x{second.Handle:x}"));

        // One native category, and possibly two wrappers of it: the wrapper of
        // an opaque record owns nothing, so it is not interned.
        Assert.Equal(first.Handle, second.Handle);

        // The candidate of the second call was discarded whole, not merged.
        Assert.Equal("The first description.", second.GetDescription());
        Assert.Equal(2u, second.GetColor());
    }

    /// <summary>
    /// A message at or below the threshold of the category reaches the log
    /// functions, tagged with the call site the compiler filled in, and a
    /// message above it reaches nothing.
    /// </summary>
    [Fact]
    public void LogRespectsTheThresholdAndReachesTheLogFunctions()
    {
        DebugCategory category = DebugCategory.New(LogCategoryName, color: 0, "The logging test.");
        category.SetThreshold(DebugLevel.Debug);

        Clear();

        category.Log(DebugLevel.Debug, "a message that passes the threshold");

        // Everything more verbose than the threshold is dropped, by the managed
        // pre-check here and by gst_debug_log_literal behind it.
        category.Log(DebugLevel.Trace, "a message that does not pass the threshold");
        category.Log(DebugLevel.Log, "another message that does not pass the threshold");

        List<LogEntry> collected = Snapshot();
        foreach (LogEntry entry in collected)
        {
            _output.WriteLine(FormattableString.Invariant(
                $"{entry.Level} {entry.Function}@{entry.Line}: {entry.Message}"));
        }

        LogEntry only = Assert.Single(collected);
        Assert.Equal(DebugLevel.Debug, only.Level);
        Assert.Equal("a message that passes the threshold", only.Message);

        // The caller information is what the compiler filled in, which is the
        // whole point of the three optional parameters.
        Assert.Equal(nameof(LogRespectsTheThresholdAndReachesTheLogFunctions), only.Function);
        Assert.EndsWith("DebugCategoryTests.cs", only.File, StringComparison.Ordinal);
        Assert.True(only.Line > 0, "The line of the call site was not filled in.");

        // An explicit call site overrides the compiler, for a logging helper
        // that forwards on behalf of its own caller.
        Clear();
        category.Log(DebugLevel.Info, "an explicit call site", null, "elsewhere.cs", "Elsewhere", 42);

        LogEntry explicitSite = Assert.Single(Snapshot());
        Assert.Equal("elsewhere.cs", explicitSite.File);
        Assert.Equal("Elsewhere", explicitSite.Function);
        Assert.Equal(42, explicitSite.Line);
    }

    /// <summary>
    /// A category has to have a name, and the refusal happens before any native
    /// code is reached.
    /// </summary>
    [Fact]
    public void NewRefusesAnEmptyName()
    {
        Assert.Throws<ArgumentNullException>(() => DebugCategory.New(null!));
        Assert.Throws<ArgumentException>(() => DebugCategory.New(string.Empty));
    }

    /// <summary>
    /// A message is required whatever the threshold says, so that the mistake
    /// does not hide behind a disabled level.
    /// </summary>
    [Fact]
    public void LogRefusesANullMessage()
    {
        DebugCategory category = DebugCategory.New(LogCategoryName + "-null");
        category.SetThreshold(DebugLevel.None);

        Assert.Throws<ArgumentNullException>(() => category.Log(DebugLevel.Error, null!));
    }

    /// <summary>
    /// Collects one log line. Everything is copied out immediately: the
    /// <see cref="DebugMessage"/> is only valid while this call runs, and the
    /// wrappers are built fresh for it.
    /// </summary>
    /// <param name="category">The category of the line.</param>
    /// <param name="level">The level of the line.</param>
    /// <param name="file">The file of the call site.</param>
    /// <param name="function">The function of the call site.</param>
    /// <param name="line">The line of the call site.</param>
    /// <param name="object">
    /// The object the line is about, which is the one argument that really is
    /// optional: most log lines are written without one. The gir omits the
    /// annotation, so it is restored by an annotation override next to the
    /// three caps callbacks.
    /// </param>
    /// <param name="message">The text of the line.</param>
    /// <remarks>
    /// The signature is the one the gir states, so everything but the object is
    /// a value the C contract promises and none of it has to be checked here.
    /// </remarks>
    private static void Collect(
        DebugCategory category,
        DebugLevel level,
        string file,
        string function,
        int line,
        Gst.GObject.Object? @object,
        DebugMessage message)
    {
        string? name = category.GetName();
        if (name is null || !name.StartsWith("gstsharp-test-", StringComparison.Ordinal))
        {
            // Every other category of the process logs through here as well.
            return;
        }

        LogEntry entry = new(name, level, message.Get(), file, function, line);

        // A log function is called on whichever thread logged.
        lock (Sync)
        {
            Entries.Add(entry);
        }
    }

    /// <summary>Drops everything collected so far.</summary>
    private static void Clear()
    {
        lock (Sync)
        {
            Entries.Clear();
        }
    }

    /// <summary>Returns what has been collected so far.</summary>
    /// <returns>The entries, oldest first.</returns>
    private static List<LogEntry> Snapshot()
    {
        lock (Sync)
        {
            return [.. Entries];
        }
    }

    /// <summary>One collected log line.</summary>
    /// <param name="Category">The name of the category.</param>
    /// <param name="Level">The level of the line.</param>
    /// <param name="Message">The text of the line.</param>
    /// <param name="File">The file of the call site.</param>
    /// <param name="Function">The function of the call site.</param>
    /// <param name="Line">The line of the call site.</param>
    private sealed record LogEntry(
        string Category,
        DebugLevel Level,
        string? Message,
        string? File,
        string? Function,
        int Line);
}
