using AutumnBox.Basic.Calling;
using AutumnBox.GUI.MVVM;
using AutumnBox.GUI.Services;
using AutumnBox.Leafx.ObjectManagement;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AutumnBox.GUI.Models;
using System.Collections.Generic;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.IO;
using System.Threading;
using System.ComponentModel; // 新增
using System.Windows.Data;   // 新增

namespace AutumnBox.GUI.ViewModels
{
    internal class VMDataPull : ViewModelBase
    {
        [AutoInject]
        private IAdbDevicesManager devicesManager;

        [AutoInject]
        private ICommandExecutor executor;

        // 应用程序集合
        public ObservableCollection<ApplicationInfo> Applications { get; } = new ObservableCollection<ApplicationInfo>();

        // ICollectionView 用于过滤应用程序
        private readonly ICollectionView _applicationsView;
        public ICollectionView ApplicationsView => _applicationsView;

        // 搜索文本属性
        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    RaisePropertyChanged();
                    _applicationsView.Refresh(); // 刷新视图以应用过滤器
                }
            }
        }

        // 当前操作状态属性
        private string _currentStatus = "准备就绪";
        public string CurrentStatus
        {
            get => _currentStatus;
            set
            {
                _currentStatus = value;
                RaisePropertyChanged();
            }
        }

        // 拉取应用程序的命令
        public ICommand PullAppsCommand { get; }

        // 选择保存目录的命令
        public ICommand SelectDumpDirectoryCommand { get; }

        // Dump 应用程序的命令
        public ICommand DumpApplicationCommand { get; }

        // 选择的保存目录
        private string _dumpDirectory;
        public string DumpDirectory
        {
            get => _dumpDirectory;
            set
            {
                _dumpDirectory = value;
                RaisePropertyChanged();
            }
        }

        // 进度显示的 Dumped Size
        private string _dumpedSizeText = "已 Dump 大小: 0 MB";
        public string DumpedSizeText
        {
            get => _dumpedSizeText;
            set
            {
                _dumpedSizeText = value;
                RaisePropertyChanged();
            }
        }

        public VMDataPull()
        {
            // 初始化命令，使用 MVVMCommand
            PullAppsCommand = new MVVMCommand(async (obj) => await PullAppsAsync());

            SelectDumpDirectoryCommand = new MVVMCommand((obj) => SelectDumpDirectory());

            DumpApplicationCommand = new MVVMCommand(async (obj) => await DumpApplicationAsync(obj));

            // 初始化 ICollectionView 并设置过滤器
            _applicationsView = CollectionViewSource.GetDefaultView(Applications);
            _applicationsView.Filter = FilterApplications;

            // 监听设备变化以更新命令的可执行状态
            devicesManager.ConnectedDevicesChanged += (s, e) =>
            {
                //(PullAppsCommand as MVVMCommand)?.RaiseCanExecuteChanged();
                //(DumpApplicationCommand as MVVMCommand<ApplicationInfo>)?.RaiseCanExecuteChanged();
            };
        }

        // 过滤器方法，根据 SearchText 过滤 Applications
        private bool FilterApplications(object obj)
        {
            if (obj is ApplicationInfo app)
            {
                if (string.IsNullOrWhiteSpace(SearchText))
                    return true; // 如果搜索文本为空，显示所有

                return app.PackageName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }

        // 判断是否为系统包
        private bool IsSystemPackage(string packageName, string packageBlock)
        {
            // 基于包名前缀过滤
            if (packageName.StartsWith("com.android.", StringComparison.OrdinalIgnoreCase) ||
                packageName.StartsWith("android.", StringComparison.OrdinalIgnoreCase) ||
                packageName.StartsWith("com.google.android.", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 基于 flags 属性过滤
            var flagsRegex = new Regex(@"flags=\[(?<flags>[^\]]+)\]", RegexOptions.IgnoreCase);
            var flagsMatch = flagsRegex.Match(packageBlock);
            if (flagsMatch.Success)
            {
                var flags = flagsMatch.Groups["flags"].Value;
                if (flags.Contains("SYSTEM") || flags.Contains("PRIVILEGED"))
                {
                    return true;
                }
            }

            return false;
        }

        // 异步拉取用户安装的应用程序列表（仅包名）
        private async Task PullAppsAsync()
        {
            if (devicesManager.SelectedDevice == null)
            {
                // 提示用户选择设备
                MessageBox.Show("当前没有连接设备，请连接设备后重试!", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var device = devicesManager.SelectedDevice;

            try
            {
                // 清空现有列表
                Applications.Clear();

                // 执行 ADB 命令获取用户安装的包列表
                // 使用 'adb shell pm list packages -3' 仅获取非系统包名
                var listPackagesResult = await executor.AdbShellAsync(device, "pm", "list", "packages", "-3");
                listPackagesResult.ThrowIfError();

                // 分割输出为每一行
                var packageLines = listPackagesResult.Output.All
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                // 正则表达式解析每一行，提取包名
                var packageRegex = new Regex(@"^package:(?<packageName>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

                var packages = packageLines.Select(line =>
                {
                    var match = packageRegex.Match(line);
                    if (match.Success)
                    {
                        return match.Groups["packageName"].Value;
                    }
                    return null;
                }).Where(pkgName => !string.IsNullOrWhiteSpace(pkgName)).ToList();

                if (!packages.Any())
                {
                    // 如果没有找到任何包，结束操作
                    MessageBox.Show("未找到任何用户安装的应用程序。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 创建应用信息列表
                var appInfos = packages.Select(pkgName => new ApplicationInfo
                {
                    PackageName = pkgName
                }).ToList();

                // 更新应用程序集合
                foreach (var app in appInfos.OrderBy(a => a.PackageName))
                {
                    Applications.Add(app);
                }

                _applicationsView.Refresh(); // 添加完毕后刷新视图
            }
            catch (Exception ex)
            {
                // 处理其他异常
                MessageBox.Show($"发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                // 也可以记录日志
            }
        }

        // 选择保存目录
        private void SelectDumpDirectory()
        {
            // 文件夹选择逻辑
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "选择Dump保存目录"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                DumpDirectory = dialog.FileName;
                // 移除选择目录后的提示
                // MessageBox.Show($"选择的目录是: {DumpDirectory}");
            }
        }

        // 异步执行Dump操作
        // 异步执行Dump操作
        private async Task DumpApplicationAsync(object obj)
        {
            var app = (ApplicationInfo)obj;
            if (devicesManager.SelectedDevice == null)
            {
                MessageBox.Show("当前没有连接设备，请连接设备后重试!", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (app == null)
            {
                MessageBox.Show("请选择一个应用程序进行Dump操作。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(DumpDirectory))
            {
                MessageBox.Show("请选择Dump保存目录。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var device = devicesManager.SelectedDevice;
            var packageName = app.PackageName;

            try
            {
                // 重置 Dumped Size 和当前状态
                DumpedSizeText = "已 Dump 大小: 0 MB";
                CurrentStatus = "准备就绪";

                // 确保保存目录存在
                if (!Directory.Exists(DumpDirectory))
                {
                    Directory.CreateDirectory(DumpDirectory);
                }

                // 定义备份目录和压缩文件路径
                var backupDir = "/sdcard/AppPullBackup"; // 设备上的备份目录
                var sysDataTar = $"{backupDir}/SysData.tar";
                var userDataTar = $"{backupDir}/UserData.tar";

                // 定义要拉取的路径
                var userDataPath = $"/storage/emulated/0/Android/data/{packageName}";
                var appDataPath = $"/data/data/{packageName}";

                // 定义目标目录
                var targetUserDataDir = Path.Combine(DumpDirectory, $"{packageName}/UserData");
                var targetSysDataDir = Path.Combine(DumpDirectory, $"{packageName}/SysData");

                // 确保目标目录存在
                if (!Directory.Exists(targetUserDataDir))
                {
                    Directory.CreateDirectory(targetUserDataDir);
                }
                if (!Directory.Exists(targetSysDataDir))
                {
                    Directory.CreateDirectory(targetSysDataDir);
                }

                // 定义一个取消令牌源，用于取消监听任务
                using (var cts = new CancellationTokenSource())
                {
                    // 启动一个任务来定期更新 DumpedSizeText 和 CurrentStatus
                    var monitorTask = Task.Run(async () =>
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            // 计算目标目录的总大小（MB）
                            double totalSize = GetDirectorySizeMB(DumpDirectory);
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                DumpedSizeText = $"已 Dump 大小: {totalSize:F2} MB";
                                // CurrentStatus 已在主任务中更新
                            });
                            await Task.Delay(1000, cts.Token); // 每秒更新一次
                        }
                    }, cts.Token);

                    // Step 1: 压缩 SysData
                    CurrentStatus = "正在压缩 SysData...";
                    var compressSysDataResult = await executor.AdbShellAsync(device, "su", "-c", $"tar -cf {sysDataTar} {appDataPath}");
                    if (compressSysDataResult.ExitCode != 0)
                    {
                        MessageBox.Show("压缩 SysData 失败: " + compressSysDataResult.Output.All);
                    }
                        
                    // Step 2: 压缩 UserData   
                    CurrentStatus = "正在压缩 UserData...";
                    var compressUserDataResult = await executor.AdbShellAsync(device, "su", "-c", $"tar -cf {userDataTar} {userDataPath}");
                    if (compressUserDataResult.ExitCode != 0)
                    { 
                        MessageBox.Show("压缩 UserData 失败: " + compressUserDataResult.Output.All);
                    }

                    // Step 3: 拉取 SysData 压缩文件
                    CurrentStatus = "正在拉取 SysData.tar...";
                    var pullSysDataResult = await executor.AdbAsync("pull", sysDataTar, targetSysDataDir);
                    if (pullSysDataResult.ExitCode != 0)
                    {
                        MessageBox.Show("拉取 SysData.tar 失败: " + pullSysDataResult.Output.All);
                    }

                    // Step 4: 拉取 UserData 压缩文件
                    CurrentStatus = "正在拉取 UserData.tar...";
                    var pullUserDataResult = await executor.AdbAsync("pull", userDataTar, targetUserDataDir);
                    if (pullUserDataResult.ExitCode != 0)
                    {
                        MessageBox.Show("拉取 UserData.tar 失败: " + pullUserDataResult.Output.All);
                    }

                    // Step 5: 删除设备上的备份压缩文件
                    CurrentStatus = "正在清理设备上的备份文件...";
                    var deleteSysDataCommand = $"su -c 'rm {sysDataTar}'";
                    var deleteSysDataResult = await executor.AdbShellAsync(device, "su", "-c", $"rm {sysDataTar}");
                    if (deleteSysDataResult.ExitCode != 0)
                    {
                        // 仅警告，不抛出异常
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"警告: 无法删除设备上的 {sysDataTar}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                    }

                    var deleteUserDataCommand = $"su -c 'rm {userDataTar}'";
                    var deleteUserDataResult = await executor.AdbShellAsync(device, "su", "-c", $"rm {userDataTar}");
                    if (deleteUserDataResult.ExitCode != 0)
                    {
                        // 仅警告，不抛出异常
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"警告: 无法删除设备上的 {userDataTar}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                    }

                    // 完成后取消监控任务
                    cts.Cancel();

                    try
                    {
                        await monitorTask;
                    }
                    catch (TaskCanceledException)
                    {
                        // 预期的取消异常，可以忽略
                    }
                }

                // 显示完成提示
                CurrentStatus = "Dump 完成";
                MessageBox.Show($"Dump 完成: {packageName}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException uaEx)
            {
                MessageBox.Show($"文件或目录访问失败: {uaEx.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ioEx)
            {
                MessageBox.Show($"IO错误: {ioEx.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 重置 Dumped Size 显示
                DumpedSizeText = "已 Dump 大小: 0 MB";
                CurrentStatus = "准备就绪";
            }
        }

        // 计算目录大小（MB）
        private double GetDirectorySizeMB(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                    return 0;

                double size = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                                       .Sum(file => new FileInfo(file).Length);

                return size / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }
    }
}
