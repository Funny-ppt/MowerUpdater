using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
        DispatcherTimer updateTimer;
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
            // App.Logger = _buffer.Enqueue;
            InitializeComponent();


            if (VersionsComboBox.ItemsSource is INotifyCollectionChanged col1)
            {
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
                App.Log($"MainWindow 尝试写入文件 {configPath}");
                File.WriteAllText(configPath, UpdaterConfig.DefaultConfigJson);
                App.Log($"MainWindow 写入文件 {configPath} 完成");
            }
            ViewModel.ConfigPath = configPath;


            updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(125) };
            updateTimer.Tick += UpdateUIPerTick;
            updateTimer.Start();
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

        private async void InstallButtonClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.Busy = true;
            try
            {
                var local = ViewModel.SelectedInstallPath;
                if (local.IsInstalled == false
                    && Directory.Exists(local.Path)
                    && Directory.EnumerateFileSystemEntries(local.Path).Any())
                {
                    var result = System.Windows.Forms.MessageBox.Show(
                        $"将在 {local.Path} 下执行全新安装，这将会删除该目录下的其余文件，确定要这么做吗？", "安装须知", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (result != System.Windows.Forms.DialogResult.OK)
                    {
                        ViewModel.Busy = false;
                        return;
                    }
                }

                int interval = 1000;
                int retries = 3;
                while (retries-- >= 0)
                {
                    try
                    {
                        ViewModel.Save();
                        break;
                    }
                    catch
                    {
                        await Task.Delay(interval);
                        interval *= 2;
                        if (retries == 0) throw;
                    }
                }
                Dispatcher.Invoke(() => ViewModel.OutputLogs = string.Empty);
                host = new RsyncHost();
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
                    Dispatcher.Invoke(() => {
                        ViewModel.Busy = false;
                        ViewModel.OutputLogs +=
                            host.ExitCode == 0 ?
                                "====================== Done ======================\n"
                              : "=================== Terminate ===================\n";
                        if (host.ExitCode != 0)
                        {
                            System.Windows.Forms.MessageBox.Show(
                                $"rsync返回值: {host.ExitCode}\n详细错误请参见底部文本框输出", "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        else
                        {
                            System.Windows.Forms.MessageBox.Show(
                                $"已经成功安装版本 {VersionsComboBox.SelectedValue}", "安装成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    });
                };
                if (!host.Start(ViewModel.ConfigPath, ((VersionInfo)VersionsComboBox.SelectedValue).VersionName))
                {
                    ViewModel.Busy = false;
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    System.Windows.Forms.MessageBox.Show(
                        ex.ToString(), "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    ViewModel.Busy = false;
                });
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


        private void UpdateUIPerTick(object sender, EventArgs e)
        {
            var sb = new StringBuilder();
            while (_buffer.TryDequeue(out var item))
            {
                sb.AppendLine(item);
            }

            ViewModel.OutputLogs += sb.ToString();
        }
    }
}
