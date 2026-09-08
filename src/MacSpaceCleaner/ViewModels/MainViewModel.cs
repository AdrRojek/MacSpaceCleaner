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
    private readonly CandidateBuilder _candidateBuilder = new();
    private readonly CleanerService _cleaner = new();
    private readonly SmartJunkAnalyzer _smartAnalyzer = new();
    private readonly AiJunkAnalyzer _aiAnalyzer = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<VolumeInfo> Volumes { get; } = [];
    public ObservableCollection<CategoryItemViewModel> SystemCategories { get; } = [];
    public ObservableCollection<CategoryItemViewModel> DeveloperCategories { get; } = [];
    public ObservableCollection<LargeFileItemViewModel> LargeFiles { get; } = [];
    public ObservableCollection<CandidateItemViewModel> Candidates { get; } = [];
    public ObservableCollection<CandidateItemViewModel> VisibleCandidates { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    private string _statusText = "Gotowy. Kliknij „Skanuj”, potem „Dalej”, aby przejrzeć pliki do usunięcia.";

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

    /// <summary>0 = przegląd dysków/kategorii, 1 = lista plików.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewStep))]
    [NotifyPropertyChangedFor(nameof(IsReviewStep))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private int _wizardStep;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private bool _hasScanResult;

    [ObservableProperty]
    private string _reviewSummary = string.Empty;

    [ObservableProperty]
    private string _selectedTotalText = "Zaznaczone: 0 B";

    [ObservableProperty]
    private bool _showOnlyRecommended = true;

    [ObservableProperty]
    private string _aiStatusText = "Analiza AI: heurystyka lokalna zawsze; Groq/OpenAI jeśli jest klucz.";

    [ObservableProperty]
    private bool _hasOpenAiKey;

    public bool IsOverviewStep => WizardStep == 0;
    public bool IsReviewStep => WizardStep == 1;
    public bool CanGoNext => HasScanResult && !IsBusy && WizardStep == 0;

    public MainViewModel()
    {
        HasOpenAiKey = _aiAnalyzer.IsAvailable;
        var provider = _aiAnalyzer.ActiveProviderName;
        AiStatusText = HasOpenAiKey
            ? $"Klucz {provider} wykryty — po „Dalej” uruchomi się też analiza AI największych plików."
            : "Bez klucza API działa heurystyka lokalna. Preferowane: Groq (~/.config/macspacecleaner/groq_api_key).";
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
        HasScanResult = false;
        WizardStep = 0;
        Candidates.Clear();
        VisibleCandidates.Clear();
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
            HasScanResult = true;
            StatusText =
                $"Skan OK. Zaznaczone kategorie ≈ {ByteFormatter.Format(reclaimable)}. " +
                $"Dużych plików: {LargeFiles.Count}. Kliknij „Dalej”, aby zobaczyć listę plików.";
            LogLines.Add(StatusText);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Skan anulowany.";
            HasScanResult = false;
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd skanu: {ex.Message}";
            LogLines.Add(StatusText);
            HasScanResult = false;
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 100;
            _cts?.Dispose();
            _cts = null;
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (!HasScanResult || IsBusy)
            return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        ProgressValue = 0;
        Candidates.Clear();

        try
        {
            StatusText = "Buduję listę plików do przejrzenia…";
            var cats = SystemCategories.Concat(DeveloperCategories).Select(c => c.Model).ToList();
            var large = LargeFiles.Select(f => f.Model).ToList();
            var progress = new Progress<CleanupProgress>(p =>
            {
                StatusText = p.Message;
                ProgressValue = p.Total <= 0 ? 0 : 100.0 * p.Current / p.Total;
            });

            var built = await _candidateBuilder.BuildAsync(cats, large, progress, ct)
                .ConfigureAwait(true);

            StatusText = "Analiza heurystyczna (niepotrzebne + ciężkie)…";
            _smartAnalyzer.Analyze(built.ToList());

            HasOpenAiKey = _aiAnalyzer.IsAvailable;
            if (HasOpenAiKey)
            {
                var aiMsg = await _aiAnalyzer.AnalyzeAsync(built.ToList(), progress, ct)
                    .ConfigureAwait(true);
                AiStatusText = aiMsg;
                LogLines.Add(aiMsg);
            }
            else
            {
                AiStatusText =
                    "Heurystyka lokalna OK. Dla AI ustaw Groq: ~/.config/macspacecleaner/groq_api_key " +
                    "(albo OPENAI_API_KEY) i kliknij „Analizuj AI”.";
            }

            foreach (var entry in built.OrderByDescending(e => e.JunkScore).ThenByDescending(e => e.SizeBytes))
            {
                var item = new CandidateItemViewModel(entry);
                item.SelectionChanged = RecalculateSelectedTotal;
                Candidates.Add(item);
            }

            WizardStep = 1;
            ShowOnlyRecommended = true;
            RefreshVisibleCandidates();
            var recCount = Candidates.Count(c => c.IsRecommended);
            var recBytes = Candidates.Where(c => c.IsRecommended).Sum(c => c.SizeBytes);
            ReviewSummary =
                $"Lista: {Candidates.Count} pozycji. Rekomendowane do usunięcia: {recCount} " +
                $"(~{ByteFormatter.Format(recBytes)}). Filtr „Tylko rekomendowane” pokazuje ciężki junk. " +
                "„W Finderze” wskazuje plik.";
            RecalculateSelectedTotal();
            StatusText = ReviewSummary;
            LogLines.Add($"Lista plików: {Candidates.Count}, rekomendowane: {recCount}");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Budowanie listy anulowane.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd listy plików: {ex.Message}";
            LogLines.Add(StatusText);
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 100;
            _cts?.Dispose();
            _cts = null;
            NextCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Back()
    {
        WizardStep = 0;
        StatusText = HasScanResult
            ? "Wrócono do kategorii. Możesz zmienić zaznaczenie i znów kliknąć „Dalej”."
            : "Gotowy do skanu.";
    }

    [RelayCommand]
    private void SelectAllCandidates()
    {
        foreach (var c in VisibleCandidates)
            c.IsSelected = true;
        RecalculateSelectedTotal();
    }

    [RelayCommand]
    private void DeselectAllCandidates()
    {
        foreach (var c in VisibleCandidates)
            c.IsSelected = false;
        RecalculateSelectedTotal();
    }

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var c in Candidates)
            c.IsSelected = c.IsRecommended;
        RecalculateSelectedTotal();
        RefreshVisibleCandidates();
    }

    private void RecalculateSelectedTotal()
    {
        var bytes = Candidates.Where(c => c.IsSelected).Sum(c => c.SizeBytes);
        var count = Candidates.Count(c => c.IsSelected);
        SelectedTotalText = $"Zaznaczone: {count} · {ByteFormatter.Format(bytes)}";
    }

    private void RefreshVisibleCandidates()
    {
        VisibleCandidates.Clear();
        IEnumerable<CandidateItemViewModel> q = Candidates;
        if (ShowOnlyRecommended)
            q = q.Where(c => c.IsRecommended);
        foreach (var c in q.OrderByDescending(c => c.JunkScore).ThenByDescending(c => c.SizeBytes))
            VisibleCandidates.Add(c);
    }

    partial void OnShowOnlyRecommendedChanged(bool value) => RefreshVisibleCandidates();

    [RelayCommand]
    private async Task AnalyzeAiAsync()
    {
        if (IsBusy || Candidates.Count == 0)
            return;

        HasOpenAiKey = _aiAnalyzer.IsAvailable;
        if (!HasOpenAiKey)
        {
            StatusText =
                "Brak klucza API. Ustaw GROQ_API_KEY / ~/.config/macspacecleaner/groq_api_key " +
                "albo OPENAI_API_KEY.";
            AiStatusText = StatusText;
            return;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            var models = Candidates.Select(c => c.Model).ToList();
            var progress = new Progress<CleanupProgress>(p =>
            {
                StatusText = p.Message;
                ProgressValue = p.Total <= 0 ? 0 : 100.0 * p.Current / p.Total;
            });

            // Refresh heuristic first, then AI overlay
            _smartAnalyzer.Analyze(models);
            var msg = await _aiAnalyzer.AnalyzeAsync(models, progress, ct).ConfigureAwait(true);
            AiStatusText = msg;
            LogLines.Add(msg);

            foreach (var c in Candidates)
                c.NotifyAnalysisChanged();

            RefreshVisibleCandidates();
            RecalculateSelectedTotal();
            StatusText = msg;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Analiza AI anulowana.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd AI: {ex.Message}";
            AiStatusText = StatusText;
            LogLines.Add(StatusText);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void RequestClean()
    {
        if (WizardStep != 1)
        {
            StatusText = "Najpierw kliknij „Dalej” i przejrzyj listę plików.";
            return;
        }

        var selected = Candidates.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Nic nie zaznaczono.";
            return;
        }

        var bytes = selected.Sum(c => c.SizeBytes);
        ConfirmSummary =
            $"Usuniesz {selected.Count} pozycji (szacunkowo {ByteFormatter.Format(bytes)}).\n\n" +
            "Tej operacji nie da się cofnąć. Kontynuować?";
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
            var items = Candidates.Select(c => c.Model).ToList();
            var progress = new Progress<CleanupProgress>(p =>
            {
                StatusText = p.Message;
                ProgressValue = p.Total <= 0 ? 0 : 100.0 * p.Current / p.Total;
            });

            var result = await _cleaner.CleanCandidatesAsync(items, progress, ct)
                .ConfigureAwait(true);

            LogLines.Clear();
            foreach (var line in result.Log)
                LogLines.Add(line);

            StatusText =
                $"Czyszczenie zakończone. Zwolniono ≈ {ByteFormatter.Format(result.FreedBytes)} " +
                $"({result.DeletedItems} elementów, błędów: {result.Errors.Count}).";
            LogLines.Add(StatusText);

            // Remove deleted from list
            var stillThere = Candidates
                .Where(c => File.Exists(c.Path) || Directory.Exists(c.Path))
                .ToList();
            Candidates.Clear();
            foreach (var c in stillThere)
                Candidates.Add(c);

            RecalculateSelectedTotal();
            RefreshVolumes();
            HasScanResult = false;
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
            NextCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnHasScanResultChanged(bool value) => NextCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value) => NextCommand.NotifyCanExecuteChanged();
    partial void OnWizardStepChanged(int value) => NextCommand.NotifyCanExecuteChanged();
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

public partial class CandidateItemViewModel : ObservableObject
{
    public CandidateEntry Model { get; }
    public Action? SelectionChanged { get; set; }

    public CandidateItemViewModel(CandidateEntry model)
    {
        Model = model;
        IsSelected = model.IsSelected;
    }

    public string Path => Model.Path;
    public string FileName =>
        Model.IsDirectory
            ? System.IO.Path.GetFileName(Model.Path.TrimEnd('/')) + "/"
            : System.IO.Path.GetFileName(Model.Path);

    public string CategoryTitle => Model.CategoryTitle;
    public long SizeBytes => Model.SizeBytes;
    public string SizeText => ByteFormatter.Format(Model.SizeBytes);
    public string KindLabel => Model.IsDirectory ? "Folder" : "Plik";
    public int JunkScore => Model.JunkScore;
    public string ScoreText => $"Score {Model.JunkScore}";
    public string? AnalysisReason => Model.AnalysisReason;
    public bool IsRecommended => Model.IsRecommended;
    public string AnalysisSource => Model.AnalysisSource;
    public string RecommendationLabel => Model.IsRecommended ? "Rekomendowane" : "Do oceny";

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        Model.IsSelected = value;
        SelectionChanged?.Invoke();
    }

    public void NotifyAnalysisChanged()
    {
        IsSelected = Model.IsSelected;
        OnPropertyChanged(nameof(JunkScore));
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(AnalysisReason));
        OnPropertyChanged(nameof(IsRecommended));
        OnPropertyChanged(nameof(AnalysisSource));
        OnPropertyChanged(nameof(RecommendationLabel));
    }

    [RelayCommand]
    private void RevealInFinder() => FinderService.RevealInFinder(Model.Path);
}
