# Nexi Desktop Assistant - Technical Overview

## Architecture
A desktop application built with .NET 8 using Avalonia UI and MVVM pattern. The app processes voice commands through local speech recognition, interprets them using either local or cloud AI models, and executes desktop control commands.

## Core Components

### Voice Processing
- Uses NAudio for audio capture
- Whisper.net for local speech-to-text conversion
- Background service for continuous listening

### AI Integration
- Local Models:
  - LLamaSharp for running local LLMs
  - Configurable model selection
  - Local inference settings

- Cloud Models:
  - OpenAI API integration
  - Azure OpenAI option
  - Extensible for other providers

### Desktop Control
- OS-specific API calls for system control
- Command parsing and validation
- Error handling and safety checks

## Implementation Details

### Main Application
- Avalonia UI for cross-platform UI
- MVVM architecture for clean separation
- Command execution pipeline
- Model switching capabilities

### Data Flow
1. Voice input captured through NAudio
2. Speech converted to text via Whisper.net
3. Text processed by selected AI model
4. Response parsed for system commands
5. Commands executed through OS APIs
6. UI updated with results

### System Integration
- File system operations
- Application control
- System settings management
- Window management
- Basic scripting support

## Development Requirements

### Tools & SDKs
- .NET 8 SDK
- Avalonia UI framework
- Visual Studio 2022 or JetBrains Rider

### Key Packages
- Avalonia.Desktop
- NAudio
- Whisper.net
- LLamaSharp
- OpenAI API client

### Dependencies
- Local AI models (optional)
- OpenAI API key (if using cloud models)
- System permissions for desktop control

## Setup Instructions
1. Install .NET 8 SDK
2. Clone repository
3. Restore NuGet packages
4. Configure AI models
5. Build and run

## Configuration
- Model selection in app settings
- Voice input parameters
- System command permissions
- API keys for cloud services
<h2> ⚠️ This is to be taken as reference, since the technologies in use during the project are subject to change. </h2>
