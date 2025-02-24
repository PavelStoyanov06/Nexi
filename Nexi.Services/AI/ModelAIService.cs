using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Nexi.Services.AI
{
    /// <summary>
    /// A mock AI service that can be used when ONNX models are not available.
    /// This provides simulated responses without requiring actual model files.
    /// </summary>
    public class MockAIService : IAIService
    {
        private readonly ILogger<MockAIService> _logger;
        private readonly Random _random = new Random();

        public event EventHandler<string> OnInferenceProgress;
        public event EventHandler<Exception> OnError;

        public MockAIService(ILogger<MockAIService> logger)
        {
            _logger = logger;
        }

        public async Task<AIResponse> GetCompletionAsync(string prompt, AIRequestOptions options)
        {
            _logger.LogInformation("Generating mock completion for prompt: {Prompt}",
                prompt.Length > 50 ? prompt.Substring(0, 50) + "..." : prompt);

            ReportProgress("Analyzing your request...");
            await Task.Delay(300); // Simulate thinking

            ReportProgress("Generating response...");
            await Task.Delay(500); // Simulate more processing

            string response = GenerateResponseForPrompt(prompt);

            ReportProgress("Response ready");

            return new AIResponse
            {
                Text = response,
                Metadata = new Dictionary<string, object>
                {
                    { "model", "mock-ai" },
                    { "temperature", options.Temperature },
                    { "prompt_length", prompt.Length },
                    { "response_length", response.Length }
                }
            };
        }

        public async Task<AIResponse> GetCompletionWithHistoryAsync(
            IEnumerable<(bool IsUser, string Message)> history,
            string prompt,
            AIRequestOptions options)
        {
            _logger.LogInformation("Generating mock completion with history for prompt: {Prompt}",
                prompt.Length > 50 ? prompt.Substring(0, 50) + "..." : prompt);

            ReportProgress("Analyzing conversation history...");
            await Task.Delay(500); // Simulate processing

            ReportProgress("Generating contextual response...");
            await Task.Delay(700); // Simulate more processing

            string response = GenerateResponseForPrompt(prompt);

            // Add some variety based on conversation length
            int historyCount = 0;
            foreach (var _ in history)
            {
                historyCount++;
            }

            if (historyCount > 3)
            {
                response = "Based on our conversation so far, " + response.ToLower();
            }

            ReportProgress("Contextual response ready");

            return new AIResponse
            {
                Text = response,
                Metadata = new Dictionary<string, object>
                {
                    { "model", "mock-ai" },
                    { "temperature", options.Temperature },
                    { "history_turns", historyCount },
                    { "prompt_length", prompt.Length },
                    { "response_length", response.Length }
                }
            };
        }

        public Task DownloadModelAsync(string modelId, IProgress<double> progress)
        {
            // Simulate a download process
            return Task.Run(async () =>
            {
                _logger.LogInformation("Simulating download for model: {ModelId}", modelId);

                for (int i = 0; i <= 10; i++)
                {
                    progress?.Report(i / 10.0);
                    await Task.Delay(200);
                }

                _logger.LogInformation("Mock download completed for model: {ModelId}", modelId);
            });
        }

        public bool IsModelLoaded(string modelId)
        {
            // Mock service always pretends models are loaded
            return true;
        }

        public Task UnloadModelAsync(string modelId)
        {
            _logger.LogInformation("Simulating unload for model: {ModelId}", modelId);
            return Task.CompletedTask;
        }

        private string GenerateResponseForPrompt(string prompt)
        {
            prompt = prompt.ToLower().Trim();

            // Handle common questions
            if (prompt.Contains("hello") || prompt.Contains("hi ") || prompt == "hi")
            {
                return "Hello! I'm a simulated AI assistant. How can I help you today?";
            }
            else if (prompt.Contains("how are you"))
            {
                return "I'm operating smoothly, thank you for asking! How can I assist you?";
            }
            else if (prompt.Contains("your name") || prompt.Contains("who are you"))
            {
                return "I'm Nexi, your AI desktop assistant. I'm currently operating in simulation mode while model loading issues are being resolved.";
            }
            else if (prompt.Contains("help") || prompt.Contains("assistance") || prompt.Contains("can you"))
            {
                return "I'd be happy to help! While I'm operating in simulation mode, I can still provide general assistance. What would you like to know?";
            }
            else if (prompt.Contains("thanks") || prompt.Contains("thank you"))
            {
                return "You're welcome! Is there anything else I can help you with?";
            }
            else if (prompt.Contains("weather"))
            {
                return "I'm sorry, I don't have access to real-time weather data. You might want to check a weather service or app for that information.";
            }
            else if (prompt.Contains("time") || prompt.Contains("date"))
            {
                return $"The current date and time is {DateTime.Now}. Note that I'm providing this from your system clock.";
            }
            else if (prompt.Contains("joke") || prompt.Contains("funny"))
            {
                string[] jokes = {
                    "Why don't scientists trust atoms? Because they make up everything!",
                    "Why did the AI assistant go to art school? To learn how to draw conclusions!",
                    "What do you call a computer that sings? A Dell-a-cappella!",
                    "Why was the math book sad? Because it had too many problems.",
                    "How many programmers does it take to change a light bulb? None, that's a hardware problem!"
                };

                return jokes[_random.Next(jokes.Length)];
            }
            else if (prompt.Length < 10)
            {
                return "I see your message. Could you provide more details so I can better assist you?";
            }
            else
            {
                // For longer prompts, create a more generic response
                string[] responses = {
                    "That's an interesting point. While I'm in simulation mode, I can't provide detailed analysis, but I appreciate your thoughtful query.",
                    "Thank you for sharing that information. I'm currently operating with limited capabilities, but I'm here to assist as best I can.",
                    "I understand what you're asking about. Once my AI models are properly configured, I'll be able to provide more specific and helpful responses.",
                    "That's a good question. I'm making note of it and will provide better assistance once my full capabilities are online.",
                    "I appreciate your patience as I operate in simulation mode. Your question is important, and I'll do my best to help with my current capabilities."
                };

                return responses[_random.Next(responses.Length)];
            }
        }

        private void ReportProgress(string message)
        {
            _logger.LogInformation(message);
            OnInferenceProgress?.Invoke(this, message);
        }
    }
}