using System;

namespace MowerUpdater;

internal class LocalVersionInfo
{
    public LocalVersionInfo(string path, bool isInstalled)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        IsInstalled = isInstalled;
    }

    public string Path { get; set; }
    public bool IsInstalled { get; set; }
    // public string VersionName { get; set; }

    public override string ToString()
    {
        if (IsInstalled)
        {
            return $"在 {Path} 更新 Mower";
        }
        else
        {
            return $"在 {Path} 全新安装 Mower";
        }
    }
}
