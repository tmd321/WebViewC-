using System.Windows;
using System.Windows.Controls;

namespace LineMonitor.App.Controls;

/// <summary>Renders one Wet/Dry Mismatch strip: per-lane Left|Right diff bars, DS/OS badges flip per panel.</summary>
public partial class MismatchPanel : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(MismatchPanel), new PropertyMetadata("Mismatch"));

    public static readonly DependencyProperty LeftLabelProperty =
        DependencyProperty.Register(nameof(LeftLabel), typeof(string), typeof(MismatchPanel), new PropertyMetadata("DS"));

    public static readonly DependencyProperty RightLabelProperty =
        DependencyProperty.Register(nameof(RightLabel), typeof(string), typeof(MismatchPanel), new PropertyMetadata("OS"));

    public static readonly DependencyProperty IsPositiveElectrodeProperty =
        DependencyProperty.Register(nameof(IsPositiveElectrode), typeof(bool), typeof(MismatchPanel), new PropertyMetadata(true));

    public bool IsPositiveElectrode
    {
        get => (bool)GetValue(IsPositiveElectrodeProperty);
        set => SetValue(IsPositiveElectrodeProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string LeftLabel
    {
        get => (string)GetValue(LeftLabelProperty);
        set => SetValue(LeftLabelProperty, value);
    }

    public string RightLabel
    {
        get => (string)GetValue(RightLabelProperty);
        set => SetValue(RightLabelProperty, value);
    }

    public MismatchPanel()
    {
        InitializeComponent();
    }
}
