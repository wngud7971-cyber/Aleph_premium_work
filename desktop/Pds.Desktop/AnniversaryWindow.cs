using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pds.Core;

namespace Pds.Desktop;

public sealed class AnniversaryWindow : Window
{
    private readonly PlanRepository repository;
    private readonly StackPanel savedList = new();
    private readonly TextBlock preview = new() { TextWrapping = TextWrapping.Wrap };
    internal readonly TextBox EntryYear = new() { MaxLength = 4, ToolTip = "태어난 연도 또는 기념일의 기준 연도 · 예: 1995" };
    internal readonly ComboBox RecurringCalendar = new() { ItemsSource = new[] { "입력한 달력 그대로", "매년 양력으로 표시", "매년 음력으로 표시" }, SelectedIndex = 0 };
    internal readonly ComboBox OriginLeap = new() { ItemsSource = new[] { "평달", "윤달" }, SelectedIndex = -1, Visibility = Visibility.Collapsed };
    private readonly TextBlock originLeapHelp = new() { Text = "이 연도에는 같은 음력 월일이 두 번 있어요. 원래 날짜가 평달인지 윤달인지 선택해 주세요.", TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly TextBlock yearLabel = new() { Text = "태어난 연도 · 예: 1995 (모르면 비워도 돼요)", TextWrapping = TextWrapping.Wrap };
    private string originKey = "";
    internal readonly TextBox EntryName = new() { MaxLength = 100 };
    internal readonly TextBox EntryNote = new() { MaxLength = 1000, Height = 64, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    internal readonly ComboBox Kind = new() { ItemsSource = new[] { "생일", "기일", "기념일" }, SelectedIndex = 0 };
    internal readonly ComboBox Calendar = new() { ItemsSource = new[] { "양력", "음력" }, SelectedIndex = 0 };
    internal readonly ComboBox Month = new() { ItemsSource = Enumerable.Range(1, 12).ToArray() };
    internal readonly ComboBox Day = new() { ItemsSource = Enumerable.Range(1, 31).ToArray() };
    internal readonly CheckBox Leap = new() { Content = "윤달 날짜", Visibility = Visibility.Collapsed };
    internal readonly ComboBox LeapPolicy = new() { ItemsSource = new[] { "윤달이 없는 해에는 표시하지 않기", "윤달이 없으면 같은 평달에 표시" }, SelectedIndex = 0, Visibility = Visibility.Collapsed };
    internal readonly ComboBox DayPolicy = new() { ItemsSource = new[] { "해당 날짜가 없으면 그달 마지막 날", "해당 날짜가 없는 해에는 표시하지 않기" }, SelectedIndex = 0 };
    internal readonly TextBlock Result = new() { TextWrapping = TextWrapping.Wrap };
    internal readonly Button SaveEntry = new() { Content = "저장", Width = 100 };
    internal readonly ScrollViewer EditorViewport = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    internal Func<MessageBoxResult>? DraftDecision;
    internal Func<Anniversary, bool>? DeleteDecision;
    private Anniversary? editing;
    private bool saving, updating, discardClose;
    private string baseline = "";
    private readonly DateOnly anchor;
    private readonly Grid root = new() { Margin = new Thickness(22) };

    public AnniversaryWindow(PlanRepository repository, Window owner, DateOnly anchor)
    {
        this.repository = repository; this.anchor = anchor; Owner = owner;
        Style = (Style)owner.FindResource(typeof(Window));
        WindowEnvironment.Watch(this);
        Title = "생일·기일·기념일"; Width = Math.Min(560, SystemParameters.WorkArea.Width);
        Height = Math.Min(740, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(440, Width); MinHeight = Math.Min(480, Height);
        MaxWidth = SystemParameters.WorkArea.Width; MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        foreach (var key in new[] { "Canvas", "Paper", "Ink", "Muted", "Line", "Soft", "Note", "Accent", "AccentInk" }) Resources[key] = owner.FindResource(key);
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = "매년 기억할 날", FontSize = UiTypography.Grow(18), Margin = new Thickness(0, 0, 0, 14) });
        var editor = new StackPanel(); EditorViewport.Content = editor; Grid.SetRow(EditorViewport, 1); root.Children.Add(EditorViewport);
        void Label(string text) => editor.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        Label("등록한 날짜는 매년 표시되고, 직접 삭제하기 전까지 남아요.");
        var listViewport = new ScrollViewer { Content = savedList, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 10) };
        editor.Children.Add(listViewport);
        var fresh = new Button { Content = "새로 등록", HorizontalAlignment = HorizontalAlignment.Left };
        fresh.Click += async (_, _) => { if (await ConfirmDraftAsync()) ResetEditor(); }; editor.Children.Add(fresh);
        Label("이름 · 예: 어머니, 친구 이름, 결혼기념일"); editor.Children.Add(EntryName);
        Label("종류 · 입력할 날짜의 양력/음력");
        var options = new Grid(); options.ColumnDefinitions.Add(new()); options.ColumnDefinitions.Add(new() { Width = new GridLength(12) }); options.ColumnDefinitions.Add(new());
        options.Children.Add(Kind); Grid.SetColumn(Calendar, 2); options.Children.Add(Calendar); editor.Children.Add(options);
        editor.Children.Add(yearLabel); editor.Children.Add(EntryYear);
        Label("등록할 월 · 일");
        var date = new Grid(); date.ColumnDefinitions.Add(new()); date.ColumnDefinitions.Add(new() { Width = new GridLength(12) }); date.ColumnDefinitions.Add(new());
        date.Children.Add(Month); Grid.SetColumn(Day, 2); date.Children.Add(Day); editor.Children.Add(date);
        editor.Children.Add(originLeapHelp); editor.Children.Add(OriginLeap);
        Label("매년 표시할 날짜 기준 · 연도를 입력하면 자동 환산해요"); editor.Children.Add(RecurringCalendar);
        editor.Children.Add(Leap); editor.Children.Add(LeapPolicy);
        Label("2월 29일·음력 30일 등 해당 날짜가 없는 해"); editor.Children.Add(DayPolicy);
        preview.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); editor.Children.Add(preview);
        Label("메모 · 선택"); editor.Children.Add(EntryNote);
        Label("음력은 해마다 양력으로 환산해요. 지원 범위: 음력 1900~2050년. 기일은 입력한 당일에 표시해요.");
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; footer.Children.Add(Result);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var close = new Button { Content = "닫기" }; close.Click += (_, _) => Close(); actions.Children.Add(close);
        SaveEntry.SetResourceReference(Button.BackgroundProperty, "Accent"); SaveEntry.SetResourceReference(Button.ForegroundProperty, "AccentInk");
        SaveEntry.Click += async (_, _) => await SaveEditorAsync(); actions.Children.Add(SaveEntry); footer.Children.Add(actions); Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        foreach (var combo in new[] { Kind, Calendar, Month, Day, LeapPolicy, DayPolicy, RecurringCalendar, OriginLeap }) combo.SelectionChanged += (_, _) => RefreshPreview();
        EntryYear.TextChanged += (_, _) => RefreshPreview();
        EntryName.TextChanged += (_, _) => RefreshPreview(); Leap.Checked += (_, _) => RefreshPreview(); Leap.Unchecked += (_, _) => RefreshPreview();
        ResetEditor(); DrawSaved(); Closing += OnClosing;
        Loaded += (_, _) => CalmMotion.Reveal(root, CalmMotion.DialogMs);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } };
    }
    private string EditorKey() => string.Join("|", EntryName.Text, EntryYear.Text, EntryNote.Text, RecurringCalendar.SelectedIndex, OriginLeap.SelectedIndex, Kind.SelectedIndex, Calendar.SelectedIndex, Month.SelectedItem, Day.SelectedItem, Leap.IsChecked, LeapPolicy.SelectedIndex, DayPolicy.SelectedIndex);
    internal bool HasDraft => EditorKey() != baseline && (editing is not null || EntryName.Text.Length > 0 || EntryNote.Text.Length > 0 || EntryYear.Text.Length > 0);
    internal void ResetEditor()
    {
        updating = true; editing = null; EntryName.Clear(); EntryNote.Clear(); EntryYear.Clear(); RecurringCalendar.SelectedIndex = 0; OriginLeap.SelectedIndex = -1; originKey = ""; Kind.SelectedIndex = Calendar.SelectedIndex = 0;
        Month.SelectedItem = anchor.Month; Day.SelectedItem = anchor.Day; Leap.IsChecked = false; LeapPolicy.SelectedIndex = DayPolicy.SelectedIndex = 0;
        updating = false; RefreshPreview(); baseline = EditorKey();
    }
    private string OriginKey() => $"{EntryYear.Text.Trim()}|{Calendar.SelectedIndex}|{Month.SelectedItem}|{Day.SelectedItem}";
    private AnniversaryOrigin ReadOrigin()
    {
        if (!int.TryParse(EntryYear.Text.Trim(), out var year) || year is < 1 or > 9999)
            throw new ArgumentException("연도는 1~9999의 숫자로 입력해 주세요.");
        var candidates = AnniversaryOrigins.Candidates(year, Month.SelectedItem is int m ? m : 0, Day.SelectedItem is int d ? d : 0, (AnniversaryCalendar)Calendar.SelectedIndex);
        if (candidates.Count == 1) return candidates[0];
        if (OriginLeap.SelectedIndex < 0) throw new ArgumentException("이 연도에는 평달과 윤달이 모두 있어요. 원래 음력 날짜의 평달·윤달을 선택해 주세요.");
        return candidates.Single(c => c.LeapMonth == (OriginLeap.SelectedIndex == 1));
    }
    private Anniversary ReadEditor()
    {
        var entry = (editing ?? Anniversary.Create(EntryName.Text, (AnniversaryKind)Kind.SelectedIndex, (AnniversaryCalendar)Calendar.SelectedIndex, 1, 1)) with
        {
            Name = EntryName.Text, Note = EntryNote.Text, Kind = (AnniversaryKind)Kind.SelectedIndex, Calendar = (AnniversaryCalendar)Calendar.SelectedIndex,
            Month = Month.SelectedItem is int m ? m : 0, Day = Day.SelectedItem is int d ? d : 0,
            Origin = null, LeapMonth = Calendar.SelectedIndex == 1 && Leap.IsChecked == true,
            LeapPolicy = LeapPolicy.SelectedIndex == 0 ? MissingLeapMonth.SkipYear : MissingLeapMonth.RegularMonth,
            DayPolicy = DayPolicy.SelectedIndex == 0 ? MissingAnniversaryDay.LastDayOfMonth : MissingAnniversaryDay.SkipYear
        };
        if (string.IsNullOrWhiteSpace(EntryYear.Text)) return entry;
        var target = RecurringCalendar.SelectedIndex switch { 1 => AnniversaryCalendar.Solar, 2 => AnniversaryCalendar.Lunar, _ => (AnniversaryCalendar)Calendar.SelectedIndex };
        return AnniversaryOrigins.Apply(entry, ReadOrigin(), target);
    }
    private void RefreshPreview()
    {
        if (updating) return;
        var hasYear = !string.IsNullOrWhiteSpace(EntryYear.Text);
        yearLabel.Text = Kind.SelectedIndex == 0 ? "태어난 연도 · 예: 1995 (모르면 비워도 돼요)" : "기준 연도 · 모르면 비워도 돼요";
        Leap.Visibility = !hasYear && Calendar.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        RecurringCalendar.IsEnabled = hasYear;
        OriginLeap.Visibility = originLeapHelp.Visibility = Visibility.Collapsed;
        try
        {
            if (hasYear)
            {
                if (originKey != OriginKey())
                { updating = true; OriginLeap.SelectedIndex = -1; updating = false; originKey = OriginKey(); }
                if (int.TryParse(EntryYear.Text.Trim(), out var year))
                {
                    var candidates = AnniversaryOrigins.Candidates(year, Month.SelectedItem is int m ? m : 0, Day.SelectedItem is int d ? d : 0, (AnniversaryCalendar)Calendar.SelectedIndex);
                    if (candidates.Count > 1) OriginLeap.Visibility = originLeapHelp.Visibility = Visibility.Visible;
                }
            }
            var entry = ReadEditor() with { Name = "미리보기" }; entry.Validate();
            LeapPolicy.Visibility = entry.LeapMonth ? Visibility.Visible : Visibility.Collapsed;
            preview.Text = (entry.Origin is null ? "" : entry.Origin.Label + " → 양력 " + AnniversaryOrigins.ToSolar(entry.Origin).ToString("yyyy.MM.dd") + "\n")
                + "매년 기준: " + entry.DateLabel + "\n양력 표시 미리보기\n" + string.Join("\n", Enumerable.Range(anchor.Year, Math.Min(3, 10000 - anchor.Year)).Select(year =>
            {
                try { var o = AnniversaryDates.InYear(entry, year); return o is null ? $"{year}년 · 표시 없음" : $"{year}년 기준 → {o.Date:yyyy.MM.dd}{(o.Adjusted ? " · 선택한 대체 규칙 적용" : "")}"; }
                catch (ArgumentOutOfRangeException) { return $"{year}년 · 음력 지원 범위 밖"; }
            }));
        }
        catch (ArgumentException ex) { LeapPolicy.Visibility = Visibility.Collapsed; preview.Text = ex.Message; }
    }
    internal async Task<bool> SaveEditorAsync()
    {
        if (saving) return false;
        try
        {
            var input = ReadEditor(); input.Validate(); saving = true; root.IsEnabled = false;
            editing = await Task.Run(() => repository.SaveAnniversary(input)); baseline = EditorKey(); DrawSaved();
            Result.Text = "저장 완료 · 매년 달력에 표시돼요."; return true;
        }
        catch (Exception ex) { Result.Text = ex.Message; return false; }
        finally { saving = false; root.IsEnabled = true; }
    }
    private async Task<bool> ConfirmDraftAsync()
    {
        if (saving) return false;
        if (!HasDraft) return true;
        var answer = DraftDecision?.Invoke() ?? System.Windows.MessageBox.Show(this, "저장하지 않은 기념일 입력이 있어요. 저장할까요?", "기념일 입력", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || answer == MessageBoxResult.Yes && await SaveEditorAsync();
    }
    internal async Task EditEntryAsync(Anniversary entry)
    {
        if (!await ConfirmDraftAsync()) return;
        updating = true; editing = entry; EntryName.Text = entry.Name; EntryNote.Text = entry.Note;
        Kind.SelectedIndex = (int)entry.Kind;
        var original = entry.Origin;
        EntryYear.Text = original?.Year.ToString() ?? "";
        Calendar.SelectedIndex = (int)(original?.Calendar ?? entry.Calendar);
        Month.SelectedItem = original?.Month ?? entry.Month; Day.SelectedItem = original?.Day ?? entry.Day;
        Leap.IsChecked = entry.LeapMonth; OriginLeap.SelectedIndex = original is null ? -1 : original.LeapMonth ? 1 : 0;
        RecurringCalendar.SelectedIndex = original is null || original.Calendar == entry.Calendar ? 0 : entry.Calendar == AnniversaryCalendar.Solar ? 1 : 2;
        originKey = OriginKey();
        LeapPolicy.SelectedIndex = entry.LeapPolicy == MissingLeapMonth.SkipYear ? 0 : 1;
        DayPolicy.SelectedIndex = entry.DayPolicy == MissingAnniversaryDay.LastDayOfMonth ? 0 : 1;
        updating = false; RefreshPreview(); baseline = EditorKey(); Result.Text = "수정 후 저장을 눌러 주세요.";
    }
    internal async Task DeleteEntryAsync(Anniversary entry)
    {
        if (saving) return;
        if (!(DeleteDecision?.Invoke(entry) ?? (System.Windows.MessageBox.Show(this, $"{entry.Name} · {entry.KindLabel}을 삭제할까요? 매년 표시도 함께 없어져요.", "기념일 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes))) return;
        try { saving = true; root.IsEnabled = false; await Task.Run(() => repository.DeleteAnniversary(entry)); if (editing?.Id == entry.Id) ResetEditor(); DrawSaved(); Result.Text = "삭제 완료"; }
        catch (Exception ex) { Result.Text = ex.Message; }
        finally { saving = false; root.IsEnabled = true; }
    }
    private void DrawSaved()
    {
        savedList.Children.Clear(); var entries = repository.LoadAnniversaries();
        if (entries.Count == 0) savedList.Children.Add(new TextBlock { Text = "아직 등록한 날이 없어요." });
        foreach (var entry in entries)
        {
            var box = new StackPanel(); box.Children.Add(new TextBlock { Text = entry.DisplayLabel, TextWrapping = TextWrapping.Wrap });
            box.Children.Add(new TextBlock { Text = entry.DateLabel, TextWrapping = TextWrapping.Wrap });
            if (entry.Origin is not null) box.Children.Add(new TextBlock { Text = entry.Origin.Label, TextWrapping = TextWrapping.Wrap });
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; var edit = new Button { Content = "수정" }; var delete = new Button { Content = "삭제" };
            edit.Click += async (_, _) => await EditEntryAsync(entry); delete.Click += async (_, _) => await DeleteEntryAsync(entry);
            actions.Children.Add(edit); actions.Children.Add(delete); box.Children.Add(actions);
            var border = new Border { Child = box, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(8) }; border.SetResourceReference(Border.BackgroundProperty, "Soft"); savedList.Children.Add(border);
        }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (saving) { e.Cancel = true; return; }
        if (discardClose || !HasDraft) return;
        e.Cancel = true;
        if (await ConfirmDraftAsync()) { discardClose = true; Close(); }
    }
}
