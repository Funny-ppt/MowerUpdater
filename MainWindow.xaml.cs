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
                Dispatcher.Invoke(() => {
                    if (host.ExitCode != 0)
                    {
                        ViewModel.OutputLogs += "=================== Terminate ===================\n";
                        System.Windows.Forms.MessageBox.Show(
                            $"rsync返回值: {host.ExitCode}\n详细错误请参见底部文本框输出", "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        ViewModel.OutputLogs += "====================== Done ======================\n";
                        System.Windows.Forms.MessageBox.Show(
                            $"已经成功安装版本 {VersionsComboBox.SelectedValue}", "安装成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                });
            };
            return host;
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
                        return;
                    }
                }

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
    }
}
