using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeGoogleKit.Models;

public sealed class DetectedApp
{
    public required string Name { get; init; }
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public string Scope { get; init; } = "User";
    public string UninstallString { get; init; } = "";
    public bool IsDeveloperTool { get; init; }
    public bool IsUpdater { get; init; }
    public string Replacement { get; init; } = "";
    public string ReplacementUrl { get; init; } = "";
    public bool CanUninstall => !string.IsNullOrWhiteSpace(UninstallString);
}

public sealed class Alternative
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public string Note { get; init; } = "";
}

public sealed class GuideItem : INotifyPropertyChanged
{
    private bool _isDone;

    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required string GoogleProduct { get; init; }
    public required string Why { get; init; }
    public required IReadOnlyList<Alternative> Alternatives { get; init; }

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (_isDone == value) return;
            _isDone = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ScanSnapshot
{
    public List<DetectedApp> Apps { get; } = [];
    public string DefaultBrowser { get; set; } = "Unknown";
    public bool DefaultBrowserIsGoogle { get; set; }
    public List<string> GoogleFolders { get; } = [];
    public List<string> GoogleTasks { get; } = [];
    public List<string> GoogleServices { get; } = [];
    public List<string> ChromeExtensions { get; } = [];
    public List<string> GoogleProcesses { get; } = [];
    public List<string> StartupEntries { get; } = [];
    public List<string> SignedInEmails { get; } = [];
    public List<string> GoogleFolderChildren { get; } = [];
    public List<string> DnsServers { get; } = [];
    public bool DnsLooksLikeGoogle { get; set; }
}

public sealed class ChatMessage
{
    public required string Role { get; init; }
    public required string Text { get; init; }
}

public sealed class GdprRight
{
    public required string Article { get; init; }
    public required string Title { get; init; }
    public required string WhatItMeans { get; init; }
    public required string HowToUse { get; init; }
    public required string Url { get; init; }
}

public sealed class TakeoutEntry
{
    public required string Name { get; init; }
    public required string Hint { get; init; }
    public long SizeBytes { get; init; }
    public string SizeLabel => SizeBytes <= 0 ? "" : $"{SizeBytes / 1024d / 1024d:0.0} MB";
}

public sealed class AuditEvent
{
    public DateTime At { get; init; } = DateTime.Now;
    public required string Action { get; init; }
    public string Detail { get; init; } = "";
    public string Label => $"{At:yyyy-MM-dd HH:mm}  {Action}";
}
