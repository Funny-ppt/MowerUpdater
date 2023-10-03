using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MowerUpdater;

internal class VCInstaller : IDepInstaller
{
    static Regex VersionRegex = new(@"(\d{4})(-(\d{4}))?", RegexOptions.Compiled);
    static Regex VCRegistryItemRegex = new(@"VC,redist\.(.*),(.*),(\d+.\d+),bundle", RegexOptions.Compiled);

    public string Name => "VC++ 2019";
    public string Version => throw new NotImplementedException();

    public bool CheckIfInstalled()
    {
        var parentKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\Installer\Dependencies");
        var keyname = parentKey.GetSubKeyNames()
                               .Where(name => VCRegistryItemRegex.IsMatch(name))
                               .FirstOrDefault();
        if (keyname == null) return false;
        var key = parentKey.OpenSubKey(keyname);
        if (key == null) return false;

        var display_name = key.GetValue("DisplayName") as string ?? throw new InvalidOperationException("未获取到已安装的VC版本");
        var match = VersionRegex.Match(display_name);
        if (match.Groups[3].Success)
        {
            var l = int.Parse(match.Groups[1].Value);
            var r = int.Parse(match.Groups[3].Value);
            return l <= 2019 && 2019 <= r;
        }
        else
        {
            return match.Groups[1].Value.Contains("2019");
        }
    }

    public async Task Install(HttpClient client, CancellationToken token = default)
    {
        string path, url;
        if (Environment.Is64BitOperatingSystem)
        {
            path = Path.Combine(Path.GetTempPath(), "MowerUpdater", "vc_redist.x64.exe");
            url = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
        }
        else
        {
            path = Path.Combine(Path.GetTempPath(), "MowerUpdater", "vc_redist.x86.exe");
            url = "https://aka.ms/vs/17/release/vc_redist.x86.exe";
        }
        await FileDownloader.EnsureDownloaded(client, url, path, token: token);
        using var proc = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = path,
                Arguments = "/install /quiet /norestart",
            },
        };
        proc.Start();
        proc.WaitForExit();
    }
}
