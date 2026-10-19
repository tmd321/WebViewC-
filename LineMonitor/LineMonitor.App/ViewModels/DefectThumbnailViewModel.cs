using System.Windows.Media;
using LineMonitor.App.Services;
using LineMonitor.Core.Models;

namespace LineMonitor.App.ViewModels;

public sealed class DefectThumbnailViewModel
{
    public DefectRecord Record { get; }

    public DefectThumbnailViewModel(DefectRecord record) => Record = record;

    public string DisplayDefectName => !string.IsNullOrWhiteSpace(Record.DefectName)
        && !string.IsNullOrWhiteSpace(Record.DefectTypeName)
        && !string.Equals(Record.DefectName, Record.DefectTypeName, StringComparison.Ordinal)
            ? $"{Record.DefectName}({Record.DefectTypeName})"
            : Record.DefectName;

    public string PositionLabel => Record.PosFullX.HasValue && Record.PosFullY.HasValue
        ? "좌표(pos full)" : "좌표(pos)";

    // IsAsync 바인딩에서 읽으며, 네트워크 파일 접근으로 UI 스레드가 멈추지 않도록 한다.
    public ImageSource? Image => DefectImageLoader.LoadImage(Record.ImagePath);
}
