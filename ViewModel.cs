using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace MowerUpdater;

internal class ViewModel : INotifyPropertyChanged
{
    static ViewModel()
    {
        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
    }
    public ViewModel() { }

    HttpClient _client = new();
    CancellationTokenSource _cts;
    string _configPath = string.Empty;
    string _mirror = string.Empty;
    ObservableCollection<VersionInfo> _versions = new();
    string _selectedVersion = string.Empty;
    string _installPath = string.Empty;
    bool _busy = false;
    string _outputLogs = string.Empty;
    string _ignorePaths = string.Empty;

    static Regex ItemRegex = new(@"<a href=""(.*)"">", RegexOptions.Compiled);
    async Task FetchVersions(CancellationToken token)
    {
        var resp = await _client.GetAsync(_mirror);
        resp.EnsureSuccessStatusCode();

        var html = await resp.Content.ReadAsStringAsync();
        var matches = ItemRegex.Matches(html);
        var tasks = new List<Task>();
        foreach (Match match in matches)
        {
            var val = match.Groups[1].Value;
            if (!val.StartsWith("..") && val.EndsWith("/"))
            {
                tasks.Add(FetchVersionDetails(val.Substring(0, val.Length - 1), token));
            }
        }
        Task.WaitAll(tasks.ToArray(), token);
    }
    async Task FetchVersionDetails(string version, CancellationToken token)
    {
        var url = $"{_mirror}/{version}/version.json";
        var resp = await _client.GetAsync(url, token);
        resp.EnsureSuccessStatusCode();

        var stream = await resp.Content.ReadAsStreamAsync();
        var jsonDocument = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var versionInfo = new VersionInfo
        {
            VersionName = version,
            PublishTime = jsonDocument.RootElement.GetProperty("time").GetDateTime(),
        };
        if (token.IsCancellationRequested) return;
        await App.Current.Dispatcher.InvokeAsync(() =>
        {
            _versions.Add(versionInfo);
        });
    }

    public ObservableCollection<VersionInfo> Versions => _versions;
    public string ConfigPath
    {
        get => _configPath;
        set
        {
            if (value != _configPath)
            {
                _configPath = value;
                PropertyChanged?.Invoke(this, new(nameof(ConfigPath)));

                if (File.Exists(ConfigPath))
                    Load(_configPath);
            }
        }
    }
    public string Mirror
    {
        get => _mirror;
        set
        {
            if (value != _mirror)
            {
                _mirror = value;
                PropertyChanged?.Invoke(this, new(nameof(Mirror)));
                
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                _versions.Clear();
                Task.Run(() => FetchVersions(_cts.Token));
            }
        }
    }
    public string SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (value != _selectedVersion)
            {
                _selectedVersion = value;
                PropertyChanged?.Invoke(this, new(nameof(SelectedVersion)));
            }
        }
    }
    public string InstallPath
    {
        get => _installPath;
        set
        {
            if (value != _installPath)
            {
                _installPath = value;
                PropertyChanged?.Invoke(this, new(nameof(InstallPath)));
            }
        }
    }
    public bool Busy
    {
        get => _busy;
        set
        {
            if (value != _busy)
            {
                _busy = value;
                PropertyChanged?.Invoke(this, new(nameof(Busy)));
            }
        }
    }
    public string OutputLogs
    {
        get => _outputLogs;
        set
        {
            if (value != _outputLogs)
            {
                _outputLogs = value;
                PropertyChanged?.Invoke(this, new(nameof(OutputLogs)));
            }
        }
    }
    public string IgnorePaths
    {
        get => _ignorePaths;
        set
        {
            if (value != _ignorePaths)
            {
                _ignorePaths = value;
                PropertyChanged?.Invoke(this, new(nameof(IgnorePaths)));
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    public void Load(string configPath)
    {
        var json = JsonDocument.Parse(File.OpenRead(configPath));
        var conf = json.RootElement;
        Mirror = conf.GetProperty("mirror").GetString();
        InstallPath = Path.Combine(conf.GetProperty("install_dir").GetString(),
                                   conf.GetProperty("dir_name").GetString());
        IgnorePaths = string.Join(";", conf.GetProperty("ignores").EnumerateArray().Select(e => e.GetString()));
    }

    static public IEnumerable<string> ParseIgnorePaths(string ignorePaths)
    {
        return ignorePaths.Split(new[] { '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(str => str.Trim());
    }

    public void Save()
    {
        using var f = File.Open(_configPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        if (f.Length == 0)
        {
            var bytes = Encoding.UTF8.GetBytes("{}");
            f.Write(bytes, 0, bytes.Length);
            f.Seek(0, SeekOrigin.Begin);
        }
        var conf = JsonNode.Parse(f);
        if (_mirror.EndsWith("/"))
            conf["mirror"] = _mirror.Substring(0, _mirror.Length - 1);
        else
            conf["mirror"] = _mirror;
        conf["install_dir"] = Path.GetDirectoryName(_installPath);
        conf["dir_name"] = Path.GetFileName(_installPath);
        var jarray = new JsonArray();
        foreach (var ignore in ParseIgnorePaths(_ignorePaths))
        {
            jarray.Add(ignore);
        }
        conf["ignores"] = jarray;
        f.Seek(0, SeekOrigin.Begin);
        using var writer = new Utf8JsonWriter(f);
        conf.WriteTo(writer);
        f.SetLength(f.Position);
    }
}
