using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private readonly DispatcherTimer maintenanceTimer = new(DispatcherPriority.Background);
    private bool maintenanceStarted, refreshOnRestore;
    private string DescribeStartupRegistration()
    {
        if (!allowSystemStartup) return "별도 데이터로 시험 중이에요. 시작 프로그램은 변경하지 않아요.";
        try { return StartupRegistration.IsRegistered() ? "이 실행 파일이 시작 프로그램에 등록되어 있어요." : "현재 실행 파일은 시작 프로그램에 등록되어 있지 않아요."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { return "등록 상태를 읽지 못했어요."; }
    }
    private void StartMaintenance()
    {
        if (verify || maintenanceStarted) return;
        maintenanceStarted = true;
        maintenanceTimer.Tick += MaintenanceTick;
        SystemEvents.PowerModeChanged += PowerChanged; SystemEvents.TimeChanged += ClockChanged;
        ScheduleMaintenance();
        try { if (allowSystemStartup) StartupRegistration.Apply(settings.AutoStart); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { Status.Text = "자동 실행을 적용하지 못했어요. 설정에서 다시 시도해 주세요."; }
    }
    private void StopMaintenance()
    {
        maintenanceTimer.Stop();
        if (!maintenanceStarted) return;
        SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.TimeChanged -= ClockChanged; maintenanceStarted = false;
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) QueueMaintenance(); }
    private void ClockChanged(object? sender, EventArgs e) => QueueMaintenance();
    private void QueueMaintenance()
    {
        if (verify || exiting || !ready) return;
        Dispatcher.BeginInvoke(async () => await MaintainAsync(), DispatcherPriority.Background);
    }
    private async void MaintenanceTick(object? sender, EventArgs e) { maintenanceTimer.Stop(); await MaintainAsync(); }
    private void ScheduleMaintenance(bool retry = false)
    {
        if (verify || exiting) return;
        var now = DateTime.Now;
        var next = now.Hour < 23 ? now.Date.AddHours(23) : now.Date.AddDays(1);
        maintenanceTimer.Interval = retry ? TimeSpan.FromSeconds(30) : next - now + TimeSpan.FromMilliseconds(100);
        maintenanceTimer.Start();
    }
    private async Task<RolloverResult?> MaintainAsync(DateTimeOffset? testNow = null)
    {
        if (exiting || !ready || verify && testNow is null) return null;
        if (busy) { ScheduleMaintenance(retry: true); return null; }
        var pending = Pending;
        var protectedDates = pending ? new HashSet<DateOnly> { selected } : null;
        busy = true; Body.IsEnabled = false;
        RolloverResult? result;
        try
        {
            result = await Task.Run(() => repository.Rollover(testNow ?? DateTimeOffset.Now, protectedDates));
            if (!pending && (result.Moved > 0 || refreshOnRestore))
            {
                snapshot = await Task.Run(() => repository.Load(selected)); plans = snapshot.Plans.ToList(); workouts = snapshot.Workouts.ToList(); dirty = false;
                ClearEditor(); ClearWorkoutEditor(); LoadReview();
            }
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                DrawPlans(); DrawWorkouts(); await RefreshCalendarAsync(); refreshOnRestore = false;
            }
            else refreshOnRestore = true;
            if (result.Moved > 0 || result.Deferred > 0)
                Status.Text = $"23시 이월 {result.Moved}건" + (result.Deferred > 0 ? $" · 입력 보호로 {result.Deferred}건 보류 중" : " · 시간 지정은 해제했어요.");
        }
        catch (Exception ex) { HandleError("23시 이월 확인", ex); result = null; }
        finally { busy = false; Body.IsEnabled = true; ScheduleMaintenance(); ContinueEntryGuidance(); }
        if (statisticsOpen && IsVisible) await RefreshStatisticsAsync();
        return result;
    }
    private async Task ShowPlanHistoryAsync(Guid id)
    {
        if (busy) return;
        try
        {
            var history = await Task.Run(() => repository.LoadPlanHistory(id));
            var message = history.Origin is null ? "저장한 뒤 내역을 확인할 수 있어요." : $"최초 배정일 {history.Origin.OriginalDate:yyyy.MM.dd}\n" +
                (history.Moves.Count == 0 ? "이월한 내역이 없어요." : string.Join("\n", history.Moves.Select(m => $"{m.From:MM/dd} → {m.To:MM/dd} · {(m.Reason == "catch-up" ? "미실행 기간 정리" : "23시 이월")} · 기존 시간 {m.PreviousStart ?? "없음"}{(m.PreviousEnd is null ? "" : "–" + m.PreviousEnd)}")));
            if (!verify) System.Windows.MessageBox.Show(this, message, "계획 이월 내역", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { HandleError("이월 내역 조회", ex); }
    }
}
