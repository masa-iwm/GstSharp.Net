using System.Globalization;
using System.Text;
using Gst;
using Gst.GObject;

/// <content>
/// Everything the tool writes about a device: <c>print_device</c>,
/// <c>device_removed</c>, the two structure field printers and the gst-launch
/// snippet.
/// </content>
/// <remarks>
/// The C tool builds these lines with <c>gst_print</c> a fragment at a time and
/// the fragments carry their own tabs and newlines, so the shapes below are
/// spelled out rather than reformatted: every one of them is compared against
/// the output of the real tool.
/// </remarks>
internal sealed partial class DeviceMonitoring
{
    /// <summary>
    /// The features a caps carries when it says nothing special, which the C
    /// tool does not print.
    /// </summary>
    private const string SystemMemory = "memory:SystemMemory";

    /// <summary>
    /// Prints one device, the way <c>print_device</c> does.
    /// </summary>
    /// <param name="device">The device to print.</param>
    /// <param name="modified">
    /// <see langword="true"/> when the device changed rather than appeared.
    /// </param>
    private static void PrintDevice(Device device, bool modified)
    {
        using Caps? caps = device.GetCaps();
        using Structure? properties = device.GetProperties();

        Console.WriteLine();
        Console.WriteLine($"Device {(modified ? "modified" : "found")}:");
        Console.WriteLine();
        Console.WriteLine($"\tname  : {device.GetDisplayName()}");
        Console.WriteLine($"\tclass : {device.GetDeviceClass()}");

        uint size = caps?.GetSize() ?? 0;

        for (uint i = 0; i < size; i++)
        {
            PrintCapsLine(caps!, i);
        }

        if (properties is not null)
        {
            Console.Write("\tproperties:");

            uint fields = (uint)properties.NFields();

            for (uint i = 0; i < fields; i++)
            {
                PrintStructureField(properties, properties.NthFieldName(i));
            }

            Console.WriteLine();
        }

        PrintLaunchLine(device);
        Console.WriteLine();
    }

    /// <summary>
    /// Prints that a device is gone, the way <c>device_removed</c> does.
    /// </summary>
    /// <param name="device">The device that was removed.</param>
    private static void PrintRemoved(Device device)
    {
        Console.WriteLine("Device removed:");
        Console.WriteLine($"\tname  : {device.GetDisplayName()}");
    }

    /// <summary>
    /// Prints one structure of the caps of a device on a line of its own.
    /// </summary>
    /// <param name="caps">The caps of the device.</param>
    /// <param name="index">Which structure to print.</param>
    /// <remarks>
    /// The fields are walked by index rather than through
    /// <c>gst_structure_foreach_id_str</c>, which has no binding.
    /// <see cref="Structure.NthFieldName"/> reports them in the order the
    /// structure stores them, which is the order the callback would have seen
    /// them in.
    /// </remarks>
    private static void PrintCapsLine(Caps caps, uint index)
    {
        using Structure structure = caps.GetStructure(index);
        using CapsFeatures? features = caps.GetFeatures(index);

        StringBuilder line = new();

        line.Append('\t')
            .Append(index == 0 ? "caps  :" : "       ")
            .Append(' ')
            .Append(structure.GetName());

        if (features is not null && IsWorthPrinting(features))
        {
            line.Append('(').Append(features).Append(')');
        }

        // print_field: ", name=serialised-value" for every field.
        uint fields = (uint)structure.NFields();

        for (uint i = 0; i < fields; i++)
        {
            string field = structure.NthFieldName(i);

            using Value value = structure.GetValue(field);

            line.Append(", ").Append(field).Append('=').Append(Global.ValueSerialize(value));
        }

        Console.WriteLine(line.ToString());
    }

    /// <summary>
    /// Tells whether the memory features of a caps say anything the C tool
    /// prints.
    /// </summary>
    /// <param name="features">The features of one structure of the caps.</param>
    /// <returns>
    /// <see langword="true"/> for ANY, and for anything that is not plain
    /// system memory.
    /// </returns>
    private static bool IsWorthPrinting(CapsFeatures features)
    {
        if (features.IsAny())
        {
            return true;
        }

        // GST_CAPS_FEATURES_MEMORY_SYSTEM_MEMORY is a static of the library and
        // has no binding; the same single feature built here compares equal.
        using CapsFeatures systemMemory = CapsFeatures.NewSingle(SystemMemory);

        return !features.IsEqual(systemMemory);
    }

    /// <summary>
    /// Prints one property of a device, the way <c>print_structure_field</c>
    /// does.
    /// </summary>
    /// <param name="properties">The properties of the device.</param>
    /// <param name="field">The name of the property to print.</param>
    /// <remarks>
    /// Each of these starts a line of its own rather than ending one, which is
    /// how the C tool keeps the <c>properties:</c> label on the first line.
    /// </remarks>
    private static void PrintStructureField(Structure properties, string field)
    {
        using Value value = properties.GetValue(field);

        GType type = value.Type;
        bool isString = type == GType.String;
        string? text;

        if (type == GType.UInt)
        {
            uint number = value.GetUInt();
            text = string.Create(CultureInfo.InvariantCulture, $"{number} (0x{number:x8})");
        }
        else if (isString)
        {
            text = value.GetString();
        }
        else
        {
            text = Global.ValueSerialize(value);
        }

        if (text is null)
        {
            Console.Write($"\n\t\t{field} - could not serialise field of type {type.Name}");
            return;
        }

        // Only a value that is not a string is followed by its type.
        Console.Write($"\n\t\t{field} = {text}{(isString ? string.Empty : $" ({type.Name})")}");
    }

    /// <summary>
    /// Prints the gst-launch snippet that would reach the device.
    /// </summary>
    /// <param name="device">The device to describe.</param>
    /// <remarks>
    /// A device that is neither a source, a sink nor a camera source gets no
    /// line at all, which is what the C tool's chain of conditions amounts to.
    /// </remarks>
    private static void PrintLaunchLine(Device device)
    {
        // get_launch_line returns NULL when the device has no element or the
        // element has no factory, and the C tool prints that through %s.
        string line = LaunchLine(device) ?? "(null)";

        if (device.HasClasses("Source"))
        {
            Console.WriteLine($"\tgst-launch-1.0 {line} ! ...");
        }
        else if (device.HasClasses("Sink"))
        {
            Console.WriteLine($"\tgst-launch-1.0 ... ! {line}");
        }
        else if (device.HasClasses("CameraSource"))
        {
            Console.WriteLine(
                $"\tgst-launch-1.0 {line}.vfsrc name=camerasrc ! ... camerasrc.vidsrc ! [video/x-h264] ... ");
        }
    }

    /// <summary>
    /// Builds the element part of the gst-launch snippet.
    /// </summary>
    /// <param name="device">The device to build it for.</param>
    /// <returns>
    /// The name of the element factory, or <see langword="null"/> when the
    /// device produces no element or the element has no factory.
    /// </returns>
    /// <remarks>
    /// <b>The properties are missing.</b> The C tool appends every property of
    /// the element whose value differs from a bare element of the same factory,
    /// which is what makes its snippet address one particular device rather
    /// than the default one. That needs to enumerate the parameter
    /// specifications of a class and to compare two values, and neither
    /// <c>g_object_class_list_properties</c> nor <c>gst_value_compare</c> has a
    /// binding. Program.cs says so at length, because a snippet that silently
    /// opens the wrong device would be worse than one that says what it left
    /// out.
    /// </remarks>
    private static string? LaunchLine(Device device)
    {
        // The element is created here and finished with here, which is the one
        // sanctioned Dispose of a GObject wrapper. The factory behind it is
        // interned and is not disposed. See docs/ownership.md.
        using Element? element = device.CreateElement(null);

        return element?.GetFactory()?.Name;
    }

    /// <summary>
    /// Prints the version of the tool and of the library it loaded.
    /// </summary>
    /// <remarks>
    /// The C tool prints a third line, the origin of the package, which is a
    /// constant of its build and has no run time equivalent here.
    /// </remarks>
    private static void PrintVersion()
    {
        Gst.Version version = GstSharp.NativeVersion;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Options.ProgramName} version {version.Major}.{version.Minor}.{version.Micro}"));
        Console.WriteLine(version.Description);
    }
}
