using System.Windows.Media;

namespace Pds.Desktop;

internal static class UiPixels
{
    internal static double RoundX(Visual target, double value)
    {
        var scale = VisualTreeHelper.GetDpi(target).DpiScaleX;
        return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
    }
    internal static double CeilingX(Visual target, double value)
    {
        var scale = VisualTreeHelper.GetDpi(target).DpiScaleX;
        return Math.Ceiling(value * scale) / scale;
    }
}
