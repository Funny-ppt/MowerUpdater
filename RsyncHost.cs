using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MowerUpdater;

internal class RsyncHost : IDisposable
{
    bool disposed = false;
    Process _rsyncProcess = null;
    Process rsyncProcess
    {
        get => _rsyncProcess;
        set
        {
            if (_rsyncProcess != value)
            {
                _rsyncProcess?.Dispose();
                _rsyncProcess = value;
            }
        }
    }

    public bool Active
    {
        get
        {
            if (disposed || rsyncProcess == null) return false;
            if (rsyncProcess.HasExited) return false;
            return true;
        }
    }

    public int? ExitCode
    {
        get
        {
            if (rsyncProcess != null && rsyncProcess.HasExited)
            {
                return rsyncProcess.ExitCode;
            }
            return null;
        }
    }

    public event Action<RsyncHost> BeginHostStart;
    public event Action<RsyncHost> HostStart;
    public event EventHandler HostExited;
    public event DataReceivedEventHandler OutputDataReceived;
    public event DataReceivedEventHandler ErrorDataReceived;

    public RsyncHost() { }

    static readonly Regex HttpSchemeRegex = new(@"^https?://", RegexOptions.Compiled);
    static char tolower(char ch) => ch <= 'Z' ? (char)(ch + 32) : ch;
    static string BuildArguments(JsonNode conf, string version)
    {
        foreach (var prop in UpdaterConfig.DefaultConfig.AsObject())
        {
            var confJObject = conf.AsObject();
            if (!confJObject.ContainsKey(prop.Key))
            {
                confJObject[prop.Key] = prop.Value.DeepClone();
            }
        }
        var ignore_patterns = conf["ignores"].AsArray();
        var sb = new StringBuilder();
        foreach (var param in conf["rsync_parameters"].AsArray())
            sb.Append(param.ToString()).Append(' ');
        foreach (var pattern in ignore_patterns)
        {
            sb.Append($"--exclude='")
              .Append(pattern)
              .Append("' ");
        }
        var mirror = conf["mirror"].ToString();
        mirror = HttpSchemeRegex.Replace(mirror, "rsync://");
        sb.Append(' ')
          .Append(mirror)
          .Append(conf["rsync_base_addr"])
          .Append('/')
          .Append(version)
          .Append('/');

        var path = Path.Combine(conf["install_dir"].ToString(), conf["dir_name"].ToString());
        if (path.Length >= 2 && path[1] == ':')
            sb.Append(" /cygdrive/")
              .Append(tolower(path[0]))
              .Append(path.Substring(2).Replace('\\', '/'))
              .Append('/');
        else
            sb.Append(' ').Append(path.Replace('\\', '/')).Append('/');
        return sb.ToString();
    }

    public bool Start(string configPath, string version)
    {
        if (disposed) throw new ObjectDisposedException(nameof(RsyncHost));
        if (!Active)
        {
            App.Log($"RsyncHost.Start 尝试读取文件 {configPath}");
            var conf = JsonNode.Parse(File.ReadAllText(configPath));
            App.Log($"RsyncHost.Start 读取文件完成 {configPath}");

            BeginHostStart?.Invoke(this);
            rsyncProcess = new Process()
            {
                EnableRaisingEvents = true,
                StartInfo = new ProcessStartInfo()
                {
                    FileName = Path.Combine(Environment.CurrentDirectory, "rsync.exe"),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    CreateNoWindow = true,
                    Arguments = BuildArguments(conf, version)
                }
            };
            rsyncProcess.Exited += HostExited;
            rsyncProcess.OutputDataReceived += OutputDataReceived;
            rsyncProcess.ErrorDataReceived += ErrorDataReceived;

            var result = MessageBox.Show($"将要执行的命令:\n\"{rsyncProcess.StartInfo.FileName}\" {rsyncProcess.StartInfo.Arguments}", "确认执行", MessageBoxButtons.OKCancel);
            if (result == DialogResult.OK)
            {
                rsyncProcess.Start();
                rsyncProcess.BeginOutputReadLine();
                rsyncProcess.BeginErrorReadLine();
                HostStart?.Invoke(this);
                return true;
            }
            else
            {
                rsyncProcess = null;
            }
        }
        else // 如果已经启动
        {
            // pass
        }
        return false;
    }

    public async Task<int?> WaitForExit()
    {
        if (Active)
        {
            await Task.Run(rsyncProcess.WaitForExit); 
        }
        return rsyncProcess.ExitCode;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing)
            {
                if (Active)
                {
                    rsyncProcess.Kill();
                    rsyncProcess = null;
                }
            }
            disposed = true;
        }
    }
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}