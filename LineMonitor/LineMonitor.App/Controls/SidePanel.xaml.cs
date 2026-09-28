using System.Windows;
using System.Windows.Controls;

namespace LineMonitor.App.Controls;

/// <summary>Renders one TOP/BACK measurement strip: DS/OS + Lane badges, edge tolerances, lane Wet/Dry cells, Avr.</summary>
public partial class SidePanel : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SidePanel), new PropertyMetadata("TOP"));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public SidePanel()
    {
        InitializeComponent();
    }
}
