using Nexi.Services.Interfaces;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;


namespace Nexi.Services
{
    public class SystemInfoService : ISystemInfoService
    {
        public string GetOSInfo()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "Windows " + Environment.OSVersion;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "Linux (Kernel: " + File.ReadAllText("/proc/version").Trim() + ")";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "macOS " + GetMacOSVersion();

            return "Unknown OS";
        }

        public string GetCPUInfo()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetWindowsCPU();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return GetLinuxCPU();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return GetMacCPU();

            return "Unknown CPU";
        }

        public int GetCPUCoreCount()
        {
            return Environment.ProcessorCount;
        }

        public ulong GetTotalRAM()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetWindowsRAM();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return GetLinuxRAM();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return GetMacRAM();

            return 0;
        }

        public string GetGPUInfo()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetWindowsGPU();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return GetLinuxGPU();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return GetMacGPU();

            return "Unknown GPU";
        }

        public double GetAvailableDiskSpace(string driveLetter)
        {
            DriveInfo drive = new DriveInfo(driveLetter);
            return drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
        }

        // Windows-specific methods
        private string GetWindowsCPU()
        {
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
            {
                foreach (var obj in searcher.Get())
                    return obj["Name"].ToString();
            }
            return "Unknown CPU";
        }

        private ulong GetWindowsRAM()
        {
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                foreach (var obj in searcher.Get())
                    return Convert.ToUInt64(obj["TotalPhysicalMemory"]) / (1024 * 1024);
            }
            return 0;
        }

        private string GetWindowsGPU()
        {
            using (var searcher = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
            {
                foreach (var obj in searcher.Get())
                    return obj["Name"].ToString();
            }
            return "Unknown GPU";
        }

        // Linux-specific methods
        private string GetLinuxCPU()
        {
            return File.ReadAllText("/proc/cpuinfo").Split('\n')[4].Split(':')[1].Trim();
        }

        private ulong GetLinuxRAM()
        {
            string[] memInfo = File.ReadAllLines("/proc/meminfo");
            foreach (string line in memInfo)
            {
                if (line.StartsWith("MemTotal:"))
                {
                    return Convert.ToUInt64(line.Split(':')[1].Trim().Split(' ')[0]) / 1024;
                }
            }
            return 0;
        }

        private string GetLinuxGPU()
        {
            return RunShellCommand("lspci | grep -i 'VGA\\|3D'");
        }

        // macOS-specific methods
        private string GetMacCPU()
        {
            return RunShellCommand("sysctl -n machdep.cpu.brand_string");
        }

        private ulong GetMacRAM()
        {
            string ramSize = RunShellCommand("sysctl -n hw.memsize");
            return Convert.ToUInt64(ramSize) / (1024 * 1024);
        }

        private string GetMacGPU()
        {
            return RunShellCommand("system_profiler SPDisplaysDataType | grep 'Chipset Model'");
        }

        private string GetMacOSVersion()
        {
            return RunShellCommand("sw_vers -productVersion");
        }

        // Helper method to execute shell commands on Linux/macOS
        private string RunShellCommand(string command)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"" + command + "\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    using (StreamReader reader = process.StandardOutput)
                    {
                        return reader.ReadToEnd().Trim();
                    }
                }
            }
            catch
            {
                return "Command failed";
            }
        }
    }
}
