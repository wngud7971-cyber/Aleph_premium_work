using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private bool statisticsOpen, statisticsHadDetails;
    private string statisticsMode = "week";
    private DateOnly statisticsAnchor;
    private DateRange? statisticsCustom;
    private WorkoutFilter statisticsFilter = new();
    private StatisticsReport? statisticsReport, statisticsComparison;
    private string statisticsMetric = "운동 시간";

    private async void Statistics_Click(object sender, RoutedEventArgs e) => await OpenStatisticsAsync();
    private async Task OpenStatisticsAsync()
    {
        if (busy || !await ConfirmPendingAsync()) return;
        if (statisticsOpen) return;
        if (statisticsReport is null) statisticsAnchor = selected;
        statisticsHadDetails = Details.Visibility == Visibility.Visible;
        statisticsOpen = true; CalendarArea.Visibility = Details.Visibility = Visibility.Collapsed; DetailColumn.Width = new GridLength(0);
        StatisticsPane.Visibility = Visibility.Visible; CloseMeditation(); await RefreshStatisticsAsync();
    }
    private void CloseStatistics()
    {
        statisticsOpen = false; StatisticsPane.Visibility = Visibility.Collapsed; CalendarArea.Visibility = Visibility.Visible;
        Details.Visibility = statisticsHadDetails ? Visibility.Visible : Visibility.Collapsed;
        DetailColumn.Width = new GridLength(Details.Visibility == Visibility.Visible ? 330 : 0); DrawCalendar();
    }
    private async Task RefreshStatisticsAsync()
    {
        if (!statisticsOpen || busy) return;
        busy = true; Body.IsEnabled = false;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var range = statisticsMode switch { "month" => DateRange.Month(statisticsAnchor), "weekday" => statisticsCustom ?? new(today.AddDays(-27), today), _ => DateRange.Week(statisticsAnchor) };
            range.Validate();
            if (statisticsMode == "weekday" && range.To > today) range = range with { To = today };
            if (range.From > range.To) throw new ArgumentException("오늘까지의 날짜를 선택해 주세요.");
            var compare = statisticsMode == "weekday" ? null : range.PreviousComparable(today, statisticsMode == "month");
            var current = compare is not null && range.Contains(today) ? range with { To = today } : range;
            var filter = statisticsFilter;
            var result = await Task.Run(() => (Report: repository.Statistics(range, filter), Compare: compare is null ? null : repository.Statistics(compare, filter), Progress: repository.Statistics(current, filter)));
            statisticsReport = result.Report; statisticsComparison = result.Compare; statisticsProgress = result.Progress;
            DrawStatistics(); Status.Text = "저장된 기록으로 통계를 표시해요.";
        }
        catch (Exception ex) { HandleError("통계 조회", ex); }
        finally { busy = false; Body.IsEnabled = true; }
    }
    private StatisticsReport? statisticsProgress;
    private static string StatsTime(long seconds) => $"{seconds / 3600}시간 {seconds % 3600 / 60}분 {seconds % 60}초";
    private static string StatsKcal(WorkoutStatistics value) => value.Kcal is decimal kcal ? $"약 {Math.Round(kcal, 0, MidpointRounding.AwayFromZero):N0} kcal ({value.Calculated}/{value.Records}건)" : "계산된 기록 없음";
    private TextBlock StatsText(string text, bool muted = false, double size = 12)
        => new() { Text = text, FontSize = UiTypography.Grow(size), Foreground = ResourceBrush(muted ? "Muted" : "Ink"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5) };
    private void DrawStatistics()
    {
        if (!statisticsOpen || statisticsReport is not { } report) return;
        StatisticsContent.Children.Clear();
        var navigation = new WrapPanel();
        void Button(WrapPanel panel, string title, Func<Task> action, bool chosen = false)
        {
            var button = new Button { Content = title, Padding = new Thickness(12, 6, 12, 6), Background = ResourceBrush(chosen ? "Note" : "Soft") };
            button.Click += async (_, _) => { if (busy) return; try { await action(); } catch (Exception ex) { HandleError("통계 이동", ex); } }; panel.Children.Add(button);
        }
        foreach (var (key, label) in new[] { ("week", "주간"), ("month", "월간"), ("weekday", "요일별") })
            Button(navigation, label, async () => { statisticsMode = key; statisticsCustom = null; await RefreshStatisticsAsync(); }, statisticsMode == key);
        StatisticsContent.Children.Add(navigation);
        var controls = new WrapPanel();
        if (statisticsMode != "weekday")
        {
            Button(controls, "‹ 이전", async () => { statisticsAnchor = statisticsMode == "month" ? statisticsAnchor.AddMonths(-1) : statisticsAnchor.AddDays(-7); await RefreshStatisticsAsync(); });
            Button(controls, statisticsMode == "month" ? "이번 달" : "이번 주", async () => { statisticsAnchor = DateOnly.FromDateTime(DateTime.Today); await RefreshStatisticsAsync(); });
            Button(controls, "다음 ›", async () => { statisticsAnchor = statisticsMode == "month" ? statisticsAnchor.AddMonths(1) : statisticsAnchor.AddDays(7); await RefreshStatisticsAsync(); });
        }
        else
        {
            Button(controls, "최근 28일", async () => { statisticsCustom = null; await RefreshStatisticsAsync(); });
            Button(controls, "이번 달", async () => { statisticsCustom = DateRange.Month(DateOnly.FromDateTime(DateTime.Today)); await RefreshStatisticsAsync(); });
            var from = new DatePicker { SelectedDate = report.Range.From.ToDateTime(TimeOnly.MinValue), Width = 128, Margin = new Thickness(6, 4, 0, 4) };
            var to = new DatePicker { SelectedDate = report.Range.To.ToDateTime(TimeOnly.MinValue), Width = 128, Margin = new Thickness(6, 4, 0, 4), DisplayDateEnd = DateTime.Today };
            controls.Children.Add(from); controls.Children.Add(to);
            Button(controls, "기간 적용", async () =>
            {
                if (from.SelectedDate is not DateTime start || to.SelectedDate is not DateTime end) { Status.Text = "시작일·종료일을 선택해 주세요."; return; }
                var range = new DateRange(DateOnly.FromDateTime(start), DateOnly.FromDateTime(end));
                if (range.From > range.To || range.From > DateOnly.FromDateTime(DateTime.Today) || range.Days > 36600) { Status.Text = "조회 기간을 확인해 주세요."; return; }
                statisticsCustom = range; await RefreshStatisticsAsync();
            });
        }
        StatisticsContent.Children.Add(controls);
        var filters = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        filters.Children.Add(StatsText("운동 필터 "));
        var category = new ComboBox { Width = 130, Margin = new Thickness(4), ItemsSource = new[] { "전체 분류" }.Concat(ExerciseCatalog.Categories), SelectedItem = statisticsFilter.Category ?? "전체 분류" };
        var exercise = new ComboBox { Width = 150, Margin = new Thickness(4) };
        exercise.Items.Add("전체 종목"); foreach (var item in ExerciseCatalog.All.Where(e => statisticsFilter.Category is null || e.Category == statisticsFilter.Category)) exercise.Items.Add(item);
        exercise.SelectedItem = statisticsFilter.ExerciseId is string id ? ExerciseCatalog.Get(id) : "전체 종목";
        category.SelectionChanged += async (_, _) => { if (busy) return; statisticsFilter = new(category.SelectedItem as string == "전체 분류" ? null : category.SelectedItem as string); await RefreshStatisticsAsync(); };
        exercise.SelectionChanged += async (_, _) => { if (busy) return; statisticsFilter = statisticsFilter with { ExerciseId = (exercise.SelectedItem as Exercise)?.Id }; await RefreshStatisticsAsync(); };
        filters.Children.Add(category); filters.Children.Add(exercise); filters.Children.Add(StatsText("운동 수치에만 적용돼요.", true)); StatisticsContent.Children.Add(filters);
        StatisticsContent.Children.Add(StatsText($"{report.Range.From:yyyy.MM.dd} — {report.Range.To:yyyy.MM.dd} · {report.Range.Days}일", size: 17));
        var rate = report.CompletionRate is decimal r ? $"{r:0.#}%" : "계획 없음";
        var summary = new WrapPanel();
        void Card(string heading, string value)
        {
            var stack = new StackPanel(); stack.Children.Add(StatsText(heading, true)); stack.Children.Add(StatsText(value, size: 15));
            summary.Children.Add(new Border { Child = stack, Background = ResourceBrush("Paper"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 8), MinWidth = 155 });
        }
        Card("계획 · 완료율", $"{report.Done}/{report.Plans}건 · {rate}"); Card("운동 · 회고", $"운동 {report.Workout.Records}건 / {report.Workout.Days}일\n회고 {report.ReviewDays}일");
        Card("운동 시간", StatsTime(report.Workout.Seconds) + $"\n시간 입력 {report.Workout.TimedRecords}/{report.Workout.Records}건"); Card("예상 kcal", StatsKcal(report.Workout));
        StatisticsContent.Children.Add(summary);
        StatisticsContent.Children.Add(StatsText(report.Workout.AverageSeconds is decimal average ? $"운동한 날 기준 평균 {StatsTime((long)decimal.Round(average, 0, MidpointRounding.AwayFromZero))} / 일 · 시간 미입력 기록은 합계에서 제외" : "운동한 날이 없어 평균을 표시하지 않아요.", true));
        if (statisticsComparison is { } previous && statisticsProgress is { } progress)
        {
            var pp = previous.CompletionRate is decimal before && progress.CompletionRate is decimal after ? $" · 완료율 {after - before:+0.#;-0.#;0} %p" : " · 완료율 비교 대상 없음";
            StatisticsContent.Children.Add(StatsText($"비교: {progress.Range.From:MM/dd}–{progress.Range.To:MM/dd} ({progress.Range.Days}일) ↔ {previous.Range.From:MM/dd}–{previous.Range.To:MM/dd} ({previous.Range.Days}일)\n완료 {progress.Done - previous.Done:+0;-0;0}건 · 운동 {progress.Workout.Records - previous.Workout.Records:+0;-0;0}건{pp}", true));
            var seconds = progress.Workout.Seconds - previous.Workout.Seconds;
            StatisticsContent.Children.Add(StatsText($"운동 일수 {progress.Workout.Days - previous.Workout.Days:+0;-0;0}일 · 시간 {(seconds < 0 ? "−" : "+")}{StatsTime(Math.Abs(seconds))}" +
                (previous.Workout.Records == 0 ? " · 이전 기간 운동 기록 없음" : ""), true));
            StatisticsContent.Children.Add(StatsText(progress.Workout.Kcal is decimal kcal && previous.Workout.Kcal is decimal previousKcal
                ? $"예상 kcal {Math.Round(kcal - previousKcal, 0, MidpointRounding.AwayFromZero):+0;-0;0} · 계산된 기록만 비교 ({progress.Workout.Calculated}/{progress.Workout.Records}건 ↔ {previous.Workout.Calculated}/{previous.Workout.Records}건)"
                : $"kcal 증감 비교 대상 없음 ({progress.Workout.Calculated}/{progress.Workout.Records}건 ↔ {previous.Workout.Calculated}/{previous.Workout.Records}건)", true));
        }
        if (statisticsMode == "weekday")
            StatisticsContent.Children.Add(StatsText($"요일별 완료는 실제 완료 날짜 기준이에요. 이 기간에 배정된 완료 중 실행 날짜가 없는 {report.UnknownActualDates}건은 제외해요. 계획·회고는 배정 날짜 기준이에요.", true));
        else StatisticsContent.Children.Add(StatsText("계획·완료는 현재 배정 날짜 기준이에요. 이월된 계획은 도착한 날짜에 포함돼요.", true));
        var rows = statisticsMode == "weekday" ? report.Weekdays : report.Days;
        var chartHeading = new WrapPanel(); chartHeading.Children.Add(StatsText(statisticsMode == "weekday" ? "요일별 비교 " : "날짜별 비교 ", size: 15));
        var metric = new ComboBox { Width = 140, Margin = new Thickness(8, 0, 0, 8), ItemsSource = new[] { "운동 시간", "완료 계획", "운동 일수", "예상 kcal" }, SelectedItem = statisticsMetric };
        metric.SelectionChanged += (_, _) => { statisticsMetric = (string)metric.SelectedItem; DrawStatistics(); }; chartHeading.Children.Add(metric); StatisticsContent.Children.Add(chartHeading);
        decimal Value(StatisticsRow row) => statisticsMetric switch { "완료 계획" => statisticsMode == "weekday" ? row.ActualDone : row.Done, "운동 일수" => row.Workout.Days, "예상 kcal" => row.Workout.Kcal ?? 0, _ => row.Workout.Seconds };
        string Label(StatisticsRow row) => row.DateOccurrences == 0 ? "집계 대상 없음" : statisticsMetric switch { "완료 계획" => Value(row) + "건", "운동 일수" => row.Workout.Days + "일", "예상 kcal" => row.Workout.Kcal is decimal kcal ? $"약 {Math.Round(kcal, 0, MidpointRounding.AwayFromZero):N0} kcal" : "계산된 기록 없음", _ => StatsTime(row.Workout.Seconds) };
        var max = Math.Max(1m, rows.Max(Value));
        foreach (var row in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.Children.Add(StatsText(row.Label + (statisticsMode == "weekday" ? $" ({row.DateOccurrences}일)" : "")));
            var bar = new Grid { Height = 9, VerticalAlignment = VerticalAlignment.Center, Background = ResourceBrush("Soft") };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)(Value(row) / max), GridUnitType.Star) }); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)(1 - Value(row) / max), GridUnitType.Star) });
            if (Value(row) > 0) bar.Children.Add(new Border { Background = ResourceBrush("Accent"), CornerRadius = new CornerRadius(4) });
            Grid.SetColumn(bar, 1); grid.Children.Add(bar);
            var value = StatsText(Label(row)); value.TextAlignment = TextAlignment.Right; Grid.SetColumn(value, 2); grid.Children.Add(value); StatisticsContent.Children.Add(grid);
        }
        if (statisticsMode == "weekday")
        {
            StatisticsContent.Children.Add(StatsText("요일 · 실제 완료 · 운동 / 일수 · 운동일 비율 · 예상 kcal", size: 15));
            foreach (var row in report.Weekdays) StatisticsContent.Children.Add(StatsText($"{row.Label} · 완료 {row.ActualDone}건 · 운동 {row.Workout.Records}건 / {row.Workout.Days}일 · " +
                (row.DateOccurrences == 0 ? "대상 날짜 없음" : $"{(decimal)row.Workout.Days / row.DateOccurrences * 100:0.#}%") + " · " + StatsKcal(row.Workout)));
        }
        StatisticsContent.Children.Add(StatsText("분류별 운동", size: 15));
        foreach (var row in report.Categories) StatisticsContent.Children.Add(StatsText($"{row.Label} · {row.Workout.Records}건 / {row.Workout.Days}일 · {StatsTime(row.Workout.Seconds)} · {StatsKcal(row.Workout)}"));
        StatisticsContent.Children.Add(StatsText("종목별 운동", size: 15));
        if (report.Exercises.Count == 0) StatisticsContent.Children.Add(StatsText("이 기간에 저장된 운동이 없어요.", true));
        foreach (var row in report.Exercises) StatisticsContent.Children.Add(StatsText($"{row.Label} · {row.Workout.Records}건 / {row.Workout.Days}일 · {StatsTime(row.Workout.Seconds)} · {StatsKcal(row.Workout)}"));
    }
    private void ActualDate_Changed(object? sender, SelectionChangedEventArgs e) { if (!suppress) UpdateDraftStatus(); }
    private DateOnly? ChooseCompletionDate()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (verify || DateTime.Now.Hour < 23 || selected != today.AddDays(1)) return selected;
        var dialog = new Window { Owner = this, Title = "실제 완료 날짜", Width = 360, Height = 250, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        foreach (var key in new[] { "Paper", "Ink", "Muted", "Line", "Soft", "Accent", "AccentInk" }) dialog.Resources[key] = FindResource(key);
        var content = new StackPanel { Margin = new Thickness(22) };
        content.Children.Add(StatsText($"현재 날짜 {today:yyyy.MM.dd}\n계획 배정일 {selected:yyyy.MM.dd}\n23시 이월 뒤 완료했다면 실제 수행한 날짜를 확인해 주세요."));
        var picker = new DatePicker { SelectedDate = today.ToDateTime(TimeOnly.MinValue) }; content.Children.Add(picker);
        var apply = new Button { Content = "이 날짜로 완료", Margin = new Thickness(0, 12, 0, 0) };
        apply.Click += (_, _) => { if (picker.SelectedDate is not null) dialog.DialogResult = true; }; content.Children.Add(apply);
        dialog.Content = content; return dialog.ShowDialog() == true && picker.SelectedDate is DateTime date ? DateOnly.FromDateTime(date) : null;
    }
}
