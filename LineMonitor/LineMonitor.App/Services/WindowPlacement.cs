using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LineMonitor.App.Services;

internal static class WindowPlacement
{
    public static void FitToOwnerWorkArea(Window window, Window owner)
    {
        const double margin = 8;
        var workArea = GetWorkArea(owner);

        window.Width = Math.Min(window.Width, Math.Max(1, workArea.Width - margin * 2));
        window.Height = Math.Min(window.Height, Math.Max(1, workArea.Height - margin * 2));
        window.Left = Math.Clamp(owner.Left + (owner.ActualWidth - window.Width) / 2,
            workArea.Left + margin, workArea.Right - margin - window.Width);
        window.Top = Math.Clamp(owner.Top + (owner.ActualHeight - window.Height) / 2,
            workArea.Top + margin, workArea.Bottom - margin - window.Height);
    }

    public static Rect GetWorkArea(Window window)
    {
        var windowHandle = new WindowInteropHelper(window).Handle;
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var workArea = SystemParameters.WorkArea;
        if (GetMonitorInfo(MonitorFromWindow(windowHandle, 2), ref monitorInfo)
            && HwndSource.FromHwnd(windowHandle)?.CompositionTarget is { } compositionTarget)
        {
            // Win32의 모니터 좌표는 픽셀이므로 WPF 화면 배율에 맞춰 변환한다.
            var transform = compositionTarget.TransformFromDevice;
            workArea = new Rect(
                transform.Transform(new Point(monitorInfo.Work.Left, monitorInfo.Work.Top)),
                transform.Transform(new Point(monitorInfo.Work.Right, monitorInfo.Work.Bottom)));
        }

        return workArea;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
