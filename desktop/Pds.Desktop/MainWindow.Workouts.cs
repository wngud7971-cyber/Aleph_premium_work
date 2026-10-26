using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private List<WorkoutEntry> workouts = [];
    private Guid? workoutEditing;
    private Exercise? previousExercise;
    private string? previousCategory;
    private bool changingExercise;
    private bool HasWorkoutValues => !string.IsNullOrWhiteSpace(WorkoutQuantity.Text) || !string.IsNullOrWhiteSpace(WorkoutMinutes.Text)
        || !string.IsNullOrWhiteSpace(WorkoutSeconds.Text) || !string.IsNullOrWhiteSpace(WorkoutNote.Text) || HasEnergyValues;
    private bool HasWorkoutInput => WorkoutExercise.SelectedItem is Exercise || HasWorkoutValues || workoutEditing is not null;

    private void InitializeWorkouts()
    {
        changingExercise = true;
        WorkoutCategory.ItemsSource = ExerciseCatalog.Categories; WorkoutCategory.SelectedIndex = 0;
        previousCategory = ExerciseCatalog.Categories[0];
        WorkoutExercise.ItemsSource = ExerciseCatalog.All.Where(e => e.Category == previousCategory).ToArray();
        changingExercise = false;
        UpdateWorkoutFields();
    }
    private void PlanTab_Click(object sender, RoutedEventArgs e) => SelectRecordTab(false);
    private void WorkoutTab_Click(object sender, RoutedEventArgs e) => SelectRecordTab(true);
    private void SelectRecordTab(bool workout) => SelectRecordTab(workout ? "workout" : "plan");
    private void SelectRecordTab(string tab)
    {
        var nextPane = tab switch { "plan" => PlanPane, "workout" => WorkoutPane, "money" => MoneyPane, "study" => StudyPane, _ => ReviewPane };
        var changingTab = nextPane.Visibility != Visibility.Visible;
        PlanPane.Visibility = tab == "plan" ? Visibility.Visible : Visibility.Collapsed;
        WorkoutPane.Visibility = tab == "workout" ? Visibility.Visible : Visibility.Collapsed;
        ReviewPane.Visibility = tab == "review" ? Visibility.Visible : Visibility.Collapsed;
        MoneyPane.Visibility = tab == "money" ? Visibility.Visible : Visibility.Collapsed;
        StudyPane.Visibility = tab == "study" ? Visibility.Visible : Visibility.Collapsed;
        PlanTab.SetResourceReference(BackgroundProperty, tab == "plan" ? "Note" : "Soft");
        WorkoutTab.SetResourceReference(BackgroundProperty, tab == "workout" ? "Note" : "Soft");
        ReviewTab.SetResourceReference(BackgroundProperty, tab == "review" ? "Note" : "Soft");
        MoneyTab.SetResourceReference(BackgroundProperty, tab == "money" ? "Note" : "Soft");
        StudyTab.SetResourceReference(BackgroundProperty, tab == "study" ? "Note" : "Soft");
        SaveButton.ToolTip = "계획·운동·회고·가계부·학습 탭의 변경을 함께 저장해요.";
        if (changingTab) CalmMotion.Reveal(nextPane, CalmMotion.TabMs);
    }
    private bool ConfirmExerciseChange() => !HasWorkoutValues || System.Windows.MessageBox.Show(this,
        "종목을 바꾸면 현재 운동의 시간·횟수·메모 입력을 비워요. 바꿀까요?", "종목 변경", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void WorkoutCategory_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (changingExercise || WorkoutExercise is null) return;
        changingExercise = true;
        try
        {
            if (!ConfirmExerciseChange()) { WorkoutCategory.SelectedItem = previousCategory; return; }
            previousCategory = WorkoutCategory.SelectedItem as string;
            ClearWorkoutValues(); previousExercise = null; workoutEditing = null;
            WorkoutExercise.ItemsSource = ExerciseCatalog.All.Where(x => x.Category == previousCategory).ToArray();
            WorkoutExercise.SelectedIndex = -1; WorkoutEditorTitle.Text = "새 운동";
        }
        finally { changingExercise = false; UpdateWorkoutFields(); UpdateDraftStatus(); }
    }
    private void WorkoutExercise_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (changingExercise || WorkoutQuantity is null) return;
        changingExercise = true;
        try
        {
            if (!ConfirmExerciseChange()) { WorkoutExercise.SelectedItem = previousExercise; return; }
            ClearWorkoutValues(); previousExercise = WorkoutExercise.SelectedItem as Exercise; workoutEditing = null; WorkoutEditorTitle.Text = "새 운동";
        }
        finally { changingExercise = false; UpdateWorkoutFields(); UpdateDraftStatus(); }
        if (ready && WorkoutExercise.SelectedItem is not null) WorkoutComposer.IsExpanded = true;
    }
    private void UpdateWorkoutFields()
    {
        var exercise = WorkoutExercise.SelectedItem as Exercise;
        var kind = exercise?.Input ?? WorkoutInputKind.Time;
        WorkoutQuantityFields.Visibility = kind == WorkoutInputKind.Time ? Visibility.Collapsed : Visibility.Visible;
        WorkoutQuantityLabel.Text = kind == WorkoutInputKind.Floors ? "이동 층수 · 선택한 이동 구간" : "횟수 · 시간만 기록할 때 생략 가능";
        WorkoutTimeLabel.Text = exercise?.Id == "futsal" ? "총 참여 시간" : kind == WorkoutInputKind.Time ? "실제 운동·활동 시간" : "실제 수행 시간 · 선택";
        WorkoutInputHint.Text = exercise?.Id switch
        {
            "futsal" => "총 참여 시간과 실제 경기 시간을 구분해요. kcal에는 확인한 경기 시간만 적용해요.",
            "lunges" => "런지 횟수는 양쪽을 합한 수로 입력해요.",
            "plank" => "실제 유지 시간을 분·초로 입력해요.",
            "stairs" => "오른 층수를 기록해요. 층수만으로 시간이나 kcal을 만들지 않아요.",
            _ => "직접 수행한 양을 입력해요. 초는 0~59로 입력할 수 있어요."
        };
        FutsalFields.Visibility = exercise?.Id == "futsal" ? Visibility.Visible : Visibility.Collapsed;
        ConfigureEnergyExercise(exercise);
    }
    private void ClearWorkoutValues() { WorkoutQuantity.Clear(); WorkoutMinutes.Clear(); WorkoutSeconds.Clear(); WorkoutNote.Clear(); ResetEnergyEditor(); }
    private void ClearWorkoutEditor()
    {
        changingExercise = true; var wasSuppressed = suppress; suppress = true;
        try { ClearWorkoutValues(); WorkoutExercise.SelectedIndex = -1; previousExercise = null; workoutEditing = null; WorkoutEditorTitle.Text = "새 운동"; }
        finally { changingExercise = false; suppress = wasSuppressed; UpdateWorkoutFields(); }
        if (WorkoutComposer is not null) WorkoutComposer.IsExpanded = false;
    }
    private void CancelWorkoutEdit_Click(object sender, RoutedEventArgs e)
    {
        if (HasWorkoutInput && System.Windows.MessageBox.Show(this, "현재 운동 입력을 취소할까요? 저장한 기록은 유지돼요.", "입력 취소", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        ClearWorkoutEditor(); UpdateDraftStatus();
    }
    private static int? ReadPositiveInteger(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
            throw new ArgumentException(label + "는 1 이상의 정수로 입력해 주세요.");
        return value;
    }
    private List<WorkoutEntry> WorkoutCandidates()
    {
        var candidate = workouts.ToList();
        if (!HasWorkoutInput) return candidate;
        var entry = ReadWorkoutDraft();
        if (workoutEditing is Guid editId) candidate[candidate.FindIndex(w => w.Id == editId)] = entry; else candidate.Add(entry);
        return candidate;
    }
    private WorkoutEntry ReadWorkoutDraft()
    {
        var exercise = WorkoutExercise.SelectedItem as Exercise ?? throw new ArgumentException("운동 종목을 선택해 주세요.");
        int? seconds = null;
        if (!string.IsNullOrWhiteSpace(WorkoutMinutes.Text) || !string.IsNullOrWhiteSpace(WorkoutSeconds.Text))
        {
            var minutes = ParseTimePart(WorkoutMinutes.Text, 1440, "분");
            var part = ParseTimePart(WorkoutSeconds.Text, 59, "초");
            seconds = checked(minutes * 60 + part);
        }
        var entry = WorkoutEntry.Create(exercise.Id, seconds, exercise.Input == WorkoutInputKind.Time ? null : ReadPositiveInteger(WorkoutQuantity.Text, "횟수·층수"), WorkoutNote.Text);
        if (workoutEditing is Guid id) entry = entry with { Id = id };
        entry = AttachEnergy(entry);
        entry.Validate();
        return entry;
    }
    private static int ParseTimePart(string text, int maximum, string unit)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 || value > maximum)
            throw new ArgumentException($"{unit} 입력은 0~{maximum} 사이의 정수여야 해요.");
        return value;
    }
    private static string FormatDuration(long seconds) => seconds % 60 == 0 ? $"{seconds / 60}분" : $"{seconds / 60}분 {seconds % 60}초";
    private static string WorkoutTimeSummary(DayCount count) => $"운동 {count.Workouts}건 · " + (count.TimedWorkouts == 0 ? "시간 미입력" : FormatDuration(count.WorkoutSeconds)
        + (count.TimedWorkouts < count.Workouts ? $" · 시간 {count.TimedWorkouts}/{count.Workouts}건" : ""));
    private void BeginWorkoutEdit(Guid id)
    {
        if (HasWorkoutInput && System.Windows.MessageBox.Show(this, "현재 운동 입력을 버리고 이 기록을 수정할까요?", "입력 확인", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var entry = workouts.Single(w => w.Id == id); var exercise = ExerciseCatalog.Get(entry.ExerciseId);
        changingExercise = true; suppress = true;
        try
        {
            WorkoutCategory.SelectedItem = previousCategory = exercise.Category;
            WorkoutExercise.ItemsSource = ExerciseCatalog.All.Where(x => x.Category == exercise.Category).ToArray();
            WorkoutExercise.SelectedItem = previousExercise = exercise; workoutEditing = id;
            WorkoutQuantity.Text = entry.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "";
            WorkoutMinutes.Text = entry.DurationSeconds is int seconds ? (seconds / 60).ToString(CultureInfo.InvariantCulture) : "";
            WorkoutSeconds.Text = entry.DurationSeconds is int duration ? (duration % 60).ToString(CultureInfo.InvariantCulture) : "";
            WorkoutNote.Text = entry.Note; WorkoutEditorTitle.Text = "운동 수정";
            ConfigureEnergyExercise(exercise); LoadEnergyEditor(entry);
        }
        finally { changingExercise = false; suppress = false; UpdateWorkoutFields(); UpdateDraftStatus(); }
        OpenComposer(WorkoutComposer, WorkoutExercise);
    }
    private void DrawWorkouts()
    {
        WorkoutList.Children.Clear(); EmptyWorkoutMessage.Visibility = workouts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var calculated = workouts.Where(w => w.Energy is not null).ToArray();
        WorkoutCount.Text = $"{workouts.Count}건" + (calculated.Length > 0 ? $" · 약 {EnergySnapshot.Display(calculated.Sum(w => w.Energy!.Kcal))} kcal\n계산 {calculated.Length}/{workouts.Count}건" : " · 계산된 기록 없음");
        foreach (var entry in workouts)
        {
            var exercise = ExerciseCatalog.Get(entry.ExerciseId);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = exercise.Name, Foreground = ResourceBrush("Ink") });
            var parts = new List<string>();
            if (entry.Quantity is int count) parts.Add($"{count}{(exercise.Input == WorkoutInputKind.Floors ? "층" : "회")}");
            if (entry.DurationSeconds is int seconds) parts.Add(FormatDuration(seconds));
            if (entry.ActiveSeconds is int active) parts.Add("경기 " + FormatDuration(active));
            panel.Children.Add(new TextBlock { Text = string.Join(" · ", parts), Foreground = ResourceBrush("Muted"), Margin = new Thickness(0, 4, 0, 0) });
            var energyText = entry.Energy is { } energy ? $"예상 소모 약 {EnergySnapshot.Display(energy.Kcal)} kcal" : "예상 kcal 미계산";
            panel.Children.Add(new TextBlock { Text = energyText, Foreground = ResourceBrush("Ink"), Margin = new Thickness(0, 4, 0, 0), ToolTip = entry.Energy is { } basis ? EnergyDescription(basis) : "체중·수행 시간·활동 기준을 갖추고 계산을 선택하면 표시해요." });
            if (!string.IsNullOrWhiteSpace(entry.Note)) panel.Children.Add(new TextBlock { Text = entry.Note, TextWrapping = TextWrapping.Wrap, Foreground = ResourceBrush("Muted"), Margin = new Thickness(0, 4, 0, 0) });
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var edit = new Button { Content = "수정", Padding = new Thickness(6, 3, 6, 3), Background = Brushes.Transparent };
            edit.Click += (_, _) => BeginWorkoutEdit(entry.Id);
            var delete = new Button { Content = "삭제", Padding = new Thickness(6, 3, 6, 3), Background = Brushes.Transparent };
            delete.Click += (_, _) =>
            {
                if (System.Windows.MessageBox.Show(this, "이 운동을 삭제할까요? 저장 버튼을 누르면 반영돼요.", "운동 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                workouts.RemoveAll(w => w.Id == entry.Id); if (workoutEditing == entry.Id) ClearWorkoutEditor(); DrawWorkouts(); MarkDirty();
            };
            actions.Children.Add(edit); actions.Children.Add(delete); panel.Children.Add(actions);
            WorkoutList.Children.Add(new Border { Background = ResourceBrush("Paper"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 7), Margin = new Thickness(0, 0, 0, 8), Child = panel });
        }
    }
}
