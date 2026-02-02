using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace RequestTracker.Services
{
    public class ClipboardService : IClipboardService
    {
        public async Task SetTextAsync(string text)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow;
                if (mainWindow?.Clipboard != null)
                {
                    await mainWindow.Clipboard.SetTextAsync(text);
                }
            }
        }
    }
}
