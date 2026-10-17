namespace Pds.Desktop;

internal static class WindowPlacement
{
    internal static (double Left, double Top) Fit(double left, double top, double width, double height,
        double areaLeft, double areaTop, double areaWidth, double areaHeight)
        => (Math.Clamp(left, areaLeft, Math.Max(areaLeft, areaLeft + areaWidth - width)),
            Math.Clamp(top, areaTop, Math.Max(areaTop, areaTop + areaHeight - height)));
}
