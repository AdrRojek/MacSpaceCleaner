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
    private string _statusText = "Ready. Scan your Mac, then review files before deleting.";

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

    /// <summary>0 = overview, 1 = file review.</summary>
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
    private string _selectedTotalText = "Selected: 0 B";

    [ObservableProperty]
    private bool _showOnlyRecommended = true;

    [ObservableProperty]
    private string _aiStatusText = "Local heuristics always run. Optional cloud AI via Groq/OpenAI.";

    [ObservableProperty]
    private bool _hasOpenAiKey;

    [ObservableProperty]
    private bool _isSuccessVisible;

    [ObservableProperty]
    private string _successTitle = "Deleted";

    [ObservableProperty]
    private string _successMessage = string.Empty;

    public bool IsOverviewStep => WizardStep == 0;
    public bool IsReviewStep => WizardStep == 1;
    public bool CanGoNext => HasScanResult && !IsBusy && WizardStep == 0;

    public MainViewModel()
    {
        HasOpenAiKey = _aiAnalyzer.IsAvailable;
        var provider = _aiAnalyzer.ActiveProviderName;
        AiStatusText = HasOpenAiKey
            ? $"{provider} API key detected — cloud AI will rank the largest items after Next."
            : "No API key — local heuristics only. Optional: ~/.config/macspacecleaner/groq_api_key";
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
        StatusText = $"Found {Volumes.Count} volume(s).";
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

            StatusText = "Looking for large files (≥ 500 MB)…";
            LargeFiles.Clear();
            var large = await _largeFileFinder.FindAsync(Volumes, progress: progress, ct: ct)
                .ConfigureAwait(true);
            foreach (var f in large)
                LargeFiles.Add(new LargeFileItemViewModel(f));

            var reclaimable = all.Where(c => c.IsSelected).Sum(c => c.SizeBytes);
            HasScanResult = true;
            StatusText =
                $"Scan complete. Selected categories ≈ {ByteFormatter.Format(reclaimable)}. " +
                $"Large files: {LargeFiles.Count}. Click Next to review paths.";
            LogLines.Add(StatusText);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
            HasScanResult = false;
        }
        catch (Exception ex)
        {
            StatusText = $"Scan error: {ex.Message}";
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
            StatusText = "Building review list…";
            var cats = SystemCategories.Concat(DeveloperCategories).Select(c => c.Model).ToList();
            var large = LargeFiles.Select(f => f.Model).ToList();
            var progress = new Progress<CleanupProgress>(p =>
            {
                StatusText = p.Message;
                ProgressValue = p.Total <= 0 ? 0 : 100.0 * p.Current / p.Total;
            });

            var built = await _candidateBuilder.BuildAsync(cats, large, progress, ct)
                .ConfigureAwait(true);

            StatusText = "Running local junk heuristics…";
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
                    "Local heuristics applied. For cloud AI, add ~/.config/macspacecleaner/groq_api_key " +
                    "then click Analyze AI.";
            }

            foreach (var entry in built.OrderByDescending(e => e.JunkScore).ThenByDescending(e => e.SizeBytes))
            {
                var item = new CandidateItemViewModel(entry);
                item.SelectionChanged = RecalculateSelectedTotal;
                Candidates.Add(item);
            }

            WizardStep = 1;
            ShowOnlyRecommended = true;
            foreach (var c in Candidates)
                c.IsSelected = c.IsRecommended;
            RefreshVisibleCandidates();
            RecalculateSelectedTotal();
            var recCount = Candidates.Count(c => c.IsRecommended);
            var recBytes = Candidates.Where(c => c.IsRecommended).Sum(c => c.SizeBytes);
            ReviewSummary =
                $"List: {Candidates.Count} items. Recommended: {recCount} (~{ByteFormatter.Format(recBytes)}). " +
                "Only recommended are selected by default. Use Finder to inspect a path.";
            StatusText = ReviewSummary;
            LogLines.Add($"File list: {Candidates.Count}, recommended: {recCount}");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Building list cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"File list error: {ex.Message}";
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
            ? "Back to categories. Adjust selection and click Next again."
            : "Ready to scan.";
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
        SelectedTotalText = $"Selected: {count} · {ByteFormatter.Format(bytes)}";
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
                "No API key. Set GROQ_API_KEY / ~/.config/macspacecleaner/groq_api_key " +
                "or OPENAI_API_KEY.";
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
            StatusText = "AI analysis cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"AI error: {ex.Message}";
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
    private void DismissSuccess()
    {
        IsSuccessVisible = false;
    }

    [RelayCommand]
    private void RequestClean()
    {
        if (WizardStep != 1)
        {
            StatusText = "Open the file review step first (Next).";
            return;
        }

        var selected = Candidates.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Nothing selected.";
            return;
        }

        var bytes = selected.Sum(c => c.SizeBytes);
        ConfirmSummary =
            $"You are about to permanently delete {selected.Count} item(s) " +
            $"(about {ByteFormatter.Format(bytes)}).\n\nThis cannot be undone. Continue?";
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
                $"Deleted. Freed ≈ {ByteFormatter.Format(result.FreedBytes)} " +
                $"({result.DeletedItems} items, errors: {result.Errors.Count}).";
            LogLines.Add(StatusText);

            SuccessTitle = "Deleted";
            SuccessMessage =
                $"Successfully removed {result.DeletedItems} item(s).\n" +
                $"Space freed: {ByteFormatter.Format(result.FreedBytes)}" +
                (result.Errors.Count > 0 ? $"\n{result.Errors.Count} item(s) could not be removed." : ".");
            IsSuccessVisible = true;

            // Remove deleted from list
            var stillThere = Candidates
                .Where(c => File.Exists(c.Path) || Directory.Exists(c.Path))
                .ToList();
            Candidates.Clear();
            foreach (var c in stillThere)
                Candidates.Add(c);

            RefreshVisibleCandidates();
            RecalculateSelectedTotal();
            RefreshVolumes();
            HasScanResult = false;
            ReviewSummary = stillThere.Count == 0
                ? "Cleanup finished. Nothing left in this list — scan again if you want another pass."
                : $"Cleanup finished. {stillThere.Count} item(s) remain in the list.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cleanup cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Cleanup error: {ex.Message}";
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

    [RelayCommand]
    private void ToggleSelect() => IsSelected = !IsSelected;

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
    public string KindLabel => Model.IsDirectory ? "Folder" : "File";
    public int JunkScore => Model.JunkScore;
    public string ScoreText => $"Score {Model.JunkScore}";
    public string? AnalysisReason => Model.AnalysisReason;
    public bool IsRecommended => Model.IsRecommended;
    public string AnalysisSource => Model.AnalysisSource;
    public string RecommendationLabel => Model.IsRecommended ? "Recommended" : "Review";

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        Model.IsSelected = value;
        SelectionChanged?.Invoke();
    }

    [RelayCommand]
    private void ToggleSelect() => IsSelected = !IsSelected;

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
