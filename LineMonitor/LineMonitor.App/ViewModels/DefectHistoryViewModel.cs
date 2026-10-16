using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LineMonitor.Core.Lines;
using LineMonitor.Core.Models;

namespace LineMonitor.App.ViewModels;

/// <summary>Backs the "실시간 불량정보" screen: recent defect thumbnails + 개소×불량유형 pivot table.</summary>
public sealed partial class DefectHistoryViewModel : ObservableObject
{
    private const int InitialLoadTake = 10;
    private const int MaxRecentDefects = 200;

    private ILineAdapter? _currentAdapter;
    private long _lastSeenId;

    public ObservableCollection<DefectThumbnailViewModel> RecentDefects { get; } = new();
    public ObservableCollection<DefectPivotRow> PivotRows { get; } = new();

    [ObservableProperty] private ObservableCollection<string> _defectTypeColumns = new();
    [ObservableProperty] private string _currentLotId = "-";
    [ObservableProperty] private int _totalDefectCount;

    public void Reset()
    {
        _currentAdapter = null;
        _lastSeenId = 0;
        CurrentLotId = "-";
        TotalDefectCount = 0;
        RecentDefects.Clear();
        PivotRows.Clear();
        DefectTypeColumns = new ObservableCollection<string>();
    }

    public async Task RefreshAsync(ILineAdapter adapter, CancellationToken ct = default)
    {
        // 같은 공정에서도 극성/접속 DB가 달라지면 이전 ID를 재사용하지 않는다.
        if (!ReferenceEquals(_currentAdapter, adapter))
        {
            Reset();
            _currentAdapter = adapter;
        }

        var defects = await adapter.GetRecentDefectsAsync(
            _lastSeenId == 0 ? InitialLoadTake : MaxRecentDefects, _lastSeenId, ct);
        if (!ReferenceEquals(_currentAdapter, adapter)) return;

        if (defects.Count > 0)
        {
            var lotId = defects.MaxBy(defect => defect.Id)!.LotId;
            if (CurrentLotId != lotId)
            {
                CurrentLotId = lotId;
                RecentDefects.Clear();
                _lastSeenId = 0;
            }

            if (_lastSeenId == 0)
            {
                // 초기 로드: 어댑터가 이미 최신순(내림차순)으로 줌 — 그대로 채움.
                RecentDefects.Clear();
                foreach (var d in defects.OrderByDescending(defect => defect.Id))
                {
                    RecentDefects.Add(new DefectThumbnailViewModel(d));
                }
            }
            else
            {
                // 증분: 어댑터가 오름차순으로 줌 — 맨 앞에 순서대로 꽂으면 최신이 맨 위로 옴.
                foreach (var d in defects)
                {
                    RecentDefects.Insert(0, new DefectThumbnailViewModel(d));
                }

                while (RecentDefects.Count > MaxRecentDefects)
                {
                    RecentDefects.RemoveAt(RecentDefects.Count - 1);
                }
            }

            _lastSeenId = defects.Max(d => d.Id);
        }

        var pivot = await adapter.GetDefectPivotAsync(ct);
        if (!ReferenceEquals(_currentAdapter, adapter)) return;
        var pivotLotId = pivot.FirstOrDefault()?.LotId;
        if (pivotLotId is not null and not "-" && pivotLotId != CurrentLotId)
        {
            CurrentLotId = pivotLotId;
            RecentDefects.Clear();
            _lastSeenId = 0;
        }
        TotalDefectCount = pivot.Sum(row => row.Total);
        PivotRows.Clear();
        foreach (var row in pivot)
        {
            PivotRows.Add(row);
        }

        var columns = pivot
            .SelectMany(r => r.CountsByDefectType.Keys)
            .Distinct()
            .OrderBy(x => x, StringComparer.CurrentCulture)
            .ToList();
        if (!DefectTypeColumns.SequenceEqual(columns))
        {
            DefectTypeColumns = new ObservableCollection<string>(columns);
        }
    }
}
