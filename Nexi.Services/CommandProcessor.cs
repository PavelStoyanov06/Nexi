using Nexi.Services.Interfaces;

namespace Nexi.Services
{
    public class CommandProcessor : ICommandProcessor
    {
        private readonly Dictionary<string, Func<string>> _commands;

        public CommandProcessor()
        {
            _commands = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["minimize"] = () => "Command recognized: Minimizing window",
                ["maximize"] = () => "Command recognized: Maximizing window",
                ["close"] = () => "Command recognized: Closing window",
                ["open browser"] = () => "Command recognized: Opening browser",
                ["help"] = () => $"Available commands: {string.Join(", ", GetAvailableCommands())}",
                ["time"] = () => $"Current time is: {DateTime.Now:T}"
            };
        }

        public bool IsCommand(string input)
        {
            return _commands.ContainsKey(input.Trim().ToLower());
        }

        public string ProcessCommand(string input)
        {
            var trimmedInput = input.Trim().ToLower();
            if (_commands.TryGetValue(trimmedInput, out var handler))
            {
                return handler();
            }
            return $"Unknown command: {input}";
        }

        public IEnumerable<string> GetAvailableCommands()
        {
            return _commands.Keys;
        }
    }
}