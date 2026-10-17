using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyEnergyFeaturesAsync(List<string> checks, Action<string> selectExercise)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        void Simple(string code)
        {
            EnergyEnabled.IsChecked = true; AdultBasis.IsChecked = true;
            EnergyPresetChoice.SelectedItem = EnergyCatalog.For(((Exercise)WorkoutExercise.SelectedItem).Id).Single(p => p.Code == code);
        }
        ClearEditor(); ClearWorkoutEditor();
        await SelectDateAsync(LocalToday.AddDays(-2)); SelectRecordTab(true);
        bodyWeight = await Task.Run(() => repository.SaveBodyWeight(70m, LocalToday)); await RefreshBodyWeightAsync();
        Check(WeightStatus.Text.Contains(bodyWeight.DueOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "weight-displays-next-required-date");
        selectExercise("walking"); WorkoutMinutes.Text = "30"; Simple("17190");
        Check(EnergyWeight.Text == "70" && EnergyPreview.Text.Contains("133 kcal"), "weight-auto-proposed-and-walking-preview-133");
        Check(await SaveAsync(), "save-calculated-walking");
        var walk = workouts.Single(); var preservedEnergy = walk.Energy;
        Check(preservedEnergy?.Kcal == 133m && preservedEnergy.SourceUrl == "https://pacompendium.com/walking/", "calculation-source-and-raw-value-persist");
        await Task.Run(() => repository.SaveBodyWeight(75m, LocalToday)); await RefreshBodyWeightAsync();
        BeginWorkoutEdit(walk.Id); WorkoutNote.Text = "기본 체중 변경 후 메모 수정";
        Check(EnergyWeight.Text == "70" && await SaveAsync() && workouts.Single().Energy == preservedEnergy, "weight-update-and-note-edit-preserve-historical-calculation");
        selectExercise("cycling-indoor"); WorkoutMinutes.Text = "30"; EnergyEnabled.IsChecked = true; AdultBasis.IsChecked = true;
        EnergyDetailed.IsChecked = true; EnergyDetailValue.Text = "50";
        Check(EnergyPreview.Text.Contains("150 kcal"), "indoor-detail-50w-preview");
        EnergyDetailValue.Text = "55";
        Check(ReadWorkoutDraft().Energy is null && EnergyPreview.Text.Contains("계산 기준"), "unsupported-detail-does-not-fall-back");
        EnergyDetailed.IsChecked = false; EnergyPresetChoice.SelectedItem = EnergyCatalog.For("cycling-indoor").Single(p => p.Code == "01200");
        Check(ReadWorkoutDraft().Energy?.Met == 6.8m && ReadWorkoutDraft().Calculation?.DetailValue is null, "simple-route-ignores-hidden-detail");
        EnergyDetailed.IsChecked = true; EnergyDetailValue.Text = "50";
        Check(await SaveAsync() && workouts.Single(w => w.ExerciseId == "cycling-indoor").Energy?.Met == 4m, "detail-route-and-result-save");
        selectExercise("futsal"); FutsalTwoHours_Click(this, new RoutedEventArgs()); Simple("15195");
        Check(ReadWorkoutDraft().Energy is null, "two-hours-does-not-auto-apply-futsal-pattern");
        var pattern = WorkoutEnergy.FutsalPattern(6);
        FutsalMinutes.Text = (pattern.ActiveSeconds / 60).ToString(CultureInfo.InvariantCulture); FutsalSeconds.Text = "0";
        Check(ReadWorkoutDraft().Energy?.Kcal == 877.5m, "confirmed-futsal-90-active-at-75kg");
        FutsalMinutes.Text = "75";
        Check(ReadWorkoutDraft().Energy?.Kcal == 731.25m && await SaveAsync(), "manual-futsal-active-time-overrides-suggestion");
        selectExercise("stairs"); WorkoutQuantity.Text = "20"; Simple("17131");
        Check(ReadWorkoutDraft().Energy is null && await SaveAsync(), "quantity-only-record-still-saves-without-kcal");
        await Task.Run(() => repository.SaveBodyWeight(75m, LocalToday.AddDays(-14))); await RefreshBodyWeightAsync();
        Check(WeightStatus.Text.Contains("다시 입력"), "expired-weight-remains-visible");
        BeginWorkoutEdit(walk.Id); WorkoutNote.Text = "만료 뒤 메모 수정";
        Check(await SaveAsync() && workouts.Single(w => w.Id == walk.Id).Energy == preservedEnergy, "expired-weight-keeps-historical-snapshot");
        selectExercise("badminton"); WorkoutMinutes.Text = "30"; Simple("15030");
        Check(ReadWorkoutDraft().Energy is null && EnergyPreview.Text.Contains("다시 입력") && await SaveAsync(), "expired-weight-allows-general-save-but-not-new-kcal");
        var uncalculated = workouts.Single(w => w.ExerciseId == "badminton").Id;
        await Task.Run(() => repository.SaveBodyWeight(75m, LocalToday)); await RefreshBodyWeightAsync();
        Check(repository.Load(selected).Workouts.Single(w => w.Id == uncalculated).Energy is null, "weight-renewal-does-not-recalculate-old-records");
        selectExercise("tennis"); WorkoutMinutes.Text = "30"; Simple("15675");
        var previousRevision = snapshot.Revision;
        EnergyWeight.Text = "75.25";
        Check(!await SaveAsync() && EnergyWeight.Text == "75.25" && repository.Load(selected).Revision == previousRevision, "invalid-record-weight-preserves-input-and-day");
        EnergyWeight.Text = "75";
        SelectRecordTab(false); SelectRecordTab(true); Theme_Click(this, new RoutedEventArgs());
        Check(EnergyEnabled.IsChecked == true && EnergyWeight.Text == "75" && ReadWorkoutDraft().Energy?.Kcal == 255m, "energy-draft-survives-tabs-and-theme");
        settings.Dark = false; ApplyTheme(); DrawCalendar(); DrawWorkouts(); EnergyPresetChoice.BringIntoView();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("energy-preview.png");
        settings.Dark = true; ApplyTheme(); DrawCalendar(); DrawWorkouts();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("energy-preview-dark.png");
        var content = (FrameworkElement)WorkoutPane.Content;
        WorkoutPane.ScrollToVerticalOffset(Math.Max(0, EnergyPreview.TranslatePoint(new Point(), content).Y - 12));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("energy-result-dark.png");
        settings.Dark = false; ApplyTheme(); ClearWorkoutEditor();
        Check(new PlanRepository(repository.DatabasePath).Load(selected).Workouts.Any(w => w.Energy == preservedEnergy), "saved-energy-survives-repository-reopen");
        var dialog = CreateWeightDialog(this);
        var form = (StackPanel)dialog.Content;
        var weightInput = form.Children.OfType<TextBox>().Single();
        var saveWeight = form.Children.OfType<Button>().Single(b => (string)b.Content == "체중 저장");
        var weightId = bodyWeight!.Id;
        weightInput.Text = "75.25"; saveWeight.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(repository.LoadBodyWeight()!.Id == weightId && weightInput.Text == "75.25", "weight-form-rejects-extra-decimal-without-clearing-input");
        weightInput.Text = "75"; saveWeight.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await ((Task)dialog.Tag).WaitAsync(TimeSpan.FromSeconds(10));
        if (!saveWeight.IsEnabled || repository.LoadBodyWeight()!.Id <= weightId || !form.Children.OfType<TextBlock>().First().Text.Contains("저장 완료"))
            throw new InvalidOperationException($"Weight form completion: enabled={saveWeight.IsEnabled}, before={weightId}, after={repository.LoadBodyWeight()!.Id}, status={form.Children.OfType<TextBlock>().First().Text}");
        Check(saveWeight.IsEnabled && repository.LoadBodyWeight()!.Id > weightId && form.Children.OfType<TextBlock>().First().Text.Contains("저장 완료"), "weight-form-save-button-persists-and-confirms");
        dialog.Close();
    }
}
