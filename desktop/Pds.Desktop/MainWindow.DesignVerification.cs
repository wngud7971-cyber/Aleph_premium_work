using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyDesignPresentationAsync(List<string> checks)
    {
        var start = checks.Count;
        var originalDark = settings.Dark;
        var originalDate = selected;
        var originalMode = statisticsMode;
        var originalAnchor = statisticsAnchor;
        var originalRange = statisticsCustom;
        var results = new List<object>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        static string Hex(Brush brush)
        {
            var c = ((SolidColorBrush)brush).Color;
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        try
        {
            ClosePlanReminder(); CloseMeditation();
            if (statisticsOpen) CloseStatistics();
            foreach (var dark in new[] { false, true })
            {
                settings.Dark = dark; ApplyTheme();
                var label = dark ? "dark" : "light";
                month = new(2026, 10, 1); await RefreshCalendarAsync();
                await SelectDateAsync(new(2026, 10, 17));
                SelectRecordTab("plan");
                CloseDetails_Click(this, new RoutedEventArgs());
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var expectedSize = FixedWindowSize(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
                Check(Math.Abs(ActualWidth - expectedSize.Width) < 1 && Math.Abs(ActualHeight - expectedSize.Height) < 1,
                    "design-keeps-fixed-window-" + label);
                var themeBounds = new Rect(ThemeButton.TranslatePoint(new Point(), TopBar), ThemeButton.RenderSize);
                var settingsBounds = new Rect(SettingsButton.TranslatePoint(new Point(), TopBar), SettingsButton.RenderSize);
                Check(settingsBounds.Left >= themeBounds.Right && Math.Abs(settingsBounds.Top - themeBounds.Top) <= 1
                    && settingsBounds.Right <= TopBar.ActualWidth + 1 && SettingsButton.IsVisible,
                    "settings-next-to-theme-without-header-overlap-" + label);
                Check(Hex(CalendarNav.Background) == Hex(ResourceBrush("Paper"))
                    && Hex(StatisticsNav.Background) == Hex(ResourceBrush("Soft"))
                    && AutomationProperties.GetHelpText(CalendarNav) == "현재 보고 있는 화면",
                    "calendar-navigation-identifies-current-screen-" + label);
                var contrast = new[] { "Paper", "Soft", "Note", "Canvas" }.SelectMany(surface => new[] { "Ink", "Muted" }
                    .Select(ink => SelectedCalendarColors.Contrast(Hex(ResourceBrush(ink)), Hex(ResourceBrush(surface))))).Min();
                Check(contrast >= 4.5, "design-primary-secondary-text-contrast-" + label);
                var firstHeader = (FrameworkElement)WeekDays.Children[0];
                var headerX = firstHeader.TranslatePoint(new Point(firstHeader.ActualWidth / 2, 0), CalendarArea).X;
                var columnX = MonthGrid.TranslatePoint(new Point(MonthGrid.Width / 14, 0), CalendarArea).X;
                Check(Math.Abs(headerX - columnX) <= 2, "centered-calendar-weekday-columns-align-" + label);
                Capture("design-calendar-" + label + ".png");

                // Opening a rightmost or leftmost day must keep the chosen date in view.
                foreach (var day in new[] { 18, 19 })
                {
                    await SelectDateAsync(new(2026, 10, day));
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    var cell = MonthGrid.Children.OfType<Button>().Single(b => b.Tag is DateOnly d && d == selected);
                    var left = cell.TranslatePoint(new Point(), CalendarViewport).X;
                    Check(left >= -1 && left + cell.ActualWidth <= CalendarViewport.ViewportWidth + 1,
                        $"selected-day-remains-visible-{day}-" + label);
                }
                await SelectDateAsync(new(2026, 10, 17));
                PlanPane.ScrollToTop();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                // DrawCalendar replaces the weekday elements, so inspect the current header.
                firstHeader = (FrameworkElement)WeekDays.Children[0];
                headerX = firstHeader.TranslatePoint(new Point(firstHeader.ActualWidth / 2, 0), CalendarArea).X;
                columnX = MonthGrid.TranslatePoint(new Point(MonthGrid.Width / 14, 0), CalendarArea).X;
                Check(Math.Abs(headerX - columnX) <= 2, "scrolled-calendar-weekday-columns-align-" + label);
                var saveBounds = new Rect(SaveButton.TranslatePoint(new Point(), Details), SaveButton.RenderSize);
                Check(saveBounds.Bottom <= Details.ActualHeight + 1 && SaveButton.ActualHeight >= 39,
                    "primary-save-visible-at-sidebar-bottom-" + label);
                Capture("design-plan-" + label + ".png");
                await VerifyDashboardStructureAsync(checks, label);
                await VerifyCalmPresentationAsync(checks, label);
                TitleInput.Text = "디자인 변경 중에도 남아야 할 초안";
                Theme_Click(this, new RoutedEventArgs());
                Check(TitleInput.Text == "디자인 변경 중에도 남아야 할 초안" && !statisticsOpen,
                    "theme-controls-preserve-plan-draft-" + label);
                Theme_Click(this, new RoutedEventArgs()); ClearEditor(); UpdateDraftStatus();
                SelectRecordTab("workout"); WorkoutPane.ScrollToTop();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture("design-workout-" + label + ".png");
                await VerifyModernScrollingAsync(checks, label);

                statisticsMode = "week"; statisticsAnchor = LocalToday.AddDays(-90); statisticsCustom = null;
                await OpenStatisticsAsync();
                StatisticsPane.ScrollToTop();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Check(Hex(StatisticsNav.Background) == Hex(ResourceBrush("Paper"))
                    && Hex(CalendarNav.Background) == Hex(ResourceBrush("Soft"))
                    && AutomationProperties.GetHelpText(StatisticsNav) == "현재 보고 있는 화면",
                    "statistics-navigation-identifies-current-screen-" + label);
                var cards = StatisticsContent.Children.OfType<UniformGrid>().Single(g => g.Columns == 4);
                var cardBorders = cards.Children.OfType<Border>().ToArray();
                Check(cardBorders.Length == 4 && cardBorders.Max(b => b.ActualWidth) - cardBorders.Min(b => b.ActualWidth) < 1,
                    "statistics-metrics-use-four-equal-columns-" + label);
                Check(DashboardHeader.Visibility == Visibility.Collapsed && StatisticsContent.Children.IndexOf(cards) == 2,
                    "statistics-prioritizes-period-metrics-without-today-dashboard-" + label);
                foreach (var card in cardBorders)
                foreach (var text in ((StackPanel)card.Child).Children.OfType<TextBlock>())
                {
                    var end = text.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                    var position = text.TranslatePoint(new Point(), card);
                    if (end.Right > text.ActualWidth + 1 || position.Y + end.Bottom > card.ActualHeight + 1)
                        throw new InvalidOperationException("Statistics metric text clipped: " + label);
                }
                Check(true, "statistics-metric-labels-and-values-fit-" + label);
                Capture("design-statistics-" + label + ".png");
                CloseStatistics();
                results.Add(new { theme = label, minimumTextContrast = contrast, settingsNextToTheme = true,
                    selectedDateVisible = true, equalMetricColumns = true, fixedWindow = true });
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "design-verification.json"),
                JsonSerializer.Serialize(new { success = true, count = checks.Count - start, results, personalDataUsed = false,
                    realKeyboardMouseTested = false, subjectiveDesignAccepted = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (statisticsOpen) CloseStatistics();
            settings.Dark = originalDark; ApplyTheme();
            statisticsMode = originalMode; statisticsAnchor = originalAnchor; statisticsCustom = originalRange;
            await SelectDateAsync(originalDate); month = new(originalDate.Year, originalDate.Month, 1); await RefreshCalendarAsync();
            SelectRecordTab("plan");
        }
    }
}
