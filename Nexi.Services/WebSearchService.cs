using Microsoft.Extensions.Logging;
using Nexi.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nexi.Services
{
    public class WebSearchService : IWebSearchService
    {
        private readonly ILogger<WebSearchService> _logger;
        private readonly HttpClient _httpClient;

        public WebSearchService(ILogger<WebSearchService> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        }

        public async Task<string> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return "Please provide a search query.";
            }

            try
            {
                // For a real implementation, you would use a proper search API like Bing, Google, or DuckDuckGo
                // This is a simplified version that scrapes search results
                string encodedQuery = Uri.EscapeDataString(query);
                string searchUrl = $"https://html.duckduckgo.com/html/?q={encodedQuery}";

                var response = await _httpClient.GetStringAsync(searchUrl);
                
                // Extract search results using regex (simplified)
                var results = ExtractSearchResults(response);
                
                if (results.Count == 0)
                {
                    return $"No results found for: {query}";
                }

                // Format results
                var formattedResults = FormatSearchResults(results, query);
                return formattedResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing web search for query: {Query}", query);
                return $"Failed to perform web search: {ex.Message}";
            }
        }

        public string OpenBrowserSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return "Please provide a search query.";
            }

            try
            {
                string encodedQuery = Uri.EscapeDataString(query);
                string searchUrl = $"https://www.google.com/search?q={encodedQuery}";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {searchUrl}")
                    {
                        CreateNoWindow = true
                    });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", searchUrl);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", searchUrl);
                }
                return $"Searching for: {query}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening browser search for query: {Query}", query);
                return $"Failed to perform web search: {ex.Message}";
            }
        }

        private List<SearchResult> ExtractSearchResults(string html)
        {
            var results = new List<SearchResult>();
            
            // Extract results using regex (simplified)
            var titleRegex = new Regex("<a class=\"result__a\" href=\"(.*?)\">(.*?)</a>");
            var descriptionRegex = new Regex("<a class=\"result__snippet\".*?>(.*?)</a>");
            
            var titleMatches = titleRegex.Matches(html);
            var descriptionMatches = descriptionRegex.Matches(html);
            
            for (int i = 0; i < Math.Min(titleMatches.Count, 5); i++)
            {
                var titleMatch = titleMatches[i];
                var descriptionMatch = i < descriptionMatches.Count ? descriptionMatches[i] : null;
                
                var url = titleMatch.Groups[1].Value;
                // Clean up URL (DuckDuckGo uses redirects)
                url = Regex.Match(url, "uddg=(.*?)&").Groups[1].Value;
                if (!string.IsNullOrEmpty(url))
                {
                    url = Uri.UnescapeDataString(url);
                }
                
                results.Add(new SearchResult
                {
                    Title = CleanHtml(titleMatch.Groups[2].Value),
                    Url = url,
                    Description = descriptionMatch != null ? CleanHtml(descriptionMatch.Groups[1].Value) : ""
                });
            }
            
            return results;
        }

        private string CleanHtml(string html)
        {
            // Remove HTML tags
            var text = Regex.Replace(html, "<.*?>", string.Empty);
            // Decode HTML entities
            text = System.Net.WebUtility.HtmlDecode(text);
            return text.Trim();
        }

        private string FormatSearchResults(List<SearchResult> results, string query)
        {
            var formattedResults = $"Search results for: {query}\n\n";
            
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                formattedResults += $"{i + 1}. {result.Title}\n";
                formattedResults += $"   {result.Description}\n";
                formattedResults += $"   URL: {result.Url}\n\n";
            }
            
            formattedResults += "To open any of these results in your browser, use the command '/open url [number]' or '/open [full url]'.";
            
            return formattedResults;
        }

        private class SearchResult
        {
            public string Title { get; set; } = "";
            public string Url { get; set; } = "";
            public string Description { get; set; } = "";
        }
    }
} 