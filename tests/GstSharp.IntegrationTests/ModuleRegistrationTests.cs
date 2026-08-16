using System.Reflection;
using Gst.GObject;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The durable half of §2.1 of the acceptance requirements: a binding assembly
/// that is loaded but never called into still hands its types to the registry.
/// </summary>
/// <remarks>
/// <para>
/// Not one type of an extension module is named anywhere in this file, and that
/// is the point. Naming one makes the runtime prepare it, preparing it runs the
/// module initialiser of its assembly, and the module initialiser is exactly
/// what is under test — a test that named <c>Gst.App.AppSink</c> would answer
/// its own question while asking it. The modules are loaded by name and the
/// registry is asked which of them arrived.
/// </para>
/// <para>
/// Loading an assembly does not prepare any of its types, so nothing runs its
/// module initialiser on its own: what runs it is the
/// <c>AppDomain.AssemblyLoad</c> handler that <c>GstSharp.Initialize</c>
/// installs, and the sweep of the already loaded assemblies it does first. With
/// both removed, every element below wraps as a plain <c>Gst.Element</c>.
/// </para>
/// <para>
/// The suite shares one process, so another test class may have registered a
/// module before this one runs. Run this class on its own —
/// <c>--filter FullyQualifiedName~ModuleRegistrationTests</c> — for the
/// discriminating version.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ModuleRegistrationTests
{
    /// <summary>The assembly of every binding module.</summary>
    private static readonly string[] BindingAssemblies =
    [
        "GstSharp.Net",
        "GstSharp.Net.App",
        "GstSharp.Net.Base",
        "GstSharp.Net.Video",
        "GstSharp.Net.Audio",
        "GstSharp.Net.Pbutils",
        "GstSharp.Net.Sdp",
        "GstSharp.Net.WebRTC",
        "GstSharp.Net.Net",
        "GstSharp.Net.Rtsp",
    ];

    /// <summary>The logical library name each of them registers.</summary>
    private static readonly string[] ModuleNames =
    [
        "Gst",
        "GstApp",
        "GstBase",
        "GstVideo",
        "GstAudio",
        "GstPbutils",
        "GstSdp",
        "GstWebRTC",
        "GstNet",
        "GstRtsp",
    ];

    /// <summary>
    /// Every module is in the registry once its assembly is loaded, without
    /// anything having called into it.
    /// </summary>
    [Fact]
    public void EveryLoadedBindingAssemblyRegistersItsModule()
    {
        foreach (string assembly in BindingAssemblies)
        {
            // Loading is all the application is assumed to do. It prepares no
            // type, so it is not, by itself, a call into the assembly.
            Assembly.Load(assembly);
        }

        string[] registered = TypeRegistry.RegisteredModules;

        foreach (string module in ModuleNames)
        {
            Assert.Contains(module, registered);
        }
    }
}
