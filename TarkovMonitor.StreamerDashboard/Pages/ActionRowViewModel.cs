using System.ComponentModel;
using TarkovMonitor.StreamerDashboard.Models;

namespace TarkovMonitor.StreamerDashboard.Pages;

public sealed class ActionRowViewModel : INotifyPropertyChanged
{
    private string _commandLine;
    private string _commandArgs;

    public string EventType { get; }

    public string CommandLine
    {
        get => _commandLine;
        set { _commandLine = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CommandLine))); }
    }

    public string CommandArgs
    {
        get => _commandArgs;
        set { _commandArgs = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CommandArgs))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ActionRowViewModel(EventAction action)
    {
        EventType = action.EventType;
        _commandLine = action.CommandLine;
        _commandArgs = action.CommandArgs;
    }

    public EventAction ToModel() => new() { EventType = EventType, CommandLine = CommandLine, CommandArgs = CommandArgs };
}
