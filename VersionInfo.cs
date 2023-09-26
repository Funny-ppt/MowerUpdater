using System;

namespace MowerUpdater;

internal class VersionInfo
{
    public string VersionName { get; set; }
    public DateTime PublishTime { get; set; }

    public string DisplayName => $"{VersionName} ({PublishTime})";
    public override string ToString()
    {
        return DisplayName;
    }
}
