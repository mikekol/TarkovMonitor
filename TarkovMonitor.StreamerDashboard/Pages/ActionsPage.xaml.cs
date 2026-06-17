using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Diagnostics;
using TarkovMonitor.StreamerDashboard.Models;
using TarkovMonitor.StreamerDashboard.Services;

namespace TarkovMonitor.StreamerDashboard.Pages;

public sealed partial class ActionsPage : Page
{
    private readonly AppSettings _settings;
    private readonly ObservableCollection<ActionRowViewModel> _rows = [];

    public ActionsPage()
    {
        InitializeComponent();

        _settings = App.Current.Services.GetRequiredService<AppSettings>();

        // Build one row per known event type; merge with saved settings
        foreach (var eventType in KnownEventTypes.All)
        {
            var saved = _settings.Actions.FirstOrDefault(a => a.EventType == eventType)
                        ?? new EventAction { EventType = eventType };
            _rows.Add(new ActionRowViewModel(saved));
        }

        ActionsList.ItemsSource = _rows;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.Actions = _rows.Select(r => r.ToModel()).ToList();
        _settings.Save();

        SaveFeedback.Text = "Saved ✓";
        SaveFeedback.Visibility = Visibility.Visible;

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (_, _) => { SaveFeedback.Visibility = Visibility.Collapsed; timer.Stop(); };
        timer.Start();
    }

    private void TestAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ActionRowViewModel row })
            FireTestEvent(row);
    }

    private void FireTestEvent(ActionRowViewModel row)
    {
        if (string.IsNullOrWhiteSpace(row.CommandLine)) return;

        var args = row.CommandArgs.Replace("%EVENT%", row.EventType);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = row.CommandLine,
                Arguments = args,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TestAction] {row.EventType}: {ex.Message}");
        }
    }
}
