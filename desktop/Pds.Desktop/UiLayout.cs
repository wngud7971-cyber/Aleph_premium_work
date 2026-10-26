using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Pds.Desktop;

// Shared geometry, rather than spaces in a string, aligns labels and figures.
internal static class UiLayout
{
    internal static void Numeric(TextBlock text)
    {
        text.TextAlignment = TextAlignment.Right;
        text.VerticalAlignment = VerticalAlignment.Center;
        Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        Typography.SetNumeralStyle(text, FontNumeralStyle.Lining);
    }

    internal static Grid Values(FrameworkElement label, params TextBlock[] values)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        foreach (var value in values)
        {
            row.ColumnDefinitions.Add(new() { Width = new GridLength(values.Length > 1 ? 1 : 1.5, GridUnitType.Star) });
            Numeric(value);
            value.Margin = new Thickness(12, 4, 0, 4);
            Grid.SetColumn(value, row.ColumnDefinitions.Count - 1);
            row.Children.Add(value);
        }
        return row;
    }

    internal static void Compact(Control control)
    {
        control.Height = 38;
        control.Margin = new Thickness(0, 4, 0, 8);
        control.VerticalContentAlignment = VerticalAlignment.Center;
    }
}
