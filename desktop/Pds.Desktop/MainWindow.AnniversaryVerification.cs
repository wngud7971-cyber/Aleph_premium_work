using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyAnniversaryFeaturesAsync(List<string> checks)
    {
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        var folder = Path.GetDirectoryName(repository.DatabasePath)!;
        settings.Compact = false; settings.Dark = false; ApplyWindowLayout(); ApplyTheme();
        CloseMeditation(); ClosePlanReminder();
        pendingChangeDecision = () => MessageBoxResult.No;
        await SelectDateAsync(new(2026, 10, 17)); pendingChangeDecision = null;
        TitleInput.Text = "기념일 편집 중에도 지킬 계획 초안";
        var dialog = new AnniversaryWindow(repository, this, selected)
        { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
        dialog.DraftDecision = () => MessageBoxResult.Cancel;
        dialog.DeleteDecision = _ => false;
        dialog.Show();
        try
        {
            dialog.EntryName.Text = "";
            Check(!await dialog.SaveEditorAsync() && repository.LoadAnniversaries().Count == 0, "blank-anniversary-keeps-input-and-database");
            dialog.EntryName.Text = "친구"; dialog.Month.SelectedItem = 4; dialog.Day.SelectedItem = 31;
            Check(!await dialog.SaveEditorAsync() && dialog.Day.SelectedItem is 31 && repository.LoadAnniversaries().Count == 0, "impossible-anniversary-date-preserves-draft");
            dialog.Month.SelectedItem = 10; dialog.Day.SelectedItem = 17;
            Check(await dialog.SaveEditorAsync() && dialog.Result.Text.Contains("저장 완료") && !dialog.HasDraft, "anniversary-manual-save-shows-complete");
            var solar = repository.LoadAnniversaries().Single();
            dialog.EntryName.Text = "친구 생일"; dialog.EntryNote.Text = "매년 잊지 않기";
            Check(await dialog.SaveEditorAsync() && repository.LoadAnniversaries().Single().Id == solar.Id, "anniversary-editor-updates-original-without-duplicate");
            dialog.ResetEditor(); dialog.EntryName.Text = "가족"; dialog.Calendar.SelectedIndex = 1;
            dialog.Month.SelectedItem = 1; dialog.Day.SelectedItem = 1;
            Check(await dialog.SaveEditorAsync(), "lunar-anniversary-saves-original-month-day");
            dialog.ResetEditor(); dialog.EntryName.Text = "윤달 기일"; dialog.Kind.SelectedIndex = 1; dialog.Calendar.SelectedIndex = 1;
            dialog.Month.SelectedItem = 6; dialog.Day.SelectedItem = 1; dialog.Leap.IsChecked = true;
            Check(dialog.LeapPolicy.SelectedIndex == 0 && await dialog.SaveEditorAsync(), "new-leap-record-defaults-to-user-selected-skip-year");
            var savedLeap = repository.LoadAnniversaries().Single(a => a.LeapMonth);
            Check(savedLeap.LeapPolicy == MissingLeapMonth.SkipYear && AnniversaryDates.InYear(savedLeap, 2026) is null, "leap-policy-persists-and-skips-year-without-leap-month");
            dialog.EntryName.Text = "취소로 보호할 기념일";
            await dialog.EditEntryAsync(solar);
            Check(dialog.EntryName.Text == "취소로 보호할 기념일" && dialog.HasDraft, "switch-anniversary-edit-cancel-protects-draft");
            dialog.Close(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(dialog.IsVisible && dialog.EntryName.Text == "취소로 보호할 기념일", "anniversary-close-cancel-preserves-editor");
            dialog.ResetEditor();
            await dialog.DeleteEntryAsync(savedLeap);
            Check(repository.LoadAnniversaries().Count == 3, "anniversary-delete-cancel-preserves-rule");
            dialog.DeleteDecision = _ => true; await dialog.DeleteEntryAsync(savedLeap);
            Check(repository.LoadAnniversaries().Count == 2, "anniversary-confirmed-delete-removes-yearly-rule");
            dialog.ResetEditor(); dialog.EntryName.Text = "생년월일 자동 계산";
            dialog.EntryYear.Text = "2025"; dialog.Month.SelectedItem = 7; dialog.Day.SelectedItem = 25;
            dialog.RecurringCalendar.SelectedIndex = 2;
            Check(dialog.Leap.Visibility == Visibility.Collapsed && dialog.OriginLeap.Visibility == Visibility.Collapsed && await dialog.SaveEditorAsync(), "solar-birth-date-infers-lunar-rule-without-leap-question");
            var birthday = repository.LoadAnniversaries().Single(a => a.Origin is not null);
            Check(birthday is { Month: 6, Day: 1, LeapMonth: true } && birthday.Origin == new AnniversaryOrigin(2025, 7, 25, AnniversaryCalendar.Solar), "solar-birth-date-saves-original-and-auto-detected-leap-six");
            dialog.Calendar.SelectedIndex = 1; dialog.Month.SelectedItem = 6; dialog.Day.SelectedItem = 1;
            Check(dialog.OriginLeap.Visibility == Visibility.Visible && dialog.OriginLeap.SelectedIndex < 0 && !await dialog.SaveEditorAsync()
                && repository.LoadAnniversaries().Single(a => a.Id == birthday.Id).Origin == birthday.Origin, "ambiguous-original-lunar-date-requires-one-choice-without-changing-saved-record");
            dialog.OriginLeap.SelectedIndex = 1;
            Check(await dialog.SaveEditorAsync() && repository.LoadAnniversaries().Single(a => a.Id == birthday.Id).Origin is { Calendar: AnniversaryCalendar.Lunar, LeapMonth: true }, "explicit-original-leap-choice-preserved-with-full-birth-date");
            dialog.EntryYear.Text = "2026";
            Check(dialog.OriginLeap.Visibility == Visibility.Collapsed && await dialog.SaveEditorAsync()
                && repository.LoadAnniversaries().Single(a => a.Id == birthday.Id) is { LeapMonth: false, Origin.Year: 2026 }, "new-birth-year-automatically-resolves-regular-month-and-clears-stale-leap-choice");
            dialog.Calendar.SelectedIndex = 0; dialog.EntryYear.Text = "2025"; dialog.Month.SelectedItem = 2; dialog.Day.SelectedItem = 29;
            Check(!await dialog.SaveEditorAsync() && dialog.EntryYear.Text == "2025" && dialog.Day.SelectedItem is 29, "non-leap-birth-year-rejects-february-29-with-input-preserved");
            dialog.DraftDecision = () => MessageBoxResult.No;
            await dialog.EditEntryAsync(repository.LoadAnniversaries().Single(a => a.Id == birthday.Id));
            Check(dialog.EntryYear.Text == "2026" && dialog.Calendar.SelectedIndex == 1 && dialog.Month.SelectedItem is 6 && !dialog.HasDraft, "full-original-birth-date-and-calendar-restore-into-editor");
            dialog.Calendar.SelectedIndex = 0; dialog.EntryYear.Text = "2025"; dialog.Month.SelectedItem = 7; dialog.Day.SelectedItem = 25;
            dialog.RecurringCalendar.SelectedIndex = 2;
            Check(await dialog.SaveEditorAsync() && repository.LoadAnniversaries().Count == 3, "birth-date-edit-updates-one-rule-without-duplicates");
            dialog.ResetEditor(); dialog.EntryYear.Text = "1995"; dialog.DraftDecision = () => MessageBoxResult.Cancel;
            dialog.Close(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(dialog.IsVisible && dialog.EntryYear.Text == "1995" && dialog.HasDraft, "year-only-unsaved-input-is-protected-when-closing");
            dialog.ResetEditor();
            Check(TitleInput.Text == "기념일 편집 중에도 지킬 계획 초안" && Pending, "anniversary-dialog-does-not-clear-or-save-main-plan-draft");
            foreach (var dark in new[] { false, true })
            {
                settings.Dark = dark; ApplyTheme();
                // Recreate the dialog to use the same theme resources as a real opening.
                var themed = new AnniversaryWindow(repository, this, selected)
                { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, Width = 440, Height = 480 };
                themed.Show();
                try
                {
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Check(((SolidColorBrush)themed.Background).Color == ((SolidColorBrush)ResourceBrush("Paper")).Color
                        && ((SolidColorBrush)themed.Foreground).Color == ((SolidColorBrush)ResourceBrush("Ink")).Color
                        && themed.FontFamily.Source == FontFamily.Source,
                        "anniversary-dialog-inherits-body-font-and-theme-" + (dark ? "dark" : "light"));
                    themed.EditorViewport.ScrollToBottom(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    var savePosition = themed.SaveEntry.TranslatePoint(new Point(), themed);
                    Check(themed.EditorViewport.ScrollableHeight > 0 && savePosition.Y >= 0 && savePosition.Y + themed.SaveEntry.ActualHeight <= themed.ActualHeight,
                        "anniversary-minimum-dialog-scroll-and-save-reachable-" + (dark ? "dark" : "light"));
                    CaptureDialog(themed, "anniversary-editor-" + (dark ? "dark" : "light") + ".png");
                    themed.Width = 560; themed.Height = Math.Min(740, SystemParameters.WorkArea.Height);
                    themed.Calendar.SelectedIndex = 1; themed.Month.SelectedItem = 1; themed.Day.SelectedItem = 1;
                    themed.EditorViewport.ScrollToTop(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    CaptureDialog(themed, "anniversary-editor-full-" + (dark ? "dark" : "light") + ".png");
                    themed.EntryName.Text = "생일 예시"; themed.EntryYear.Text = "2025";
                    themed.Calendar.SelectedIndex = 0; themed.Month.SelectedItem = 7; themed.Day.SelectedItem = 25; themed.RecurringCalendar.SelectedIndex = 2;
                    themed.EditorViewport.ScrollToVerticalOffset(280); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    CaptureDialog(themed, "anniversary-birth-date-" + (dark ? "dark" : "light") + ".png");
                    themed.ResetEditor();
                }
                finally { themed.Close(); }
            }
            foreach (var dark in new[] { false, true })
            {
                settings.Dark = dark; ApplyWindowLayout(openDetails: true); ApplyTheme();
                month = new(2026, 10, 1); await RefreshCalendarAsync();
                var button = MonthGrid.Children.OfType<Button>().Single(b => (DateOnly)b.Tag == new DateOnly(2026, 10, 17));
                Check(button.ToolTip.ToString()!.Contains("친구 생일") && AnniversaryDayList.Children.Count > 0, "anniversary-visible-in-fixed-calendar-and-sidebar-" + (dark ? "dark" : "light"));
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture("anniversary-calendar-" + (dark ? "dark" : "light") + ".png");
            }
            var saved = repository.LoadAnniversaries();
            File.WriteAllText(Path.Combine(folder, "anniversary-expected.json"), JsonSerializer.Serialize(saved));
            File.WriteAllText(Path.Combine(folder, "anniversary.json"), JsonSerializer.Serialize(new { success = true, records = saved.Count, leapWithoutMonth = "skip", personalDataUsed = false }));
        }
        finally { dialog.ResetEditor(); dialog.Close(); settings.Compact = false; settings.Dark = false; ApplyWindowLayout(); ApplyTheme(); }
    }
    private void CaptureDialog(Window window, string name)
    {
        CalmMotion.Settle(window); window.UpdateLayout();
        var target = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(ResourceBrush("Paper"), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
        target.Render(background); target.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, name)); encoder.Save(file);
    }
    private async Task VerifyFixedWindowAsync(List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        settings.Compact = true; settings.Pinned = true;
        settings.GeneralWidth = 1200; settings.GeneralHeight = 700;
        settingsStore.Save(settings); settings.CopyFrom(settingsStore.Load());
        var accent = settings.Accent; var dark = settings.Dark;
        ApplyWindowLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var size = FixedWindowSize(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        var width = ActualWidth; var height = ActualHeight;
        Check(!settings.Compact && !settings.Pinned && !Topmost && settings.Accent == accent && settings.Dark == dark,
            "legacy-preferences-keep-theme-and-drop-compact-pin");
        Check(Math.Abs(width - size.Width) < 1 && Math.Abs(height - size.Height) < 1 && ResizeMode == ResizeMode.CanMinimize
            && MinWidth == MaxWidth && MinHeight == MaxHeight, "fixed-size-and-resize-maximize-disabled");
        Check(TopActions.Children.OfType<System.Windows.Controls.Button>().SequenceEqual(new[] { ThemeButton, SettingsButton }),
            "compact-and-pin-controls-removed-from-header");
        Width = width + 100; Height = height + 100;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(Math.Abs(ActualWidth - width) < 1 && Math.Abs(ActualHeight - height) < 1, "fixed-dimensions-reject-resize");
        ApplyWindowLayout(); pendingChangeDecision = () => MessageBoxResult.No;
        await SelectDateAsync(selected); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(Details.Visibility == Visibility.Visible && ActualWidth == width && ActualHeight == height, "sidebar-opens-inside-fixed-window");
        CloseDetails_Click(this, new RoutedEventArgs()); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(Details.Visibility == Visibility.Collapsed && ActualWidth == width && ActualHeight == height, "sidebar-closes-without-resize");
        await OpenStatisticsAsync(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(statisticsOpen && ActualWidth == width && ActualHeight == height, "statistics-does-not-resize-fixed-window");
        CloseStatistics(); pendingChangeDecision = null;
        Check(FixedWindowSize(1920, 1032) == (900d, 880d) && FixedWindowSize(1280, 672) == (900d, 672d)
            && FixedWindowSize(800, 600) == (800d, 600d), "fixed-window-clamps-only-to-small-work-area");
        RememberWindowPosition(); PersistSettings(); var saved = settingsStore.Load();
        Check(!saved.Compact && !saved.Pinned && saved.GeneralWidth == width && saved.GeneralHeight == height,
            "fixed-window-settings-persist-without-legacy-mode");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "fixed-window.json"),
            JsonSerializer.Serialize(new { success = true, width, height, compactRemoved = true, resizeDisabled = true, smallWorkAreaCalculationTested = true, actualMultipleMonitorsTested = false }));
    }
}
