using System.Windows;
using System.Windows.Controls;

namespace LineMonitor.App.Controls;

/// <summary>Centers edge labels on the endpoints and boundaries of equally sized coating lanes.</summary>
public sealed class LaneBoundaryPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(0, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        for (var index = 0; index < count; index++)
        {
            var child = InternalChildren[index];
            var width = child.DesiredSize.Width;
            var center = count > 1 ? finalSize.Width * index / (count - 1) : finalSize.Width / 2;
            child.Arrange(new Rect(center - width / 2, 0, width, finalSize.Height));
        }

        return finalSize;
    }
}
