using System.Windows;
using System.Windows.Controls;

namespace LineMonitor.App.Controls;

/// <summary>Extends horizontal scrolling only as far as the placed boxes require.</summary>
public sealed class WorkspaceCanvas : Canvas
{
    private double _contentWidth;

    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);
        _contentWidth = GetContentWidth();
        return new Size(_contentWidth, 0);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var result = base.ArrangeOverride(arrangeSize);
        // Canvas.Left 변경은 Arrange만 무효화하므로 스크롤 범위도 다시 측정한다.
        if (GetContentWidth() != _contentWidth)
        {
            InvalidateMeasure();
        }
        return result;
    }

    private double GetContentWidth()
    {
        var width = 0d;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var left = GetLeft(child);
            width = Math.Max(width, (double.IsNaN(left) ? 0 : left) + child.DesiredSize.Width);
        }
        return width;
    }
}
