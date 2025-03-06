using Microsoft.Extensions.Logging;
using Nexi.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nexi.Services
{
    public class HuggingFaceService : IHuggingFaceService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<HuggingFaceService> _logger;
        private const string HuggingFaceApiUrl = "https://huggingface.co/api";

        public HuggingFaceService(HttpClient httpClient, ILogger<HuggingFaceService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<IEnumerable<HuggingFaceModelInfo>> SearchModelsAsync(string query)
        {
            try
            {
                // Search for GGUF models
                var searchQuery = string.IsNullOrWhiteSpace(query) ? "gguf" : $"{query} gguf";
                var response = await _httpClient.GetAsync($"{HuggingFaceApiUrl}/models?search={Uri.EscapeDataString(searchQuery)}&limit=50");
                response.EnsureSuccessStatusCode();

                var searchResults = await response.Content.ReadFromJsonAsync<List<HuggingFaceModelSearchResult>>();
                if (searchResults == null || searchResults.Count == 0)
                {
                    return new List<HuggingFaceModelInfo>();
                }

                var modelInfos = new List<HuggingFaceModelInfo>();
                foreach (var result in searchResults)
                {
                    // Only include models that likely have GGUF files
                    if (result.ModelId.Contains("gguf", StringComparison.OrdinalIgnoreCase) ||
                        result.Tags.Contains("gguf") ||
                        result.Tags.Contains("llama") ||
                        result.Tags.Contains("llm"))
                    {
                        modelInfos.Add(new HuggingFaceModelInfo
                        {
                            Id = result.ModelId,
                            Name = result.ModelId.Split('/').Length > 1 ? result.ModelId.Split('/')[1] : result.ModelId,
                            Description = result.Description ?? string.Empty
                        });
                    }
                }

                return modelInfos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching HuggingFace models");
                throw;
            }
        }

        public async Task<IEnumerable<HuggingFaceModelFile>> GetModelFilesAsync(string modelId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{HuggingFaceApiUrl}/models/{modelId}/tree");
                response.EnsureSuccessStatusCode();

                var fileTree = await response.Content.ReadFromJsonAsync<List<HuggingFaceFileTreeItem>>();
                if (fileTree == null || fileTree.Count == 0)
                {
                    return new List<HuggingFaceModelFile>();
                }

                var ggufFiles = new List<HuggingFaceModelFile>();
                foreach (var file in fileTree)
                {
                    if (file.Type == "file" && file.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract quantization info from filename
                        var quantization = ExtractQuantizationInfo(file.Path);
                        
                        ggufFiles.Add(new HuggingFaceModelFile
                        {
                            FileName = file.Path,
                            Size = FormatFileSize(file.Size),
                            SizeInBytes = file.Size,
                            DownloadUrl = $"https://huggingface.co/{modelId}/resolve/main/{file.Path}",
                            Quantization = quantization
                        });
                    }
                }

                return ggufFiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting model files for {ModelId}", modelId);
                throw;
            }
        }

        private string ExtractQuantizationInfo(string fileName)
        {
            // Common quantization patterns in GGUF filenames
            var patterns = new[]
            {
                @"q(\d+)_(\w+)",  // q4_K, q5_K, etc.
                @"(\w+)\.(\d+)bit", // model.4bit.gguf
                @"(\d+)bit", // 4bit, 8bit
                @"q(\d+)k", // q4k, q5k
                @"q(\d+)_(\d+)" // q4_0, q5_1
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(fileName, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Value;
                }
            }

            // If no quantization info found, check for common terms
            if (fileName.Contains("fp16", StringComparison.OrdinalIgnoreCase))
                return "FP16";
            if (fileName.Contains("int8", StringComparison.OrdinalIgnoreCase))
                return "INT8";
            if (fileName.Contains("int4", StringComparison.OrdinalIgnoreCase))
                return "INT4";

            return "Unknown";
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }

    public class HuggingFaceModelSearchResult
    {
        public string ModelId { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }

    public class HuggingFaceFileTreeItem
    {
        public string Type { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
    }
} 