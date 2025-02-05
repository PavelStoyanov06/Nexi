namespace Nexi.Services.Interfaces
{
    public interface ICommandProcessor
    {
        bool IsCommand(string input);
        string ProcessCommand(string input);
        IEnumerable<string> GetAvailableCommands();
    }
}