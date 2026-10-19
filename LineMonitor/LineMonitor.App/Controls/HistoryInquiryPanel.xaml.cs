using System.Windows;
using System.Windows.Controls;

namespace LineMonitor.App.Controls;

public partial class HistoryInquiryPanel : UserControl
{
    public HistoryInquiryPanel()
    {
        InitializeComponent();
        StartDatePicker.SelectedDate = DateTime.Today;
        EndDatePicker.SelectedDate = DateTime.Today;
    }

    private void OnSearchClick(object sender, RoutedEventArgs e)
    {
        if (StartDatePicker.SelectedDate is not { } startDate || EndDatePicker.SelectedDate is not { } endDate)
        {
            SearchStatusText.Text = "시작 날짜와 종료 날짜를 모두 선택해 주세요.";
            return;
        }
        if (startDate > endDate)
        {
            SearchStatusText.Text = "시작 날짜는 종료 날짜보다 늦을 수 없습니다.";
            return;
        }

        SearchStatusText.Text = $"조회 기간: {startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd}\n이력 데이터 조회는 아직 연결되지 않았습니다.";
    }
}
