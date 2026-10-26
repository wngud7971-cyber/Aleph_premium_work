using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private List<MoneyEntry> money = [];
    private List<StudyEntry> studies = [];
    private Guid? moneyEditing, studyEditing;
    private Func<MessageBoxResult>? journalRemovalDecision;
    private bool HasMoneyInput => MoneyAmount is not null && (moneyEditing is not null || !string.IsNullOrWhiteSpace(MoneyAmount.Text) || !string.IsNullOrWhiteSpace(MoneyNote.Text) || !string.IsNullOrWhiteSpace(MoneyTitle.Text) || MoneyFixed.IsChecked==true || MoneyRepeat.IsChecked==true);
    private bool HasStudyInput => StudyTitle is not null && (studyEditing is not null || studyDraftAttachments.Count>0 || !string.IsNullOrWhiteSpace(StudyTitle.Text) || !string.IsNullOrWhiteSpace(StudyContent.Text) || !string.IsNullOrWhiteSpace(StudySummary.Text) || !string.IsNullOrWhiteSpace(StudyHours?.Text) || !string.IsNullOrWhiteSpace(StudyMinutes?.Text));
    private static string Won(long value) => value.ToString("N0", Korean) + "원";
    private MoneyKind ChosenMoneyKind => MoneyKindChoice.SelectedIndex == 1 ? MoneyKind.Income : MoneyKind.Expense;

    private void InitializeJournal()
    {
        MoneyKindChoice.ItemsSource = new[] { "지출", "수입" }; MoneyKindChoice.SelectedIndex = 0;
    }
    private void LoadJournal()
    {
        money = snapshot.Money.ToList(); studies = snapshot.Studies.ToList();
        ClearMoneyEditor(); ClearStudyEditor(); DrawJournal();
    }
    private void MoneyTab_Click(object sender, RoutedEventArgs e) => SelectRecordTab("money");
    private void StudyTab_Click(object sender, RoutedEventArgs e) => SelectRecordTab("study");
    private void MoneyKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MoneyCategoryChoice is null) return;
        MoneyCategoryChoice.ItemsSource = MoneyEntry.Categories(ChosenMoneyKind); MoneyCategoryChoice.SelectedIndex = 0;
        if(MoneyFixed is not null && MoneyRepeat is not null)
        {
            MoneyFixed.IsEnabled=ChosenMoneyKind==MoneyKind.Expense && (moneyEditing is not Guid edit || money.All(e=>e.Id!=edit || e.FixedCostId is null));
            if(ChosenMoneyKind==MoneyKind.Income) { MoneyFixed.IsChecked=false;MoneyRepeat.IsChecked=false; }
        }
        if (ready && !suppress) UpdateDraftStatus();
    }
    private void JournalChoice_Changed(object sender, SelectionChangedEventArgs e)
    { if (ready && !suppress) { MoneyError.Visibility = Visibility.Collapsed; UpdateDraftStatus(); } }
    private void Journal_Changed(object sender, TextChangedEventArgs e)
    {
        if (!ready || suppress || StudySummary is null) return;
        if (ReferenceEquals(sender, MoneyAmount) || ReferenceEquals(sender, MoneyNote) || ReferenceEquals(sender,MoneyTitle))
        { MoneyError.Visibility = Visibility.Collapsed; if (HasMoneyInput) MoneyComposer.IsExpanded = true; }
        else { StudyError.Visibility = Visibility.Collapsed; if (HasStudyInput) StudyComposer.IsExpanded = true; }
        UpdateDraftStatus();
    }
    private void MoneyAmount_LostFocus(object sender, RoutedEventArgs e)
    {
        if (suppress || !HasMoneyInput) return;
        try { MoneyEntry.ParseWon(MoneyAmount.Text); MoneyError.Visibility = Visibility.Collapsed; }
        catch (ArgumentException ex) { ShowJournalError(MoneyError, ex.Message); }
    }
    private void StudyTitle_LostFocus(object sender, RoutedEventArgs e)
    {
        if (suppress || !HasStudyInput) return;
        if (string.IsNullOrWhiteSpace(StudyTitle.Text)) ShowJournalError(StudyError, "학습 제목을 입력해 주세요.");
        else StudyError.Visibility = Visibility.Collapsed;
    }
    private static void ShowJournalError(TextBlock label, string message)
    { label.Text = message; label.Visibility = Visibility.Visible; }
    private void ClearMoneyEditor()
    {
        var previous = suppress; suppress = true;
        moneyEditing = null; MoneyAmount.Clear(); MoneyNote.Clear(); MoneyTitle.Clear();MoneyFixed.IsChecked=false;MoneyRepeat.IsChecked=false;MoneyFixed.IsEnabled=ChosenMoneyKind==MoneyKind.Expense;MoneyRepeat.IsEnabled=false; MoneyError.Visibility = Visibility.Collapsed;
        MoneyKindChoice.IsEnabled=true;MoneyEditorTitle.Text = "새 가계부 기록"; MoneyComposer.IsExpanded = false; suppress = previous;
    }
    private void ClearStudyEditor()
    {
        ReleaseStudyAttachmentDraft();
        var previous = suppress; suppress = true;
        studyEditing = null; StudyTitle.Clear(); StudyContent.Clear(); StudySummary.Clear(); StudyHours.Clear(); StudyMinutes.Clear(); StudyError.Visibility = Visibility.Collapsed;
        StudyEditorTitle.Text = "새 학습 기록"; StudyComposer.IsExpanded = false; suppress = previous;
    }
    private void MoneyCancel_Click(object sender, RoutedEventArgs e) { ClearMoneyEditor(); UpdateDraftStatus(); }
    private void StudyCancel_Click(object sender, RoutedEventArgs e) { ClearStudyEditor(); UpdateDraftStatus(); }
    private List<MoneyEntry> MoneyCandidates()
    {
        var result = money.ToList();
        if (!HasMoneyInput) return result;
        try
        {
            var entry = MoneyEntry.Create(ChosenMoneyKind, MoneyEntry.ParseWon(MoneyAmount.Text), MoneyCategoryChoice.SelectedItem as string ?? "", MoneyNote.Text) with { Title=MoneyTitle.Text.Trim(),IsFixed=MoneyFixed.IsChecked==true,RepeatMonthly=MoneyRepeat.IsChecked==true };
            if(moneyEditing is Guid originalId)entry=entry with { FixedCostId=money.Single(e=>e.Id==originalId).FixedCostId };
            if (moneyEditing is Guid id) entry = entry with { Id = id };
            entry.Validate();
            if (moneyEditing is Guid editId) result[result.FindIndex(e => e.Id == editId)] = entry; else result.Add(entry);
            return result;
        }
        catch (ArgumentException ex) { ShowJournalError(MoneyError, ex.Message); SelectRecordTab("money"); OpenComposer(MoneyComposer, MoneyAmount); throw; }
    }
    private List<StudyEntry> StudyCandidates()
    {
        var result = studies.ToList();
        if (!HasStudyInput) return result;
        try
        {
            var entry = StudyEntry.Create(StudyTitle.Text, StudyContent.Text, StudySummary.Text) with { Attachments=studyDraftAttachments.ToArray(), DurationMinutes=StudyEntry.ParseDuration(StudyHours.Text,StudyMinutes.Text) };
            if (studyEditing is Guid id) entry = entry with { Id = id };
            entry.Validate();
            if (studyEditing is Guid editId) result[result.FindIndex(e => e.Id == editId)] = entry; else result.Add(entry);
            return result;
        }
        catch (ArgumentException ex) { ShowJournalError(StudyError, ex.Message); SelectRecordTab("study"); OpenComposer(StudyComposer, StudyTitle); throw; }
    }
    private bool ConfirmJournalRemoval(string label) => verify && journalRemovalDecision is not null ? journalRemovalDecision() == MessageBoxResult.Yes
        : System.Windows.MessageBox.Show(this, $"이 {label} 기록을 삭제할까요?\n하단 저장 버튼을 누르면 삭제가 반영돼요.", "삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private bool ConfirmJournalEdit(bool hasInput) => !hasInput || System.Windows.MessageBox.Show(this,
        "현재 탭의 입력을 버리고 이 기록을 수정할까요?", "입력 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void BeginMoneyEdit(Guid id)
    {
        if (!ConfirmJournalEdit(HasMoneyInput)) return;
        var entry = money.Single(e => e.Id == id); var previous = suppress; suppress = true;
        moneyEditing = id; MoneyKindChoice.SelectedIndex = entry.Kind == MoneyKind.Income ? 1 : 0;
        MoneyCategoryChoice.SelectedItem = entry.Category;MoneyTitle.Text=entry.Title;MoneyFixed.IsChecked=entry.IsFixed;MoneyRepeat.IsChecked=entry.RepeatMonthly;
        MoneyFixed.IsEnabled=entry.Kind==MoneyKind.Expense && entry.FixedCostId is null;MoneyRepeat.IsEnabled=false;
        MoneyKindChoice.IsEnabled=entry.FixedCostId is null;
        MoneyAmount.Text = entry.Won.ToString("N0", Korean); MoneyNote.Text = entry.Note;
        MoneyError.Visibility = Visibility.Collapsed; MoneyEditorTitle.Text = "가계부 수정"; suppress = previous;
        UpdateDraftStatus(); SelectRecordTab("money"); OpenComposer(MoneyComposer, MoneyAmount);
    }
    private void BeginStudyEdit(Guid id)
    {
        if (!ConfirmJournalEdit(HasStudyInput)) return;
        ReleaseStudyAttachmentDraft();
        var entry = studies.Single(e => e.Id == id); var previous = suppress; suppress = true;
        studyDraftAttachments=entry.Attachments.ToList(); DrawStudyAttachmentDraft();
        studyEditing = id; StudyTitle.Text = entry.Title; StudyContent.Text = entry.Content; StudySummary.Text = entry.Summary;
        StudyHours.Text=entry.DurationMinutes is int duration?(duration/60).ToString():"";StudyMinutes.Text=entry.DurationMinutes is int total?(total%60).ToString():"";
        StudyError.Visibility = Visibility.Collapsed; StudyEditorTitle.Text = "학습 수정"; suppress = previous;
        UpdateDraftStatus(); SelectRecordTab("study"); OpenComposer(StudyComposer, StudyTitle);
    }
    private Border JournalCard(StackPanel content) => new() { Child = content, Background = ResourceBrush("Paper"),
        CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10) };
    private Button JournalAction(string title, Action action)
    {
        var button = new Button { Content = title, Background = Brushes.Transparent, Padding = new Thickness(6, 3, 6, 3) };
        button.Click += (_, _) => action(); return button;
    }
    private void DrawJournal()
    {
        DrawFixedCosts(); MoneyList.Children.Clear(); StudyList.Children.Clear();
        var income = money.Where(e => e.Kind == MoneyKind.Income).Sum(e => e.Won);
        var expense = money.Where(e => e.Kind == MoneyKind.Expense).Sum(e => e.Won);
        var savings=money.Where(e=>e.IsSaving).Sum(e=>e.Won);
        MoneyDaySummary.Children.Clear();
        foreach(var (label,value) in new[]{("수입",income),("지출",expense-savings),("적금·저축",savings),("차액",income-expense)})MoneyDaySummary.Children.Add(UiLayout.Values(StatsText(label,true),StatsText(Won(value),true)));
        MoneyDaySummary.ToolTip = "목록의 변경 사항은 하단 저장 후 통계에 반영돼요. 차액은 이 날짜의 수입−지출이며 계좌 잔액이 아니에요.";
        MoneyEmpty.Visibility = money.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StudyEmpty.Visibility = studies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StudyCount.Text = $"{selected:yyyy.MM.dd} · 학습 {studies.Count}건";
        foreach (var entry in money)
        {
            var content = new StackPanel();
            var amount = StatsText(Won(entry.Won));amount.FontWeight=FontWeights.SemiBold;
            var heading = UiLayout.Values(StatsText(entry.Kind == MoneyKind.Income ? "수입" : entry.IsSaving ? "적금·저축" : "지출"),amount);
            if(entry.Title.Length>0) { var name=StatsText(entry.Title);name.FontWeight=FontWeights.SemiBold;content.Children.Add(name); }
            content.Children.Add(heading); content.Children.Add(StatsText(entry.Category + (entry.IsFixed?" · 고정":"") + (entry.FixedCostId is not null?" · 매월 반복 내역":""), true));
            if (entry.Note.Length > 0) content.Children.Add(JournalPreview(entry.Note));
            var actions = new WrapPanel(); actions.Children.Add(JournalAction("수정", () => BeginMoneyEdit(entry.Id)));
            actions.Children.Add(JournalAction("삭제", () => DeleteMoney(entry.Id))); content.Children.Add(actions);
            MoneyList.Children.Add(JournalCard(content));
        }
        foreach (var entry in studies)
        {
            var content = new StackPanel(); var heading = StatsText(entry.Title); heading.FontWeight = FontWeights.SemiBold; content.Children.Add(heading);
            content.Children.Add(StatsText(StudyEntry.DurationLabel(entry.DurationMinutes),true));
            if (entry.Summary.Length > 0) content.Children.Add(JournalPreview("핵심 요약\n" + entry.Summary));
            if (entry.Content.Length > 0) content.Children.Add(JournalPreview(entry.Content));
            if (entry.Attachments.Count>0) content.Children.Add(CreateStudyAttachmentList(entry.Attachments));
            var actions = new WrapPanel(); actions.Children.Add(JournalAction("내용 보기 · 수정", () => BeginStudyEdit(entry.Id)));
            actions.Children.Add(JournalAction("삭제", () => DeleteStudy(entry.Id))); content.Children.Add(actions);
            StudyList.Children.Add(JournalCard(content));
        }
    }
    private TextBlock JournalPreview(string text)
    {
        var label = StatsText(text, true); label.MaxHeight = 90; label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.ClipToBounds = true; label.ToolTip = "수정을 누르면 전체 내용을 볼 수 있어요."; return label;
    }
    private void DeleteMoney(Guid id)
    {
        if (!ConfirmJournalRemoval("가계부")) return;
        money.RemoveAll(e => e.Id == id); if (moneyEditing == id) ClearMoneyEditor(); DrawJournal(); MarkDirty();
    }
    private void DeleteStudy(Guid id)
    {
        if (!ConfirmJournalRemoval("학습")) return;
        studies.RemoveAll(e => e.Id == id); if (studyEditing == id) ClearStudyEditor(); DrawJournal(); MarkDirty();
    }
}
