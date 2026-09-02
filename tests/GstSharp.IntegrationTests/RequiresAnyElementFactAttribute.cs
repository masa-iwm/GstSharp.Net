extern alias gstsharp;

using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A fact that runs as soon as one of the plugins it names is installed.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RequiresElementFactAttribute"/> needs every factory it is given,
/// which is the right gate for a test that builds one pipeline out of several
/// elements. This is the gate for the other shape: a test that only needs
/// <em>an</em> element of a family, whose member differs per platform — an
/// audio sink is <c>wasapisink</c> on Windows, <c>alsasink</c> or
/// <c>pulsesink</c> on Linux and <c>osxaudiosink</c> on macOS, and the test
/// does not care which one it gets.
/// </para>
/// <para>
/// The skip is computed in the constructor for the same reason the sibling
/// attribute computes it there, and it names every candidate so that an
/// installation which carries none of them is visible in the test report.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresAnyElementFactAttribute : FactAttribute
{
    /// <summary>
    /// Initialises the fact with the element factories the test can use.
    /// </summary>
    /// <param name="factoryNames">
    /// The names of the factories. The test runs as soon as one of them
    /// exists.
    /// </param>
    public RequiresAnyElementFactAttribute(params string[] factoryNames)
    {
        try
        {
            gstsharp::GstSharp.Initialize();
        }
        catch
        {
            // Let the test run and fail with the real load error.
            return;
        }

        foreach (string factoryName in factoryNames)
        {
            // Not disposed, for the reason RequiresElementFactAttribute states:
            // a factory is a singleton of the plugin registry.
            if (Gst.ElementFactory.Find(factoryName) is not null)
            {
                return;
            }
        }

        Skip = "needs one of the \"" + string.Join("\", \"", factoryNames)
            + "\" elements, none of which the installed GStreamer provides";
    }
}
