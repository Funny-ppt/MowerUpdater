using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MowerUpdater;

internal class WebViewInstaller : IDepInstaller
{
    const string UrlAmd64 = "https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/aaf09f8a-0685-4239-89a4-f8d1769380a4/MicrosoftEdgeWebView2RuntimeInstallerX64.exe";
    const string Urlx86 = "https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/29f904f8-fdb4-44ba-b731-1ee2bb89c2a0/MicrosoftEdgeWebView2RuntimeInstallerX86.exe";
    const string WebViewKeyAmd64 = @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    const string WebViewKey = @"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    static readonly Version VersionConstant = System.Version.Parse("0.0.0.0");

    public string Name => "WebView";
    public string Version => throw new NotImplementedException();

    public bool CheckIfInstalled()
    {
        var key = Registry.LocalMachine.OpenSubKey(Environment.Is64BitOperatingSystem ? WebViewKeyAmd64 : WebViewKey)
               ?? Registry.CurrentUser.OpenSubKey(WebViewKey);
        if (key == null) return false;
        var version = System.Version.Parse(key.GetValue("pv")?.ToString() ?? "0.0.0.0");
        return version > VersionConstant;
    }

    public async Task Install(HttpClient client, CancellationToken token = default)
    {
        string path, url;
        if (Environment.Is64BitOperatingSystem)
        {
            path = Path.Combine(Path.GetTempPath(), "MowerUpdater", "MicrosoftEdgeWebView2RuntimeInstallerX64.exe");
            url = UrlAmd64;
        }
        else
        {
            path = Path.Combine(Path.GetTempPath(), "MowerUpdater", "MicrosoftEdgeWebView2RuntimeInstallerX86.exe");
            url = Urlx86;
        }
        await FileDownloader.EnsureDownloaded(client, url, path, token: token);
        using var proc = new Process()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = path,
            },
        };
        proc.Start();
        proc.WaitForExit();
    }
}
