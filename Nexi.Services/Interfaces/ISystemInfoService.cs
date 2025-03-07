namespace Nexi.Services.Interfaces
{
    public interface ISystemInfoService
    {
        string GetOSInfo();
        string GetCPUInfo();
        int GetCPUCoreCount();
        ulong GetTotalRAM();
        string GetGPUInfo();
        double GetAvailableDiskSpace(string driveLetter);
    }
}
