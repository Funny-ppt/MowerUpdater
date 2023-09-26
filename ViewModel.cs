using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MowerUpdater;

internal class ViewModel : INotifyPropertyChanged
{
    static ViewModel()
    {
        // 老的.NET框架居然是默认tls1.0, 还是抓包才发现这会导致直接被新版nginx拒绝
        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
    }
    public ViewModel() { }

    HttpClient _client = new();
    CancellationTokenSource _cts1; // 用于取消镜像版本搜索功能
    CancellationTokenSource _cts2; // 用于取消后台目录遍历功能
    string _configPath = string.Empty;
    string _mirror = string.Empty;
    ObservableCollection<VersionInfo> _versions = new();
    ObservableCollection<LocalVersionInfo> _possibleInstallPaths = new();
    LocalVersionInfo _selectedInstallPath = null;
    string _installPath = string.Empty;
    bool _busy = false;
    string _outputLogs = string.Empty;
    string _ignorePaths = string.Empty;

    static Regex ItemRegex = new(@"<a href=""(.*)"">", RegexOptions.Compiled);
    async Task FetchVersions(CancellationToken token)
    { // 后台获取版本列表
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
    { // 后台下载版本细节信息
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

    void SearchPossibleInstallPaths(string path0, CancellationToken token)
    { // 后台查找所有可能是Mower安装目录的目录
        var path = path0.Replace('\\', '/');
        var parent = path.EndsWith("/")
            ? Path.GetDirectoryName(path.Substring(0, path.Length - 1))
            : Path.GetDirectoryName(path);
        if (parent != null && Directory.Exists(parent))
        { // 遍历父文件夹的其余目录
            foreach (var dir in Directory.EnumerateDirectories(parent))
            {
                if (token.IsCancellationRequested) return;
                if (CheckIfMowerInstalled(dir))
                {
                    if (token.IsCancellationRequested) return;
                    if (dir == path0) continue;
                    App.Current.Dispatcher.Invoke(() => _possibleInstallPaths.Add(new LocalVersionInfo(dir, true)));
                }
            }
        }
        if (Directory.Exists(path0))
        { // 遍历子文件夹
            foreach (var dir in Directory.EnumerateDirectories(path0))
            {
                if (token.IsCancellationRequested) return;
                if (CheckIfMowerInstalled(dir))
                {
                    if (token.IsCancellationRequested) return;
                    App.Current.Dispatcher.Invoke(() => _possibleInstallPaths.Add(new LocalVersionInfo(dir, true)));
                }
            }
        }
    }
    bool CheckIfMowerInstalled(string path)
    {
        //var mower1 = Path.Combine(path, "mower.exe");
        //var mower2 = Path.Combine(path, "Mower0.exe");
        //return File.Exists(mower1) && File.Exists(mower2);

        return File.Exists(Path.Combine(path, "mower.exe"));
    }

    public ObservableCollection<VersionInfo> Versions => _versions;
    public ObservableCollection<LocalVersionInfo> PossibleInstallPaths => _possibleInstallPaths;
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
                
                _cts1?.Cancel();
                _cts1?.Dispose();
                _cts1 = new CancellationTokenSource();
                _versions.Clear();
                Task.Run(() => FetchVersions(_cts1.Token));
            }
        }
    }
    public LocalVersionInfo SelectedInstallPath
    {
        get => _selectedInstallPath;
        set
        {
            if (value != _selectedInstallPath)
            {
                _selectedInstallPath = value;
                PropertyChanged?.Invoke(this, new(nameof(SelectedInstallPath)));
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

                _possibleInstallPaths.Clear();
                if (!Directory.Exists(_installPath) || !Directory.EnumerateFileSystemEntries(_installPath).Any())
                {
                    _possibleInstallPaths.Add(new LocalVersionInfo(_installPath, false));
                }
                else
                {
                    if (CheckIfMowerInstalled(_installPath))
                    {
                        _possibleInstallPaths.Add(new LocalVersionInfo(_installPath, true));
                    }
                    else
                    {
                        _possibleInstallPaths.Add(new LocalVersionInfo(_installPath, false));
                    }
                }

                _cts2?.Cancel();
                _cts2?.Dispose();
                _cts2 = new CancellationTokenSource();
                Task.Run(() => SearchPossibleInstallPaths(_installPath, _cts2.Token));
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
        var json = JsonDocument.Parse(File.ReadAllText(configPath));
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
        conf["install_dir"] = Path.GetDirectoryName(_selectedInstallPath.Path);
        conf["dir_name"] = Path.GetFileName(_selectedInstallPath.Path);
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
