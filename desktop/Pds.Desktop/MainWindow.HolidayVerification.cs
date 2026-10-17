using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyHolidayPresentationAsync(List<string> checks)
    {
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        var geometry = new List<object>();
        foreach (var scale in new[] { 1d, 1.25, 1.5, 1.75, 2d })
        foreach (var available in new[] { 329d, 511.5, 803d, 875.25 })
        {
            var m = CalendarCellMetrics(available, 550, 6, scale, scale);
            var pixels = (m.Width - 2 * m.GapX) * scale;
            CheckMetric(Math.Abs(pixels - Math.Round(pixels)) < 0.001 && Math.Abs(m.Width - m.Height) < 0.001 && m.Height * 6 <= 550.001);
        }
        void CheckMetric(bool value) { if (!value) throw new InvalidOperationException("Fractional DPI produced inconsistent cell pixels"); }
        Check(true, "calendar-cell-metrics-use-whole-pixels-at-five-display-scales");
        var mother = repository.SaveAnniversary(Anniversary.Create("어머니", AnniversaryKind.Birthday, AnniversaryCalendar.Solar, 10, 2));
        var holidayMother = repository.SaveAnniversary(Anniversary.Create("어머니", AnniversaryKind.Birthday, AnniversaryCalendar.Solar, 5, 24));
        var day = new DateOnly(2026, 10, 2);
        repository.Save(repository.Load(day) with { Plans = [PlanEntry.Create("함께 표시할 계획") with { Done = true, ActualDate = day }], Workouts = [WorkoutEntry.Create("walking", 600, null)] });
        pendingChangeDecision = () => MessageBoxResult.No;
        settings.Compact = false; ApplyWindowLayout(); await SelectDateAsync(day); pendingChangeDecision = null;
        var originalCount = repository.Counts(day, day)[day];
        var workoutDay = new DateOnly(2026, 10, 16); var planDay = new DateOnly(2026, 10, 19);
        repository.Save(repository.Load(workoutDay) with { Workouts = [WorkoutEntry.Create("walking", 600, null)] });
        repository.Save(repository.Load(planDay) with { Plans = Enumerable.Range(1, 3).Select(i => PlanEntry.Create("계획 " + i)).ToArray() });
        try
        {
            foreach (var layout in new[] { (Name: "calendar", Details: false), (Name: "sidebar", Details: true) })
            foreach (var dark in new[] { false, true })
            {
                settings.Dark = dark; ApplyWindowLayout(openDetails: layout.Details); Details.Visibility = layout.Details ? Visibility.Visible : Visibility.Collapsed; DetailColumn.Width = new GridLength(layout.Details ? 330 : 0); ApplyTheme();
                selected = day; month = new(2026, 10, 1); await RefreshCalendarAsync();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var label = layout.Name + (dark ? "-dark" : "-light");
                CheckSquares(); Check(true, "all-date-backgrounds-have-identical-square-dimensions-" + label);
                Button Cell(DateOnly date) => MonthGrid.Children.OfType<Button>().Single(b => (DateOnly)b.Tag == date);
                var content = (Grid)Cell(day).Content;
                var caption = content.Children.OfType<StackPanel>().SelectMany(s => s.Children.OfType<TextBlock>()).Single(t => t.Tag as string == "anniversary-caption");
                var last = caption.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                var formatted = new FormattedText(caption.Text, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
                    new Typeface(caption.FontFamily, caption.FontStyle, caption.FontWeight, caption.FontStretch), caption.FontSize, Brushes.Black,
                    VisualTreeHelper.GetDpi(caption).PixelsPerDip) { MaxTextWidth = caption.ActualWidth };
                Check(caption.Text.Replace('\n', ' ') == "생일: 어머니" && formatted.Height <= caption.MaxHeight + 0.1 && !last.IsEmpty && last.Right <= caption.ActualWidth + 1 && last.Bottom <= caption.ActualHeight + 1,
                    "mother-birthday-full-colon-caption-fits-" + label);
                var holidayCell = Cell(new(2026, 10, 3)); var number = ((Grid)holidayCell.Content).Children.OfType<TextBlock>().First();
                Check(((SolidColorBrush)number.Foreground).Color == ((SolidColorBrush)ResourceBrush("Sunday")).Color
                    && holidayCell.ToolTip.ToString()!.Contains("개천절"), "saturday-holiday-is-red-with-name-" + label);
                var holidayText = ((Grid)holidayCell.Content).Children.OfType<TextBlock>().Single(t => t.Tag as string == "holiday-caption");
                var headerPosition = holidayText.TranslatePoint(new Point(), (Grid)holidayCell.Content);
                var datePosition = number.TranslatePoint(new Point(), (Grid)holidayCell.Content);
                var headerEnd = holidayText.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                Rect InkBounds(TextBlock text, string value)
                {
                    var shape = CalendarFormattedText(text, value);
                    var ink = shape.BuildGeometry(new Point()).Bounds;
                    var position = text.TranslatePoint(new Point(), (Grid)holidayCell.Content);
                    return new Rect(position.X + ink.X, position.Y + ink.Y, ink.Width, ink.Height);
                }
                var dateInk = InkBounds(number, number.Text);
                var nameInk = InkBounds(holidayText, holidayText.Text.Split('\n')[0]);
                var inkTopDelta = Math.Abs(dateInk.Top - nameInk.Top);
                var inkHeightDelta = Math.Abs(dateInk.Height - nameInk.Height);
                Check(headerPosition.X >= datePosition.X + number.ActualWidth && inkTopDelta <= 1.01 && inkHeightDelta <= 0.5
                    && holidayText.FontFamily.Source == number.FontFamily.Source && !headerEnd.IsEmpty && headerPosition.Y + headerEnd.Bottom <= 44.5,
                    "holiday-and-date-visible-glyph-heights-and-tops-match-" + label);
                var cells = MonthGrid.Children.OfType<Button>().ToArray();
                geometry.Add(new { label, width = cells[0].ActualWidth, height = cells[0].ActualHeight,
                    widthSpread = cells.Max(b => b.ActualWidth) - cells.Min(b => b.ActualWidth),
                    heightSpread = cells.Max(b => b.ActualHeight) - cells.Min(b => b.ActualHeight), inkTopDelta, inkHeightDelta, dateFontSize = number.FontSize, holidayFontSize = holidayText.FontSize });
                foreach (var recordDay in new[] { workoutDay, planDay })
                {
                    var recordCell = Cell(recordDay); var recordContent = (Grid)recordCell.Content;
                    var text = recordContent.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<TextBlock>()).Single(t => t.Tag as string == "record-count-caption");
                    var end = text.ContentEnd.GetCharacterRect(LogicalDirection.Backward); var position = text.TranslatePoint(new Point(), recordContent);
                    if (end.IsEmpty || end.Right > text.ActualWidth + 1 || position.Y + end.Bottom > recordContent.ActualHeight + 1)
                        throw new InvalidOperationException("Saved record caption clipped: " + label);
                    var recordNumber = recordContent.Children.OfType<TextBlock>().First();
                    if (Math.Abs(recordNumber.TranslatePoint(new Point(), recordCell).X - number.TranslatePoint(new Point(), holidayCell).X) > 1)
                        throw new InvalidOperationException("Date numbers have inconsistent padding: " + label);
                }
                Check(true, "workout-and-three-plan-captions-fit-with-aligned-dates-" + label);
                var weekdayY = WeekDays.TranslatePoint(new Point(), CalendarArea).Y;
                CalendarViewport.ScrollToBottom(); CalendarViewport.ScrollToRightEnd(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var weekdayCenter = ((FrameworkElement)WeekDays.Children[0]).TranslatePoint(new Point(((FrameworkElement)WeekDays.Children[0]).ActualWidth / 2, 0), CalendarArea).X;
                var dayCenter = MonthGrid.TranslatePoint(new Point(MonthGrid.Width / 14, 0), CalendarArea).X;
                Check(Math.Abs(WeekDays.TranslatePoint(new Point(), CalendarArea).Y - weekdayY) < 1 && Math.Abs(weekdayCenter - dayCenter) <= 2,
                    "weekday-header-stays-visible-and-horizontal-columns-align-" + label);
                CalendarViewport.ScrollToTop(); CalendarViewport.ScrollToLeftEnd(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture("holiday-and-birthday-" + label + ".png");
                selected = new(2026, 10, 3); DrawCalendar(); holidayCell = Cell(selected); number = ((Grid)holidayCell.Content).Children.OfType<TextBlock>().First();
                Check(((SolidColorBrush)number.Foreground).Color == ((SolidColorBrush)ResourceBrush(layout.Details ? "SelectedSunday" : "Sunday")).Color,
                    "selected-holiday-uses-readable-red-" + label);
                foreach (var year in new[] { 2025, 2026 })
                {
                    month = new(year, 5, 1); await RefreshCalendarAsync();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    foreach (var holidayGrid in MonthGrid.Children.OfType<Button>().Select(b => (Grid)b.Content))
                    foreach (var text in holidayGrid.Children.OfType<TextBlock>().Where(t => t.Tag as string == "holiday-caption"))
                    {
                        var end = text.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                        var start = text.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                        var top = text.TranslatePoint(new Point(), holidayGrid).Y;
                        if (start.IsEmpty || top + start.Top < -0.1 || end.IsEmpty
                            || text.TextTrimming != TextTrimming.CharacterEllipsis && end.Right > text.ActualWidth + 1
                            || top + end.Bottom > holidayGrid.RowDefinitions[0].ActualHeight + 0.5)
                            throw new InvalidOperationException($"Long holiday header clipped: {year}-{label}: {text.Text}; top={top}, first={start}, last={end}, width={text.ActualWidth}");
                    }
                    Check(true, $"long-and-overlapping-holiday-names-fit-{year}-" + label);
                    if (year == 2026)
                    {
                        var shared = (Grid)Cell(new(2026, 5, 24)).Content;
                        var name = shared.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<TextBlock>()).Single(t => t.Tag as string == "anniversary-caption");
                        var lastName = name.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                        Check(name.Text.Replace('\n', ' ') == "생일: 어머니" && !lastName.IsEmpty
                            && lastName.Right <= name.ActualWidth + 1 && name.TranslatePoint(new Point(), shared).Y + lastName.Bottom <= shared.ActualHeight + 1,
                            "holiday-and-birthday-coexist-with-readable-full-name-" + label);
                    }
                    if (year == 2026) {
                        CalendarViewport.ScrollToRightEnd(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                        Capture("holiday-may-" + label + ".png"); CalendarViewport.ScrollToLeftEnd();
                    }
                }
            }
            settings.Compact = false; settings.Dark = false; ApplyWindowLayout(openDetails: true); ApplyTheme();
            await SelectDateAsync(new(2026, 10, 3));
            Check(HolidayDayList.Visibility == Visibility.Visible && HolidayDayList.Children.OfType<TextBlock>().Single().Text == "공휴일: 개천절", "selected-day-shows-full-holiday-name-in-sidebar");
            month = new(2027, 7, 1); await RefreshCalendarAsync();
            Check(MonthGrid.Children.OfType<Button>().Single(b => (DateOnly)b.Tag == new DateOnly(2027, 7, 19)).ToolTip.ToString()!.Contains("제헌절"), "next-year-substitute-holiday-name-is-visible");
            month = new(2028, 1, 1); await RefreshCalendarAsync();
            Check(HolidayCoverage.Visibility == Visibility.Visible && HolidayCoverage.Text.Contains("미포함"), "unverified-holiday-year-shows-coverage-notice");
            Check(repository.Counts(day, day)[day] == originalCount, "holiday-and-caption-rendering-do-not-change-saved-plan-workout-counts");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "calendar-geometry.json"),
                JsonSerializer.Serialize(new { success = true, simulatedDpiScales = new[] { 1, 1.25, 1.5, 1.75, 2 }, layouts = geometry }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            repository.DeleteAnniversary(mother); repository.DeleteAnniversary(holidayMother); settings.Compact = false; settings.Dark = false; ApplyWindowLayout(); ApplyTheme();
            month = new(2026, 10, 1); await RefreshCalendarAsync();
        }
    }
}
