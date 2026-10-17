using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private bool entryGuidancePending, waitingForEntryMeditation, openingReminder;
    private DateOnly? lastEntryDate, reminderTestDate;
    private DateOnly reminderDate;
    private IInputElement? reminderPreviousFocus;
    private DateOnly EntryToday => verify && reminderTestDate is DateOnly test ? test : LocalToday;

    private void RequestEntryGuidance()
    {
        if (verify || exiting || !ready) return;
        lastEntryDate = EntryToday; entryGuidancePending = true; ContinueEntryGuidance();
    }
    private void ContinueEntryGuidance()
    {
        if (verify || exiting || !entryGuidancePending || !ready) return;
        Dispatcher.BeginInvoke(async () => await ProcessEntryGuidanceAsync(), DispatcherPriority.Background);
    }
    private async Task ProcessEntryGuidanceAsync(bool allowVerification = false)
    {
        if (verify && !allowVerification || exiting || !entryGuidancePending || !ready || busy || openingReminder || !IsVisible || WindowState == WindowState.Minimized) return;
        var todayKey = EntryToday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (statisticsOpen && settings.MeditationEnabled && settings.MeditationDate != todayKey && !settings.MeditationShownDates.Contains(todayKey)) CloseStatistics();
        if (MeditationCard.Visibility != Visibility.Visible) ShowMeditationIfNeeded();
        if (MeditationCard.Visibility == Visibility.Visible)
        { ClosePlanReminder(); waitingForEntryMeditation = true; return; }
        waitingForEntryMeditation = false; entryGuidancePending = false;
        await OpenPlanReminderAsync(EntryToday);
    }
    private async Task OpenPlanReminderAsync(DateOnly date)
    {
        if (openingReminder) return;
        openingReminder = true;
        try
        {
            var day = await Task.Run(() => repository.Load(date));
            if (!IsVisible || WindowState == WindowState.Minimized || EntryToday != date)
            { entryGuidancePending = true; return; }
            var remaining = day.Plans.Where(p => !p.Done).ToArray();
            ReminderList.Children.Clear();
            if (remaining.Length == 0) { ReminderOverlay.Visibility = Visibility.Collapsed; return; }
            reminderDate = date;
            ReminderHeading.Text = $"{date:yyyy.MM.dd} · 오늘 남은 할 일 {remaining.Length}건";
            foreach (var entry in remaining)
            {
                var item = new StackPanel();
                item.Children.Add(new TextBlock { Text = entry.Title, TextWrapping = TextWrapping.Wrap });
                if (entry.Start is not null) item.Children.Add(new TextBlock { Text = $"{entry.Start}–{entry.End}", Foreground = ResourceBrush("Muted"), Margin = new Thickness(0, 5, 0, 0) });
                ReminderList.Children.Add(new Border { Background = ResourceBrush("Soft"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Child = item });
            }
            if (ReminderOverlay.Visibility != Visibility.Visible) reminderPreviousFocus = Keyboard.FocusedElement;
            ReminderOverlay.Visibility = Visibility.Visible; FitReminder(); ReminderDismiss.Focus();
        }
        catch (Exception ex) { HandleError("오늘 할 일 확인", ex); }
        finally { openingReminder = false; }
    }
    private void FitReminder()
    {
        if (ReminderCard is null) return;
        ReminderCard.Width = Math.Min(520, Math.Max(260, ActualWidth - 72));
        ReminderCard.MaxHeight = Math.Max(180, ActualHeight - 72);
    }
    private void ClosePlanReminder()
    {
        ReminderOverlay.Visibility = Visibility.Collapsed;
        if (reminderPreviousFocus is UIElement { IsVisible: true, IsEnabled: true } previous) Keyboard.Focus(previous);
        reminderPreviousFocus = null;
    }
    private void DismissReminder_Click(object sender, RoutedEventArgs e) => ClosePlanReminder();
    private async void OpenReminderDate_Click(object sender, RoutedEventArgs e) => await OpenReminderDateAsync();
    private async Task OpenReminderDateAsync()
    {
        if (busy || !await ConfirmPendingAsync()) return;
        ClosePlanReminder(); if (statisticsOpen) CloseStatistics();
        await SelectDateAsync(reminderDate); SelectRecordTab("plan");
        month = new(reminderDate.Year, reminderDate.Month, 1);
        try { await RefreshCalendarAsync(); } catch (Exception ex) { HandleError("오늘 계획 열기", ex); }
    }
}
