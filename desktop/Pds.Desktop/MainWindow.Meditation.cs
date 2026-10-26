using System.Windows;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private MeditationSession meditation = new();
    private readonly DispatcherTimer meditationTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private void InitializeMeditation()
    {
        meditationTimer.Tick += (_, _) => RefreshMeditation();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) meditationTimer.Stop();
            else if (MeditationCard.Visibility == Visibility.Visible) RefreshMeditation();
        };
    }
    private void OpenMeditation()
    {
        MeditationCard.Visibility = Visibility.Visible;
        RefreshMeditation(); QueueCalendarFit(); CalmMotion.Reveal(MeditationCard, CalmMotion.DialogMs);
    }
    private void CloseMeditation()
    {
        meditationTimer.Stop(); meditation.Close(); MeditationCard.Visibility = Visibility.Collapsed;
        RefreshMeditation();
        ContinueEntryGuidance();
    }
    private void StartMeditation_Click(object sender, RoutedEventArgs e)
    {
        if (meditation.State == MeditationState.Running) return;
        meditation.Start(); RefreshMeditation();
    }
    private void RefreshMeditation()
    {
        var remaining = meditation.Remaining;
        if (waitingForEntryMeditation && meditation.State == MeditationState.Completed)
        { CloseMeditation(); return; }
        var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        MeditationRemaining.Text = $"{seconds / 60:00}:{seconds % 60:00}";
        MeditationStart.IsEnabled = meditation.State != MeditationState.Running;
        MeditationStart.Content = meditation.State == MeditationState.Completed ? "다시 시작" : "시작";
        MeditationMessage.Text = meditation.State switch
        {
            MeditationState.Running => "편안하게 호흡해 보세요. 언제든 닫을 수 있어요.",
            MeditationState.Completed => "5분이 지났어요. 차분하게 하루를 이어가세요.",
            _ => "잠시 호흡을 고르고, 차분하게 시작해 보세요."
        };
        if (IsVisible && WindowState != WindowState.Minimized && MeditationCard.Visibility == Visibility.Visible && meditation.State == MeditationState.Running) meditationTimer.Start();
        else meditationTimer.Stop();
    }
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized) CalmMotion.Settle(this);
        if (MeditationCard is not null) RefreshMeditation();
    }
}
