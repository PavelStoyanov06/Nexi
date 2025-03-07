using Microsoft.Extensions.Logging;
using Nexi.Services.Interfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Nexi.Services
{
    public class DocumentService : IDocumentService
    {
        private readonly ILogger<DocumentService> _logger;

        public DocumentService(ILogger<DocumentService> logger)
        {
            _logger = logger;
        }

        public string CreateTextDocument(string fileName, string? content = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "Please provide a filename for the document.";
            }

            try
            {
                // Ensure the filename has a .txt extension
                if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    fileName += ".txt";
                }

                // Get the Documents folder path
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string filePath = Path.Combine(documentsPath, fileName);

                // Create the text file with optional content
                File.WriteAllText(filePath, content ?? "");

                // Open the file with the default text editor
                OpenFile(filePath);

                return $"Created and opened text document: {fileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating text document: {FileName}", fileName);
                return $"Failed to create text document: {ex.Message}";
            }
        }

        public string CreateDocument(string fileName, string? content = null)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "Please provide a filename for the document.";
            }

            try
            {
                // Ensure the filename has a .docx extension for Word documents
                if (!fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    fileName += ".docx";
                }

                // Get the Documents folder path
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string filePath = Path.Combine(documentsPath, fileName);

                // Create an empty document file
                // Note: Creating a proper Word document would require a library like OpenXML
                // For simplicity, we're just creating an empty file
                File.WriteAllBytes(filePath, new byte[0]);

                // Open the file with the default application
                OpenFile(filePath);

                return $"Created and opened document: {fileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating document: {FileName}", fileName);
                return $"Failed to create document: {ex.Message}";
            }
        }

        public string OpenDocument(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return "Please provide a file path.";
            }

            try
            {
                // If the path doesn't exist, check if it's in the Documents folder
                if (!File.Exists(filePath))
                {
                    string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    string fullPath = Path.Combine(documentsPath, filePath);
                    
                    if (File.Exists(fullPath))
                    {
                        filePath = fullPath;
                    }
                    else
                    {
                        return $"File not found: {filePath}";
                    }
                }

                // Open the file with the default application
                OpenFile(filePath);

                return $"Opened document: {Path.GetFileName(filePath)}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening document: {FilePath}", filePath);
                return $"Failed to open document: {ex.Message}";
            }
        }

        private void OpenFile(string filePath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{filePath}\"")
                {
                    CreateNoWindow = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", filePath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", filePath);
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported operating system");
            }
        }
    }
} 