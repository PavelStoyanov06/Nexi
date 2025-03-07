using ReactiveUI;
using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Nexi.Services.Interfaces;

namespace Nexi.UI.ViewModels
{
    public class SystemInfoViewModel : ViewModelBase
    {
        private readonly ISystemInfoService _systemInfoService;
        
        private string _osInfo;
        private string _cpuInfo;
        private int _cpuCoreCount;
        private string _gpuInfo;
        private string _ramInfo;
        private string _diskSpaceInfo;
        
        public string OSInfo
        {
            get => _osInfo;
            private set => this.RaiseAndSetIfChanged(ref _osInfo, value);
        }
        
        public string CPUInfo
        {
            get => _cpuInfo;
            private set => this.RaiseAndSetIfChanged(ref _cpuInfo, value);
        }
        
        public int CPUCoreCount
        {
            get => _cpuCoreCount;
            private set => this.RaiseAndSetIfChanged(ref _cpuCoreCount, value);
        }
        
        public string GPUInfo
        {
            get => _gpuInfo;
            private set => this.RaiseAndSetIfChanged(ref _gpuInfo, value);
        }
        
        public string RAMInfo
        {
            get => _ramInfo;
            private set => this.RaiseAndSetIfChanged(ref _ramInfo, value);
        }
        
        public string DiskSpaceInfo
        {
            get => _diskSpaceInfo;
            private set => this.RaiseAndSetIfChanged(ref _diskSpaceInfo, value);
        }
        
        public SystemInfoViewModel(ISystemInfoService systemInfoService)
        {
            _systemInfoService = systemInfoService;
            
            // Load system information
            LoadSystemInfo();
        }
        
        private void LoadSystemInfo()
        {
            try
            {
                OSInfo = _systemInfoService.GetOSInfo();
                CPUInfo = _systemInfoService.GetCPUInfo();
                CPUCoreCount = _systemInfoService.GetCPUCoreCount();
                GPUInfo = _systemInfoService.GetGPUInfo();
                
                // Format RAM as GB
                ulong totalRamBytes = _systemInfoService.GetTotalRAM();
                double ramGB = totalRamBytes / 1024.0;
                RAMInfo = $"{ramGB:F2} GB";
                
                // Get disk space for C: drive (or first available drive)
                string driveLetter = "C";
                try
                {
                    double availableSpace = _systemInfoService.GetAvailableDiskSpace(driveLetter);
                    DiskSpaceInfo = $"{availableSpace:F2} GB available on {driveLetter}:";
                }
                catch (Exception ex)
                {
                    DiskSpaceInfo = $"Error getting disk space: {ex.Message}";
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions
                OSInfo = $"Error: {ex.Message}";
            }
        }
    }
} 