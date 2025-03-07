using System.Threading.Tasks;

namespace Nexi.Services.Interfaces
{
    public interface IWebSearchService
    {
        /// <summary>
        /// Performs a web search and returns the results as a formatted string
        /// </summary>
        /// <param name="query">The search query</param>
        /// <returns>A formatted string containing search results</returns>
        Task<string> SearchAsync(string query);
        
        /// <summary>
        /// Opens the default web browser with the specified search query
        /// </summary>
        /// <param name="query">The search query</param>
        /// <returns>A message indicating the search was performed</returns>
        string OpenBrowserSearch(string query);
    }
} 