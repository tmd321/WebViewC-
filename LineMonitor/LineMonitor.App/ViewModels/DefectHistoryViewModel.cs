using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LineMonitor.Core.Lines;
using LineMonitor.Core.Models;

namespace LineMonitor.App.ViewModels;

/// <summary>Backs the "실시간 불량정보" screen: recent defect thumbnails + 개소×불량유형 pivot table.</summary>
public sealed partial class DefectHistoryViewModel : ObservableObject
{
    private const int InitialLoadTake = 30;
    private const int MaxRecentDefects = 200;

    private LineId? _currentLineId;
    private long _lastSeenId;

    public ObservableCollection<DefectRecord> RecentDefects { get; } = new();
    public ObservableCollection<DefectPivotRow> PivotRows { get; } = new();

    [ObservableProperty] private ObservableCollection<string> _defectTypeColumns = new();

    public async Task RefreshAsync(ILineAdapter adapter, CancellationToken ct = default)
    {
        // 라인 바뀌면 워터마크/목록 리셋 — 이전 라인 sinceId로 새 라인 조회하면 안 됨.
        if (_currentLineId != adapter.LineId)
        {
            _currentLineId = adapter.LineId;
            _lastSeenId = 0;
            RecentDefects.Clear();
        }

        var defects = await adapter.GetRecentDefectsAsync(
            _lastSeenId == 0 ? InitialLoadTake : MaxRecentDefects, _lastSeenId, ct);

        if (defects.Count > 0)
        {
            if (_lastSeenId == 0)
            {
                // 초기 로드: 어댑터가 이미 최신순(내림차순)으로 줌 — 그대로 채움.
                RecentDefects.Clear();
                foreach (var d in defects)
                {
                    RecentDefects.Add(d);
                }
            }
            else
            {
                // 증분: 어댑터가 오름차순으로 줌 — 맨 앞에 순서대로 꽂으면 최신이 맨 위로 옴.
                foreach (var d in defects)
                {
                    RecentDefects.Insert(0, d);
                }

                while (RecentDefects.Count > MaxRecentDefects)
                {
                    RecentDefects.RemoveAt(RecentDefects.Count - 1);
                }
            }

            _lastSeenId = defects.Max(d => d.Id);
        }

        var pivot = await adapter.GetDefectPivotAsync(ct);
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
        DefectTypeColumns = new ObservableCollection<string>(columns);
    }
}
