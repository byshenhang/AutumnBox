using AutumnBox.Basic.Calling;
using AutumnBox.GUI.MVVM;
using AutumnBox.GUI.Services;
using AutumnBox.Leafx.ObjectManagement;
using AutumnBox.GUI.Configuration; 
using Newtonsoft.Json; 
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace AutumnBox.GUI.ViewModels
{
    internal class VMDeviceRemoteConnect : ViewModelBase
    {
        [AutoInject]
        private IAdbDevicesManager devicesManager;

        [AutoInject]
        private ICommandExecutor executor;

        public ICommand RequestConnect { get; }

        private List<Device> _allDevices;
        public List<string> ConnectDevice { get; set; }

        private string _selectedDev;
        public string SelectedQuickDevice
        {
            get => _selectedDev;
            set
            {
                if (_selectedDev != value)
                {
                    _selectedDev = value;
                    RaisePropertyChanged();
                    SelectionChanged();
                }
            }
        }

        private void SelectionChanged()
        {
            var device = _allDevices.Find(d => d.Name == SelectedQuickDevice);
            if (device != null)
            {
                ConnectIP = device.IP;
            }
            else
            {
                ConnectIP = string.Empty;
            }
        }

        private string _connectIP;
        public string ConnectIP
        {
            get => _connectIP;
            set
            {
                if (_connectIP != value)
                {
                    _connectIP = value;
                    RaisePropertyChanged();
                }
            }
        }

        public VMDeviceRemoteConnect()
        {
            RequestConnect = new MVVMCommand(OnRequestConnect);
            LoadDeviceConfiguration();
        }

        private void LoadDeviceConfiguration()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ConfigData", "devices.json");
                if (!File.Exists(configPath))
                {
                    MessageBox.Show("配置文件 devices.json 未找到!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ConnectDevice = new List<string>();
                    return;
                }

                string jsonContent = File.ReadAllText(configPath);
                var deviceConfig = JsonConvert.DeserializeObject<DeviceRemoteConfig>(jsonContent);

                if (deviceConfig?.Devices != null && deviceConfig.Devices.Count > 0)
                {
                    _allDevices = deviceConfig.Devices;
                    ConnectDevice = _allDevices.ConvertAll(d => d.Name);
                }
                else
                {
                    MessageBox.Show("配置文件中没有有效的设备信息!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ConnectDevice = new List<string>();
                }

                RaisePropertyChanged(nameof(ConnectDevice));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取配置文件时发生错误: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ConnectDevice = new List<string>();
            }
        }

        private void OnRequestConnect(object obj)
        {
            if (string.IsNullOrWhiteSpace(ConnectIP))
            {
                MessageBox.Show("请选择一个设备或配置有效的IP地址!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var connect = executor.Adb($"connect {ConnectIP}");
            if (connect.Output.Contains("cannot connect"))
            {
                MessageBox.Show($"连接失败 {connect.Output}!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show("连接成功!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
