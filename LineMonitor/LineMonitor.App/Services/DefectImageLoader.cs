using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LineMonitor.App.Services;

public static class DefectImageLoader
{
    public static ImageSource? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 320;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or FormatException
            or InvalidOperationException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
