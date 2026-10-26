namespace Pds.Desktop;

public static class UiTypography
{
    // WPF font units are DIPs. One typographic point is 96/72 DIPs.
    public const double OnePoint = 96d / 72;
    // Two approved one-point steps; v1.78 adds one point to v1.77.
    public const double PointIncrease = 2 * OnePoint;
    public const double Small = 10 + PointIncrease;
    public const double Body = 12 + PointIncrease;
    public const double Tick = 13 + PointIncrease;
    public const double Date = 14 + PointIncrease;
    public const double Subheading = 16 + PointIncrease;
    public const double Heading = 18 + PointIncrease;
    public const double Display = 20 + PointIncrease;
    public const double Timer = 24 + PointIncrease;
    public const double SelectedDate = 28 + PointIncrease;
    public static double Grow(double original) => original + PointIncrease;
}
