using System.Collections.Generic;
using System.Threading.Tasks;

namespace IpDeskCtrl.Core
{
    /// <summary>Appunti del sistema, letti e scritti dall'interfaccia (thread dell'interfaccia).</summary>
    public interface IClipboardBridge
    {
        Task<string?> GetTextAsync();
        Task SetTextAsync(string text);
        /// <summary>File e cartelle negli appunti (percorsi locali); vuoto se non ce ne sono.</summary>
        Task<List<string>> GetFilesAsync();
        /// <summary>Mette file e cartelle negli appunti; false se il sistema non lo permette.</summary>
        Task<bool> SetFilesAsync(List<string> paths);
    }
}
