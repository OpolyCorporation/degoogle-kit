using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeGoogleKit.Models;

public enum MigrationMode
{
    Easy,
    Privacy,
    Own
}

public enum MigrationLevel
{
    Easy,
    Converter,
    Limited
}

public enum ServiceStatus
{
    NotStarted,
    ExportRequired,
    ReadyToImport,
    Imported,
    Verified,
    GoogleStillConnected,
    ReadyToDisconnect,
    Complete
}

public sealed class Destination
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public string Note { get; init; } = "";
}

public sealed class GoogleServiceDef
{
    public required string Id { get; init; }
    public required string GoogleName { get; init; }
    public required string Category { get; init; }
    public required MigrationLevel Level { get; init; }
    public required int Weight { get; init; }
    public required string How { get; init; }
    public required Destination Easy { get; init; }
    public required Destination Privacy { get; init; }
    public required Destination Own { get; init; }
    public bool InV1 { get; init; } = true;
    public bool YoutubeBackendWarning { get; init; }
    public string TakeoutHint { get; init; } = "";
}

public sealed class PlanRow : INotifyPropertyChanged
{
    private bool _selected = true;
    private ServiceStatus _status = ServiceStatus.NotStarted;

    public required GoogleServiceDef Def { get; init; }
    public Destination Destination { get; set; } = new() { Name = "", Url = "" };

    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; OnPropertyChanged(); }
    }

    public ServiceStatus Status
    {
        get => _status;
        set { if (_status == value) return; _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusLabel)); OnPropertyChanged(nameof(Progress)); }
    }

    public string StatusLabel => Status switch
    {
        ServiceStatus.NotStarted => "Not started",
        ServiceStatus.ExportRequired => "Export required (Takeout)",
        ServiceStatus.ReadyToImport => "Ready to import",
        ServiceStatus.Imported => "Imported — verify before disconnecting Google",
        ServiceStatus.Verified => "Verified",
        ServiceStatus.GoogleStillConnected => "Google still connected",
        ServiceStatus.ReadyToDisconnect => "Safe to disconnect Google",
        ServiceStatus.Complete => "Complete",
        _ => Status.ToString()
    };

    public double Progress => Status switch
    {
        ServiceStatus.NotStarted => 0,
        ServiceStatus.ExportRequired => 0.15,
        ServiceStatus.ReadyToImport => 0.4,
        ServiceStatus.Imported => 0.7,
        ServiceStatus.Verified => 0.85,
        ServiceStatus.GoogleStillConnected => 0.85,
        ServiceStatus.ReadyToDisconnect => 0.95,
        ServiceStatus.Complete => 1,
        _ => 0
    };

    public string LevelLabel => Def.Level switch
    {
        MigrationLevel.Easy => "Easy",
        MigrationLevel.Converter => "Converter",
        MigrationLevel.Limited => "Limited / no 1:1",
        _ => ""
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TakeoutInventory
{
    public string SourcePath { get; set; } = "";
    public List<TakeoutEntry> Bundles { get; } = [];
    public long Emails { get; set; }
    public long Contacts { get; set; }
    public long Events { get; set; }
    public long Photos { get; set; }
    public long Videos { get; set; }
    public long KeepNotes { get; set; }
    public long SavedPlaces { get; set; }
    public long YoutubeSubscriptions { get; set; }
    public long YoutubePlaylists { get; set; }
    public long DriveBytes { get; set; }
    public bool HasPasswordCsv { get; set; }
    public bool HasChromeBookmarks { get; set; }
    public bool HasLocationHistory { get; set; }
    public List<string> DetectedServiceIds { get; } = [];
    public string Summary { get; set; } = "";
}

public sealed class ScoreSnapshot
{
    public int Percent { get; init; }
    public int Remaining { get; init; }
    public List<string> RemainingHigh { get; init; } = [];
}
