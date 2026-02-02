using System.Threading.Tasks;

namespace RequestTracker.Services
{
    public interface IClipboardService
    {
        Task SetTextAsync(string text);
    }
}
