using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RequestTracker.Views;

public partial class RequestDetailsView : UserControl
{
    public RequestDetailsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}