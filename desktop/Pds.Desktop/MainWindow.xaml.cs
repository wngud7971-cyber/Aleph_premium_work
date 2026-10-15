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
    private readonly bool verify, verifyReopen;
    private System.Windows.Forms.NotifyIcon? tray;
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

    public MainWindow(PlanRepository repository, SettingsStore settingsStore, bool verify, bool verifyReopen = false)
    {
        this.repository = repository; this.settingsStore = settingsStore; this.verify = verify; this.verifyReopen = verifyReopen;
        snapshot = new(selected, 0, []);
        settings = settingsStore.Load();
        InitializeComponent();
        ApplyTheme(); ApplyMode();
        if (settings.Left is double left && double.IsFinite(left) && settings.Top is double top && double.IsFinite(top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Clamp(left, SystemParameters.WorkArea.Left, Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - Width));
            Top = Math.Clamp(top, SystemParameters.WorkArea.Top, Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - Height));
        }
        CreateTray();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Activated += (_, _) => { if (ready) ShowMeditationIfNeeded(); };
        SizeChanged += (_, _) => { if (ready && WindowState == WindowState.Normal) RememberSize(); };
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.S && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            { e.Handled = true; await SaveAsync(); }
            if (e.Key == System.Windows.Input.Key.Escape && MeditationCard.Visibility == Visibility.Visible) MeditationCard.Visibility = Visibility.Collapsed;
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loadedOnce) return;
        loadedOnce = true;
        try
        {
            await LoadSelectedAsync(); await RefreshCalendarAsync();
            ready = true; startup.Stop(); processReadyMs = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalMilliseconds;
            Status.Text = "날짜를 누르면 계획을 기록할 수 있어요.";
            ShowMeditationIfNeeded();
            if (verify) await VerifyAsync();
        }
        catch (Exception ex) { HandleError("앱 준비", ex); if (verify) FinishVerification(ex); }
    }

    private async Task LoadSelectedAsync()
    {
        busy = true; Body.IsEnabled = false;
        try
        {
            snapshot = await Task.Run(() => repository.Load(selected));
            plans = snapshot.Plans.ToList(); dirty = false;
            ClearEditor(); DrawPlans(); SelectedTitle.Text = selected.ToString("M월 d일 dddd", Korean);
            SaveStatus.Text = "선택한 날짜의 기록";
        }
        finally { busy = false; Body.IsEnabled = true; }
    }

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
            return result;
        });
        if (generation != refreshGeneration) return;
        weeks = requestedWeeks; counts = loaded;
        DrawCalendar();
    }

    private void DrawCalendar()
    {
        MonthTitle.Text = month.ToString("yyyy년 M월", Korean);
        WeekDays.Children.Clear(); MonthGrid.Children.Clear(); MonthGrid.Rows = weeks;
        var names = new[] { "월", "화", "수", "목", "금", "토", "일" };
        for (var i = 0; i < 7; i++) WeekDays.Children.Add(new TextBlock { Text = names[i], FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = ResourceBrush(i == 5 ? "Saturday" : i == 6 ? "Sunday" : "Muted") });
        var first = month.AddDays(-(((int)month.DayOfWeek + 6) % 7));
        for (var i = 0; i < weeks * 7; i++)
        {
            var date = first.AddDays(i); var isSelected = Details.Visibility == Visibility.Visible && date == selected;
            var weekend = date.DayOfWeek is DayOfWeek.Sunday or DayOfWeek.Saturday;
            var content = new StackPanel();
            var number = new TextBlock { Text = date.Day.ToString(Korean), FontSize = settings.Compact ? 12 : 14, Margin = new Thickness(0, 0, 0, settings.Compact ? 1 : 4), Foreground = ResourceBrush(date.DayOfWeek == DayOfWeek.Sunday ? "Sunday" : date.DayOfWeek == DayOfWeek.Saturday ? "Saturday" : isSelected ? "AccentInk" : date.Month == month.Month ? "Ink" : "Muted") };
            if (date == DateOnly.FromDateTime(DateTime.Today)) number.FontWeight = FontWeights.Bold;
            if (isSelected && weekend) number.Background = ResourceBrush("Paper");
            content.Children.Add(number);
            if (counts.TryGetValue(date, out var count))
                content.Children.Add(new TextBlock { Text = $"계획 {count.Plans}\n완료 {count.Done}", FontSize = 11, LineHeight = 13, Foreground = ResourceBrush(isSelected ? "AccentInk" : "Muted") });
            var button = new Button { Content = content, Margin = new Thickness(0), Padding = new Thickness(settings.Compact ? 4 : 6), BorderThickness = new Thickness(0, 0, 1, 1), Background = ResourceBrush(isSelected ? "Accent" : date.Month == month.Month ? "Paper" : "Soft"), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, ToolTip = date.ToString("yyyy년 M월 d일 dddd", Korean), Tag = date };
            button.Template = CalendarButtonTemplate();
            System.Windows.Automation.AutomationProperties.SetName(button, date.ToString("yyyy년 M월 d일 dddd", Korean) + (count is null ? "" : $", 계획 {count.Plans}건 완료 {count.Done}건"));
            button.Click += async (_, _) => await SelectDateAsync(date);
            MonthGrid.Children.Add(button);
        }
        var today = DateOnly.FromDateTime(DateTime.Today);
        var todayCount = counts.TryGetValue(today, out var value) ? value : new DayCount(0, 0);
        Summary.Text = $"오늘 계획 {todayCount.Plans} · 완료 {todayCount.Done}";
        FitCalendar();
    }

    private static ControlTemplate CalendarButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 1));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private async Task SelectDateAsync(DateOnly date)
    {
        if (busy || !await ConfirmPendingAsync()) return;
        selected = date;
        if (settings.Compact) { RememberSize(); settings.Compact = false; ApplyMode(); }
        DetailColumn.Width = new GridLength(312); Details.Visibility = Visibility.Visible;
        try { await LoadSelectedAsync(); DrawCalendar(); }
        catch (Exception ex) { HandleError("기록 불러오기", ex); }
    }

    private void DrawPlans()
    {
        PlanList.Children.Clear(); EmptyMessage.Visibility = plans.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var entry in plans)
        {
            var item = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var check = new CheckBox { Content = new TextBlock { Text = entry.Title, TextWrapping = TextWrapping.Wrap, MaxWidth = 210 }, IsChecked = entry.Done };
            check.Click += (_, _) => { var index = plans.FindIndex(p => p.Id == entry.Id); plans[index] = plans[index] with { Done = check.IsChecked == true }; MarkDirty(); };
            item.Children.Add(check);
            if (entry.Start is not null) item.Children.Add(new TextBlock { Text = $"{entry.Start}–{entry.End}", FontSize = 11, Foreground = ResourceBrush("Muted") });
            if (!string.IsNullOrEmpty(entry.Note)) item.Children.Add(new TextBlock { Text = entry.Note, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = ResourceBrush("Muted") });
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var edit = new Button { Content = "수정", FontSize = 11, Padding = new Thickness(6, 3, 6, 3) };
            edit.Click += (_, _) => BeginEdit(entry.Id);
            var delete = new Button { Content = "삭제", FontSize = 11, Padding = new Thickness(6, 3, 6, 3) };
            delete.Click += (_, _) =>
            {
                if (System.Windows.MessageBox.Show(this, "이 계획을 삭제할까요? 저장 버튼을 누르면 반영돼요.", "계획 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                plans.RemoveAll(p => p.Id == entry.Id); if (editing == entry.Id) ClearEditor(); DrawPlans(); MarkDirty();
            };
            actions.Children.Add(edit); actions.Children.Add(delete); item.Children.Add(actions);
            PlanList.Children.Add(item);
        }
    }

    private bool HasInput => !string.IsNullOrWhiteSpace(TitleInput.Text) || !string.IsNullOrWhiteSpace(NoteInput.Text) || TimeEnabled.IsChecked == true || editing is not null;
    private bool Pending => dirty || HasInput;

    private void BeginEdit(Guid id)
    {
        if (HasInput && System.Windows.MessageBox.Show(this, "현재 입력을 버리고 이 계획을 수정할까요?", "입력 확인", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var entry = plans.Single(p => p.Id == id); suppress = true; editing = id;
        TitleInput.Text = entry.Title; NoteInput.Text = entry.Note; TimeEnabled.IsChecked = entry.Start is not null;
        StartInput.Text = entry.Start ?? ""; EndInput.Text = entry.End ?? ""; TimeFields.Visibility = entry.Start is null ? Visibility.Collapsed : Visibility.Visible;
        EditorTitle.Text = "계획 수정"; CancelEditButton.Visibility = Visibility.Visible; suppress = false; UpdateDraftStatus(); TitleInput.Focus();
    }

    private void ClearEditor()
    {
        suppress = true; editing = null; TitleInput.Clear(); NoteInput.Clear(); StartInput.Clear(); EndInput.Clear(); TimeEnabled.IsChecked = false;
        TimeFields.Visibility = Visibility.Collapsed; EditorTitle.Text = "새 계획"; CancelEditButton.Visibility = Visibility.Collapsed; suppress = false;
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
                if (editing is Guid id) entry = entry with { Id = id, Done = plans.Single(p => p.Id == id).Done };
                entry.Validate();
                if (editing is Guid editId) candidate[candidate.FindIndex(p => p.Id == editId)] = entry; else candidate.Add(entry);
            }
            busy = true; Body.IsEnabled = false; SaveStatus.Text = "저장 중…";
            var saved = await Task.Run(() => repository.Save(snapshot with { Plans = candidate }));
            snapshot = saved; plans = saved.Plans.ToList(); dirty = false; ClearEditor(); DrawPlans();
            await RefreshCalendarAsync(); SaveStatus.Text = "저장 완료"; Status.Text = "기록을 저장했어요."; return true;
        }
        catch (Exception ex) { HandleError("저장", ex); SaveStatus.Text = "저장하지 못했어요. 입력은 유지돼요."; return false; }
        finally { busy = false; Body.IsEnabled = true; }
    }

    private async Task<bool> ConfirmPendingAsync()
    {
        if (!Pending) return true;
        var result = System.Windows.MessageBox.Show(this, "저장하지 않은 변경이 있어요.\n저장할까요? ‘아니요’는 변경을 버립니다.", "입력 확인", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.Yes) return await SaveAsync();
        plans = snapshot.Plans.ToList(); dirty = false; ClearEditor(); DrawPlans(); return true;
    }

    private void MarkDirty() { if (!suppress && ready) { dirty = true; SaveStatus.Text = "저장하지 않은 변경"; } }
    private void UpdateDraftStatus() { if (!suppress && ready) SaveStatus.Text = Pending ? "저장하지 않은 변경" : "선택한 날짜의 기록"; }
    private void Editor_Changed(object sender, TextChangedEventArgs e) { if (IsInitialized) UpdateDraftStatus(); }
    private void Time_Changed(object sender, RoutedEventArgs e) { if (TimeFields is null) return; TimeFields.Visibility = TimeEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; UpdateDraftStatus(); }
    private void CancelEdit_Click(object sender, RoutedEventArgs e) { ClearEditor(); SaveStatus.Text = dirty ? "저장하지 않은 변경" : "선택한 날짜의 기록"; }
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();
    private async void Previous_Click(object sender, RoutedEventArgs e) => await ShiftMonthAsync(-1);
    private async void Next_Click(object sender, RoutedEventArgs e) => await ShiftMonthAsync(1);
    private async Task ShiftMonthAsync(int offset) { if (busy) return; try { month = month.AddMonths(offset); await RefreshCalendarAsync(); } catch (Exception ex) { HandleError("월 이동", ex); } }
    private async void Today_Click(object sender, RoutedEventArgs e) { if (busy) return; var today = DateOnly.FromDateTime(DateTime.Today); await SelectDateAsync(today); month = new(today.Year, today.Month, 1); try { await RefreshCalendarAsync(); } catch (Exception ex) { HandleError("오늘 보기", ex); } }
    private async void CloseDetails_Click(object sender, RoutedEventArgs e) { if (busy || !await ConfirmPendingAsync()) return; DetailColumn.Width = new GridLength(0); Details.Visibility = Visibility.Collapsed; DrawCalendar(); }
    private async void Mode_Click(object sender, RoutedEventArgs e) { if (busy || !await ConfirmPendingAsync()) return; RememberSize(); settings.Compact = !settings.Compact; ApplyMode(); DrawCalendar(); PersistSettings(); }
    private void Pin_Click(object sender, RoutedEventArgs e) { settings.Pinned = !settings.Pinned; Topmost = settings.Compact && settings.Pinned; PinButton.Content = settings.Pinned ? "고정 켜짐" : "고정 꺼짐"; PersistSettings(); }

    private void ApplyMode()
    {
        applyingMode = true;
        try
        {
        var area = SystemParameters.WorkArea;
        var width = settings.Compact ? settings.CompactWidth : settings.GeneralWidth;
        var height = settings.Compact ? settings.CompactHeight : settings.GeneralHeight;
        MinWidth = Math.Min(area.Width, settings.Compact ? 420 : 900); MinHeight = Math.Min(area.Height, settings.Compact ? 450 : 620);
        MaxWidth = area.Width; MaxHeight = area.Height;
        Width = Math.Clamp(double.IsFinite(width) ? width : 940, MinWidth, MaxWidth);
        Height = Math.Clamp(double.IsFinite(height) ? height : 700, MinHeight, MaxHeight);
        ModeButton.Content = settings.Compact ? "일반 창" : "소형 모드";
        PinButton.Visibility = settings.Compact ? Visibility.Visible : Visibility.Collapsed;
        Tagline.Visibility = settings.Compact ? Visibility.Collapsed : Visibility.Visible;
        Topmost = settings.Compact && settings.Pinned; PinButton.Content = settings.Pinned ? "고정 켜짐" : "고정 꺼짐";
        MeditationCard.Width = settings.Compact ? 380 : 420; MeditationCard.MinHeight = settings.Compact ? 144 : 160;
        if (settings.Compact) { Details.Visibility = Visibility.Collapsed; DetailColumn.Width = new GridLength(0); }
        FitCalendar();
        }
        finally { applyingMode = false; }
    }

    private void Calendar_SizeChanged(object sender, SizeChangedEventArgs e) => FitCalendar();
    private void FitCalendar()
    {
        if (CalendarViewport is null || CalendarViewport.ActualWidth < 1) return;
        var side = Math.Max(settings.Compact ? 48 : 76, Math.Min((CalendarViewport.ActualWidth - 20) / 7, (CalendarViewport.ActualHeight - 30) / Math.Max(1, weeks)));
        side = Math.Floor(side); MonthGrid.Width = WeekDays.Width = side * 7; MonthGrid.Height = side * weeks;
        MeditationCard.MaxWidth = Math.Max(160, CalendarArea.ActualWidth - 24);
        var height = Math.Max(MeditationCard.MinHeight, MeditationCard.ActualHeight);
        var headingBottom = CalendarViewport.TranslatePoint(new Point(0, 0), CalendarArea).Y + WeekDays.ActualHeight;
        var top = Math.Max(headingBottom + 8, (CalendarArea.ActualHeight - height) / 2 - (settings.Compact ? 28 : 64));
        MeditationCard.Margin = new Thickness(0, top, 0, 0);
    }

    private void Theme_Click(object sender, RoutedEventArgs e) { settings.Dark = !settings.Dark; ApplyTheme(); DrawCalendar(); DrawPlans(); PersistSettings(); }
    private void ApplyTheme()
    {
        var palette = settings.Dark ? new[] { "#171717", "#FFFFFF", "#D0D0D0", "#484848", "#242424", "#FF746B", "#80AFFF" } : new[] { "#FFFFFF", "#161616", "#545454", "#D6D6D6", "#F5F5F5", "#DF251A", "#2161C4" };
        var keys = new[] { "Paper", "Ink", "Muted", "Line", "Soft", "Sunday", "Saturday" };
        for (var i = 0; i < keys.Length; i++) SetBrush(keys[i], palette[i]);
        Color accent;
        try { accent = (Color)ColorConverter.ConvertFromString(settings.Accent); } catch (FormatException) { accent = (Color)ColorConverter.ConvertFromString("#FFEEA0"); settings.Accent = "#FFEEA0"; }
        Resources["Accent"] = new SolidColorBrush(accent);
        var light = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255;
        SetBrush("AccentInk", light > 0.55 ? "#161616" : "#FFFFFF");
        ThemeButton.Content = settings.Dark ? "라이트 모드" : "다크 모드";
    }
    private void SetBrush(string key, string color) => Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private Brush ResourceBrush(string key) => (Brush)FindResource(key);

    private void ShowMeditationIfNeeded()
    {
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!settings.MeditationEnabled || settings.MeditationDate == today || !IsVisible) return;
        MeditationCard.Visibility = Visibility.Visible; settings.MeditationDate = today; FitCalendar(); PersistSettings();
    }
    private void DismissMeditation_Click(object sender, RoutedEventArgs e) => MeditationCard.Visibility = Visibility.Collapsed;
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window { Owner = this, Title = "설정", Width = 380, Height = 290, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        foreach (var key in new[] { "Paper", "Ink", "Muted", "Line", "Soft", "Accent", "AccentInk" }) dialog.Resources[key] = FindResource(key);
        var content = new StackPanel { Margin = new Thickness(22) };
        var meditation = new CheckBox { Content = "하루 첫 사용 시 5분 명상 안내", IsChecked = settings.MeditationEnabled };
        meditation.Click += (_, _) => { settings.MeditationEnabled = meditation.IsChecked == true; if (!settings.MeditationEnabled) MeditationCard.Visibility = Visibility.Collapsed; PersistSettings(); };
        var color = new Button { Content = "강조색 선택", HorizontalAlignment = HorizontalAlignment.Left };
        color.Click += (_, _) =>
        {
            using var picker = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(settings.Accent) };
            if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            settings.Accent = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}"; ApplyTheme(); DrawCalendar(); DrawPlans(); PersistSettings();
        };
        var reset = new Button { Content = "파스텔 노랑으로", HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => { settings.Accent = "#FFEEA0"; ApplyTheme(); DrawCalendar(); DrawPlans(); PersistSettings(); };
        var close = new Button { Content = "닫기", HorizontalAlignment = HorizontalAlignment.Right }; close.Click += (_, _) => dialog.Close();
        content.Children.Add(meditation); content.Children.Add(color); content.Children.Add(reset); content.Children.Add(close); dialog.Content = content; dialog.ShowDialog();
    }

    private void CreateTray()
    {
        tray = new System.Windows.Forms.NotifyIcon { Text = "PDS · 하루를 가볍게", Icon = System.Drawing.SystemIcons.Application, Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("앱 열기", null, (_, _) => Dispatcher.Invoke(RestoreWindow));
        menu.Items.Add("오늘 보기", null, (_, _) => Dispatcher.Invoke(() => { RestoreWindow(); Today_Click(this, new RoutedEventArgs()); }));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("완전히 종료", null, (_, _) => Dispatcher.Invoke(() => Exit_Click(this, new RoutedEventArgs())));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreWindow);
    }
    private void RestoreWindow() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        if (busy) { Status.Text = "기록 처리가 끝난 뒤 닫아 주세요."; return; }
        RememberSize(); PersistSettings(); Hide();
    }
    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !await ConfirmPendingAsync()) return;
        RememberSize(); PersistSettings(); exiting = true; tray?.Dispose(); Close(); System.Windows.Application.Current.Shutdown();
    }
    private void RememberSize()
    {
        if (applyingMode || WindowState != WindowState.Normal) return;
        if (settings.Compact) { settings.CompactWidth = ActualWidth; settings.CompactHeight = ActualHeight; }
        else { settings.GeneralWidth = ActualWidth; settings.GeneralHeight = ActualHeight; }
        settings.Left = Left; settings.Top = Top;
    }
    private void PersistSettings()
    {
        try { settingsStore.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = "설정을 기억하지 못했어요. " + ex.Message; }
    }
    private void HandleError(string action, Exception ex)
    {
        Status.Text = action + "에 실패했어요. 입력은 유지됩니다.";
        if (!verify) System.Windows.MessageBox.Show(this, ex.Message, action + " 확인", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task VerifyAsync()
    {
        try
        {
            if (verifyReopen)
            {
                var restored = repository.Load(selected);
                if (restored.Plans.Count != 1 || !restored.Plans[0].Done || restored.Plans[0].Title != "수정과 완료 저장 확인" || !settings.Compact)
                    throw new InvalidOperationException($"Restart mismatch: plans={restored.Plans.Count}, done={restored.Plans.FirstOrDefault()?.Done}, title={restored.Plans.FirstOrDefault()?.Title}, compact={settings.Compact}");
                MeditationCard.Visibility = Visibility.Collapsed;
                settings.Compact = false; ApplyMode(); DetailColumn.Width = new GridLength(312); Details.Visibility = Visibility.Visible; DrawCalendar(); SaveStatus.Text = "저장 완료";
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); CheckSquares(); Capture("general.png");
                settings.Compact = true; ApplyMode(); DrawCalendar();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); CheckSquares(); Capture("compact.png");
                RememberSize(); PersistSettings();
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "reopen.json"), "{\"success\":true,\"savedRecordsPreserved\":true,\"compactModeRestored\":true}");
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
            RememberSize(); settings.Compact = true; ApplyMode(); DrawCalendar();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            CheckSquares(); ModeButton.Focus();
            await Task.Delay(20000);
            process.Refresh(); cpuStart = process.TotalProcessorTime; timer.Restart();
            await Task.Delay(10000); process.Refresh(); timer.Stop();
            var compactCpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / timer.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            var compactMemory = process.WorkingSet64 / 1048576d;
            var compactPrivateMemory = process.PrivateMemorySize64 / 1048576d;
            Hide(); process.Refresh(); cpuStart = process.TotalProcessorTime; timer.Restart();
            await Task.Delay(10000); process.Refresh(); timer.Stop();
            var trayCpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / timer.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            var trayMemory = process.WorkingSet64 / 1048576d;
            var trayPrivateMemory = process.PrivateMemorySize64 / 1048576d;
            PersistSettings(); TitleInput.Text = "미저장 내용";
            if (!Pending || repository.Load(selected).Plans.Count != 1) throw new InvalidOperationException("Draft separation failed");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "verification.json"), JsonSerializer.Serialize(new { success = true, processStartToReadyMs = Math.Round(processReadyMs), windowConstructionToReadyMs = startup.ElapsedMilliseconds, generalWorkingSetMB = Math.Round(generalMemory, 1), generalPrivateMB = Math.Round(generalPrivateMemory, 1), generalIdleCpuPercent = Math.Round(idleCpu, 3), compactWorkingSetMB = Math.Round(compactMemory, 1), compactPrivateMB = Math.Round(compactPrivateMemory, 1), compactIdleCpuPercent = Math.Round(compactCpu, 3), trayWorkingSetMB = Math.Round(trayMemory, 1), trayPrivateMB = Math.Round(trayPrivateMemory, 1), trayIdleCpuPercent = Math.Round(trayCpu, 3), warmupBeforeGeneralSeconds = 20, warmupAfterCompactSwitchSeconds = 20, idleSampleSeconds = 10, focus = "save button / mode button, no active text caret", logicalProcessors = Environment.ProcessorCount, database = repository.DatabasePath }, new JsonSerializerOptions { WriteIndented = true }));
            ClearEditor(); Show(); settings.Compact = false; ApplyMode(); DetailColumn.Width = new GridLength(312); Details.Visibility = Visibility.Visible; DrawCalendar();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("general.png");
            settings.Compact = true; ApplyMode(); DrawCalendar();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("compact.png");
            RememberSize(); PersistSettings();
            FinishVerification(null);
        }
        catch (Exception ex) { FinishVerification(ex); }
    }
    private void CheckSquares()
    {
        UpdateLayout();
        foreach (FrameworkElement cell in MonthGrid.Children)
            if (Math.Abs(cell.ActualWidth - cell.ActualHeight) > 1) throw new InvalidOperationException("Calendar cells not square");
    }
    private void Capture(string name)
    {
        UpdateLayout();
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
        exiting = true; tray?.Dispose(); Close(); System.Windows.Application.Current.Shutdown(error is null ? 0 : 1);
    }
}
