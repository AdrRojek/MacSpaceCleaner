using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacSpaceCleaner.Core;
using MacSpaceCleaner.Core.Models;

namespace MacSpaceCleaner.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly VolumeScanner _volumeScanner = new();
    private readonly CategoryScanner _categoryScanner = new();
    private readonly LargeFileFinder _largeFileFinder = new();
    private readonly CleanerService _cleaner = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<VolumeInfo> Volumes { get; } = [];
    public ObservableCollection<CategoryItemViewModel> SystemCategories { get; } = [];
    public ObservableCollection<CategoryItemViewModel> DeveloperCategories { get; } = [];
    public ObservableCollection<LargeFileItemViewModel> LargeFiles { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    private string _statusText = "Gotowy. Kliknij „Skanuj”, aby zobaczyć zajętość całego Maca.";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _showDeveloperJunk;

    [ObservableProperty]
    private string _confirmSummary = string.Empty;

    [ObservableProperty]
    private bool _isConfirmVisible;

    public MainViewModel()
    {
        ReloadCategoryShell();
        RefreshVolumes();
    }

    private void ReloadCategoryShell()
    {
        SystemCategories.Clear();
        DeveloperCategories.Clear();
        foreach (var cat in _categoryScanner.CreateCategories())
        {
            var vm = new CategoryItemViewModel(cat);
            if (cat.Group == CleanupGroup.System)
                SystemCategories.Add(vm);
            else
                DeveloperCategories.Add(vm);
        }
    }

    [RelayCommand]
    private void RefreshVolumes()
    {
        Volumes.Clear();
        foreach (var v in _volumeScanner.Scan())
            Volumes.Add(v);
        StatusText = $"Znaleziono {Volumes.Count} wolumen(ów).";
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        LogLines.Clear();
        ProgressValue = 0;

        try
        {
            RefreshVolumes();
            ReloadCategoryShell();

            var all = SystemCategories.Concat(DeveloperCategories).Select(c => c.Model).ToList();
            var progress = new Progress<CleanupProgress>(p =>
            {
                StatusText = p.Message;
                ProgressValue = p.Total <= 0 ? 0 : 100.0 * p.Current / p.Total;
            });

            await _categoryScanner.ScanSizesAsync(all, progress, ct).ConfigureAwait(true);

            foreach (var item in SystemCategories.Concat(DeveloperCategories))
                item.NotifySizeChanged();

            StatusText = "Szukam dużych plików (≥ 500 MB)…";
            LargeFiles.Clear();
            var large = await _largeFileFinder.FindAsync(Volumes, progress: progress, ct: ct)
                .ConfigureAwait(true);
            foreach (var f in large)
                LargeFiles.Add(new LargeFileItemViewModel(f));

            var reclaimable = all.Where(c => c.IsSelected).Sum(c => c.SizeBytes);
            StatusText =
                $"Skan OK. Zaznaczone kategorie ≈ {ByteFormatter.Format(reclaimable)}. Dużych plików: {LargeFiles.Count}.";
            LogLines.Add(StatusText);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Skan anulowany.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd skanu: {ex.Message}";
            LogLines.Add(StatusText);
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 100;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void RequestClean()
    {
        var selectedCats = SystemCategories.Concat(DeveloperCategories)
            .Where(c => c.IsSelected)
            .ToList();
        var selectedFiles = LargeFiles.Where(f => f.IsSelected).ToList();

        if (selectedCats.Count == 0 && selectedFiles.Count == 0)
        {
            StatusText = "Nic nie zaznaczono.";
            return;
        }

        var bytes = selectedCats.Sum(c => c.Model.SizeBytes) + selectedFiles.Sum(f => f.SizeBytes);
        ConfirmSummary =
            $"Usuniesz dane z {selectedCats.Count} kategorii i {selectedFiles.Count} dużych plików " +
            $"(szacunkowo {ByteFormatter.Format(bytes)}).\n\nTej operacji nie da się cofnąć. Kontynuować?";
        IsConfirmVisible = true;
    }

    [RelayCommand]
    private void CancelConfirm()
    {
        IsConfirmVisible = false;
    }

    [RelayCommand]
    private async Task ConfirmCleanAsync()
    {
        IsConfirmVisible = false;
        if (IsBusy)
            return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        ProgressValue = 0;

        try
        {
            var cats = SystemCategories.Concat(DeveloperCategories).Select(c => c.Model).ToList();
            var files = LargeFiles.Select(f => f.Model).ToList();
            var progress = new Progress<CleanupProgress>(p =>
            {
                StatusText = p.Message;
                ProgressValue = p.Total <= 0 ? 0 : 100.0 * p.Current / p.Total;
            });

            var result = await _cleaner.CleanAsync(cats, files, progress, ct).ConfigureAwait(true);

            LogLines.Clear();
            foreach (var line in result.Log)
                LogLines.Add(line);

            StatusText =
                $"Czyszczenie zakończone. Zwolniono ≈ {ByteFormatter.Format(result.FreedBytes)} " +
                $"({result.DeletedItems} elementów, błędów: {result.Errors.Count}).";
            LogLines.Add(StatusText);
            RefreshVolumes();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Czyszczenie anulowane.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd czyszczenia: {ex.Message}";
            LogLines.Add(StatusText);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }
}

public partial class CategoryItemViewModel : ObservableObject
{
    public CleanupCategory Model { get; }

    public CategoryItemViewModel(CleanupCategory model)
    {
        Model = model;
        IsSelected = model.IsSelected;
    }

    public string Title => Model.Title;
    public string Description => Model.Description;
    public long SizeBytes => Model.SizeBytes;
    public string SizeText => ByteFormatter.Format(Model.SizeBytes);
    public string? Error => Model.Error;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => Model.IsSelected = value;

    public void NotifySizeChanged()
    {
        OnPropertyChanged(nameof(SizeBytes));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(Error));
    }
}

public partial class LargeFileItemViewModel : ObservableObject
{
    public LargeFileEntry Model { get; }

    public LargeFileItemViewModel(LargeFileEntry model)
    {
        Model = model;
        IsSelected = model.IsSelected;
    }

    public string Path => Model.Path;
    public long SizeBytes => Model.SizeBytes;
    public string SizeText => ByteFormatter.Format(Model.SizeBytes);

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => Model.IsSelected = value;
}
