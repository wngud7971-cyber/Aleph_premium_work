using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private BodyWeight? bodyWeight;
    private string? energyExercise;
    private string energyReason = "";
    private bool refreshingEnergy;
    private int weightLoadGeneration;
    private static DateOnly LocalToday => DateOnly.FromDateTime(DateTime.Today);
    private bool HasEnergyValues => EnergyEnabled?.IsChecked == true || !string.IsNullOrWhiteSpace(FutsalMinutes?.Text) || !string.IsNullOrWhiteSpace(FutsalSeconds?.Text);
    private async Task RefreshBodyWeightAsync()
    {
        var generation = ++weightLoadGeneration;
        try { var loaded = await Task.Run(repository.LoadBodyWeight); if (generation != weightLoadGeneration) return; bodyWeight = loaded; WeightStatus.Text = DescribeWeight(); RefreshEnergyPreview(); }
        catch (Exception) { WeightStatus.Text = "체중을 읽지 못했어요. 기록은 유지돼요. 다시 입력에서 확인해 주세요."; }
    }
    private string DescribeWeight() => bodyWeight is null ? "기본 체중 미등록 · 운동 기록은 저장할 수 있어요."
        : $"기본 체중 {bodyWeight.Kg} kg · 입력 {bodyWeight.EnteredOn:yyyy-MM-dd}\n" + (bodyWeight.IsCurrent(LocalToday)
            ? $"다음 필수 입력 {bodyWeight.DueOn:yyyy-MM-dd}" : "체중을 다시 입력해 주세요. 입력일·현재 날짜도 확인하세요.");

    private void Weight_Click(object sender, RoutedEventArgs e) => ShowWeightDialog(this);
    private void ShowWeightDialog(Window owner) => CreateWeightDialog(owner).ShowDialog();
    private Window CreateWeightDialog(Window owner)
    {
        var dialog = new Window { Owner = owner, Title = "현재 체중", Width = 400, Height = 330, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        foreach (var key in new[] { "Paper", "Ink", "Muted", "Soft", "Accent", "AccentInk" }) dialog.Resources[key] = FindResource(key);
        var panel = new StackPanel { Margin = new Thickness(22) };
        var status = new TextBlock { Text = DescribeWeight(), TextWrapping = TextWrapping.Wrap };
        var input = new TextBox { MaxLength = 10 };
        var pending = false; input.TextChanged += (_, _) => pending = !string.IsNullOrWhiteSpace(input.Text);
        var save = new Button { Content = "체중 저장", Background = ResourceBrush("Accent"), Foreground = ResourceBrush("AccentInk") };
        async Task SaveWeightAsync()
        {
            try
            {
                var kg = ReadDecimal(input.Text, "체중") ?? throw new ArgumentException("현재 체중을 입력해 주세요.");
                save.IsEnabled = false; input.IsEnabled = false;
                bodyWeight = await Task.Run(() => repository.SaveBodyWeight(kg, LocalToday));
                weightLoadGeneration++;
                pending = false; status.Text = "저장 완료\n" + DescribeWeight(); WeightStatus.Text = DescribeWeight();
                if (EnergyEnabled.IsChecked == true && string.IsNullOrWhiteSpace(EnergyWeight.Text)) EnergyWeight.Text = kg.ToString(CultureInfo.InvariantCulture);
                RefreshEnergyPreview();
            }
            catch (Exception ex) { status.Text = "저장하지 못했어요. " + ex.Message; }
            finally { save.IsEnabled = true; input.IsEnabled = true; }
        }
        save.Click += (_, _) => dialog.Tag = SaveWeightAsync();
        var close = new Button { Content = "닫기", HorizontalAlignment = HorizontalAlignment.Right }; close.Click += (_, _) => dialog.Close();
        dialog.Closing += (_, args) =>
        {
            if (!save.IsEnabled) { args.Cancel = true; return; }
            if (pending && System.Windows.MessageBox.Show(dialog, "저장하지 않은 체중 입력을 버릴까요?", "입력 확인", MessageBoxButton.YesNo) != MessageBoxResult.Yes) args.Cancel = true;
        };
        panel.Children.Add(status); panel.Children.Add(new TextBlock { Text = "현재 체중 · kg (소수 첫째 자리까지)", Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(input); panel.Children.Add(save); panel.Children.Add(close); dialog.Content = panel; return dialog;
    }
    private static decimal? ReadDecimal(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0)
            throw new ArgumentException(label + "은 0보다 큰 숫자로 입력해 주세요.");
        return value;
    }
    private void ConfigureEnergyExercise(Exercise? exercise)
    {
        if (EnergyFields is null) return;
        if (energyExercise != exercise?.Id)
        {
            energyExercise = exercise?.Id;
            EnergyPresetChoice.ItemsSource = exercise is null ? Array.Empty<EnergyPreset>() : EnergyCatalog.For(exercise.Id).ToArray();
            EnergyPresetChoice.SelectedIndex = -1;
        }
        EnergyDetailed.Visibility = exercise is not null && EnergyCatalog.HasDetail(exercise.Id) ? Visibility.Visible : Visibility.Collapsed;
        EnergyDetailLabel.Text = exercise?.Id == "cycling-indoor" ? "장비 평균 출력 · W (저항 단계는 입력하지 않아요)" : "평지·짐 없는 평균 속도 · km/h";
        RefreshEnergyPreview();
    }
    private void ResetEnergyEditor()
    {
        if (EnergyEnabled is null) return;
        var previous = refreshingEnergy; refreshingEnergy = true;
        try
        {
            EnergyEnabled.IsChecked = false; AdultBasis.IsChecked = false; EnergyDetailed.IsChecked = false;
            EnergyWeight.Clear(); EnergyDetailValue.Clear(); EnergyPresetChoice.SelectedIndex = -1;
            FutsalMinutes.Clear(); FutsalSeconds.Clear(); FutsalCycles.Text = "6"; energyReason = "";
            EnergyFields.Visibility = Visibility.Collapsed; EnergyDetailFields.Visibility = Visibility.Collapsed; EnergyPresetChoice.Visibility = Visibility.Visible;
        }
        finally { refreshingEnergy = previous; }
    }
    private void LoadEnergyEditor(WorkoutEntry entry)
    {
        refreshingEnergy = true;
        try
        {
            FutsalMinutes.Text = entry.ActiveSeconds is int active ? (active / 60).ToString(CultureInfo.InvariantCulture) : "";
            FutsalSeconds.Text = entry.ActiveSeconds is int seconds ? (seconds % 60).ToString(CultureInfo.InvariantCulture) : "";
            EnergyEnabled.IsChecked = entry.Calculation is not null;
            AdultBasis.IsChecked = entry.Calculation?.AdultConfirmed == true;
            EnergyWeight.Text = entry.Calculation?.WeightKg?.ToString(CultureInfo.InvariantCulture) ?? "";
            EnergyDetailed.IsChecked = entry.Calculation?.Mode == "detail";
            EnergyDetailValue.Text = entry.Calculation?.DetailValue?.ToString(CultureInfo.InvariantCulture) ?? "";
            EnergyPresetChoice.SelectedIndex = -1;
            if (entry.Calculation?.PresetCode is string code)
            {
                var presets = EnergyCatalog.For(entry.ExerciseId).ToList();
                if (presets.All(p => p.Code != code) && entry.Energy is { } historical)
                    presets.Add(new(code, historical.Label + " · 저장 당시 기준", historical.Met, "", [entry.ExerciseId]));
                EnergyPresetChoice.ItemsSource = presets; EnergyPresetChoice.SelectedItem = presets.FirstOrDefault(p => p.Code == code);
            }
        }
        finally { refreshingEnergy = false; RefreshEnergyPreview(); }
    }
    private void EnergyText_Changed(object sender, TextChangedEventArgs e) { if (IsInitialized) { UpdateDraftStatus(); RefreshEnergyPreview(); } }
    private void EnergyPreset_Changed(object sender, SelectionChangedEventArgs e) { UpdateDraftStatus(); RefreshEnergyPreview(); }
    private void EnergyOption_Changed(object sender, RoutedEventArgs e)
    {
        if (EnergyFields is null || refreshingEnergy || changingExercise || suppress) return;
        if (EnergyEnabled.IsChecked == true && string.IsNullOrWhiteSpace(EnergyWeight.Text) && bodyWeight?.IsCurrent(LocalToday) == true)
            EnergyWeight.Text = bodyWeight.Kg.ToString(CultureInfo.InvariantCulture);
        UpdateDraftStatus(); RefreshEnergyPreview();
    }
    private void UseCurrentWeight_Click(object sender, RoutedEventArgs e)
    {
        if (bodyWeight?.IsCurrent(LocalToday) == true) EnergyWeight.Text = bodyWeight.Kg.ToString(CultureInfo.InvariantCulture);
        else { EnergyPreview.Text = "기본 체중을 등록하거나 다시 입력해 주세요."; }
    }
    private WorkoutEntry AttachEnergy(WorkoutEntry entry)
    {
        if (entry.ExerciseId == "futsal" && (!string.IsNullOrWhiteSpace(FutsalMinutes.Text) || !string.IsNullOrWhiteSpace(FutsalSeconds.Text)))
            entry = entry with { ActiveSeconds = checked(ParseTimePart(FutsalMinutes.Text, 1440, "경기 분") * 60 + ParseTimePart(FutsalSeconds.Text, 59, "경기 초")) };
        energyReason = "계산을 선택하지 않았어요. 운동 기록만 저장해요.";
        if (EnergyEnabled.IsChecked != true) return entry;
        var detail = EnergyDetailed.IsChecked == true;
        var input = new EnergyInput(detail ? "detail" : "simple", detail ? null : (EnergyPresetChoice.SelectedItem as EnergyPreset)?.Code,
            detail ? ReadDecimal(EnergyDetailValue.Text, "속도·출력") : null, ReadDecimal(EnergyWeight.Text, "적용 체중"), AdultBasis.IsChecked == true);
        entry = entry with { Calculation = input };
        var previous = workoutEditing is Guid id ? workouts.FirstOrDefault(w => w.Id == id) : null;
        if (previous?.Energy is not null && previous.Calculation == input && previous.ExerciseId == entry.ExerciseId && previous.DurationSeconds == entry.DurationSeconds && previous.ActiveSeconds == entry.ActiveSeconds)
        { energyReason = "저장 당시 계산값을 유지해요."; return entry with { Energy = previous.Energy }; }
        var result = WorkoutEnergy.Calculate(entry, input, bodyWeight, LocalToday); energyReason = result.Reason;
        return entry with { Energy = result.Snapshot };
    }
    private static string EnergyDescription(EnergySnapshot energy) => $"{energy.Label} · MET {energy.Met}\n체중 {energy.WeightKg} kg · 계산 시간 {FormatDuration(energy.Seconds)}\n기본 체중 입력일 {energy.WeightEnteredOn:yyyy-MM-dd}\n2024 성인 기준 · 휴식 대사 포함\n{energy.SourceUrl}";
    private void RefreshEnergyPreview()
    {
        if (!IsInitialized || EnergyFields is null || refreshingEnergy || changingExercise || suppress) return;
        refreshingEnergy = true;
        try
        {
            WeightStatus.Text = DescribeWeight();
            EnergyFields.Visibility = EnergyEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            EnergyDetailFields.Visibility = EnergyDetailed.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            EnergyPresetChoice.Visibility = EnergyDetailed.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
            EnergyBasis.Text = "";
            if (WorkoutExercise.SelectedItem is not Exercise) { EnergyPreview.Text = "종목을 선택해 주세요."; return; }
            var entry = ReadWorkoutDraft();
            if (entry.Energy is { } energy)
            { EnergyPreview.Text = $"예상 소모 약 {EnergySnapshot.Display(energy.Kcal)} kcal\n" + (workoutEditing is not null ? energyReason : ""); EnergyBasis.Text = EnergyDescription(energy); }
            else EnergyPreview.Text = energyReason;
        }
        catch (ArgumentException ex) { EnergyPreview.Text = ex.Message; }
        finally { refreshingEnergy = false; }
    }
    private void FutsalTwoHours_Click(object sender, RoutedEventArgs e) { WorkoutMinutes.Text = "120"; WorkoutSeconds.Text = "0"; }
    private void FutsalPattern_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cycles = ReadPositiveInteger(FutsalCycles.Text, "주기 수") ?? throw new ArgumentException("완료한 주기 수를 입력해 주세요.");
            var suggestion = WorkoutEnergy.FutsalPattern(cycles);
            if (System.Windows.MessageBox.Show(this, $"{cycles}회 완료 기준: 총 {suggestion.TotalSeconds / 60}분, 경기 {suggestion.ActiveSeconds / 60}분을 적용할까요?\n실제 교체·추가 휴식이 있었다면 경기 시간을 수정하세요.", "15+5 시간 제안", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            WorkoutMinutes.Text = (suggestion.TotalSeconds / 60).ToString(CultureInfo.InvariantCulture); WorkoutSeconds.Text = "0";
            FutsalMinutes.Text = (suggestion.ActiveSeconds / 60).ToString(CultureInfo.InvariantCulture); FutsalSeconds.Text = "0";
        }
        catch (ArgumentException ex) { EnergyPreview.Text = ex.Message; }
    }
}
