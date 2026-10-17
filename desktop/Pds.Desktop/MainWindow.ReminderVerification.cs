using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyReminderFeaturesAsync(List<string> checks)
    {
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        var day = LocalToday.AddDays(60);
        var originalMeditation = meditation;
        var savedEnabled = settings.MeditationEnabled;
        var savedDate = settings.MeditationDate;
        var dateKey = day.ToString("yyyy-MM-dd");
        var entries = Enumerable.Range(1, 16).Select(i => PlanEntry.Create($"할 일 {i} · 긴 제목도 줄바꿈하고 아래까지 확인하기", "메모", i == 1 ? "09:00" : null, i == 1 ? "10:00" : null)).ToArray();
        repository.Save(repository.Load(day) with { Plans = entries.Concat([PlanEntry.Create("이미 완료한 일") with { Done = true, ActualDate = day }]).ToArray() });
        repository.Save(repository.Load(day.AddDays(1)) with { Plans = [PlanEntry.Create("다음 날 할 일")] });
        try
        {
            CloseMeditation(); ClosePlanReminder(); if (statisticsOpen) CloseStatistics();
            pendingChangeDecision = () => MessageBoxResult.No;
            await SelectDateAsync(day.AddDays(2)); pendingChangeDecision = null;
            settings.Compact = false; ApplyWindowLayout(openDetails: true); 
            reminderTestDate = day; settings.MeditationEnabled = true; settings.MeditationDate = null; settings.MeditationShownDates.Remove(dateKey);
            TitleInput.Text = "보호할 계획 입력";
            var exercise = ExerciseCatalog.Get("walking"); WorkoutCategory.SelectedItem = exercise.Category; WorkoutExercise.SelectedItem = exercise;
            WorkoutMinutes.Text = "12"; ReviewGood.Text = "보호할 회고 입력";
            var revision = repository.Load(day).Revision;
            entryGuidancePending = true; await ProcessEntryGuidanceAsync(true);
            Check(MeditationCard.Visibility == Visibility.Visible && ReminderOverlay.Visibility != Visibility.Visible, "entry-shows-meditation-before-today-plans");
            CloseMeditation(); await ProcessEntryGuidanceAsync(true);
            Check(ReminderOverlay.Visibility == Visibility.Visible && ReminderList.Children.Count == 16 && ReminderHeading.Text.Contains(dateKey.Replace('-', '.')), "skip-meditation-shows-only-today-saved-incomplete-plans");
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            RaiseEvent(escape);
            Check(escape.Handled && ReminderOverlay.Visibility == Visibility.Collapsed && repository.Load(day).Revision == revision, "popup-escape-closes-without-saving");
            entryGuidancePending = true; await ProcessEntryGuidanceAsync(true);
            ReminderClose.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(ReminderOverlay.Visibility == Visibility.Collapsed && repository.Load(day).Revision == revision && TitleInput.Text == "보호할 계획 입력" && WorkoutMinutes.Text == "12" && ReviewGood.Text == "보호할 회고 입력", "popup-x-preserves-all-drafts-and-saved-records");
            entryGuidancePending = true; await ProcessEntryGuidanceAsync(true);
            Check(MeditationCard.Visibility == Visibility.Collapsed && ReminderOverlay.Visibility == Visibility.Visible, "same-day-entry-repeats-remaining-plans-without-meditation");
            pendingChangeDecision = () => MessageBoxResult.Cancel; await OpenReminderDateAsync();
            Check(ReminderOverlay.Visibility == Visibility.Visible && selected == day.AddDays(2) && TitleInput.Text == "보호할 계획 입력", "open-today-cancel-protects-draft-and-popup");
            pendingChangeDecision = () => MessageBoxResult.No; await OpenReminderDateAsync(); pendingChangeDecision = null;
            Check(selected == day && !Pending && ReminderOverlay.Visibility == Visibility.Collapsed && PlanPane.Visibility == Visibility.Visible, "confirmed-open-today-navigates-to-plan-sidebar");
            Check(WorkoutRecordDate.Text.Contains(day.ToString("yyyy.MM.dd")), "workout-editor-displays-selected-record-date");
            foreach (var dark in new[] { false, true })
            {
                settings.Dark = dark; ApplyWindowLayout(); ApplyTheme();
                await OpenPlanReminderAsync(day);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                ReminderViewport.ScrollToBottom(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var last = (FrameworkElement)ReminderList.Children[^1]; var position = last.TranslatePoint(new Point(), ReminderViewport);
                var closePosition = ReminderDismiss.TranslatePoint(new Point(), ReminderOverlay);
                Check(ReminderViewport.ScrollableHeight > 0 && position.Y >= -1 && position.Y + last.ActualHeight <= ReminderViewport.ViewportHeight + 1
                    && closePosition.Y >= 0 && closePosition.Y + ReminderDismiss.ActualHeight <= ReminderOverlay.ActualHeight + 1,
                    "reminder-scroll-and-close-reachable-" + (dark ? "dark" : "light"));
                Capture("reminder-" + (dark ? "dark" : "light") + ".png"); ClosePlanReminder();
            }
            settings.Compact = false; settings.Dark = false; ApplyWindowLayout(); ApplyTheme(); 
            var clock = new ReminderTimestampClock(); meditation = new MeditationSession(clock);
            settings.MeditationShownDates.Remove(dateKey); settings.MeditationDate = null;
            entryGuidancePending = true; await ProcessEntryGuidanceAsync(true); StartMeditation_Click(this, new RoutedEventArgs());
            clock.Ticks = TimeSpan.FromMinutes(5).Ticks; RefreshMeditation(); await ProcessEntryGuidanceAsync(true);
            Check(MeditationCard.Visibility == Visibility.Collapsed && ReminderOverlay.Visibility == Visibility.Visible && !meditationTimer.IsEnabled, "five-minute-completion-opens-today-plans-and-stops-timer");
            ReminderDismiss.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(ReminderOverlay.Visibility == Visibility.Collapsed && repository.Load(day).Revision == revision, "popup-close-button-does-not-complete-or-save-plans");
            Hide(); RestoreWindow(); entryGuidancePending = true; await ProcessEntryGuidanceAsync(true);
            Check(ReminderOverlay.Visibility == Visibility.Visible, "restore-can-show-remaining-plans-again"); ClosePlanReminder();
            repository.Save(repository.Load(day) with { Plans = entries.Select(p => p with { Done = true, ActualDate = day }).ToArray() });
            entryGuidancePending = true; await ProcessEntryGuidanceAsync(true);
            Check(ReminderOverlay.Visibility == Visibility.Collapsed, "all-complete-skips-empty-reminder");
            reminderTestDate = day.AddDays(1); settings.MeditationEnabled = false;
            entryGuidancePending = true; await ProcessEntryGuidanceAsync(true);
            Check(ReminderList.Children.Count == 1 && ReminderOverlay.Visibility == Visibility.Visible && MeditationCard.Visibility == Visibility.Collapsed, "disabled-meditation-opens-new-day-plans-directly"); ClosePlanReminder();
            reminderTestDate = day.AddDays(3); entryGuidancePending = true; await ProcessEntryGuidanceAsync(true);
            Check(ReminderOverlay.Visibility == Visibility.Collapsed, "no-saved-plan-skips-reminder");
            settings.MeditationEnabled = true; settings.MeditationDate = null;
            await OpenStatisticsAsync(); entryGuidancePending = true; await ProcessEntryGuidanceAsync(true);
            Check(!statisticsOpen && MeditationCard.Visibility == Visibility.Visible && ReminderOverlay.Visibility == Visibility.Collapsed, "first-new-day-entry-from-statistics-still-shows-meditation-first");
        }
        finally
        {
            pendingChangeDecision = null; entryGuidancePending = waitingForEntryMeditation = false;
            ClosePlanReminder(); CloseMeditation(); meditation = originalMeditation; reminderTestDate = null;
            settings.MeditationEnabled = savedEnabled; settings.MeditationDate = savedDate; settings.MeditationShownDates.Remove(dateKey);
        }
    }
    private sealed class ReminderTimestampClock : TimeProvider
    {
        public long Ticks { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }
}
