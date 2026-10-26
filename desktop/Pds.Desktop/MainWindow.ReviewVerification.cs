using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyReviewFeaturesAsync(List<string> checks, Action<string> selectExercise)
    {
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        // Isolate this persistent fixture from the calendar holiday fixtures, independent of today.
        var day = new DateOnly(2026, 4, 14);
        try
        {
            await SelectDateAsync(day); SelectRecordTab("review");
            Check(!Pending && ReviewGood.Text == "" && ReviewDifficult.Text == "" && ReviewNextAction.Text == "", "empty-review-is-optional-and-not-dirty");
            ReviewGood.Text = "5분간 마음을 가다듬었다.";
            Check(Pending && repository.CountReviewDays(day, day) == 0, "review-edit-is-pending-until-save");
            Check(await SaveAsync() && SaveStatus.Text == "저장 완료" && !Pending && repository.Load(day).Review?.Good == ReviewGood.Text, "single-review-field-saves-and-confirms");
            ReviewDifficult.Text = "계획을 늦게 시작했다."; ReviewNextAction.Text = "내일은 먼저 한 가지 끝내기.";
            Check(await SaveAsync() && repository.CountReviewDays(day, day) == 1, "three-review-fields-update-one-daily-entry");
            var fullReview = snapshot.Review;
            await SelectDateAsync(day.AddDays(1));
            Check(ReviewGood.Text == "" && ReviewDifficult.Text == "" && ReviewNextAction.Text == "", "review-input-separated-by-date");
            await SelectDateAsync(day);
            Check(ReviewInput.Normalize() == fullReview, "saved-review-restores-on-date-return");
            TitleInput.Text = "회고와 함께 유지할 계획"; selectExercise("walking"); WorkoutMinutes.Text = "15";
            Check(await SaveAsync() && repository.Load(day).Review == fullReview, "saving-plan-and-workout-keeps-existing-review");
            var revision = snapshot.Revision; var prompts = 0;
            reviewRemovalDecision = () => { prompts++; return MessageBoxResult.No; };
            ReviewGood.Clear(); ReviewDifficult.Clear(); ReviewNextAction.Clear();
            Check(!await SaveAsync() && prompts == 1 && HasReviewChanges && repository.Load(day).Revision == revision && repository.Load(day).Review == fullReview,
                "cancel-review-removal-keeps-draft-and-saved-record");
            reviewRemovalDecision = () => { prompts++; return MessageBoxResult.Yes; };
            Check(await SaveAsync() && prompts == 2 && snapshot.Review is null && repository.CountReviewDays(day, day) == 0
                && repository.Load(day).Plans.Count == 1 && repository.Load(day).Workouts.Count == 1, "confirm-empty-review-removes-only-review");
            ReviewNextAction.Text = "다음 한 걸음";
            Check(await SaveAsync() && snapshot.Review?.Good == "" && snapshot.Review.NextAction == ReviewNextAction.Text && prompts == 2, "next-action-only-review-saves-without-removal-prompt");
            var beforeInvalid = repository.Load(day);
            ReviewGood.Text = new string('가', 2001);
            Check(!await SaveAsync() && ReviewGood.Text.Length == 2001 && repository.Load(day).Revision == beforeInvalid.Revision, "invalid-review-keeps-input-and-saved-day");
            ReviewGood.Clear();
            TitleInput.Text = "아직 저장하지 않은 계획"; selectExercise("cycling-indoor"); WorkoutMinutes.Text = "30";
            ReviewGood.Text = "아직 저장하지 않은 회고";
            SelectRecordTab(false); SelectRecordTab(true); SelectRecordTab("review"); Theme_Click(this, new RoutedEventArgs());
            Check(TitleInput.Text == "아직 저장하지 않은 계획" && WorkoutMinutes.Text == "30" && ReviewGood.Text == "아직 저장하지 않은 회고", "all-three-tab-drafts-survive-tabs-and-theme");
            Close(); RestoreWindow();
            Check(ReviewGood.Text == "아직 저장하지 않은 회고" && Pending && TitleInput.Text == "아직 저장하지 않은 계획", "review-draft-survives-hide-and-restore");
            pendingChangeDecision = () => MessageBoxResult.Cancel;
            await SelectDateAsync(day.AddDays(1));
            Check(selected == day && ReviewGood.Text == "아직 저장하지 않은 회고" && WorkoutMinutes.Text == "30", "cancel-date-change-preserves-three-tab-drafts");
            CloseDetails_Click(this, new RoutedEventArgs());
            Check(Details.Visibility == Visibility.Visible && HasReviewChanges, "cancel-closing-sidebar-keeps-review-draft");
            pendingChangeDecision = () => MessageBoxResult.No;
            await SelectDateAsync(day.AddDays(1));
            Check(selected == day.AddDays(1) && !Pending && ReviewGood.Text == "" && TitleInput.Text == "" && WorkoutExercise.SelectedItem is null,
                "discard-pending-changes-restores-all-tabs-before-navigation");
            await SelectDateAsync(day);
            Check(repository.Load(day).Review == beforeInvalid.Review && ReviewGood.Text == "", "discarded-review-was-not-saved");
            pendingChangeDecision = null;
            ReviewGood.Text = "운동 기록을 남겼다."; ReviewDifficult.Text = "예정보다 늦어졌다."; ReviewNextAction.Text = "내일은 10분 걷기.";
            Check(await SaveAsync() && new PlanRepository(repository.DatabasePath).Load(day).Review == snapshot.Review, "review-survives-repository-reopen");
            var layouts = new List<object>();
            foreach (var dark in new[] { false, true })
                        {
                settings.Dark = dark; ApplyTheme(); DrawPlans(); DrawWorkouts(); DrawCalendar();
                ApplyWindowLayout(openDetails: true);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                ReviewComposer.IsExpanded = true;
                ReviewNextAction.BringIntoView();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var fieldTop = ReviewNextAction.TranslatePoint(new Point(), ReviewPane).Y;
                if (fieldTop < -1 || fieldTop + ReviewNextAction.ActualHeight > ReviewPane.ActualHeight + 1
                    || SaveButton.TranslatePoint(new Point(), Details).Y + SaveButton.ActualHeight > Details.ActualHeight + 1)
                    throw new InvalidOperationException("Review fields or save are inaccessible after resizing");
                layouts.Add(new { dark, width = Width, height = Height, lastFieldReachable = true, saveReachable = true });
            }
            Check(true, "review-fields-and-save-accessible-in-fixed-window-both-themes");
            ApplyWindowLayout(); settings.Dark = false; ApplyTheme(); DrawCalendar();
            ReviewPane.ScrollToTop(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("review.png");
            ReviewPane.ScrollToBottom(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("review-bottom.png");
            settings.Dark = true; ApplyTheme(); DrawCalendar(); ReviewPane.ScrollToTop();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("review-dark.png");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "review-layouts.json"), JsonSerializer.Serialize(layouts, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "review-reopen-expected.json"), JsonSerializer.Serialize(
                new ReviewReopenFixture(day, new("운동 기록을 남겼다.", "예정보다 늦어졌다.", "내일은 10분 걷기."))));
        }
        finally { pendingChangeDecision = null; reviewRemovalDecision = null; }
    }
    private sealed record ReviewReopenFixture(DateOnly Date, DayReview Review);
    private async Task VerifyReviewReopenAsync()
    {
        try
        {
            var folder = Path.GetDirectoryName(repository.DatabasePath)!;
            var expected = JsonSerializer.Deserialize<ReviewReopenFixture>(File.ReadAllText(Path.Combine(folder, "review-reopen-expected.json")))
                ?? throw new InvalidDataException("Review restart fixture missing");
            CloseMeditation(); await SelectDateAsync(expected.Date); SelectRecordTab("review");
            if (snapshot.Review != expected.Review || ReviewInput.Normalize() != expected.Review || HasReviewChanges
                || plans.Single().Title != "회고와 함께 유지할 계획" || workouts.Single().ExerciseId != "walking" || workouts.Single().DurationSeconds != 900)
                throw new InvalidOperationException("Review, plan or workout did not restore after process restart");
            var anniversaryPath = Path.Combine(folder, "anniversary-expected.json");
            if (File.Exists(anniversaryPath))
            {
                var expectedAnniversaries = JsonSerializer.Deserialize<Anniversary[]>(File.ReadAllText(anniversaryPath))!;
                if (!repository.LoadAnniversaries().SequenceEqual(expectedAnniversaries)) throw new InvalidOperationException("Anniversaries did not survive process restart");
                File.WriteAllText(Path.Combine(folder, "anniversary-reopen.json"), JsonSerializer.Serialize(new { success = true, records = expectedAnniversaries.Length, personalDataUsed = false }));
            }
            File.WriteAllText(Path.Combine(folder, "review-reopen.json"), JsonSerializer.Serialize(new {
                success = true, reviewRestoredInNewProcess = true, reviewFieldsRestored = true, plansAndWorkoutsPreserved = true, personalDataUsed = false }));
            await VerifyJournalReopenAsync(folder);
            await VerifyAttachmentReopenAsync(folder);
            await VerifyFixedCostReopenAsync(folder);
            await VerifyRecordToolsReopenAsync(folder);
            FinishVerification(null);
        }
        catch (Exception ex) { FinishVerification(ex); }
    }
}
