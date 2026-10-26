using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow : Window
{
    private readonly PlanRepository repository;
    private readonly SettingsStore settingsStore;
    private readonly UserSettings settings;
    private readonly bool verify, verifyReopen, verifyFeatures, verifyReviewReopen;
    private readonly bool allowSystemStartup;
    private readonly bool verifyTray;
    private readonly bool verifyEndurance;
    private NativeTrayIcon? tray;
    private DaySnapshot snapshot;
    private List<PlanEntry> plans = [];
    private IReadOnlyDictionary<DateOnly, DayCount> counts = new Dictionary<DateOnly, DayCount>();
    private DateOnly selected = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private Guid? editing;
    private bool dirty, busy, suppress, exiting, ready, applyingMode, loadedOnce;
    private int weeks = 5, refreshGeneration;
    private readonly Stopwatch startup = Stopwatch.StartNew();
    private double processReadyMs;
    private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");

    public MainWindow(PlanRepository repository, SettingsStore settingsStore, bool verify, bool verifyReopen = false, bool verifyFeatures = false, bool verifyReviewReopen = false, bool allowSystemStartup = false, bool verifyTray = false, bool verifyEndurance = false)
    {
        this.repository = repository; this.settingsStore = settingsStore; this.verify = verify; this.verifyReopen = verifyReopen; this.verifyFeatures = verifyFeatures;
        this.verifyReviewReopen = verifyReviewReopen;
        this.verifyTray = verifyTray;
        this.verifyEndurance = verifyEndurance;
        this.allowSystemStartup = allowSystemStartup && !verify;
        snapshot = new(selected, 0, []);
        settings = settingsStore.Load();
        InitializeComponent();
        InitializeWorkouts(); InitializeMeditation(); InitializeJournal();
        ConfigureSidebarMotion(); ConfigurePageTurn();
        ApplyTheme(); ApplyWindowLayout();
        if (settings.Left is double left && double.IsFinite(left) && settings.Top is double top && double.IsFinite(top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left; Top = top;
        }
        CreateTray();
        if(!verify)WindowEnvironment.Watch(this,()=>ApplyWindowLayout());
        Loaded += OnLoaded;
        Closing += OnClosing;
        Activated += (_, _) => { if (ready) { if (lastEntryDate != EntryToday) RequestEntryGuidance(); _ = RefreshBodyWeightAsync(); QueueMaintenance(); } };
        SizeChanged += (_, _) => { FitHeader(); FitReminder(); if (ready && WindowState == WindowState.Normal) RememberWindowPosition(); };
        PreviewKeyDown += async (_, e) =>
        {
            if (busy) { e.Handled = true; return; }
            if (ReminderOverlay.Visibility == Visibility.Visible)
            { if (e.Key == System.Windows.Input.Key.Escape) ClosePlanReminder(); e.Handled = e.Key == System.Windows.Input.Key.Escape || e.Key == System.Windows.Input.Key.S && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control; return; }
            if(recordToolsOpen){if(recordToolsView is not null)await recordToolsView.HandleShortcutAsync(e);return;}
            if (e.Key == System.Windows.Input.Key.S && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            { e.Handled = true; await SaveAsync(); }
            if (e.Key == System.Windows.Input.Key.Escape && MeditationCard.Visibility == Visibility.Visible) { CloseMeditation(); e.Handled = true; }
        };
        PreviewTextInput += (_, e) => { if (busy) e.Handled = true; };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loadedOnce) return;
        loadedOnce = true;
        try
        {
            await LoadSelectedAsync(); await RefreshCalendarAsync(); await RefreshBodyWeightAsync();
            ready = true; startup.Stop(); processReadyMs = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalMilliseconds;
            Status.Text = "날짜를 누르면 기록할 수 있어요. X는 트레이로 숨겨요.";
            if (verify) ShowMeditationIfNeeded();
            if (verifyEndurance) await VerifyEnduranceAsync();
            else if (verifyTray) await VerifyTrayAsync();
            else if (verifyReviewReopen) await VerifyReviewReopenAsync();
            else if (verifyFeatures) await VerifyFeaturesAsync();
            else if (verify) await VerifyAsync();
            else { StartMaintenance(); await MaintainAsync(); RequestEntryGuidance(); }
        }
        catch (Exception ex) { HandleError("앱 준비", ex); if (verify) FinishVerification(ex); }
    }

    private async Task LoadSelectedAsync(Task? beforePresentation=null)
    {
        busy = true; Body.IsHitTestVisible = false; CalmMotion.Settle(Details);
        try
        {
            var date=selected;
            var loaded = await Task.Run(() => { if(!verify)repository.ApplyFixedCosts(LocalToday);return repository.Load(date); });
            // Fetch in parallel, but keep editor reconstruction out of the calendar's motion frames.
            if(beforePresentation is not null)await beforePresentation;
            snapshot=loaded;
            plans = snapshot.Plans.ToList(); workouts = snapshot.Workouts.ToList(); dirty = false;
            ClearEditor(); ClearWorkoutEditor(); LoadReview(); LoadJournal(); DrawPlans(); DrawWorkouts(); DrawSelectedAnniversaries(); DrawSelectedHolidays();
            SelectedNumber.Text = selected.Day.ToString(Korean);
            SelectedYear.Text = selected.ToString("yyyy년 M월", Korean);
            SelectedTitle.Text = selected.ToString("dddd", Korean);
            WorkoutRecordDate.Text = $"{selected:yyyy.MM.dd}의 운동 · 늦게 저장해도 날짜 유지";
            SaveStatus.Text = "선택한 날짜의 기록";
        }
        finally { busy = false; Body.IsHitTestVisible = true; }
    }

    private DateOnly? displayedCalendarMonth;
    private async Task RefreshCalendarAsync()
    {
        var generation = ++refreshGeneration;
        var requestedMonth = month;
        var first = requestedMonth.AddDays(-(((int)requestedMonth.DayOfWeek + 6) % 7));
        var requestedWeeks = (((int)requestedMonth.DayOfWeek + 6) % 7 + DateTime.DaysInMonth(requestedMonth.Year, requestedMonth.Month) + 6) / 7;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var loaded = await Task.Run(() =>
        {
            var result = repository.Counts(first, first.AddDays(requestedWeeks * 7 - 1)).ToDictionary();
            var todayCount = repository.Counts(today, today);
            if (todayCount.TryGetValue(today, out var count)) result[today] = count;
            return (Counts: result, Anniversaries: repository.LoadAnniversaries());
        });
        if (generation != refreshGeneration) return;
        if (displayedCalendarMonth != requestedMonth) { CalendarViewport.ScrollToTop(); CalendarViewport.ScrollToLeftEnd(); displayedCalendarMonth = requestedMonth; }
        weeks = requestedWeeks; counts = loaded.Counts; anniversaries = loaded.Anniversaries;
        DrawSelectedAnniversaries(); DrawSelectedHolidays();
        DrawCalendar();
    }

    private void DrawCalendar()
    {
        DrawSelectedHolidays();
        MonthTitle.Text = month.ToString("MMMM", CultureInfo.InvariantCulture);
        YearTitle.Text = month.ToString("yyyy년", Korean);
        HolidayCoverage.Visibility = KoreanHolidays.Covers(month.Year) ? Visibility.Collapsed : Visibility.Visible;
        HolidayCoverage.Text = $"{month.Year}년 공휴일 자료 미포함";
        MonthTitle.ToolTip = "한국 공휴일 자료: 2025~2027년 · 확인일 2026.10.17";
        WeekDays.Children.Clear(); MonthGrid.Children.Clear(); MonthGrid.Rows = weeks;
        var names = new[] { "월", "화", "수", "목", "금", "토", "일" };
        for (var i = 0; i < 7; i++) WeekDays.Children.Add(new TextBlock { Text = names[i], FontSize = UiTypography.Grow(12), HorizontalAlignment = HorizontalAlignment.Center, Foreground = ResourceBrush(i == 5 ? "Saturday" : i == 6 ? "Sunday" : "Ink") });
        var first = month.AddDays(-(((int)month.DayOfWeek + 6) % 7));
        var occasions = AnniversaryDates.Between(anniversaries, first, first.AddDays(weeks * 7 - 1)).GroupBy(o => o.Date).ToDictionary(g => g.Key, g => g.ToArray());
        for (var i = 0; i < weeks * 7; i++)
        {
            var date = first.AddDays(i); var isSelected = (Details.Visibility == Visibility.Visible) && date == selected && date.Month == month.Month;
            var holiday = KoreanHolidays.On(date);
            var content = new Grid { ClipToBounds = true };
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(holiday is null ? 24 : 44) });
            content.RowDefinitions.Add(new RowDefinition());
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            var numberColor = holiday is not null || date.DayOfWeek == DayOfWeek.Sunday ? isSelected ? "SelectedSunday" : "Sunday"
                : date.DayOfWeek == DayOfWeek.Saturday ? isSelected ? "SelectedSaturday" : "Saturday"
                : isSelected ? "AccentInk" : date.Month == month.Month ? "Ink" : "Muted";
            var number = new TextBlock { Text = date.Day.ToString(Korean), FontFamily = FontFamily, FontSize = UiTypography.Date, VerticalAlignment = VerticalAlignment.Top, Foreground = ResourceBrush(numberColor), Opacity = date.Month == month.Month ? 1 : 0.65 };
            if (date == DateOnly.FromDateTime(DateTime.Today)) number.TextDecorations = TextDecorations.Underline;
            content.Children.Add(number);
            occasions.TryGetValue(date, out var specialDays);
            var caption = "";
            if (counts.TryGetValue(date, out var count))
            {
                caption = count.Plans > 0 ? $"계획 {count.Plans}\n완료 {count.Done}" : "";
                if (count.Workouts > 0)
                {
                    var badge = new TextBlock { Text = "•", FontSize = UiTypography.Grow(12), HorizontalAlignment = HorizontalAlignment.Right, Foreground = ResourceBrush(isSelected ? "AccentInk" : "Ink"), ToolTip = WorkoutTimeSummary(count) };
                    if (count.Plans > 0 && holiday is null) { Grid.SetColumn(badge, 2); content.Children.Add(badge); }
                    if (count.Plans == 0) caption = $"운동: {count.Workouts}건";
                }
            }
            // Quiet row guides replace a separate gray card around every day.
            if (date != LocalToday && date.Month == month.Month)
            {
                var guide = new Border { BorderBrush = ResourceBrush("Line"), BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin = new Thickness(0), IsHitTestVisible = false, Visibility = isSelected ? Visibility.Collapsed : Visibility.Visible };
                Grid.SetRowSpan(guide, 2); Grid.SetColumnSpan(guide, 3); content.Children.Insert(0, guide);
            }
            AddCalendarLabels(content, specialDays, holiday, caption, isSelected);
            var button = new Button { Content = content, Style = (Style)FindResource("CalendarDay"), Margin = new Thickness(3), Padding = new Thickness(4), Background = date.Month != month.Month ? Brushes.Transparent : ResourceBrush(isSelected ? "Accent" : date == LocalToday ? "Note" : "Paper"), BorderBrush = isSelected ? ResourceBrush("AccentInk") : date == LocalToday ? ResourceBrush("Accent") : Brushes.Transparent, ToolTip = date.ToString("yyyy년 M월 d일 dddd", Korean), Tag = date };
            if (specialDays is { Length: > 0 }) button.ToolTip += "\n" + string.Join("\n", specialDays.Select(o => $"{o.Entry.DisplayLabel} · {o.Entry.DateLabel}"));
            if (holiday is not null) button.ToolTip += "\n공휴일: " + holiday.Label;
            if (count is not null) button.ToolTip += $"\n계획 {count.Plans} · 완료 {count.Done} · 운동: {count.Workouts}건";
            System.Windows.Automation.AutomationProperties.SetName(button, button.ToolTip.ToString() + (count is null ? "" : $", 계획 {count.Plans}건 완료 {count.Done}건, 운동 {count.Workouts}건"));
            button.Click += async (_, _) => await SelectDateAsync(date);
            MonthGrid.Children.Add(button);
        }
        var today = DateOnly.FromDateTime(DateTime.Today);
        var todayCount = counts.TryGetValue(today, out var value) ? value : new DayCount(0, 0);
        RefreshDashboard(todayCount);
        FitCalendar();
    }

    private async Task SelectDateAsync(DateOnly date)
    {
        if (busy || !await ConfirmPendingAsync()) return;
        await LoadDateAndOpenAsync(date);
    }

    private async Task LoadDateAndOpenAsync(DateOnly date)
    {
        selected = date;
        try
        {
            Task<bool>? moving=null;
            if(Details.Visibility==Visibility.Visible && Math.Abs(DetailColumn.ActualWidth-330)<.1)
            {
                UpdateCalendarSelection(true);
                moving=AnimateSidebarAsync(true);
            }
            await LoadSelectedAsync(moving);
            if(moving is not null)await moving;
            else
            {
                UpdateCalendarSelection(true);
                await Dispatcher.InvokeAsync(() => { FitCalendar(); UpdateLayout(); }, DispatcherPriority.Loaded);
                await AnimateSidebarAsync(true);
            }
        }
        catch (Exception ex) { HandleError("기록 불러오기", ex); }
        PersistSettings();
    }

    private void DrawPlans()
    {
        PlanList.Children.Clear(); EmptyMessage.Visibility = plans.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DailyCount.Text = $"{plans.Count(p => p.Done)} / {plans.Count} 완료";
        foreach (var entry in plans)
        {
            var item = new StackPanel();
            var title = new TextBlock { Text = entry.Title, TextWrapping = TextWrapping.Wrap, Foreground = ResourceBrush(entry.Done ? "Muted" : "Ink"), TextDecorations = entry.Done ? TextDecorations.Strikethrough : null };
            var check = new CheckBox { Content = title, IsChecked = entry.Done, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            check.Click += (_, _) =>
            {
                var index = plans.FindIndex(p => p.Id == entry.Id); var done = check.IsChecked == true;
                var actual = done ? ChooseCompletionDate() : null;
                if (done && actual is null) { check.IsChecked = false; return; }
                plans[index] = plans[index] with { Done = done, ActualDate = actual };
                if (editing == entry.Id)
                {
                    suppress = true; ActualDateFields.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
                    ActualDateInput.SelectedDate = actual?.ToDateTime(TimeOnly.MinValue); suppress = false;
                }
                DrawPlans(); MarkDirty();
            };
            item.Children.Add(check);
            if (entry.Start is not null) item.Children.Add(new TextBlock { Text = $"{entry.Start}–{entry.End}", FontSize = UiTypography.Grow(12), Margin = new Thickness(25, 0, 0, 0), Foreground = ResourceBrush("Muted") });
            if (!string.IsNullOrEmpty(entry.Note)) item.Children.Add(new TextBlock { Text = entry.Note, TextWrapping = TextWrapping.Wrap, FontSize = UiTypography.Grow(12), Foreground = ResourceBrush("Muted") });
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var edit = new Button { Content = "수정", FontSize = UiTypography.Grow(12), Background = Brushes.Transparent, Padding = new Thickness(6, 3, 6, 3) };
            edit.Click += (_, _) => BeginEdit(entry.Id);
            var delete = new Button { Content = "삭제", FontSize = UiTypography.Grow(12), Background = Brushes.Transparent, Padding = new Thickness(6, 3, 6, 3) };
            delete.Click += (_, _) =>
            {
                if (System.Windows.MessageBox.Show(this, "이 계획을 삭제할까요? 저장 버튼을 누르면 반영돼요.", "계획 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                plans.RemoveAll(p => p.Id == entry.Id); if (editing == entry.Id) ClearEditor(); DrawPlans(); MarkDirty();
            };
            actions.Children.Add(edit); actions.Children.Add(delete); item.Children.Add(actions);
            var history = new Button { Content = "이월 내역", Background = Brushes.Transparent, Padding = new Thickness(6, 3, 6, 3), FontSize = UiTypography.Grow(12) };
            history.Click += async (_, _) => await ShowPlanHistoryAsync(entry.Id); actions.Children.Add(history);
            if (entry.Done) item.Children.Add(new TextBlock { Text = entry.ActualDate is DateOnly actual ? $"실행 {actual:yyyy.MM.dd}" : "실행 날짜 미입력", Foreground = ResourceBrush("Muted") });
            PlanList.Children.Add(new Border { Background = ResourceBrush("Paper"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 7), Margin = new Thickness(0, 0, 0, 8), Child = item });
        }
    }

    private bool HasInput => !string.IsNullOrWhiteSpace(TitleInput.Text) || !string.IsNullOrWhiteSpace(NoteInput.Text) || TimeEnabled.IsChecked == true || editing is not null;
    private bool Pending => dirty || HasInput || HasWorkoutInput || HasReviewChanges || HasMoneyInput || HasStudyInput;

    private void BeginEdit(Guid id)
    {
        if (HasInput && System.Windows.MessageBox.Show(this, "현재 입력을 버리고 이 계획을 수정할까요?", "입력 확인", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var entry = plans.Single(p => p.Id == id); suppress = true; editing = id;
        TitleInput.Text = entry.Title; NoteInput.Text = entry.Note; TimeEnabled.IsChecked = entry.Start is not null;
        StartInput.Text = entry.Start ?? ""; EndInput.Text = entry.End ?? ""; TimeFields.Visibility = entry.Start is null ? Visibility.Collapsed : Visibility.Visible;
        ActualDateFields.Visibility = entry.Done ? Visibility.Visible : Visibility.Collapsed; ActualDateInput.SelectedDate = entry.ActualDate?.ToDateTime(TimeOnly.MinValue);
        EditorTitle.Text = "계획 수정"; CancelEditButton.Visibility = Visibility.Visible; suppress = false; UpdateDraftStatus(); OpenComposer(PlanComposer, TitleInput);
    }

    private void ClearEditor()
    {
        suppress = true; editing = null; TitleInput.Clear(); NoteInput.Clear(); StartInput.Clear(); EndInput.Clear(); TimeEnabled.IsChecked = false;
        TimeFields.Visibility = Visibility.Collapsed; EditorTitle.Text = "새 계획"; CancelEditButton.Visibility = Visibility.Collapsed; suppress = false;
        ActualDateFields.Visibility = Visibility.Collapsed; ActualDateInput.SelectedDate = null;
        if (PlanComposer is not null) PlanComposer.IsExpanded = false;
    }

    private async Task<bool> SaveAsync()
    {
        if (busy) return false;
        try
        {
            var candidate = plans.ToList();
            if (HasInput)
            {
                var entry = PlanEntry.Create(TitleInput.Text, NoteInput.Text, TimeEnabled.IsChecked == true ? StartInput.Text.Trim() : null, TimeEnabled.IsChecked == true ? EndInput.Text.Trim() : null);
                if (editing is Guid id) entry = entry with { Id = id, Done = plans.Single(p => p.Id == id).Done, ActualDate = ActualDateInput.SelectedDate is DateTime actual ? DateOnly.FromDateTime(actual) : null };
                entry.Validate();
                if (editing is Guid editId) candidate[candidate.FindIndex(p => p.Id == editId)] = entry; else candidate.Add(entry);
            }
            var workoutCandidate = WorkoutCandidates();
            var reviewCandidate = ReviewCandidate();
            var moneyCandidate = MoneyCandidates(); var studyCandidate = StudyCandidates();
            if (snapshot.Review is not null && reviewCandidate is null && AskReviewRemoval() != MessageBoxResult.Yes)
            { SaveStatus.Text = "회고 삭제를 취소했어요. 입력은 유지돼요."; return false; }
            busy = true; Body.IsEnabled = false; SaveStatus.Text = "저장 중…";
            var saved = await Task.Run(() => repository.Save(snapshot with { Plans = candidate, Workouts = workoutCandidate, Review = reviewCandidate, Money = moneyCandidate, Studies = studyCandidate }));
            snapshot = saved; plans = saved.Plans.ToList(); workouts = saved.Workouts.ToList(); dirty = false; ClearEditor(); ClearWorkoutEditor(); LoadReview(); LoadJournal(); DrawPlans(); DrawWorkouts();
            await RefreshCalendarAsync(); SaveStatus.Text = "저장 완료"; Status.Text = $"{selected:yyyy.MM.dd}의 기록을 저장했어요."; return true;
        }
        catch (Exception ex) { HandleError("저장", ex); SaveStatus.Text = "저장하지 못했어요. 입력은 유지돼요."; return false; }
        finally { busy = false; Body.IsEnabled = true; QueueMaintenance(); ContinueEntryGuidance(); }
    }

    private async Task<bool> ConfirmPendingAsync()
    {
        if(recordToolsOpen && recordToolsView?.TryLeave()!=true)return false;
        if (!Pending) return true;
        var result = AskPendingChanges();
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.Yes) return await SaveAsync();
        plans = snapshot.Plans.ToList(); workouts = snapshot.Workouts.ToList(); dirty = false; ClearEditor(); ClearWorkoutEditor(); LoadReview(); LoadJournal(); DrawPlans(); DrawWorkouts(); return true;
    }

    private void MarkDirty() { if (!suppress && ready) { dirty = true; SaveStatus.Text = "저장하지 않은 변경"; } }
    private void UpdateDraftStatus()
    {
        if(suppress || !ready)return;
        SaveStatus.Text = Pending ? "저장하지 않은 변경" : "선택한 날짜의 기록";
        if(!Pending && fixedCostDeferred)QueueMaintenance();
    }
    private void Editor_Changed(object sender, TextChangedEventArgs e) { if (IsInitialized) { ExpandComposerForDraft(sender); UpdateDraftStatus(); RefreshEnergyPreview(); } }
    private void Time_Changed(object sender, RoutedEventArgs e) { if (TimeFields is null) return; TimeFields.Visibility = TimeEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; UpdateDraftStatus(); }
    private void CancelEdit_Click(object sender, RoutedEventArgs e) { ClearEditor(); UpdateDraftStatus(); }
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();
    private async void Calendar_Click(object sender, RoutedEventArgs e) { if (busy) return; if(recordToolsOpen && !await CloseRecordToolsAsync())return; if (statisticsOpen) await CloseStatisticsAnimatedAsync(); CalendarViewport.Focus(); }
    private void Meditation_Click(object sender, RoutedEventArgs e) { if (MeditationCard.Visibility == Visibility.Visible) CloseMeditation(); else OpenMeditation(); }
    private async void Previous_Click(object sender, RoutedEventArgs e) => await ShiftMonthAsync(-1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await ShiftMonthAsync(1);
    private async Task ShiftMonthAsync(int offset) { if (busy) return; try { month = month.AddMonths(offset); await RefreshCalendarAsync(); CalmMotion.Reveal(MonthGrid, CalmMotion.TabMs); } catch (Exception ex) { HandleError("월 이동", ex); } }
    private async void Today_Click(object sender, RoutedEventArgs e) => await ShowTodayAsync();
    private async Task ShowTodayAsync()
    {
        if (busy || !await ConfirmPendingAsync()) return;
        if(recordToolsOpen && !await CloseRecordToolsAsync())return;
        if(statisticsOpen)await CloseStatisticsAnimatedAsync();
        var today = LocalToday; var targetMonth = new DateOnly(today.Year, today.Month, 1);
        if (month != targetMonth || displayedCalendarMonth != targetMonth)
        {
            var oldMonth = month; month = targetMonth;
            try { await RefreshCalendarAsync(); }
            catch (Exception ex) { month = oldMonth; HandleError("오늘 보기", ex); return; }
        }
        // Avoid rebuilding the calendar after the slide has finished, and confirm drafts only once.
        await LoadDateAndOpenAsync(today);
    }
    private async void CloseDetails_Click(object sender, RoutedEventArgs e) { if (busy || !await ConfirmPendingAsync()) return; await CloseDetailsAnimatedAsync(); }
    private const double FixedWindowWidth = 900, FixedWindowHeight = 880;
    internal bool VerificationMode=>verify;
    private void ApplyWindowLayout(bool openDetails = false)
    {
        CancelSidebarMotion(true);
        applyingMode = true;
        try
        {
            using var noReentry = Dispatcher.DisableProcessing();
            // Keep old preference fields readable, but never restore the removed compact mode or pin.
            settings.Compact = false; settings.Pinned = false; Topmost = false;
            var area = WindowEnvironment.WorkArea(this);
            var size = FixedWindowSize(area.Width, area.Height);
            MinWidth = MinHeight = 0; MaxWidth = MaxHeight = double.PositiveInfinity;
            Width = size.Width; Height = size.Height;
            MinWidth = MaxWidth = size.Width; MinHeight = MaxHeight = size.Height;
            ResizeMode = ResizeMode.CanMinimize;
            settings.GeneralWidth = size.Width; settings.GeneralHeight = size.Height;
            if (openDetails) { Details.Visibility = Visibility.Visible; DetailColumn.Width = new GridLength(330); }
            if (!verify && WindowState == WindowState.Normal && double.IsFinite(Left) && double.IsFinite(Top))
            {
                var position = WindowPlacement.Fit(Left, Top, Width, Height, area.Left, area.Top, area.Width, area.Height);
                Left = position.Left; Top = position.Top;
            }
            FitHeader();
        }
        finally { applyingMode = false; QueueCalendarFit(); }
    }
    private static (double Width, double Height) FixedWindowSize(double availableWidth, double availableHeight)
        => (Math.Min(FixedWindowWidth, availableWidth), Math.Min(FixedWindowHeight, availableHeight));

    private void FitHeader()
    {
        if (TopActions is null) return;
        var stacked = Width < 570;
        Grid.SetRow(TopActions, stacked ? 1 : 0); Grid.SetColumn(TopActions, stacked ? 0 : 1); Grid.SetColumnSpan(TopActions, stacked ? 2 : 1);
        TopActions.Margin = new Thickness(0, stacked ? 5 : 0, 0, 0);
    }

    private bool fitQueued;
    private void Calendar_SizeChanged(object sender, SizeChangedEventArgs e) => QueueCalendarFit();
    private void Calendar_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        SynchronizeWeekdays();
    }
    private void QueueCalendarFit()
    {
        if(sidebarPanActive && !datePanActive){SynchronizeWeekdays();return;}
        if (fitQueued || applyingMode) return;
        fitQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () => { fitQueued = false; if(!sidebarPanActive || datePanActive)FitCalendar(); });
    }
    private void FitCalendar()
    {
        if (CalendarViewport is null || CalendarViewport.ActualWidth < 1) return;
        // Reserve horizontal scrollbar space consistently; all week rows must fit vertically.
        var available = Math.Max(1, CalendarViewport.ActualWidth + DetailColumn.ActualWidth - 1);
        var availableHeight = Math.Max(1, CalendarViewport.ActualHeight - SystemParameters.HorizontalScrollBarHeight - 1);
        var dpi = VisualTreeHelper.GetDpi(MonthGrid);
        var metrics = CalendarCellMetrics(available, availableHeight, weeks, dpi.DpiScaleX, dpi.DpiScaleY);
        MonthGrid.Width = WeekDays.Width = metrics.Width * 7; MonthGrid.Height = metrics.Height * weeks;
        foreach (Button cell in MonthGrid.Children)
        {
            cell.Margin = new Thickness(metrics.GapX, metrics.GapY, metrics.GapX, metrics.GapY);
            cell.Width = metrics.Width - 2 * metrics.GapX; cell.Height = metrics.Height - 2 * metrics.GapY;
            cell.HorizontalAlignment = HorizontalAlignment.Left; cell.VerticalAlignment = VerticalAlignment.Top;
            cell.ClipToBounds = true;
        }
        SynchronizeWeekdays();
        // Reserve this row even when the hint is unnecessary so sliding never changes cell height.
        CalendarScrollHint.Visibility = MonthGrid.Width > CalendarViewport.ActualWidth + 1 ? Visibility.Visible : Visibility.Hidden;
        MeditationCard.MaxWidth = Math.Max(160, CalendarArea.ActualWidth - 24);
        var height = Math.Max(MeditationCard.MinHeight, MeditationCard.ActualHeight);
        var headingBottom = CalendarViewport.TranslatePoint(new Point(0, 0), CalendarArea).Y;
        var top = Math.Max(headingBottom + 8, (CalendarArea.ActualHeight - height) / 2 - 64);
        MeditationCard.Margin = new Thickness(0, top, 0, 0);
    }

    private static (double Width, double Height, double GapX, double GapY) CalendarCellMetrics(double availableWidth, double availableHeight, int rows, double dpiX, double dpiY)
    {
        // Keep readable squares; grow horizontally when needed, but never require vertical scrolling.
        var widthPixels = Math.Max(92 * dpiX, availableWidth * dpiX / 7);
        var heightPixels = availableHeight * dpiY / rows;
        var pixels = Math.Max(1, Math.Floor(Math.Min(widthPixels, heightPixels)));
        var gapPixels = Math.Round(3 * dpiX, MidpointRounding.AwayFromZero);
        return (pixels / dpiX, pixels / dpiY, gapPixels / dpiX, gapPixels / dpiY);
    }

    private void Theme_Click(object sender, RoutedEventArgs e) { settings.Dark = !settings.Dark; ApplyTheme(); DrawCalendar(); DrawPlans(); DrawWorkouts(); DrawStatistics(); PersistSettings(); }
    private void ApplyTheme()
    {
        var palette = settings.Dark ? new[] { "#22222B", "#F5F4FA", "#C0BDCB", "#3D3B49", "#2A2A35", "#FFACC0", "#AAC7FF", "#18181F" } : new[] { "#FFFFFF", "#292831", "#666371", "#E7E5ED", "#F3F3F7", "#BE2F50", "#315FA5", "#F7F7FA" };
        var keys = new[] { "Paper", "Ink", "Muted", "Line", "Soft", "Sunday", "Saturday", "Canvas" };
        for (var i = 0; i < keys.Length; i++) SetBrush(keys[i], palette[i]);
        Color accent;
        try { accent = (Color)ColorConverter.ConvertFromString(settings.Accent); } catch (FormatException) { accent = (Color)ColorConverter.ConvertFromString("#E3BACA"); settings.Accent = "#E3BACA"; }
        Resources["Accent"] = new SolidColorBrush(accent);
        var accentHex = $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
        SetBrush("SelectedSaturday", SelectedCalendarColors.Saturday(accentHex));
        SetBrush("SelectedSunday", SelectedCalendarColors.Sunday(accentHex));
        SetBrush("AccentInk", SelectedCalendarColors.Ink(accentHex));
        var noteBase = (Color)ColorConverter.ConvertFromString(settings.Dark ? "#18181F" : "#FFFFFF");
        var ratio = settings.Dark ? 0.10 : 0.22;
        Resources["Note"] = new SolidColorBrush(Color.FromRgb((byte)Math.Round(accent.R * ratio + noteBase.R * (1 - ratio)), (byte)Math.Round(accent.G * ratio + noteBase.G * (1 - ratio)), (byte)Math.Round(accent.B * ratio + noteBase.B * (1 - ratio))));
        ThemeButton.Content = settings.Dark ? "라이트 모드" : "다크 모드";
        recordToolsView?.RefreshTheme();
        RefreshNavigationVisuals(); CalmMotion.RefreshShadows(this);
        if (MoneyList is not null) DrawJournal();
        if (StudyAttachmentDraftList is not null) DrawStudyAttachmentDraft();
    }
    private void SetBrush(string key, string color) => Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private Brush ResourceBrush(string key) => (Brush)FindResource(key);

    private void ShowMeditationIfNeeded()
    {
        var today = EntryToday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!settings.MeditationEnabled || settings.MeditationDate == today || settings.MeditationShownDates.Contains(today) || !IsVisible || statisticsOpen || recordToolsOpen) return;
        OpenMeditation(); settings.MeditationDate = today; settings.MeditationShownDates.Add(today); PersistSettings();
    }
    private void DismissMeditation_Click(object sender, RoutedEventArgs e) => CloseMeditation();
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new Window { Owner = this, Title = "설정", Width = 430, Height = Math.Min(570, SystemParameters.WorkArea.Height), ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        foreach (var key in new[] { "Paper", "Ink", "Muted", "Line", "Soft", "Accent", "AccentInk" }) dialog.Resources[key] = FindResource(key);
        WindowEnvironment.Watch(dialog);
        var content = new StackPanel { Margin = new Thickness(22) };
        var meditation = new CheckBox { Content = "하루 첫 사용 시 5분 명상 안내", IsChecked = settings.MeditationEnabled };
        meditation.Click += (_, _) => { settings.MeditationEnabled = meditation.IsChecked == true; if (!settings.MeditationEnabled) CloseMeditation(); PersistSettings(); };
        var color = new Button { Content = "강조색 선택", HorizontalAlignment = HorizontalAlignment.Left };
        color.Click += (_, _) =>
        {
            using var picker = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(settings.Accent) };
            if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            settings.Accent = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}"; ApplyTheme(); DrawCalendar(); DrawPlans(); DrawWorkouts(); DrawStatistics(); PersistSettings();
        };
        var reset = new Button { Content = "기본 분홍으로", HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => { settings.Accent = "#E3BACA"; ApplyTheme(); DrawCalendar(); DrawPlans(); DrawWorkouts(); DrawStatistics(); PersistSettings(); };
        var close = new Button { Content = "닫기", HorizontalAlignment = HorizontalAlignment.Right }; close.Click += (_, _) => dialog.Close();
        var weightStatus = new TextBlock { Text = DescribeWeight(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 4) };
        var weight = new Button { Content = "체중 등록·다시 입력", HorizontalAlignment = HorizontalAlignment.Left };
        weight.Click += (_, _) => { ShowWeightDialog(dialog); weightStatus.Text = DescribeWeight(); };
        var autoStart = new CheckBox { Content = "Windows 로그인 시 자동 실행", IsChecked = settings.AutoStart, IsEnabled = allowSystemStartup, Margin = new Thickness(0, 10, 0, 6) };
        var startupStatus = new TextBlock { Text = DescribeStartupRegistration(), Foreground = ResourceBrush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        autoStart.Click += (_, _) =>
        {
            try { if (allowSystemStartup) StartupRegistration.Apply(autoStart.IsChecked == true); settings.AutoStart = autoStart.IsChecked == true; PersistSettings(); startupStatus.Text = DescribeStartupRegistration(); }
            catch (Exception ex) { autoStart.IsChecked = settings.AutoStart; HandleError("자동 실행 설정", ex); }
        };
        var export = new Button { Content = "백업 파일 내보내기", HorizontalAlignment = HorizontalAlignment.Left };
        export.Click += async (_, _) => { dialog.Close(); await ExportBackupAsync(); };
        var restore = new Button { Content = "백업 파일로 복원", HorizontalAlignment = HorizontalAlignment.Left };
        restore.Click += async (_, _) => { dialog.Close(); await RestoreBackupAsync(); };
        content.Children.Add(meditation); content.Children.Add(autoStart); content.Children.Add(startupStatus); content.Children.Add(color); content.Children.Add(reset); content.Children.Add(weightStatus); content.Children.Add(weight);
        content.Children.Add(new TextBlock { Text = SystemParameters.ClientAreaAnimation ? "부드러운 전환 켜짐 · Windows 설정을 따라요." : "부드러운 전환 꺼짐 · Windows 설정을 따라요.",
            Foreground = ResourceBrush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "Windows 설정 → 접근성 → 시각 효과 → 애니메이션 효과에서 변경할 수 있어요." });
        content.Children.Add(new TextBlock { Text = "한국 공휴일: 2025~2027년 자료 포함 · 2026.10.17 확인. 새 임시공휴일·다음 연도는 자료 업데이트가 필요해요.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        content.Children.Add(new TextBlock { Text = "개인 기록: " + repository.DatabasePath + "\n백업 형식: PDS v8 · 학습 시간·휴지통 첨부 포함 · 데이터 형식: 12", TextWrapping = TextWrapping.Wrap, Foreground = ResourceBrush("Muted"), Margin = new Thickness(0, 10, 0, 0) });
        content.Children.Add(new TextBlock { Text = "수동 백업 · 내보내도 기록은 유지돼요.\n복원은 확인 후 현재 기록 전체를 교체해요.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 8) });
        content.Children.Add(export); content.Children.Add(restore); content.Children.Add(close); dialog.Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.Loaded += (_, _) => CalmMotion.Reveal((FrameworkElement)dialog.Content, CalmMotion.DialogMs); dialog.ShowDialog();
    }

    private void CreateTray()
    {
        if (verify && !verifyTray && !verifyEndurance) return;
        try
        {
        var uri = new Uri("pack://application:,,,/Pds.Desktop;component/Assets/Pds.ico");
        using var stream = System.Windows.Application.GetResourceStream(uri)!.Stream;
        using var icon = new System.Drawing.Icon(stream);
        Icon = BitmapFrame.Create(uri);
        tray = new NativeTrayIcon(icon, verifyTray || verifyEndurance ? "PDS 검사 · 임시 아이콘" : "PDS · 클릭하면 열기", repository.DatabasePath);
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var menuFont = new System.Drawing.Font("Malgun Gothic", 11);
        menu.Font = menuFont; menu.Disposed += (_, _) => menuFont.Dispose();
        menu.Items.Add("앱 열기", null, (_, _) => Dispatcher.Invoke(RestoreWindow));
        menu.Items.Add("오늘 보기", null, (_, _) => Dispatcher.Invoke(() => { RestoreWindow(); Today_Click(this, new RoutedEventArgs()); }));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("완전히 종료", null, (_, _) => Dispatcher.Invoke(() => Exit_Click(this, new RoutedEventArgs())));
        tray.ContextMenuStrip = menu;
        tray.Activated += (_, _) => Dispatcher.Invoke(RestoreWindow);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.ComponentModel.Win32Exception)
        { tray?.Dispose(); tray = null; }
    }
    internal void RestoreWindow() { if (exiting) return; Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; if (!verify) Activate(); QueueMaintenance(); RequestEntryGuidance(); }
    internal void ReportActivationUnavailable() => Status.Text = "다시 실행해서 열기 연결을 준비하지 못했어요. 트레이 아이콘으로 열어 주세요.";
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        if (busy || recordToolsView?.IsBusy==true) { Status.Text = "기록 처리가 끝난 뒤 닫아 주세요."; return; }
        RememberWindowPosition(); PersistSettings();
        var registered = verify && !verifyTray && !verifyEndurance || tray?.TryEnsureRegistered() == true;
        if (verify && trayAvailabilityOverride is bool available) registered = available;
        if (!registered) { ShowInTaskbar = true; WindowState = WindowState.Minimized; Status.Text = "트레이 표시를 확인하지 못해 작업 표시줄에 최소화했어요. 작업 표시줄의 PDS를 누르면 열려요."; return; }
        Hide();
        if (!verify && !settings.TrayHintShown && tray is not null)
        {
            tray.ShowBalloonTip(4000, "PDS는 실행 중이에요", "시계 옆의 분홍 P 아이콘을 누르세요. 앱을 다시 실행해도 이 창이 열려요.", System.Windows.Forms.ToolTipIcon.Info);
            settings.TrayHintShown = true; PersistSettings();
        }
    }
    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !await ConfirmPendingAsync()) return;
        RememberWindowPosition(); PersistSettings(); exiting = true; StopMaintenance(); meditationTimer.Stop(); tray?.Dispose(); Close(); System.Windows.Application.Current.Shutdown();
    }
    private void RememberWindowPosition()
    {
        if (applyingMode || WindowState != WindowState.Normal) return;
        settings.Compact = false; settings.Pinned = false;
        settings.GeneralWidth = ActualWidth; settings.GeneralHeight = ActualHeight;
        settings.Left = Left; settings.Top = Top;
    }
    private void PersistSettings()
    {
        try { settingsStore.Save(settings); }
        catch (Exception ex) { Status.Text = "설정을 기억하지 못했어요. " + ex.Message; }
    }
    private void HandleError(string action, Exception ex)
    {
        Status.Text = action + "에 실패했어요. 입력은 유지됩니다.";
        if (!verify && IsVisible) System.Windows.MessageBox.Show(this, ex.Message, action + " 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task VerifyAsync()
    {
        try
        {
            if (verifyReopen)
            {
                var restored = repository.Load(selected);
                if (restored.Plans.Count != 1 || !restored.Plans[0].Done || restored.Plans[0].Title != "수정과 완료 저장 확인" || settings.Compact)
                    throw new InvalidOperationException($"Restart mismatch: plans={restored.Plans.Count}, done={restored.Plans.FirstOrDefault()?.Done}, title={restored.Plans.FirstOrDefault()?.Title}, compact={settings.Compact}");
                MeditationCard.Visibility = Visibility.Collapsed;
                await VerifyExistingBehaviorAsync();
                await VerifyPresentationAsync();
                settings.Compact = false; ApplyWindowLayout(); DetailColumn.Width = new GridLength(330); Details.Visibility = Visibility.Visible; DrawCalendar(); SaveStatus.Text = "저장 완료";
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); CheckSquares(); Capture("general.png");
                ApplyWindowLayout(); Details.Visibility = Visibility.Collapsed; DetailColumn.Width = new GridLength(0); DrawCalendar();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); CheckSquares(); Capture("calendar.png");
                RememberWindowPosition(); PersistSettings();
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "reopen.json"), "{\"success\":true,\"savedRecordsPreserved\":true,\"fixedWindowRestored\":true}");
                FinishVerification(null); return;
            }
            await SelectDateAsync(selected); MeditationCard.Visibility = Visibility.Collapsed;
            TitleInput.Text = "시제품 저장 확인"; NoteInput.Text = "오늘 계획을 차분히 점검하기";
            if (!await SaveAsync() || repository.Load(selected).Plans.Count != 1) throw new InvalidOperationException("UI save failed");
            var id = plans[0].Id; plans[0] = plans[0] with { Done = true }; dirty = true;
            if (!await SaveAsync() || !repository.Load(selected).Plans[0].Done) throw new InvalidOperationException("Completion save failed");
            BeginEdit(id); TitleInput.Text = "수정과 완료 저장 확인";
            if (!await SaveAsync() || repository.Load(selected).Plans[0].Title != "수정과 완료 저장 확인") throw new InvalidOperationException("Edit save failed");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            CheckSquares(); SaveButton.Focus();
            await Task.Delay(20000);
            var process = Process.GetCurrentProcess(); process.Refresh();
            var cpuStart = process.TotalProcessorTime; var timer = Stopwatch.StartNew();
            await Task.Delay(10000); process.Refresh(); timer.Stop();
            var idleCpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / timer.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            var generalMemory = process.WorkingSet64 / 1048576d;
            var generalPrivateMemory = process.PrivateMemorySize64 / 1048576d;
            RememberWindowPosition(); ApplyWindowLayout(); Details.Visibility = Visibility.Collapsed; DetailColumn.Width = new GridLength(0); DrawCalendar();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            CheckSquares(); CalendarViewport.Focus();
            await Task.Delay(20000);
            process.Refresh(); cpuStart = process.TotalProcessorTime; timer.Restart();
            await Task.Delay(10000); process.Refresh(); timer.Stop();
            var calendarCpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / timer.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            var calendarMemory = process.WorkingSet64 / 1048576d;
            var calendarPrivateMemory = process.PrivateMemorySize64 / 1048576d;
            Hide(); process.Refresh(); cpuStart = process.TotalProcessorTime; timer.Restart();
            await Task.Delay(10000); process.Refresh(); timer.Stop();
            var trayCpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / timer.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            var trayMemory = process.WorkingSet64 / 1048576d;
            var trayPrivateMemory = process.PrivateMemorySize64 / 1048576d;
            PersistSettings(); TitleInput.Text = "미저장 내용";
            if (!Pending || repository.Load(selected).Plans.Count != 1) throw new InvalidOperationException("Draft separation failed");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "verification.json"), JsonSerializer.Serialize(new { success = true, processStartToReadyMs = Math.Round(processReadyMs), windowConstructionToReadyMs = startup.ElapsedMilliseconds, generalWorkingSetMB = Math.Round(generalMemory, 1), generalPrivateMB = Math.Round(generalPrivateMemory, 1), generalIdleCpuPercent = Math.Round(idleCpu, 3), calendarWorkingSetMB = Math.Round(calendarMemory, 1), calendarPrivateMB = Math.Round(calendarPrivateMemory, 1), calendarIdleCpuPercent = Math.Round(calendarCpu, 3), trayWorkingSetMB = Math.Round(trayMemory, 1), trayPrivateMB = Math.Round(trayPrivateMemory, 1), trayIdleCpuPercent = Math.Round(trayCpu, 3), warmupBeforeGeneralSeconds = 20, warmupAfterClosingSidebarSeconds = 20, idleSampleSeconds = 10, focus = "save button / calendar, no active text caret", logicalProcessors = Environment.ProcessorCount, database = repository.DatabasePath }, new JsonSerializerOptions { WriteIndented = true }));
            ClearEditor(); Show(); settings.Compact = false; ApplyWindowLayout(); DetailColumn.Width = new GridLength(330); Details.Visibility = Visibility.Visible; DrawCalendar();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("general.png");
            await VerifyStatisticsPerformanceAsync();
            ApplyWindowLayout(); Details.Visibility = Visibility.Collapsed; DetailColumn.Width = new GridLength(0); DrawCalendar();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("calendar.png");
            RememberWindowPosition(); PersistSettings();
            FinishVerification(null);
        }
        catch (Exception ex) { FinishVerification(ex); }
    }
    private async Task VerifyExistingBehaviorAsync()
    {
        var saved = repository.Load(selected);
        var initialDark = settings.Dark;
        TitleInput.Text = "숨김과 테마 전환 중 유지할 입력";
        Theme_Click(this, new RoutedEventArgs());
        if (TitleInput.Text != "숨김과 테마 전환 중 유지할 입력" || !Pending)
            throw new InvalidOperationException("Theme change lost pending input");
        Close();
        if (IsVisible || !Pending || TitleInput.Text != "숨김과 테마 전환 중 유지할 입력")
            throw new InvalidOperationException("Close-to-tray lost pending input");
        RestoreWindow();
        if (!IsVisible || TitleInput.Text != "숨김과 테마 전환 중 유지할 입력")
            throw new InvalidOperationException("Restore lost pending input");
        ClearEditor();
        NoteInput.Text = "제목 없이 저장할 수 없는 메모";
        if (await SaveAsync() || NoteInput.Text != "제목 없이 저장할 수 없는 메모")
            throw new InvalidOperationException("Invalid title did not preserve input");
        ClearEditor();
        TitleInput.Text = "거꾸로 된 시간 검증"; TimeEnabled.IsChecked = true; StartInput.Text = "10:00"; EndInput.Text = "09:00";
        if (await SaveAsync() || TitleInput.Text != "거꾸로 된 시간 검증" || StartInput.Text != "10:00" || EndInput.Text != "09:00")
            throw new InvalidOperationException("Invalid time did not preserve input");
        var after = repository.Load(selected);
        if (after.Revision != saved.Revision || !after.Plans.SequenceEqual(saved.Plans))
            throw new InvalidOperationException("Failed saves changed persisted records");
        ClearEditor(); settings.Dark = initialDark; ApplyTheme(); DrawPlans(); PersistSettings();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "behavior.json"), JsonSerializer.Serialize(new { closeHidesWindow = true, restoreKeepsDraft = true, themeKeepsDraft = true, invalidTitleKeepsInput = true, invalidTimeKeepsInput = true, failedSavesPreserveDatabase = true, settingsPersisted = settingsStore.Load().Dark == initialDark && settingsStore.Load().Accent == settings.Accent }));
    }

    private async Task VerifyPresentationAsync()
    {
        var face = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        if (!face.TryGetGlyphTypeface(out var glyphs) || !glyphs.FontUri.ToString().Contains("malgun", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Malgun Gothic did not resolve: family={FontFamily.Source}, resolved={glyphs?.FontUri}");
        var originalMonth = month;
        var samples = new List<object>();
        foreach (var details in new[] { false, true })
        foreach (var dark in new[] { false, true })
        foreach (var testMonth in new[] { new DateOnly(2021, 2, 1), new DateOnly(2026, 10, 1), new DateOnly(2020, 8, 1) })
        {
            ApplyWindowLayout(); settings.Dark = dark; ApplyTheme();
            Details.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
            DetailColumn.Width = new GridLength(details ? 330 : 0);
            month = testMonth; await RefreshCalendarAsync();
            var first = month.AddDays(-(((int)month.DayOfWeek + 6) % 7));
            counts = Enumerable.Range(0, weeks * 7).ToDictionary(i => first.AddDays(i), _ => new DayCount(12, 9));
            DrawCalendar(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); CheckSquares();
            foreach (Button cell in MonthGrid.Children)
            {
                var content = (Grid)cell.Content;
                var required = Enumerable.Range(0, content.RowDefinitions.Count).Sum(row => content.Children.OfType<FrameworkElement>()
                    .Where(child => Grid.GetRow(child) == row).Select(child => child.DesiredSize.Height).DefaultIfEmpty().Max());
                if (required > content.ActualHeight + 1) throw new InvalidOperationException("Fixed calendar text clipped");
            }
            var lastCell = (FrameworkElement)MonthGrid.Children[^1];
            var lastBottom = lastCell.TranslatePoint(new Point(0, lastCell.ActualHeight), CalendarViewport).Y;
            if (CalendarViewport.ScrollableHeight > 0.1 || CalendarViewport.VerticalOffset > 0.1 || lastBottom > CalendarViewport.ViewportHeight + 1)
                throw new InvalidOperationException($"All calendar week rows must fit without vertical scrolling: details={details}, dark={dark}, weeks={weeks}, last={lastBottom}, viewport={CalendarViewport.ViewportHeight}, actual={CalendarViewport.ActualHeight}, grid={MonthGrid.Height}, scrollable={CalendarViewport.ScrollableHeight}, offset={CalendarViewport.VerticalOffset}");
            if (details && SaveButton.TranslatePoint(new Point(), Details).Y + SaveButton.ActualHeight > Details.ActualHeight + 1)
                throw new InvalidOperationException("Save button outside fixed sidebar");
            samples.Add(new { details, dark, width = ActualWidth, height = ActualHeight, weeks, squareCells = true, labelsFit = true, allWeeksVisible = true, calendarVerticalScrolling = false });
            if (!details && !dark) Capture("fixed-calendar-" + weeks + "-weeks.png");
        }
        settings.Dark = false; ApplyTheme(); month = originalMonth; await RefreshCalendarAsync();
        ApplyWindowLayout(openDetails: true);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "presentation.json"), JsonSerializer.Serialize(new { resolvedFont = glyphs.FontUri.ToString(), accent = settings.Accent, bodySize = FontSize, displayFont = MonthTitle.FontFamily.Source, samples }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void CheckSquares()
    {
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(MonthGrid);
        var cells = MonthGrid.Children.OfType<Button>().ToArray();
        var reference = cells.First();
        foreach (FrameworkElement cell in MonthGrid.Children)
        {
            if (Math.Abs(cell.ActualWidth * dpi.DpiScaleX - cell.ActualHeight * dpi.DpiScaleY) > 0.01
                || Math.Abs(cell.ActualWidth - reference.ActualWidth) > 0.01 || Math.Abs(cell.ActualHeight - reference.ActualHeight) > 0.01)
                throw new InvalidOperationException("Calendar cells must have identical square dimensions");
            var button = (Button)cell;
            var border = (Border)button.Template.FindName("Cell", button);
            if (Math.Abs(border.ActualWidth - reference.ActualWidth) > 0.01 || Math.Abs(border.ActualHeight - reference.ActualHeight) > 0.01)
                throw new InvalidOperationException("Calendar background expanded with its contents");
        }
    }
    private void Capture(string name)
    {
        CalmMotion.Settle(this); UpdateLayout();
        var content = (FrameworkElement)Content;
        var target = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(ResourceBrush("Paper"), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        target.Render(background);
        target.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, name)); encoder.Save(file);
    }
    private void FinishVerification(Exception? error)
    {
        if (error is not null) File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "verification-error.txt"), error.ToString());
        exiting = true; StopMaintenance(); meditationTimer.Stop(); tray?.Dispose(); Close(); System.Windows.Application.Current.Shutdown(error is null ? 0 : 1);
    }
}
