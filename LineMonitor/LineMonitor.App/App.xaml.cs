using System.Windows;
using System.Windows.Threading;
using LineMonitor.App.Services;
using LineMonitor.App.ViewModels;
using LineMonitor.Core.Configuration;
using LineMonitor.Core.Lines;
using Microsoft.Extensions.Configuration;

namespace LineMonitor.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 스플래시 → 라인 선택 → 메인창 순서로 뜰 동안, 창이 잠깐 0개인 순간에
        // 기본 ShutdownMode(OnLastWindowClose)가 앱을 조기 종료시키지 않도록 명시적 종료로 전환.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var options = new LineMonitorOptions();
        configuration.GetSection("LineMonitor").Bind(options);
        var factory = new LineAdapterFactory(options, SampleImageProvider.GetSample);

        var splash = new SplashWindow();
        splash.Show();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            splash.Close();
            ShowLinePickerThenMain(factory, options);
        };
        timer.Start();
    }

    private void ShowLinePickerThenMain(LineAdapterFactory factory, LineMonitorOptions options)
    {
        var picker = new LineSelectWindow();
        var picked = picker.ShowDialog();

        if (picked != true || picker.SelectedLineId is not { } lineId)
        {
            Shutdown();
            return;
        }

        var mainViewModel = new MainViewModel(factory, options, lineId);
        var window = new MainWindow { DataContext = mainViewModel };

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = window;
        window.Show();
    }
}
