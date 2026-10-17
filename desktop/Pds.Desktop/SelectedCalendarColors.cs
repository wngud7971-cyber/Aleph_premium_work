namespace Pds.Desktop;

// Selection uses the accent background in either theme, so its text colors
// must be derived from that surface rather than the surrounding dark panel.
internal static class SelectedCalendarColors
{
    internal static string Saturday(string background) => Readable("#1C4C91", background);
    internal static string Sunday(string background) => Readable("#9D1D42", background);
    internal static string Ink(string background) => Readable("#211F25", background);

    private static string Readable(string preferred, string background)
    {
        if (Contrast(preferred, background) >= 4.5) return preferred;
        var value = Convert.ToInt32(preferred[1..], 16);
        for (var step = 1; step <= 20; step++)
        {
            foreach (var target in new[] { 0, 255 })
            {
                var amount = step / 20d;
                int Blend(int channel) => (int)Math.Round(channel * (1 - amount) + target * amount);
                var candidate = $"#{Blend(value >> 16):X2}{Blend((value >> 8) & 255):X2}{Blend(value & 255):X2}";
                if (Contrast(candidate, background) >= 4.5) return candidate;
            }
        }
        throw new InvalidOperationException("Unable to find readable calendar text");
    }

    internal static double Contrast(string foreground, string background)
    {
        static double Luminance(string hex)
        {
            var rgb = Convert.ToInt32(hex[1..], 16);
            static double Linear(int channel)
            {
                var value = channel / 255d;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(rgb >> 16) + 0.7152 * Linear((rgb >> 8) & 255) + 0.0722 * Linear(rgb & 255);
        }
        var first = Luminance(foreground); var second = Luminance(background);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
}
