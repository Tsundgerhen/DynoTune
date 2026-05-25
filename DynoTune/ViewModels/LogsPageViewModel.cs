using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Linq;
using DynoTune.Models;

namespace DynoTune.ViewModels;

[SupportedOSPlatform("windows10.0.19041.0")]
public class LogsPageViewModel : INotifyPropertyChanged
{
    private LogRecord? _selectedRecord;
    private string _searchText = string.Empty;
    private string _selectedWorkload = "All";
    private string _selectedDanger = "All";
    private string _statusText = "Ready";

    public ObservableCollection<LogRecord> Records { get; } = new();

    public IReadOnlyList<string> WorkloadOptions { get; } = new[] { "All", "Idle", "Browsing", "Office", "Media", "Gaming", "HeavyCompute", "Unknown" };
    public IReadOnlyList<string> DangerOptions { get; } = new[] { "All", "Safe", "Warning", "Critical" };

    public LogRecord? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            _selectedRecord = value;
            OnPropertyChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string SelectedWorkload
    {
        get => _selectedWorkload;
        set
        {
            _selectedWorkload = string.IsNullOrWhiteSpace(value) ? "All" : value;
            OnPropertyChanged();
        }
    }

    public string SelectedDanger
    {
        get => _selectedDanger;
        set
        {
            _selectedDanger = string.IsNullOrWhiteSpace(value) ? "All" : value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public void Refresh()
    {
        Records.Clear();
        IReadOnlyList<LogRecord> source = App.LoggingService?.GetRecords() ?? Array.Empty<LogRecord>();
        IEnumerable<LogRecord> filtered = source;

        if (!SelectedWorkload.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(r => r.WorkloadType.ToString().Equals(SelectedWorkload, StringComparison.OrdinalIgnoreCase));
        }

        if (!SelectedDanger.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(r => r.DangerLevel.ToString().Equals(SelectedDanger, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string q = SearchText.Trim();
            filtered = filtered.Where(r =>
                r.ClassificationReason.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.ActiveProfile.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.DangerReasonDetail.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        foreach (LogRecord record in filtered.OrderByDescending(r => r.Timestamp))
        {
            Records.Add(record);
        }

        StatusText = $"Loaded {Records.Count.ToString(CultureInfo.InvariantCulture)} record(s)";
    }

    public async Task ExportNowAsync()
    {
        if (App.LoggingService is null)
        {
            StatusText = "Logging service unavailable.";
            return;
        }

        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DynoTune",
            "logs");
        Directory.CreateDirectory(logDir);
        string path = Path.Combine(logDir, $"logs-page-export-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        await App.LoggingService.SaveToCsvAsync(path);
        StatusText = $"Exported: {path}";
    }

    public void OpenLatestLogFolder()
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DynoTune",
            "logs");
        Directory.CreateDirectory(logDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{logDir}\"") { UseShellExecute = true });
        StatusText = $"Opened folder: {logDir}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
