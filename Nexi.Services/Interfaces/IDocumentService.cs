using System.Threading.Tasks;

namespace Nexi.Services.Interfaces
{
    public interface IDocumentService
    {
        /// <summary>
        /// Creates a new text document with the specified name and content
        /// </summary>
        /// <param name="fileName">The name of the file to create</param>
        /// <param name="content">Optional content to write to the file</param>
        /// <returns>A message indicating the result of the operation</returns>
        string CreateTextDocument(string fileName, string? content = null);
        
        /// <summary>
        /// Creates a new document with the specified name and content using the default document editor
        /// </summary>
        /// <param name="fileName">The name of the file to create</param>
        /// <param name="content">Optional content to write to the file</param>
        /// <returns>A message indicating the result of the operation</returns>
        string CreateDocument(string fileName, string? content = null);
        
        /// <summary>
        /// Opens an existing document with the default application
        /// </summary>
        /// <param name="filePath">The path to the file to open</param>
        /// <returns>A message indicating the result of the operation</returns>
        string OpenDocument(string filePath);
    }
} 