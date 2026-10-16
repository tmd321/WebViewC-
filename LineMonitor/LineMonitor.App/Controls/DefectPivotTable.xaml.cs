using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LineMonitor.App.ViewModels;

namespace LineMonitor.App.Controls;

/// <summary>
/// The 개소×불량유형 pivot table needs its defect-type columns generated at runtime
/// (the set of defect types differs per line), so we rebuild DataGrid.Columns whenever
/// DefectHistoryViewModel.DefectTypeColumns changes.
/// </summary>
public partial class DefectPivotTable : UserControl
{
    private DefectHistoryViewModel? _viewModel;

    public DefectPivotTable()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as DefectHistoryViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            RebuildPivotColumns();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DefectHistoryViewModel.DefectTypeColumns))
        {
            Dispatcher.Invoke(RebuildPivotColumns);
        }
    }

    private void RebuildPivotColumns()
    {
        if (_viewModel is null)
        {
            return;
        }

        while (PivotGrid.Columns.Count > 5)
        {
            PivotGrid.Columns.RemoveAt(PivotGrid.Columns.Count - 1);
        }

        foreach (var type in _viewModel.DefectTypeColumns)
        {
            PivotGrid.Columns.Add(new DataGridTextColumn
            {
                Header = type,
                Width = 140,
                ElementStyle = (Style)Resources["PivotCellText"],
                Binding = new Binding($"CountsByDefectType[{type}]")
                {
                    Mode = BindingMode.OneWay,
                    StringFormat = "{0:#,##0;-#,##0;-}",
                    TargetNullValue = "-",
                    FallbackValue = "-"
                }
            });
        }
    }
}
