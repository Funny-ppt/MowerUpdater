using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MowerUpdater;

internal class VC2013Installer : IDepInstaller
{
    public string Name => "VC++ 2013";
    public string Version => throw new NotImplementedException();

    public bool CheckIfInstalled()
    {
        try
        {
            if (MsiHelper.CheckIfInstalled("Microsoft Visual C++ 2013")) return true;
            var x64Key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\Installer\Dependencies\{050d4fc8-5d48-4b8f-8972-47c82c46020f}");
            var x86Key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\Installer\Dependencies\{f65db027-aff3-4070-886a-0d87064aabb1}");
            return x64Key != null || x86Key != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task Install(HttpClient client, CancellationToken token = default)
    {
        string path, url;
        if (Environment.Is64BitOperatingSystem)
        {
            path = Path.Combine(Path.GetTempPath(), "MowerUpdater", "vc_redist.2013.x64.exe");
            url = "https://download.microsoft.com/download/F/3/5/F3500770-8A08-488E-94B6-17A1E1DD526F/vcredist_x64.exe";
        }
        else
        {
            path = Path.Combine(Path.GetTempPath(), "MowerUpdater", "vc_redist.2013.x86.exe");
            url = "https://download.microsoft.com/download/F/3/5/F3500770-8A08-488E-94B6-17A1E1DD526F/vcredist_x86.exe";
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
