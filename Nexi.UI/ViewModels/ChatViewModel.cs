using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.Specialized;
using System.Text;
using ReactiveUI;
using Nexi.Services.Interfaces;
using Nexi.UI.Models;
using Microsoft.Extensions.Logging;
using Avalonia.Threading;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Nexi.Data.Models;

namespace Nexi.UI.ViewModels
{
    public class ChatViewModel : ViewModelBase
    {
        private readonly ICommandProcessor _commandProcessor;
        private readonly IVoiceService _voiceService;
        private readonly IChatStorageService _chatStorage;
        private readonly IAIModelService _aiModelService;
        private readonly IUserSettingsService _userSettingsService;
        private readonly ILlamaSharpService _llamaSharpService;
        private readonly ILogger<ChatViewModel> _logger;
        private readonly IWebSearchService _webSearchService;
        private string _currentMessage = string.Empty;
        private bool _isVoiceModeEnabled;
        private ObservableCollection<ChatMessage> _messages;
        private bool _isProcessing;
        private string _sessionId;
        private string _title;
        private string? _selectedModelId;
        private bool _isDisposed = false;
        private bool _showCommandInstructions = false;
        private ChatMessage? _lastUsedAiMessage = null;

        // Event that the view can subscribe to for scrolling to bottom
        public event Action? ScrollToBottom;

        public ChatViewModel(
            ICommandProcessor commandProcessor,
            IVoiceService voiceService,
            IChatStorageService chatStorage,
            IAIModelService aiModelService,
            IUserSettingsService userSettingsService,
            ILlamaSharpService llamaSharpService,
            ILogger<ChatViewModel> logger,
            IWebSearchService webSearchService,
            string? sessionId = null,
            bool createNewSession = true)
        {
            _commandProcessor = commandProcessor;
            _voiceService = voiceService;
            _chatStorage = chatStorage;
            _aiModelService = aiModelService;
            _userSettingsService = userSettingsService;
            _llamaSharpService = llamaSharpService;
            _logger = logger;
            _webSearchService = webSearchService;
            _sessionId = sessionId ?? Guid.NewGuid().ToString();
            _title = "New Chat";
            Messages = new ObservableCollection<ChatMessage>();

            // Initialize commands
            SendMessageCommand = ReactiveCommand.CreateFromTask(SendMessageAsync, this.WhenAnyValue(x => x.HasMessageText));
            ClearMessageCommand = ReactiveCommand.Create(() => CurrentMessage = string.Empty);
            ToggleCommandInstructionsCommand = ReactiveCommand.Create<bool>(show => ShowCommandInstructions = show);
            CopyToClipboardCommand = ReactiveCommand.Create<string>(CopyTextToClipboard);

            // Load the selected model from user settings
            _ = LoadSelectedModelAsync();

            // Set up voice recognition if enabled
            _voiceService.SpeechRecognized += OnSpeechRecognized;

            // Either load existing chat history or add welcome message for new chats
            if (!string.IsNullOrEmpty(sessionId))
            {
                // Load existing chat
                _ = LoadChatHistoryAsync(sessionId);
            }
            else if (createNewSession)
            {
                // Add welcome message for new chats
                _ = AddWelcomeMessageAsync();
            }

            // Subscribe to voice mode changes
            this.WhenAnyValue(x => x.IsVoiceModeEnabled)
                .Subscribe(async isEnabled =>
                {
                    if (isEnabled)
                    {
                        await _voiceService.StartListeningAsync();
                        await AddMessageAsync(new ChatMessage
                        {
                            Content = "Nexi: Voice mode enabled. Speak your commands.",
                            Timestamp = DateTime.Now,
                            IsUser = false
                        });
                    }
                    else
                    {
                        await _voiceService.StopListeningAsync();
                        await AddMessageAsync(new ChatMessage
                        {
                            Content = "Nexi: Voice mode disabled.",
                            Timestamp = DateTime.Now,
                            IsUser = false
                        });
                    }
                });
        }

        public string Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        public ObservableCollection<ChatMessage> Messages
        {
            get => _messages;
            set => this.RaiseAndSetIfChanged(ref _messages, value);
        }

        public string CurrentMessage
        {
            get => _currentMessage;
            set
            {
                this.RaiseAndSetIfChanged(ref _currentMessage, value);
                this.RaisePropertyChanged(nameof(HasMessageText));
            }
        }

        public bool IsVoiceModeEnabled
        {
            get => _isVoiceModeEnabled;
            set => this.RaiseAndSetIfChanged(ref _isVoiceModeEnabled, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            private set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
        }

        public bool ShowCommandInstructions
        {
            get => _showCommandInstructions;
            set => this.RaiseAndSetIfChanged(ref _showCommandInstructions, value);
        }

        public string? SelectedModelId
        {
            get => _selectedModelId;
            set 
            {
                try
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        _logger.LogWarning("Attempted to set null or empty SelectedModelId");
                        return;
                    }

                    // Log the model change
                    _logger.LogInformation($"Setting SelectedModelId to {value}");
                    
                    // Validate the model exists and is downloaded before setting
                    Task.Run(async () =>
                    {
                        try
                        {
                            // Check if model exists
                            var model = await _aiModelService.GetModelAsync(value);
                            if (model == null)
                            {
                                _logger.LogWarning($"Model {value} not found in database");
                                return;
                            }

                            // Check if model is downloaded
                            bool isDownloaded = await _aiModelService.IsModelDownloadedAsync(value);
                            if (!isDownloaded)
                            {
                                _logger.LogWarning($"Model {value} is not downloaded");
                                return;
                            }

                            // Model is valid, set it on the UI thread
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                this.RaiseAndSetIfChanged(ref _selectedModelId, value);
                                _logger.LogInformation($"Successfully set SelectedModelId to {value}");
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error validating model {value}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error setting SelectedModelId");
                }
            }
        }

        public bool HasMessageText => !string.IsNullOrWhiteSpace(CurrentMessage);

        public ICommand SendMessageCommand { get; }
        public ICommand ClearMessageCommand { get; }
        public ICommand ToggleCommandInstructionsCommand { get; }
        public ICommand CopyToClipboardCommand { get; }

        private async Task LoadChatHistoryAsync(string sessionId)
        {
            try
            {
                var session = await _chatStorage.GetSessionAsync(sessionId);
                if (session != null)
                {
                    Title = session.Title;
                    foreach (var message in session.Messages)
                    {
                        Messages.Add(new ChatMessage
                        {
                            Content = message.Content,
                            Timestamp = message.Timestamp,
                            IsUser = message.IsUser
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading chat history");
            }
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentMessage))
                return;
                
            await SendMessageAsync(CurrentMessage.Trim());
            CurrentMessage = string.Empty;
        }

        private async Task SendMessageAsync(string userMessageText)
        {
            if (string.IsNullOrWhiteSpace(userMessageText))
            {
                return;
            }

            try
            {
                // Set processing state
                IsProcessing = true;

                // Check if this is a slash command
                if (userMessageText.StartsWith("/"))
                {
                    await ProcessCommandAsync(userMessageText);
                    return;
                }

                // Check if this is a command without the slash prefix
                if (_commandProcessor.IsCommand(userMessageText))
                {
                    string response = _commandProcessor.ProcessCommand(userMessageText);
                    
                    // Add user message to the chat
                    var commandUserMessage = new ChatMessage
                    {
                        Content = userMessageText,
                        Timestamp = DateTime.Now,
                        IsUser = true
                    };
                    
                    await AddMessageAsync(commandUserMessage);
                    
                    // Add response
                    await AddMessageAsync(new ChatMessage
                    {
                        Content = response,
                        Timestamp = DateTime.Now,
                        IsUser = false,
                        IsSystemMessage = true
                    });
                    
                    return;
                }

                // Add user message to the chat
                var userMessage = new ChatMessage
                {
                    Content = userMessageText,
                    Timestamp = DateTime.Now,
                    IsUser = true
                };
                
                await AddMessageAsync(userMessage);
                
                // Auto-set title if this is the first user message
                if (Title == "New Chat" && Messages.Count <= 3)
                {
                    // Always use the user's first message as the title
                    var firstUserMessage = Messages.FirstOrDefault(m => m.IsUser)?.Content ?? userMessageText;
                    Title = firstUserMessage.Length > 25 ? firstUserMessage.Substring(0, 22) + "..." : firstUserMessage;
                    await UpdateSessionTitleAsync(_sessionId, Title);
                }

                // Process the input
                await ProcessInputAsync(userMessageText);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void OnSpeechRecognized(object? sender, string text)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                // Add user message first
                var userMessage = new ChatMessage
                {
                    Content = text,
                    Timestamp = DateTime.Now,
                    IsUser = true
                };
                
                await AddMessageAsync(userMessage);
                
                // Then process the input for AI response
                await ProcessInputAsync(text);
            });
        }

        // Helper method to clean up AI responses
        private string CleanupAIResponse(string response)
        {
            // Remove "User:" at the end of the response
            if (response.EndsWith("User:"))
            {
                response = response.Substring(0, response.Length - 5).Trim();
            }
            
            // Remove "User:" anywhere in the response
            response = response.Replace("User:", "").Trim();
            
            // Handle case where there might be multiple newlines at the end
            response = response.TrimEnd('\r', '\n');
            
            // If the response is just "Thinking...", don't add the prefix
            if (response == "Thinking...")
            {
                return response;
            }
            
            // If the response doesn't already start with "Nexi:", add it
            if (!response.StartsWith("Nexi:"))
            {
                // If the response starts with "System:", replace it with "Nexi:"
                if (response.StartsWith("System:"))
                {
                    response = "Nexi:" + response.Substring(7);
                }
                else
                {
                    response = "Nexi: " + response;
                }
            }
            
            return response;
        }

        private async Task ProcessInputAsync(string input)
        {
            IsProcessing = true;
            _logger.LogInformation("Processing input: {Input}", input);
            
            try
            {
                // Auto-set title if this is the first user message
                if (Title == "New Chat" && Messages.Count <= 3)
                {
                    // Always use the user's first message as the title
                    var firstUserMessage = Messages.FirstOrDefault(m => m.IsUser)?.Content ?? input;
                    Title = firstUserMessage.Length > 25 ? firstUserMessage.Substring(0, 22) + "..." : firstUserMessage;
                    await UpdateSessionTitleAsync(_sessionId, Title);
                }

                // Process message
                string response;
                
                // First check if the input is a command, regardless of AI model availability
                if (_commandProcessor.IsCommand(input))
                {
                    response = _commandProcessor.ProcessCommand(input);
                    
                    // Add response
                    await AddMessageAsync(new ChatMessage
                    {
                        Content = response,
                        Timestamp = DateTime.Now,
                        IsUser = false,
                        IsSystemMessage = true // Mark as system message for proper styling
                    });
                }
                // Then check if we should use AI model
                else if (!string.IsNullOrEmpty(SelectedModelId) && await _aiModelService.IsModelDownloadedAsync(SelectedModelId))
                {
                    _logger.LogInformation("Using AI model {ModelId} to generate response", SelectedModelId);
                    
                    // Create a response message placeholder that will be updated in real-time
                    var responseMessage = new ChatMessage
                    {
                        Content = "Thinking...",
                        Timestamp = DateTime.Now,
                        IsUser = false
                    };
                    
                    // Add the placeholder message to the chat directly to ensure immediate UI update
                    await Dispatcher.UIThread.InvokeAsync(() => 
                    {
                        Messages.Add(responseMessage);
                        this.RaisePropertyChanged(nameof(Messages));
                        ScrollToBottom?.Invoke();
                    });
                    
                    _logger.LogInformation("Added 'Thinking...' placeholder message to chat");
                    
                    // Use the AI model for generating a response
                    string generatedText = "";
                    bool receivedAnyTokens = false;
                    
                    // Create a token handler that updates the UI
                    Action<string> tokenHandler = token => 
                    {
                        if (string.IsNullOrEmpty(token)) return;
                        
                        receivedAnyTokens = true;
                        generatedText += token;
                        
                        // Log the first few tokens to help with debugging
                        if (generatedText.Length <= 20)
                        {
                            _logger.LogDebug("Received token: {Token}", token);
                        }
                        
                        // Update the message content as tokens arrive - use InvokeAsync instead of Post
                        // to ensure the UI update happens immediately
                        Dispatcher.UIThread.InvokeAsync(() => 
                        {
                            try
                            {
                                // Clean up the response as it's being generated
                                responseMessage.Content = CleanupAIResponse(generatedText);
                                
                                // Force property change notification on the specific message
                                // This is crucial for the UI to update
                                var index = Messages.IndexOf(responseMessage);
                                if (index >= 0)
                                {
                                    // Replace the message to force a collection change notification
                                    Messages[index] = responseMessage;
                                }
                                
                                // Also raise property changed for the entire collection
                                this.RaisePropertyChanged(nameof(Messages));
                                
                                // Trigger scroll to bottom to follow the generating text
                                ScrollToBottom?.Invoke();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error updating UI during token generation");
                            }
                        });
                    };
                    
                    _logger.LogInformation("Starting model inference");
                    // Run the model inference
                    bool success = await _aiModelService.RunModelInferenceAsync(
                        SelectedModelId,
                        input,
                        tokenHandler);
                    _logger.LogInformation("Model inference completed with success={Success}, receivedAnyTokens={ReceivedAnyTokens}", success, receivedAnyTokens);
                    
                    // If we didn't receive any tokens or the inference failed, show an error
                    if (!receivedAnyTokens || !success)
                    {
                        _logger.LogError("AI model inference failed or didn't generate any tokens");
                        
                        // Update the message to show an error - use InvokeAsync for immediate UI update
                        await Dispatcher.UIThread.InvokeAsync(() => 
                        {
                            responseMessage.Content = "Nexi: Sorry, I couldn't generate a response. Please try again.";
                            
                            // Force property change notification
                            var index = Messages.IndexOf(responseMessage);
                            if (index >= 0)
                            {
                                // Replace the message to force a collection change notification
                                Messages[index] = responseMessage;
                            }
                            
                            this.RaisePropertyChanged(nameof(Messages));
                            ScrollToBottom?.Invoke();
                        });
                    }
                    else
                    {
                        // Update the final response with cleaned text
                        string cleanedResponse = CleanupAIResponse(generatedText);
                        _logger.LogInformation("Generated response of length {Length}", cleanedResponse.Length);
                        
                        // Update the UI on the UI thread - use InvokeAsync for immediate UI update
                        await Dispatcher.UIThread.InvokeAsync(() => 
                        {
                            responseMessage.Content = cleanedResponse;
                            
                            // Force property change notification
                            var index = Messages.IndexOf(responseMessage);
                            if (index >= 0)
                            {
                                // Replace the message to force a collection change notification
                                Messages[index] = responseMessage;
                            }
                            
                            this.RaisePropertyChanged(nameof(Messages));
                            ScrollToBottom?.Invoke();
                        });
                        
                        // Save the message to storage
                        await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
                        {
                            Content = cleanedResponse,
                            IsUser = false,
                            Timestamp = responseMessage.Timestamp
                        });
                        
                        // Remember this message for potential document creation
                        _lastUsedAiMessage = responseMessage;
                    }
                }
                else
                {
                    response = "Nexi: I need an AI model to respond to that. Please select a model in the Settings or use a command. Type 'help' to see available commands.";
                    
                    // Add response
                    await AddMessageAsync(new ChatMessage
                    {
                        Content = response,
                        Timestamp = DateTime.Now,
                        IsUser = false,
                        IsSystemMessage = true
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing input: {Message}", ex.Message);
                
                // Add error message to chat
                await AddMessageAsync(new ChatMessage
                {
                    Content = "Nexi: Sorry, I encountered an error processing your request. Please try again.",
                    Timestamp = DateTime.Now,
                    IsUser = false,
                    IsSystemMessage = true
                });
            }
            finally
            {
                IsProcessing = false;
                
                // Ensure we scroll to the bottom after processing
                ScrollToBottom?.Invoke();
            }
        }

        private async Task AddMessageAsync(ChatMessage message)
        {
            Messages.Add(message);

            // Convert to storage message
            var messageData = new ChatMessageData
            {
                Content = message.Content,
                Timestamp = message.Timestamp,
                IsUser = message.IsUser
            };

            try
            {
                var session = await _chatStorage.GetSessionAsync(_sessionId);
                if (session == null)
                {
                    // Only create a new session if we're explicitly told to
                    _logger.LogWarning($"Session {_sessionId} not found. This should not happen with the new flow.");
                    return;
                }

                await _chatStorage.AddMessageAsync(_sessionId, messageData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving message: {Message}", ex.Message);
            }
        }

        private async Task UpdateSessionTitleAsync(string sessionId, string title)
        {
            try
            {
                var session = await _chatStorage.GetSessionAsync(sessionId);
                if (session != null)
                {
                    session.Title = title;
                    await _chatStorage.SaveSessionAsync(session);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating session title: {Message}", ex.Message);
            }
        }

        private async Task LoadSelectedModelAsync()
        {
            try
            {
                _logger.LogInformation("Loading selected model from user settings");
                
                // Get user settings
                var settings = await _userSettingsService.GetSettingsAsync();
                if (settings != null && !string.IsNullOrEmpty(settings.SelectedModelId))
                {
                    _logger.LogInformation($"Found selected model ID in settings: {settings.SelectedModelId}");
                    
                    // Check if the model exists and is downloaded
                    var model = await _aiModelService.GetModelAsync(settings.SelectedModelId);
                    if (model != null)
                    {
                        bool isDownloaded = await _aiModelService.IsModelDownloadedAsync(settings.SelectedModelId);
                        if (isDownloaded)
                        {
                            _logger.LogInformation($"Setting selected model to {settings.SelectedModelId}");
                            SelectedModelId = settings.SelectedModelId;
                            
                            // Add a system message indicating the selected model
                            await AddMessageAsync(new ChatMessage
                            {
                                Content = $"Using AI model: {model.Name}",
                                Timestamp = DateTime.Now,
                                IsUser = false,
                                IsSystemMessage = true
                            });
                        }
                        else
                        {
                            _logger.LogWarning($"Selected model {settings.SelectedModelId} is not downloaded");
                            
                            // Add a system message indicating the model is not downloaded
                            await AddMessageAsync(new ChatMessage
                            {
                                Content = $"The selected model '{model.Name}' is not downloaded. Please go to Models and download it first.",
                                Timestamp = DateTime.Now,
                                IsUser = false,
                                IsSystemMessage = true
                            });
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Selected model {settings.SelectedModelId} not found");
                    }
                }
                else
                {
                    _logger.LogInformation("No selected model found in settings");
                    
                    // Add a system message indicating no model is selected
                    await AddMessageAsync(new ChatMessage
                    {
                        Content = "No AI model selected. Please go to Settings to select a model.",
                        Timestamp = DateTime.Now,
                        IsUser = false,
                        IsSystemMessage = true
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading selected model");
            }
        }

        private string GenerateTitleFromMessages()
        {
            // Generate a title based on the first user message
            var firstUserMessage = Messages.FirstOrDefault(m => m.IsUser)?.Content ?? "New Chat";
            
            // Truncate to a reasonable length
            if (firstUserMessage.Length > 30)
            {
                firstUserMessage = firstUserMessage.Substring(0, 27) + "...";
            }
            
            return firstUserMessage;
        }

        private async Task ProcessCommandAsync(string command)
        {
            // Trim the command and remove the leading slash
            command = command.Trim();
            if (command.StartsWith("/"))
            {
                command = command.Substring(1);
            }

            // Add the command to the chat
            var userMessage = new ChatMessage
            {
                Content = "/" + command,
                IsUser = true,
                Timestamp = DateTime.Now
            };
            Messages.Add(userMessage);

            // Clear the input
            CurrentMessage = string.Empty;

            // Special handling for search in chat command
            if (command.StartsWith("search in chat ", StringComparison.OrdinalIgnoreCase))
            {
                string searchQuery = command.Substring("search in chat".Length).Trim();
                
                // Add a processing message
                var processingMessage = new ChatMessage
                {
                    Content = $"Searching for: {searchQuery}...",
                    IsUser = false,
                    IsSystemMessage = true,
                    Timestamp = DateTime.Now
                };
                Messages.Add(processingMessage);
                
                // Perform the search
                try
                {
                    string searchResults = await _webSearchService.SearchAsync(searchQuery);
                    
                    // Replace the processing message with the results
                    Messages.Remove(processingMessage);
                    
                    var searchResponseMessage = new ChatMessage
                    {
                        Content = searchResults,
                        IsUser = false,
                        IsSystemMessage = true,
                        Timestamp = DateTime.Now
                    };
                    Messages.Add(searchResponseMessage);
                    
                    // Save the search results to storage
                    await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
                    {
                        Content = searchResults,
                        IsUser = false,
                        Timestamp = DateTime.Now
                    });
                    
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error performing web search: {Message}", ex.Message);
                    
                    // Replace the processing message with the error
                    Messages.Remove(processingMessage);
                    
                    var errorMessage = new ChatMessage
                    {
                        Content = $"Error performing web search: {ex.Message}",
                        IsUser = false,
                        IsSystemMessage = true,
                        Timestamp = DateTime.Now
                    };
                    Messages.Add(errorMessage);
                    
                    return;
                }
            }
            
            // Special handling for AI-assisted document creation
            if ((command.StartsWith("create text ", StringComparison.OrdinalIgnoreCase) || 
                 command.StartsWith("create document ", StringComparison.OrdinalIgnoreCase)) && 
                !command.Contains("|"))
            {
                // Check if there's a previous AI message that we can use as content
                var lastAiMessage = Messages
                    .Where(m => !m.IsUser && !m.IsSystemMessage)
                    .LastOrDefault();
                
                if (lastAiMessage != null && lastAiMessage != _lastUsedAiMessage)
                {
                    // Add the AI content to the command
                    string originalCommand = command;
                    command = $"{command} | {lastAiMessage.Content}";
                    
                    // Process the modified command
                    var commandResult = _commandProcessor.ProcessCommand(command);
                    
                    // Add the result to the chat
                    var commandResponseMessage = new ChatMessage
                    {
                        Content = commandResult,
                        IsUser = false,
                        IsSystemMessage = true,
                        Timestamp = DateTime.Now
                    };
                    Messages.Add(commandResponseMessage);
                    
                    // Save the command and response to storage
                    await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
                    {
                        Content = "/" + originalCommand,
                        IsUser = true,
                        Timestamp = DateTime.Now
                    });
                    
                    await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
                    {
                        Content = commandResult,
                        IsUser = false,
                        Timestamp = DateTime.Now
                    });
                    
                    // Mark this AI message as used
                    _lastUsedAiMessage = lastAiMessage;
                    
                    return;
                }
            }

            // Process the command
            var result = _commandProcessor.ProcessCommand(command);

            // Add the result to the chat
            var responseMessage = new ChatMessage
            {
                Content = result,
                IsUser = false,
                IsSystemMessage = true, // Mark as system message for proper styling
                Timestamp = DateTime.Now
            };
            Messages.Add(responseMessage);

            // Save the command and response to storage
            await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
            {
                Content = "/" + command,
                IsUser = true,
                Timestamp = DateTime.Now
            });

            await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
            {
                Content = result,
                IsUser = false,
                Timestamp = DateTime.Now
            });

            // Scroll to bottom
            ScrollToBottom?.Invoke();
        }

        // Method to add welcome message for new chats
        public async Task AddWelcomeMessageAsync()
        {
            var welcomeMessage = new ChatMessage
            {
                Content = "Nexi: Welcome to Nexi! I'm here to help you. Type a message to start chatting, or use commands like /help to see what I can do.\n\n" +
                          "New features available:\n" +
                          "• Web search - Try '/search in chat [query]'\n" +
                          "• Document creation - Try '/create text [filename]'\n" +
                          "• AI-assisted documents - Ask me to create content, then use '/create text [filename]' to save it\n\n" +
                          "Click the help icon (?) in the bottom left for more commands.",
                IsUser = false,
                Timestamp = DateTime.Now
            };
            
            await AddMessageAsync(welcomeMessage);
        }

        private void CopyTextToClipboard(string text)
        {
            try
            {
                // Show a system message indicating the text was copied
                var copyMessage = new ChatMessage
                {
                    Content = "Text copied to clipboard",
                    IsUser = false,
                    IsSystemMessage = true,
                    Timestamp = DateTime.Now
                };
                Messages.Add(copyMessage);
                
                // Remove the message after 3 seconds
                Task.Delay(3000).ContinueWith(_ => 
                {
                    Dispatcher.UIThread.Post(() => 
                    {
                        if (Messages.Contains(copyMessage))
                        {
                            Messages.Remove(copyMessage);
                        }
                    });
                });
                
                // Copy to clipboard using platform-specific code
                Task.Run(() => {
                    try {
                        // Use a simple approach that works cross-platform
                        var tempFile = System.IO.Path.GetTempFileName();
                        System.IO.File.WriteAllText(tempFile, text);
                        
                        // Use platform-specific clipboard command
                        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                        {
                            System.Diagnostics.Process.Start("cmd.exe", $"/c clip < \"{tempFile}\"");
                        }
                        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                        {
                            System.Diagnostics.Process.Start("bash", $"-c \"cat '{tempFile}' | pbcopy\"");
                        }
                        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                        {
                            System.Diagnostics.Process.Start("bash", $"-c \"cat '{tempFile}' | xclip -selection clipboard\"");
                        }
                        
                        // Clean up temp file
                        Task.Delay(1000).ContinueWith(_ => {
                            try {
                                if (System.IO.File.Exists(tempFile))
                                {
                                    System.IO.File.Delete(tempFile);
                                }
                            } catch {}
                        });
                        
                        _logger.LogInformation("Text copied to clipboard");
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error in clipboard process: {Message}", ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying text to clipboard");
            }
        }

        public override void Dispose()
        {
            try
            {
                // Unsubscribe from events
                _voiceService.SpeechRecognized -= OnSpeechRecognized;
                
                // Clean up LlamaSharpService resources
                if (_llamaSharpService is IDisposable disposableLlama)
                {
                    _logger.LogInformation("Disposing LlamaSharpService from ChatViewModel");
                    disposableLlama.Dispose();
                }
                
                // Clear any references that might hold onto resources
                Messages.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ChatViewModel disposal");
            }
            finally
            {
                base.Dispose();
            }
        }
    }
}