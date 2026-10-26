using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pds.Desktop;

public partial class MainWindow
{
    private void SynchronizeWeekdays()
    {
        if (CalendarViewport is null || MonthGrid is null || WeekDays is null || !double.IsFinite(MonthGrid.Width)) return;
        if(datePanActive)return;
        var width = CalendarViewport.ViewportWidth > 0 ? CalendarViewport.ViewportWidth : CalendarViewport.ActualWidth;
        var centered = Math.Max(0, (width - MonthGrid.Width) / 2);
        var offset=DisplayedCalendarOffset;
        calendarPan.X=sidebarPanActive?UiPixels.RoundX(MonthGrid,CalendarViewport.HorizontalOffset-offset):0;
        weekdayPan.X=UiPixels.RoundX(WeekDays,centered-offset);
    }

    private void RevealSelectedDate()
    {
        if (Details.Visibility != Visibility.Visible || CalendarViewport.ScrollableWidth <= 0) return;
        var cell = MonthGrid.Children.OfType<Button>().FirstOrDefault(b => b.Tag is DateOnly day && day == selected);
        if (cell is null) return;
        var left = cell.TranslatePoint(new Point(), CalendarViewport).X;
        var right = left + cell.ActualWidth;
        var offset = CalendarViewport.HorizontalOffset;
        if (left < 4) offset += left - 4;
        else if (right > CalendarViewport.ViewportWidth - 4) offset += right - CalendarViewport.ViewportWidth + 4;
        CalendarViewport.ScrollToHorizontalOffset(UiPixels.RoundX(CalendarViewport, Math.Clamp(offset, 0, CalendarViewport.ScrollableWidth)));
    }

    private void RefreshNavigationVisuals()
    {
        if (CalendarNav is null || StatisticsNav is null) return;
        void Mark(Button button, bool current)
        {
            button.SetResourceReference(BackgroundProperty, current ? "Paper" : "Soft");
            button.SetResourceReference(ForegroundProperty, "Ink");
            button.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetHelpText(button, current ? "현재 보고 있는 화면" : "화면 열기");
        }
        Mark(CalendarNav, !statisticsOpen && !recordToolsOpen);
        if(RecordsNav is not null)Mark(RecordsNav,recordToolsOpen);
        Mark(StatisticsNav, statisticsOpen);
    }
}
