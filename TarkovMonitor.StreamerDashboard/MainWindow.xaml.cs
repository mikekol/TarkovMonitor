using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TarkovMonitor.StreamerDashboard.Models;
using TarkovMonitor.StreamerDashboard.Pages;
using TarkovMonitor.StreamerDashboard.Services;
using Windows.UI;

namespace TarkovMonitor.StreamerDashboard;

public sealed partial class MainWindow : Window
{
    private readonly GrpcEventClient _grpcClient;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        _grpcClient = App.Current.Services.GetRequiredService<GrpcEventClient>();
        var settings = App.Current.Services.GetRequiredService<AppSettings>();

        OverlayUrlText.Text = $"http://localhost:{settings.OverlayPort}/";

        _grpcClient.ConnectionStateChanged += (_, e) =>
        {
            DispatcherQueue.TryEnqueue(() => UpdateConnectionStatus(e));
        };

        // Navigate to dashboard on open
        ContentFrame.Navigate(typeof(DashboardPage));
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void UpdateConnectionStatus(ConnectionStateArgs e)
    {
        if (e.IsConnected)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 68, 200, 100));
            StatusText.Text = "Connected to service";
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 220, 60, 60));
            StatusText.Text = string.IsNullOrEmpty(e.Message) ? "Disconnected" : $"Disconnected — {e.Message}";
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            var pageType = tag switch
            {
                "EventActions" => typeof(ActionsPage),
                _ => typeof(DashboardPage),
            };
            ContentFrame.Navigate(pageType);
        }
    }
}
