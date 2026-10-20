using System.Windows;
using System.Windows.Controls;

namespace LineMonitor.App.Controls;

/// <summary>Renders one TOP/BACK measurement strip: DS/OS + Lane badges, edge tolerances, lane Wet/Dry cells, Avr.</summary>
public partial class SidePanel : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SidePanel), new PropertyMetadata("TOP"));

    public static readonly DependencyProperty ShowInsulationProperty =
        DependencyProperty.Register(nameof(ShowInsulation), typeof(bool), typeof(SidePanel), new PropertyMetadata(true));

    public static readonly DependencyProperty IsMirroredProperty =
        DependencyProperty.Register(nameof(IsMirrored), typeof(bool), typeof(SidePanel), new PropertyMetadata(false));

    public bool IsMirrored
    {
        get => (bool)GetValue(IsMirroredProperty);
        set => SetValue(IsMirroredProperty, value);
    }

    public bool ShowInsulation
    {
        get => (bool)GetValue(ShowInsulationProperty);
        set => SetValue(ShowInsulationProperty, value);
    }

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
