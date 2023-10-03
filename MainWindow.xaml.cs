using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Threading;

namespace MowerUpdater
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        internal ViewModel ViewModel => DataContext as ViewModel;
        RsyncHost _host;
        ConcurrentQueue<string> _buffer = new();
        DispatcherTimer _updateTimer;
        RsyncHost host
        {
            get => _host;
            set
            {
                if (_host != value)
                {
                    _host?.Dispose();
                    _host = value;
                }
            }
        }

        public MainWindow()
        {
            App.Logger = _buffer.Enqueue;
            InitializeComponent();


            if (VersionsComboBox.ItemsSource is INotifyCollectionChanged col1) 
            { // 稍稍改变多选框的逻辑，使得有可选项时立刻选中第一项
                col1.CollectionChanged += (e, args) =>
                {
                    if (args.OldItems == null || args.OldItems.Count == 0)
                    {
                        VersionsComboBox.SelectedIndex = 0;
                    }
                };
            }
            if (PossibleInstallPathsComboBox.ItemsSource is INotifyCollectionChanged col2)
            {
                col2.CollectionChanged += (e, args) =>
                {
                    if (args.OldItems == null || args.OldItems.Count == 0)
                    {
                        PossibleInstallPathsComboBox.SelectedIndex = 0;
                    }
                };
            }

            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "mower_updater"
            );
            var configPath = Path.Combine(configDir, "config.json");

            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }
            if (!File.Exists(configPath))
            {
                File.WriteAllText(configPath, UpdaterConfig.DefaultConfigJson);
            }
            ViewModel.ConfigPath = configPath;


            // 缓冲区刷新计时器
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
            _updateTimer.Tick += UpdateUIPerTick;
            _updateTimer.Start();
        }

        private void SelectConfigPathButtonClicked(object sender, RoutedEventArgs e)
        {
            using var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
            openFileDialog.DefaultExt = ".json";
            var result = openFileDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                ViewModel.ConfigPath = openFileDialog.FileName;
            }
        }

        private void SelectInstallPathButtonClicked(object sender, RoutedEventArgs e)
        {
            using var folderDialog = new FolderBrowserDialog();
            var result = folderDialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK)
            {
                ViewModel.InstallPath = folderDialog.SelectedPath;
            }
        }

        private async Task OnInstallSuccess()
        {
            _buffer.Enqueue("====================== Done ======================");

            _buffer.Enqueue("正在检查运行Mower的必要依赖");

            var deps = new IDepInstaller[] { new VCInstaller(), new WebViewInstaller() };
            var depsToInstall = deps.Where(dep => !dep.CheckIfInstalled());

            if (depsToInstall.Any())
            {
                var depNames = string.Join(", ", depsToInstall.Select(dep => dep.Name));
                var result = System.Windows.Forms.MessageBox.Show(
                    $"检测到以下依赖未被安装: {depNames}, 是否需要安装？", "确认安装依赖", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    foreach (var dep in depsToInstall)
                    {
                        await dep.Install(ViewModel.Client);
                    }
                }
            }

            System.Windows.Forms.MessageBox.Show(
                $"已经成功安装版本 {VersionsComboBox.SelectedValue}", "安装成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private RsyncHost InitilizeHost()
        {
            var host = new RsyncHost();
            host.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    _buffer.Enqueue(args.Data);
                }
            };
            host.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    _buffer.Enqueue(args.Data);
                }
            };
            host.HostExited += (sender, args) =>
            {
                if (host.ExitCode != 0)
                {
                    _buffer.Enqueue("=================== Terminate ===================");
                    Dispatcher.Invoke(() => System.Windows.Forms.MessageBox.Show(
                            $"rsync返回值: {host.ExitCode}\n详细错误请参见底部文本框输出", "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error));
                }
                else
                {
                    Dispatcher.Invoke(OnInstallSuccess);
                }
                
            };
            return host;
        }

        const long BytesPerMB = 1024 * 1024;
        private void ProgressUpdated((long downloaded, long total) info)
        {
            if (info.total != -1)
            {
                _buffer.Enqueue($"Download progress: {info.downloaded / BytesPerMB} / {info.total / BytesPerMB} MB");
            }
            else
            {
                _buffer.Enqueue($"Download progress: {info.downloaded / BytesPerMB} MB");
            }
        }

        private void OnDownloadFailed((int retries, Exception ex) info)
        {
            _buffer.Enqueue($"下载失败 #{info.retries}: {info.ex.Message}");
        }

        private async Task EnsureDownloaded(string url, string file, bool forceRedownload = false)
        {
            if (!File.Exists(file) || forceRedownload)
            {
                var downloading_file = file + ".downloading";
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                using (var fs = File.Open(downloading_file, FileMode.OpenOrCreate, FileAccess.Write))
                {
                    var downloader = new FileDownloader(ViewModel.Client, url, fs);
                    downloader.ProgressUpdated += ProgressUpdated;
                    downloader.OnFailed += OnDownloadFailed;
                    await downloader.DownloadAsync();
                }
                File.Move(downloading_file, file);
            }
        }

        private async Task NewInstall(string mirror, string version, string path)
        {
            var url = $"{mirror}/{version}.zip";

            var dest = Path.Combine(Path.GetTempPath(), "MowerUpdater", $"{version}.zip");
            await EnsureDownloaded(url, dest);

            using var fs = File.OpenRead(dest);
            using var zipArchive = new ZipArchive(fs);
            Directory.CreateDirectory(path);
            zipArchive.ExtractToDirectory(path);
            var folder = new DirectoryInfo(Path.Combine(path, version));
            if (folder.Exists)
            {
                foreach (var item in folder.EnumerateFileSystemInfos())
                {
                    if (item is DirectoryInfo d)
                    {
                        d.MoveTo(Path.Combine(path, d.Name));
                    }
                    if (item is FileInfo f)
                    {
                        f.MoveTo(Path.Combine(path, f.Name));
                    }
                }
                folder.Delete();
            }

            await Dispatcher.Invoke(OnInstallSuccess);
        }

        private async void InstallButtonClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.Busy = true;
            try
            {

                int interval = 1000;    // 之前因为没关闭文件流导致报错的一个修复尝试
                int retries = 3;        // 想了想也没必要删除
                while (retries-- >= 0)  // 是不是很像TCP超时重传
                {
                    try
                    {
                        ViewModel.Save();
                        break;
                    }
                    catch
                    {
                        await Task.Delay(interval);
                        //interval *= 2;
                        if (retries == 0) throw;
                    }
                }
                Dispatcher.Invoke(() => ViewModel.OutputLogs = string.Empty);

                var local = ViewModel.SelectedInstallPath;
                if (local.IsInstalled == false
                    && Directory.Exists(local.Path)
                    && Directory.EnumerateFileSystemEntries(local.Path).Any())
                {
                    var result = System.Windows.Forms.MessageBox.Show(
                        $"将在 {local.Path} 下执行全新安装，该目录非空，确定要这么做吗？", "安装须知", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (result != System.Windows.Forms.DialogResult.OK)
                    {
                        return;
                    }
                }

                if (local.IsInstalled == false)
                {
                    await NewInstall(ViewModel.Mirror, ((VersionInfo)VersionsComboBox.SelectedValue).VersionName, ViewModel.InstallPath);
                    return;
                }

                host = InitilizeHost();
                host.Start(ViewModel.ConfigPath, ((VersionInfo)VersionsComboBox.SelectedValue).VersionName);
                await host.WaitForExit();
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    System.Windows.Forms.MessageBox.Show(
                        ex.ToString(), "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
            finally
            {
                ViewModel.Busy = false;
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            _host?.Dispose();
        }

        private void ConsoleOutputTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            bool isAtBottom =
                ConsoleOutputScrollViewer.VerticalOffset == ConsoleOutputScrollViewer.ScrollableHeight;
            if (isAtBottom)
            {
                ConsoleOutputScrollViewer.ScrollToBottom();
            }
        }


        private void UpdateUIPerTick(object sender, EventArgs e) // 将缓冲区的内容一次性推到文本框内容
        {
            var sb = new StringBuilder();
            while (_buffer.TryDequeue(out var item))
            {
                sb.AppendLine(item);
            }

            ViewModel.OutputLogs += sb.ToString();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(e.Uri.ToString());
        }
    }
}
