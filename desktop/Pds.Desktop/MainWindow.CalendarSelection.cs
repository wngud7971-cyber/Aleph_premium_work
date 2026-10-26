using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    // Opening/closing a record changes selection colors, not the calendar's visual tree.
    private void UpdateCalendarSelection(bool detailsOpen)
    {
        foreach (var cell in MonthGrid.Children.OfType<Button>())
        {
            if (cell.Tag is not DateOnly date) continue;
            var chosen = detailsOpen && date == selected && date.Month == month.Month;
            cell.Background = date.Month != month.Month ? Brushes.Transparent : ResourceBrush(chosen ? "Accent" : date == LocalToday ? "Note" : "Paper");
            cell.BorderBrush = chosen ? ResourceBrush("AccentInk") : date == LocalToday ? ResourceBrush("Accent") : Brushes.Transparent;
            var grid = (Grid)cell.Content;
            var holiday = KoreanHolidays.On(date);
            var number = grid.Children.OfType<TextBlock>().First();
            number.Foreground = ResourceBrush(holiday is not null || date.DayOfWeek == DayOfWeek.Sunday ? chosen ? "SelectedSunday" : "Sunday"
                : date.DayOfWeek == DayOfWeek.Saturday ? chosen ? "SelectedSaturday" : "Saturday"
                : chosen ? "AccentInk" : date.Month == month.Month ? "Ink" : "Muted");
            foreach (var text in grid.Children.OfType<TextBlock>().Where(t => t.Tag as string == "holiday-caption"))
                text.Foreground = ResourceBrush(chosen ? "SelectedSunday" : "Sunday");
            foreach (var text in grid.Children.OfType<TextBlock>().Where(t => t.Text == "•"))
                text.Foreground = ResourceBrush(chosen ? "AccentInk" : "Ink");
            foreach (var text in grid.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<TextBlock>()))
                text.Foreground = ResourceBrush(chosen ? "AccentInk" : text.Tag as string == "record-count-caption" ? "Muted" : "Ink");
            foreach (var guide in grid.Children.OfType<Border>())
                guide.Visibility = chosen ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
