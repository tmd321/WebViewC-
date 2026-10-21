using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace LineMonitor.App.Controls;

public partial class DefectThumbGrid : UserControl
{
    public DefectThumbGrid()
    {
        InitializeComponent();
    }

    public double GetMinimumRowHeight(double width)
    {
        DefectItems.Measure(new Size(width, double.PositiveInfinity));
        var rowCount = Math.Max(1, (DefectItems.Items.Count + 9) / 10);
        Debug.Assert(rowCount * 10 >= DefectItems.Items.Count);
        // UniformGrid의 각 행 높이는 같으므로 한 행의 이미지와 설명 높이를 사용한다.
        return Math.Max(120, Math.Ceiling(DefectItems.DesiredSize.Height / rowCount));
    }
}
