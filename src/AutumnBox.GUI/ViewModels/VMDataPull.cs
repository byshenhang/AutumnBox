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

            // 监听设备变化以更新命令的可执行状态
            devicesManager.ConnectedDevicesChanged += (s, e) =>
            {
                //(PullAppsCommand as MVVMCommand)?.RaiseCanExecuteChanged();
                //(DumpApplicationCommand as MVVMCommand<ApplicationInfo>)?.RaiseCanExecuteChanged();
            };
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
                // 重置 Dumped Size
                DumpedSizeText = "已 Dump 大小: 0 MB";

                // 确保保存目录存在
                if (!Directory.Exists(DumpDirectory))
                {
                    Directory.CreateDirectory(DumpDirectory);
                }

                // 定义要拉取的路径
                var userDataPath = $"/storage/emulated/0/Android/data/{packageName}";
                var appDataPath = $"/data/app/{packageName}-*"; // 注意：/data/app/下的目录可能以-{random}结尾

                // 创建目标目录
                var targetUserDataDir = Path.Combine(DumpDirectory, $"{packageName}/UserData");
                var targetAppDataDir = Path.Combine(DumpDirectory, $"{packageName}/SysData");

                // 确保目标目录存在
                if (!Directory.Exists(targetUserDataDir))
                {
                    Directory.CreateDirectory(targetUserDataDir);
                }
                if (!Directory.Exists(targetAppDataDir))
                {
                    Directory.CreateDirectory(targetAppDataDir);
                }

                int totalSteps = 2; // 用户数据目录和应用数据目录
                int currentStep = 0;

                // 定义一个取消令牌源，用于取消监听任务
                using (var cts = new CancellationTokenSource())
                {
                    // 启动一个任务来定期更新 DumpedSizeText
                    var monitorTask = Task.Run(async () =>
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            // 计算目标目录的总大小（MB）
                            double totalSize = GetDirectorySizeMB(DumpDirectory);
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                DumpedSizeText = $"已 Dump 大小: {totalSize:F2} MB";
                            });
                            await Task.Delay(1000, cts.Token); // 每秒更新一次
                        }
                    }, cts.Token);

                    // 拉取用户数据目录
                    var pullUserDataTask = Task.Run(async () =>
                    {
                        var result = await executor.AdbShellAsync(device, "ls", userDataPath);
                        if (result.ExitCode == 0)
                        {
                            var pullResult = await executor.AdbAsync("pull", userDataPath, targetUserDataDir);
                            pullResult.ThrowIfError();
                        }
                        else
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show($"用户数据目录不存在或无法访问: {userDataPath}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                            });
                        }
                        currentStep++;
                        // 更新 DumpedSizeText
                        double size = GetDirectorySizeMB(DumpDirectory);
                        DumpedSizeText = $"已 Dump 大小: {size:F2} MB";
                    });

                    // 拉取 /data/app/ 目录
                     var appDataListResult = await executor.AdbShellAsync(device, "su", "-c", "ls /data/data/");
                    if (appDataListResult.ExitCode == 0)
                    {
                        var appDirs = appDataListResult.Output.All
                            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(line => line.Contains(packageName))
                            .ToList();

                        if (appDirs.Any())
                        {
                            foreach (var appDir in appDirs)
                            {
                                var targetDir = Path.Combine(targetAppDataDir, Path.GetFileName(appDir));
                                // 确保每一级目录都存在
                                if (!Directory.Exists(targetDir))
                                {
                                    Directory.CreateDirectory(targetDir);
                                }

                                var pullAppDataTask = Task.Run(async () =>
                                {
                                    // 设置目标目录
                                    var backupDir = "/sdcard/AppPullBackup"; // 备份目录
                                    var targetDirOnDevice = $"/data/data/{appDir}"; // 应用数据源目录
                                    var backupAppDir = $"{backupDir}/{appDir}"; // 目标备份路径

                                    // Step 1: 使用 su 命令检查并创建 /sdcard/backup 目录
                                    var createBackupDirCommand = $"su -c 'mkdir -p {backupDir}'";
                                    var createDirResult = await executor.AdbShellAsync(device, "su", "-c", createBackupDirCommand);
                                    createDirResult.ThrowIfError(); // 确保目录创建成功

                                    // Step 2: 使用 su 命令将文件从 /data/data/<应用目录> 复制到 /sdcard/backup/ 中
                                    var copyCommand = $"su -c 'cp -r {targetDirOnDevice} {backupAppDir}'";
                                    var copyResult = await executor.AdbShellAsync(device, "su", "-c", copyCommand);
                                    copyResult.ThrowIfError(); // 确保复制成功

                                    // Step 3: 使用 adb pull 从 /sdcard/backup/ 拉取文件
                                    var pullResult = await executor.AdbAsync("pull", backupAppDir, targetDir);
                                    pullResult.ThrowIfError(); // 确保拉取成功

                                    // Step 4: 删除设备上的备份文件
                                    var deleteCommand = $"su -c 'rm -r {backupAppDir}'";
                                    var deleteResult = await executor.AdbShellAsync(device, "su", "-c", deleteCommand);
                                    deleteResult.ThrowIfError(); // 确保删除成功


                                });

                                await pullAppDataTask;
                            }
                        }
                        else
                        {
                            MessageBox.Show($"未找到应用目录: {packageName}-*", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        MessageBox.Show("无法访问 /data/app/ 目录。请确保设备已 root 或具有足够权限。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    await pullUserDataTask;

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
