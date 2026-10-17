using System.Windows;
using System.Windows.Controls;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private Func<MessageBoxResult>? reviewRemovalDecision;
    private Func<MessageBoxResult>? pendingChangeDecision;
    private DayReview ReviewInput => new(ReviewGood.Text, ReviewDifficult.Text, ReviewNextAction.Text);
    private bool HasReviewChanges => ReviewGood is not null && ReviewInput.Normalize() != snapshot.Review;
    private void ReviewTab_Click(object sender, RoutedEventArgs e) => SelectRecordTab("review");
    private void LoadReview()
    {
        var wasSuppressed = suppress; suppress = true;
        try
        {
            ReviewGood.Text = snapshot.Review?.Good ?? "";
            ReviewDifficult.Text = snapshot.Review?.Difficult ?? "";
            ReviewNextAction.Text = snapshot.Review?.NextAction ?? "";
            UpdateReviewState();
        }
        finally { suppress = wasSuppressed; }
    }
    private void Review_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized || ReviewNextAction is null) return;
        UpdateReviewState(); UpdateDraftStatus();
    }
    private void UpdateReviewState()
    {
        var filled = new[] { ReviewGood.Text, ReviewDifficult.Text, ReviewNextAction.Text }.Count(value => !string.IsNullOrWhiteSpace(value));
        ReviewState.Text = $"{filled} / 3칸 작성" + (HasReviewChanges ? " · 저장 전" : snapshot.Review is not null ? " · 저장됨" : "");
    }
    private DayReview? ReviewCandidate()
    {
        var review = ReviewInput; review.Validate(); return review.Normalize();
    }
    private MessageBoxResult AskReviewRemoval() => verify && reviewRemovalDecision is not null ? reviewRemovalDecision()
        : System.Windows.MessageBox.Show(this, "저장한 회고 세 칸이 모두 비어 있어요.\n이 날짜의 회고를 삭제하고 저장할까요? 계획·운동은 유지돼요.",
            "회고 삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
    private MessageBoxResult AskPendingChanges() => verify && pendingChangeDecision is not null ? pendingChangeDecision()
        : System.Windows.MessageBox.Show(this, "저장하지 않은 변경이 있어요.\n저장할까요? ‘아니요’는 변경을 버립니다.",
            "입력 확인", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
}
