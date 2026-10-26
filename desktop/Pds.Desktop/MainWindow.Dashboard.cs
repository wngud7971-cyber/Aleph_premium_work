using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private void RefreshDashboard(DayCount count)
    {
        DashboardDate.Text = DateTime.Today.ToString("M월 d일 dddd", Korean);
        DashboardPlanValue.Text = $"{count.Done} / {count.Plans}";
        DashboardWorkoutValue.Text = $"{count.Workouts}건";
        DashboardDoneColumn.Width = new GridLength(count.Done, GridUnitType.Star);
        DashboardRemainingColumn.Width = new GridLength(Math.Max(count.Plans == 0 ? 1 : 0, count.Plans - count.Done), GridUnitType.Star);
        Summary.Text = count.Workouts == 0 ? "아직 운동 기록이 없어요." : count.TimedWorkouts == 0 ? "시간 미입력"
            : FormatDuration(count.WorkoutSeconds) + (count.TimedWorkouts < count.Workouts ? $" · 시간 입력 {count.TimedWorkouts}/{count.Workouts}건" : "");
        DashboardHeader.ToolTip = "오늘 날짜에 저장한 기록 기준이에요. 입력 중인 내용은 저장 후 반영돼요.";
    }

    private void ExpandComposerForDraft(object? sender = null)
    {
        if (!ready || suppress || PlanComposer is null || WorkoutComposer is null) return;
        if (HasInput && (ReferenceEquals(sender, TitleInput) || ReferenceEquals(sender, NoteInput)
            || ReferenceEquals(sender, StartInput) || ReferenceEquals(sender, EndInput))) PlanComposer.IsExpanded = true;
        if (HasWorkoutInput && (ReferenceEquals(sender, WorkoutQuantity) || ReferenceEquals(sender, WorkoutMinutes)
            || ReferenceEquals(sender, WorkoutSeconds) || ReferenceEquals(sender, WorkoutNote))) WorkoutComposer.IsExpanded = true;
    }

    private void OpenComposer(Expander composer, FrameworkElement firstField)
    {
        composer.IsExpanded = true;
        Dispatcher.BeginInvoke(() => { firstField.BringIntoView(); firstField.Focus(); }, DispatcherPriority.Loaded);
    }

    private void UpdateReviewPreview()
    {
        if (ReviewPreview is null) return;
        var saved = snapshot.Review;
        ReviewPreview.Text = saved is null ? "아직 회고를 남기지 않았어요.\n한 칸만 적어도 괜찮아요."
            : string.Join("\n\n", new[] { ("잘한 점", saved.Good), ("아쉬운 점", saved.Difficult), ("다음 행동", saved.NextAction) }
                .Where(item => !string.IsNullOrWhiteSpace(item.Item2)).Select(item => item.Item1 + "\n" + item.Item2));
        ReviewPreview.MaxHeight = 132;
        ReviewPreview.TextTrimming = TextTrimming.CharacterEllipsis;
        ReviewPreview.ClipToBounds = true;
        ReviewPreview.ToolTip = "저장된 회고예요. 아래 ‘회고 작성 · 수정’을 펼치면 전체 내용을 볼 수 있어요.";
    }
}
