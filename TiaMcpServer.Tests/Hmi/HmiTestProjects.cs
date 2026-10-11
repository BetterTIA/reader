using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;

namespace TiaMcpServer.Tests.Hmi;

/// <summary>Builders for offline HMI projects used by the hmi_read worker-side tests.</summary>
internal static class HmiTestProjects
{
    public static HmiSoftware Unified(string name) => new() { Name = name };

    /// <summary>The project a paged reader test holds its snapshots under.</summary>
    public static readonly object TestProject = new();

    /// <summary>Fresh-scan paging for reader tests.</summary>
    public static TiaMcpServer.OpennessWorker.Openness.Hmi.HmiPaging Paged(int offset, int limit) => new(offset, limit, "test", null);

    public static Siemens.Engineering.Hmi.HmiTarget Classic(string name) => new() { Name = name };

    /// <summary>A device whose single depth-1 item hosts <paramref name="software"/>.</summary>
    public static Device DeviceWith(string deviceName, object software, string? typeIdentifier = null)
    {
        var device = new Device { Name = deviceName, TypeIdentifier = typeIdentifier };
        device.DeviceItems.Items.Add(new DeviceItem
        {
            Name = deviceName + "_Software",
            Container = new SoftwareContainer { Software = software },
        });
        return device;
    }

    public static Siemens.Engineering.Project ProjectWith(params Device[] devices)
    {
        var project = new Siemens.Engineering.Project();
        project.Devices.Items.AddRange(devices);
        return project;
    }

    public static Siemens.Engineering.Project WithLanguages(this Siemens.Engineering.Project project, string editing, string reference, params string[] active)
    {
        foreach (var culture in active)
        {
            project.LanguageSettings.ActiveLanguages.Items.Add(Language.Of(culture));
        }

        project.LanguageSettings.EditingLanguage = Language.Of(editing);
        project.LanguageSettings.ReferenceLanguage = Language.Of(reference);
        return project;
    }
}
