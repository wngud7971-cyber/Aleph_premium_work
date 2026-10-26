using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    // Runs only against the explicit isolated verification directory, without mouse/keyboard automation.
    private async Task VerifyFeaturesAsync()
    {
        var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        void SetExercise(string id)
        {
            ClearWorkoutEditor();
            var exercise = ExerciseCatalog.Get(id);
            WorkoutCategory.SelectedItem = exercise.Category;
            WorkoutExercise.SelectedItem = exercise;
        }
        try
        {
            if(Environment.GetEnvironmentVariable("PDS_VERIFY_SIDEBAR_ONLY")=="1")
            {await VerifySidebarMotionAsync(checks);FinishVerification(null);return;}
            if(Environment.GetEnvironmentVariable("PDS_VERIFY_MOTION_ONLY")=="1")
            {await VerifyPageTurnAsync(checks);FinishVerification(null);return;}
            if(Environment.GetEnvironmentVariable("PDS_VERIFY_FEEDBACK_ONLY")=="1")
            {await VerifyFeedbackBatchAsync(checks);FinishVerification(null);return;}
            if(Environment.GetEnvironmentVariable("PDS_VERIFY_RECORDS_ONLY")=="1")
            {await VerifyEmbeddedRecordsAsync(checks);FinishVerification(null);return;}
            if(Environment.GetEnvironmentVariable("PDS_VERIFY_UNIFIED_ONLY")=="1")
            {await VerifyUnifiedMotionAsync(checks);FinishVerification(null);return;}
            Check(!statisticsOpen && CalendarArea.IsVisible,"initial-page-is-calendar-home");
            var iconUri = new Uri("pack://application:,,,/Pds.Desktop;component/Assets/Pds.ico");
            using (var iconStream = System.Windows.Application.GetResourceStream(iconUri)!.Stream)
            using (var icon = new System.Drawing.Icon(iconStream))
                Check(icon.Width == 32 && System.Windows.Media.Imaging.BitmapFrame.Create(iconUri).PixelWidth == 32, "tray-and-window-icon-decode");
            CloseMeditation();
            await SelectDateAsync(selected); SelectRecordTab(true);
            SetExercise("pushups"); WorkoutQuantity.Text = "30"; WorkoutNote.Text = "횟수만 저장";
            TitleInput.Text = "운동과 계획 동시 저장";
            Check(await SaveAsync(), "save-plan-and-quantity-workout");
            Check(repository.Load(selected).Plans.Count == 1 && repository.Load(selected).Workouts.Single().DurationSeconds is null, "quantity-only-does-not-invent-time");
            Check(Summary.Text.Contains("시간 미입력") && !Summary.Text.Contains("0분"), "missing-duration-is-not-displayed-as-zero");
            var pushupsId = workouts.Single().Id;
            BeginWorkoutEdit(pushupsId); WorkoutQuantity.Text = "40";
            Check(await SaveAsync() && workouts.Count == 1 && workouts[0].Id == pushupsId && workouts[0].Quantity == 40, "workout-edit-preserves-id");
            SetExercise("plank"); WorkoutMinutes.Text = "1"; WorkoutSeconds.Text = "30";
            Check(await SaveAsync() && workouts.Single(w => w.ExerciseId == "plank").DurationSeconds == 90, "plank-minutes-and-seconds");
            SetExercise("stairs"); WorkoutQuantity.Text = "12";
            Check(await SaveAsync() && workouts.Single(w => w.ExerciseId == "stairs").DurationSeconds is null, "floors-without-time");
            SetExercise("futsal"); WorkoutMinutes.Text = "120";
            Check(await SaveAsync() && workouts.Single(w => w.ExerciseId == "futsal").DurationSeconds == 7200, "futsal-total-time-only-no-calorie-assumption");
            foreach (var id in new[] { "cycling-indoor", "cycling-outdoor", "badminton", "tennis" })
            { SetExercise(id); WorkoutMinutes.Text = "30"; Check(await SaveAsync(), "save-" + id); }
            var revision = snapshot.Revision;
            SetExercise("walking"); WorkoutMinutes.Text = "0";
            Check(!await SaveAsync() && WorkoutMinutes.Text == "0" && repository.Load(selected).Revision == revision, "invalid-workout-preserves-input-and-database");
            ClearWorkoutEditor(); SetExercise("plank"); WorkoutSeconds.Text = "60";
            Check(!await SaveAsync() && WorkoutSeconds.Text == "60" && repository.Load(selected).Revision == revision, "invalid-seconds-preserved");
            ClearWorkoutEditor(); SetExercise("walking"); WorkoutMinutes.Text = "15"; WorkoutNote.Text = "아직 저장하지 않은 운동";
            TitleInput.Text = "아직 저장하지 않은 계획";
            SelectRecordTab(false); SelectRecordTab(true); Theme_Click(this, new RoutedEventArgs());
            Check(WorkoutMinutes.Text == "15" && WorkoutNote.Text == "아직 저장하지 않은 운동" && TitleInput.Text == "아직 저장하지 않은 계획", "tabs-and-theme-preserve-both-drafts");
            CancelEdit_Click(this, new RoutedEventArgs());
            if (WorkoutMinutes.Text != "15" || SaveStatus.Text != "저장하지 않은 변경") throw new InvalidOperationException("Cancelling plan input hid the unsaved workout status");
            TitleInput.Text = "아직 저장하지 않은 계획";
            OpenMeditation();
            Check(meditation.State == MeditationState.Ready && !meditationTimer.IsEnabled, "meditation-open-does-not-start");
            StartMeditation_Click(this, new RoutedEventArgs());
            Check(meditation.State == MeditationState.Running && meditationTimer.IsEnabled && MeditationRemaining.Text == "05:00", "meditation-starts-on-button");
            Close();
            Check(!IsVisible && !meditationTimer.IsEnabled && Pending, "hide-stops-view-ticks-and-preserves-drafts");
            var pipe = "PdsWindowCheck-" + Guid.NewGuid().ToString("N");
            using (var server = new InstanceActivation(pipe))
            using (var client = new InstanceActivation(pipe))
            {
                Exception? failure = null;
                server.Listen(() => Dispatcher.InvokeAsync(RestoreWindow).Task, ex => failure = ex);
                Check(await client.RequestAsync(TimeSpan.FromSeconds(3)) && failure is null, "activation-request-restores-hidden-window");
            }
            Check(IsVisible && WorkoutMinutes.Text == "15" && TitleInput.Text == "아직 저장하지 않은 계획" && meditationTimer.IsEnabled, "restore-keeps-both-drafts-and-running-session");
            CloseMeditation();
            Check(meditation.State == MeditationState.Ready && !meditationTimer.IsEnabled && MeditationCard.Visibility == Visibility.Collapsed, "meditation-closes-midway");
            ClearEditor(); ClearWorkoutEditor();
            var savedWorkouts = repository.Load(selected).Workouts;
            Check(!await HasUnexpectedWorkoutChangeAsync(savedWorkouts), "draft-and-meditation-do-not-save-workouts");
            workouts.RemoveAll(w => w.Id == pushupsId); dirty = true;
            Check(await SaveAsync() && !repository.Load(selected).Workouts.Any(w => w.Id == pushupsId), "workout-delete-saves");
            var day = selected;
            await SelectDateAsync(day.AddDays(1));
            Check(workouts.Count == 0 && plans.Count == 0, "workouts-separated-by-date");
            await SelectDateAsync(day);
            Check(workouts.Count == 7 && plans.Count == 1, "workouts-return-after-date-navigation");
            settings.Compact = true; settings.Pinned = true; ApplyWindowLayout(); DrawCalendar();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var fixedSize = (ActualWidth, ActualHeight);
            await SelectDateAsync(day);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(!settings.Compact && !settings.Pinned && !Topmost, "legacy-compact-and-pin-settings-normalized");
            Check(Details.Visibility == Visibility.Visible && DetailColumn.ActualWidth >= 329 && (ActualWidth, ActualHeight) == fixedSize, "date-opens-sidebar-without-window-resize");
            await VerifyPresentationAsync();
            Check(true, "12-fixed-window-theme-sidebar-calendar-week-layouts");
            Check(true, "all-four-five-six-week-rows-visible-without-vertical-scrolling");
            Check(Math.Abs(FontSize - UiTypography.Grow(12)) < 0.001 && Math.Abs(Brand.FontSize - UiTypography.Grow(20)) < 0.001
                && Math.Abs(MeditationRemaining.FontSize - UiTypography.Grow(24)) < 0.001, "body-logo-timer-follow-approved-two-point-scale");
            settings.Compact = false; ApplyWindowLayout(openDetails: true); SelectRecordTab(true); DrawCalendar(); DrawWorkouts();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("workouts.png");
            SetExercise("cycling-indoor"); WorkoutMinutes.Text = "30"; WorkoutPane.ScrollToBottom();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("workout-editor.png");
            settings.Dark = true; ApplyTheme(); DrawWorkouts(); DrawCalendar();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("workout-editor-dark.png");
            WorkoutCategory.BringIntoView();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("workout-selectors-dark.png");
            settings.Dark = false; ApplyTheme(); DrawWorkouts(); DrawCalendar(); ClearWorkoutEditor(); WorkoutPane.ScrollToTop();
            OpenMeditation(); StartMeditation_Click(this, new RoutedEventArgs());
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("meditation-timer.png"); CloseMeditation();
            await VerifyEnergyFeaturesAsync(checks, SetExercise);
            await VerifyReviewFeaturesAsync(checks, SetExercise);
            await VerifyCompletionFeaturesAsync(checks);
            await VerifyReminderFeaturesAsync(checks);
            await VerifyMeditationLayoutAsync(checks);
            await VerifyAnniversaryFeaturesAsync(checks);
            await VerifyFixedWindowAsync(checks);
            await VerifyHolidayPresentationAsync(checks);
            pendingChangeDecision = () => MessageBoxResult.No;
            await SelectDateAsync(LocalToday.AddDays(-90)); month = new(selected.Year, selected.Month, 1); await RefreshCalendarAsync();
            var monthBeforeCancel = month; var dayBeforeCancel = selected; TitleInput.Text = "오늘 이동 취소 시 보호";
            pendingChangeDecision = () => MessageBoxResult.Cancel; await ShowTodayAsync();
            Check(selected == dayBeforeCancel && month == monthBeforeCancel && TitleInput.Text == "오늘 이동 취소 시 보호", "today-navigation-cancel-keeps-displayed-month-date-and-input");
            pendingChangeDecision = () => MessageBoxResult.No; await ShowTodayAsync(); pendingChangeDecision = null;
            Check(selected == LocalToday && month == new DateOnly(LocalToday.Year, LocalToday.Month, 1) && !Pending, "today-navigation-confirm-updates-month-and-date-together");
            await VerifyDesignPresentationAsync(checks);
            await VerifyJournalFeaturesAsync(checks);
            await VerifyStudyOverviewAsync(checks);
            await VerifyAttachmentsAsync(checks);
            await VerifyFixedCostsAsync(checks);
            await VerifyRecordToolsAsync(checks);
            await VerifySidebarMotionAsync(checks);
            await VerifyFontStepAsync(checks);
            await VerifyCrispSpacingAsync(checks);
            await VerifySteadyUiAsync(checks);
            await VerifyPageTurnAsync(checks);
            await VerifyFeedbackBatchAsync(checks);
            await VerifyEmbeddedRecordsAsync(checks);
            await VerifyUnifiedMotionAsync(checks);
            PersistSettings();
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "features.json"), JsonSerializer.Serialize(new { success = true, checks, count = checks.Count, personalDataUsed = false, realTrayClickTested = false, subjectiveTransitionSmoothnessTested = false }, new JsonSerializerOptions { WriteIndented = true }));
            FinishVerification(null);
        }
        catch (Exception ex) { FinishVerification(ex); }
    }
    private Task<bool> HasUnexpectedWorkoutChangeAsync(IReadOnlyList<WorkoutEntry> expected)
        => Task.Run(() => !repository.Load(selected).Workouts.SequenceEqual(expected));
}
