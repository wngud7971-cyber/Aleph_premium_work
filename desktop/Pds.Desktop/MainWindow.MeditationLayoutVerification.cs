using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyMeditationLayoutAsync(List<string> checks)
    {
        var original = meditation;
        var originalDark = settings.Dark;
        var results = new List<object>();
        try
        {
            entryGuidancePending = waitingForEntryMeditation = false;
            ClosePlanReminder(); CloseMeditation();
            if (statisticsOpen) CloseStatistics();
            foreach (var dark in new[] { false, true })
            {
                settings.Dark = dark; ApplyWindowLayout(); ApplyTheme();
                var clock = new ReminderTimestampClock(); meditation = new MeditationSession(clock);
                OpenMeditation();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                FitCalendar(); UpdateLayout();
                var label = "fixed" + (dark ? "-dark" : "-light");
                double[] Geometry()
                {
                    var digit = MeditationRemaining.TranslatePoint(new Point(), this);
                    var button = MeditationStart.TranslatePoint(new Point(), this);
                    var card = MeditationCard.TranslatePoint(new Point(), this);
                    return [digit.X, digit.Y, MeditationRemaining.ActualWidth, MeditationRemaining.ActualHeight,
                        MeditationRemaining.BaselineOffset, button.X, button.Y, MeditationStart.ActualWidth,
                        MeditationStart.ActualHeight, card.X, card.Y, MeditationCard.ActualWidth, MeditationCard.ActualHeight];
                }
                var baseline = Geometry();
                var face = new Typeface(MeditationRemaining.FontFamily, MeditationRemaining.FontStyle,
                    MeditationRemaining.FontWeight, MeditationRemaining.FontStretch);
                double Advance(string text) => new FormattedText(text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, face, MeditationRemaining.FontSize, Brushes.Black,
                    VisualTreeHelper.GetDpi(MeditationRemaining).PixelsPerDip).WidthIncludingTrailingWhitespace;
                var advance = Advance("05:00");
                var maxGeometryDelta = 0d; var maxAdvanceDelta = 0d;
                var cardBounds = new Rect(MeditationCard.TranslatePoint(new Point(), CalendarArea), MeditationCard.RenderSize);
                if (cardBounds.Left < -1 || cardBounds.Top < -1 || cardBounds.Right > CalendarArea.ActualWidth + 1
                    || cardBounds.Bottom > CalendarArea.ActualHeight + 1)
                    throw new InvalidOperationException("Meditation card clipped: " + label);
                StartMeditation_Click(this, new RoutedEventArgs());
                for (var seconds = 300; seconds >= 0; seconds--)
                {
                    clock.Ticks = TimeSpan.FromSeconds(300 - seconds).Ticks;
                    RefreshMeditation(); UpdateLayout();
                    var expected = $"{seconds / 60:00}:{seconds % 60:00}";
                    if (MeditationRemaining.Text != expected) throw new InvalidOperationException("Wrong timer text: " + label);
                    maxGeometryDelta = Math.Max(maxGeometryDelta, Geometry().Zip(baseline, (a, b) => Math.Abs(a - b)).Max());
                    maxAdvanceDelta = Math.Max(maxAdvanceDelta, Math.Abs(Advance(expected) - advance));
                    if (seconds is 300 or 299 or 191 or 0)
                        Capture("meditation-stable-" + label + "-" + expected.Replace(':', '-') + ".png");
                }
                if (maxGeometryDelta > 0.1 || maxAdvanceDelta > 0.1 || meditationTimer.IsEnabled)
                    throw new InvalidOperationException($"Timer layout shifted: {label}, geometry={maxGeometryDelta}, advance={maxAdvanceDelta}");
                checks.Add("meditation-stable-301-values-" + label);
                results.Add(new { layout = label, values = 301, maxGeometryDelta, maxAdvanceDelta, cardFits = true });
                CloseMeditation();
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "meditation-layout.json"),
                JsonSerializer.Serialize(new { success = true, results, personalDataUsed = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            entryGuidancePending = waitingForEntryMeditation = false;
            CloseMeditation(); meditation = original;
            settings.Dark = originalDark; ApplyWindowLayout(); ApplyTheme();
        }
    }
}
