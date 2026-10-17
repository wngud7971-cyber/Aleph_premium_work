using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyCompletionFeaturesAsync(List<string> checks)
    {
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        var folder = Path.GetDirectoryName(repository.DatabasePath)!;
        var rolloverDay = LocalToday.AddDays(-40);
        var midday = rolloverDay.ToDateTime(new TimeOnly(12, 0));
        var now = new DateTimeOffset(midday, TimeZoneInfo.Local.GetUtcOffset(midday));
        var fixture = new PlanRepository(repository.DatabasePath, new VerificationWallClock(now));
        var rolloverPlan = PlanEntry.Create("23시 이월 검증", "메모 유지", "10:00", "11:00");
        fixture.Save(fixture.Load(rolloverDay) with { Plans = [rolloverPlan], Workouts = [WorkoutEntry.Create("walking", 600, null)], Review = new("이월해도 유지", "", "") });
        try
        {
            await SelectDateAsync(rolloverDay); SelectRecordTab("review"); ReviewGood.Text = "보호할 입력";
            var result = await MaintainAsync(now.AddHours(11));
            Check(result?.Moved == 0 && result.Deferred == 1 && ReviewGood.Text == "보호할 입력" && Pending, "23-cutoff-protects-unsaved-day-and-draft");
            pendingChangeDecision = () => MessageBoxResult.No;
            await SelectDateAsync(rolloverDay.AddDays(1)); pendingChangeDecision = null;
            result = await MaintainAsync(now.AddHours(11));
            Check(result?.Moved == 1 && plans.Single().Id == rolloverPlan.Id && plans[0].Start is null && !Pending, "23-cutoff-reloads-visible-target-and-clears-time");
            Check(repository.Load(rolloverDay).Workouts.Count == 1 && repository.Load(rolloverDay).Review?.Good == "이월해도 유지", "23-cutoff-keeps-source-workout-and-review");
            result = await MaintainAsync(now.AddHours(11));
            Check(result?.Moved == 0 && repository.LoadPlanHistory(rolloverPlan.Id).Moves.Count == 1, "repeated-ui-maintenance-is-idempotent");
            SelectRecordTab("plan");
            var check = (CheckBox)((StackPanel)((Border)PlanList.Children[0]).Child).Children[0];
            check.IsChecked = true; check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(plans[0].Done && plans[0].ActualDate == selected && Pending, "completion-checkbox-sets-explicit-selected-date");
            Check(await SaveAsync(), "completion-date-saves");
            BeginEdit(rolloverPlan.Id); NoteInput.Text = "완료 체크 중 지킬 수정 입력";
            check = (CheckBox)((StackPanel)((Border)PlanList.Children[0]).Child).Children[0]; check.IsChecked = false; check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(NoteInput.Text == "완료 체크 중 지킬 수정 입력" && editing == rolloverPlan.Id && ActualDateFields.Visibility == Visibility.Collapsed, "completion-toggle-preserves-plan-editor-draft");
            check = (CheckBox)((StackPanel)((Border)PlanList.Children[0]).Child).Children[0]; check.IsChecked = true; check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(await SaveAsync(), "completion-toggle-and-edit-save-together");
            BeginEdit(rolloverPlan.Id); ActualDateInput.SelectedDate = rolloverDay.AddDays(2).ToDateTime(TimeOnly.MinValue);
            Check(ActualDateFields.Visibility == Visibility.Visible && await SaveAsync() && plans[0].ActualDate == rolloverDay.AddDays(2), "actual-completion-date-can-be-corrected");
            check = (CheckBox)((StackPanel)((Border)PlanList.Children[0]).Child).Children[0]; check.IsChecked = false; check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(await SaveAsync() && plans[0].ActualDate is null, "undo-completion-clears-actual-date");

            var statsDay = DateRange.Week(LocalToday.AddDays(-90)).From;
            var done = PlanEntry.Create("통계 완료") with { Done = true, ActualDate = statsDay };
            fixture.Save(fixture.Load(statsDay) with { Plans = [done, done with { Id = Guid.NewGuid() }, done with { Id = Guid.NewGuid(), ActualDate = statsDay.AddDays(1) }, PlanEntry.Create("통계 미완료")],
                Workouts = [WorkoutEntry.Create("walking", 1800, null), WorkoutEntry.Create("cycling-indoor", 1200, null)], Review = new("통계 첫 회고", "", "") });
            fixture.Save(fixture.Load(statsDay.AddDays(2)) with { Workouts = [WorkoutEntry.Create("badminton", 2400, null)], Review = new("둘째 회고", "", "") });
            settings.Dark = false; ApplyTheme(); DrawCalendar(); DrawPlans(); DrawWorkouts();
            await SelectDateAsync(statsDay); TitleInput.Text = "통계 이동 전 입력";
            pendingChangeDecision = () => MessageBoxResult.Cancel;
            await OpenStatisticsAsync();
            Check(!statisticsOpen && TitleInput.Text == "통계 이동 전 입력", "statistics-navigation-cancel-protects-draft");
            pendingChangeDecision = () => MessageBoxResult.No;
            await OpenStatisticsAsync(); pendingChangeDecision = null;
            Check(statisticsOpen && StatisticsPane.Visibility == Visibility.Visible && CalendarArea.Visibility == Visibility.Collapsed && Details.Visibility == Visibility.Collapsed, "statistics-is-a-separate-screen");
            Check(statisticsReport?.Plans == 4 && statisticsReport.Done == 3 && statisticsReport.CompletionRate == 75 && statisticsReport.Workout.Seconds == 5400 && statisticsReport.ReviewDays == 2, "weekly-ui-shows-reference-statistics");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("statistics-week.png");
            statisticsMode = "month"; await RefreshStatisticsAsync();
            Check(statisticsReport?.Range == DateRange.Month(statsDay), "monthly-ui-uses-calendar-month");
            statisticsMode = "weekday"; statisticsCustom = new(statsDay, statsDay.AddDays(6)); await RefreshStatisticsAsync();
            Check(statisticsReport?.Weekdays[0].ActualDone == 2 && statisticsReport.Weekdays[1].ActualDone == 1, "weekday-ui-counts-actual-dates");
            var reportBeforeMetric = statisticsReport;
            foreach (var metricName in new[] { "완료 계획", "운동 일수", "예상 kcal", "운동 시간" }) { statisticsMetric = metricName; DrawStatistics(); }
            Check(ReferenceEquals(statisticsReport, reportBeforeMetric), "chart-metric-changes-reuse-the-same-statistics-result");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("statistics-weekdays.png");
            statisticsFilter = new("사이클"); await RefreshStatisticsAsync();
            Check(statisticsReport?.Plans == 4 && statisticsReport.ReviewDays == 2 && statisticsReport.Workout.Records == 1 && statisticsReport.Workout.Seconds == 1200, "ui-workout-filter-keeps-plan-and-review-values");
            statisticsFilter = new(); settings.Dark = true; ApplyTheme(); DrawStatistics();
            await RefreshStatisticsAsync(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("statistics-dark.png");
            var layouts = new List<object>();
            foreach (var dark in new[] { false, true })
            {
                settings.Dark = dark; ApplyTheme(); ApplyWindowLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                StatisticsPane.ScrollToBottom(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var last = (FrameworkElement)StatisticsContent.Children[^1]; var top = last.TranslatePoint(new Point(), StatisticsPane).Y;
                Check(top >= -1 && top + last.ActualHeight <= StatisticsPane.ActualHeight + 1, "statistics-bottom-reachable-fixed-" + (dark ? "dark" : "light"));
                layouts.Add(new { width = Width, height = Height, lastRowReachable = true });
            }
            File.WriteAllText(Path.Combine(folder, "statistics-layouts.json"), JsonSerializer.Serialize(layouts));
            settings.Dark = false; ApplyTheme(); ApplyWindowLayout(); CloseStatistics();
            Check(!statisticsOpen && CalendarArea.Visibility == Visibility.Visible && Details.Visibility == Visibility.Visible, "calendar-navigation-restores-sidebar");
            var beforeStats = (ActualWidth, ActualHeight); await OpenStatisticsAsync();
            Check(!settings.Compact && statisticsOpen && (ActualWidth, ActualHeight) == beforeStats, "statistics-keeps-fixed-window-size");
            CloseStatistics();

            var path = Path.Combine(folder, "manual-export.json");
            await ExportBackupAsync(path);
            var backup = BackupFile.Read(path);
            Check(backup.Plans > 0 && backup.Workouts > 0 && backup.Reviews > 0 && repository.Load(statsDay).Plans.Count == 4, "manual-ui-export-keeps-original-records");
            var extraDay = LocalToday.AddDays(50); await SelectDateAsync(extraDay); TitleInput.Text = "복원으로 교체할 기록";
            Check(await SaveAsync(), "prepare-extra-record-before-restore");
            backupReplacementDecision = _ => MessageBoxResult.No;
            Check(!await RestoreBackupAsync(path) && repository.Load(extraDay).Plans.Count == 1, "restore-confirmation-cancel-keeps-records");
            var invalidPath = Path.Combine(folder, "invalid-backup.json"); File.WriteAllText(invalidPath, "{broken}");
            Check(!await RestoreBackupAsync(invalidPath) && repository.Load(extraDay).Plans.Count == 1, "invalid-backup-does-not-change-live-records");
            BackupFile.Write(invalidPath, backup with { PreferencesJson = "{\"Accent\":\"invalid\"}" });
            Check(!await RestoreBackupAsync(invalidPath) && repository.Load(extraDay).Plans.Count == 1, "invalid-settings-rejected-before-restore");
            TitleInput.Text = "복원 전 남은 입력"; pendingChangeDecision = () => MessageBoxResult.Cancel; backupReplacementDecision = _ => MessageBoxResult.Yes;
            Check(!await RestoreBackupAsync(path) && TitleInput.Text == "복원 전 남은 입력" && repository.Load(extraDay).Plans.Count == 1, "pending-restore-cancel-protects-draft-and-database");
            pendingChangeDecision = () => MessageBoxResult.No;
            settings.MeditationShownDates.Add(LocalToday.AddDays(-10).ToString("yyyy-MM-dd"));
            Check(await RestoreBackupAsync(path) && !Pending && repository.Load(extraDay).Plans.Count == 0 && repository.Load(statsDay).Plans.Count == 4, "confirmed-restore-replaces-records-and-reloads-screen");
            Check(File.Exists(path) && BackupFile.Read(path).Plans == backup.Plans && settingsStore.Load().Accent == settings.Accent, "restore-keeps-source-file-and-restores-settings");
            Check(settings.MeditationShownDates.Contains(LocalToday.AddDays(-10).ToString("yyyy-MM-dd")) && settingsStore.Load().MeditationShownDates.Contains(LocalToday.AddDays(-10).ToString("yyyy-MM-dd")), "restore-preserves-local-meditation-shown-dates");
            Check(!maintenanceTimer.IsEnabled && !maintenanceStarted, "verification-does-not-register-startup-or-run-background-maintenance");
        }
        finally { pendingChangeDecision = null; backupReplacementDecision = null; statisticsFilter = new(); }
    }
    private sealed class VerificationWallClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now.ToUniversalTime(); }
}
