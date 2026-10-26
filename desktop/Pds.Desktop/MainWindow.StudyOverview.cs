using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private Border? StudyOverviewSection;
    private StackPanel? StudyOverviewList;
    private TextBlock? StudyPageLabel;

    private void JumpToStudyOverview()
    {
        if (StudyOverviewSection is null) return;
        StatisticsPane.UpdateLayout();
        var top = StudyOverviewSection.TranslatePoint(new Point(),StatisticsPane).Y;
        StatisticsPane.ScrollToVerticalOffset(StatisticsPane.VerticalOffset+top-StatisticsPane.Padding.Top);
    }
    private void DrawStudyOverview()
    {
        if (studyOverview is not { } report) return;
        var content = new StackPanel();
        var heading = StatsText("학습 모아보기", size: 18); heading.FontWeight = FontWeights.SemiBold; content.Children.Add(heading);
        content.Children.Add(StatsText($"{report.Range.From:yyyy.MM.dd} — {report.Range.To:yyyy.MM.dd} · 저장된 학습 기록", true));
        var cards = new UniformGrid { Columns = 2, Margin = new Thickness(0,8,0,8) };
        foreach (var (label,value) in new[] { ("학습 기록", $"{report.Records:N0}건"), ("학습한 날", $"{report.StudyDays:N0}일") })
        {
            var stack = new StackPanel(); stack.Children.Add(StatsText(label,true));
            var number = StatsText(value,size: 15); number.FontWeight = FontWeights.SemiBold; UiLayout.Numeric(number); stack.Children.Add(number);
            cards.Children.Add(new Border { Background = ResourceBrush("Soft"), Padding = new Thickness(14),
                Margin = new Thickness(0,0,8,0), CornerRadius = new CornerRadius(12), Child = stack });
        }
        content.Children.Add(cards);
        content.Children.Add(StatsText($"입력된 학습 시간 {report.Minutes/60:N0}시간 {report.Minutes%60}분 · 시간 입력 {report.TimedRecords}/{report.Records}건",true));
        content.Children.Add(StatsText("같은 날짜의 여러 기록은 하루로 세어요. 최신 날짜순이며 운동 필터와 무관해요.",true));
        StudyOverviewList = new StackPanel(); content.Children.Add(StudyOverviewList);
        if (report.Records == 0) StudyOverviewList.Children.Add(StatsText("이 기간에 저장된 학습 기록이 없어요.",true));
        foreach (var item in report.Items) StudyOverviewList.Children.Add(CreateStudyOverviewCard(item));
        var pages = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,8,0,8) };
        var previous = JournalAction("‹ 이전 10건", async () => await ChangeStudyPageAsync(-1)); previous.IsEnabled = report.Page > 0;
        var next = JournalAction("다음 10건 ›", async () => await ChangeStudyPageAsync(1)); next.IsEnabled = report.Page+1 < report.Pages;
        StudyPageLabel = StatsText($"{report.Page+1} / {report.Pages} 페이지 · 전체 {report.Records:N0}건",true);
        StudyPageLabel.Margin = new Thickness(12,8,12,8);StudyPageLabel.VerticalAlignment=VerticalAlignment.Center;
        pages.Children.Add(previous); pages.Children.Add(StudyPageLabel); pages.Children.Add(next); content.Children.Add(pages);
        StudyOverviewSection = new Border { Child = content, Padding = new Thickness(16), Background = ResourceBrush("Paper"),
            BorderBrush = ResourceBrush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Margin = new Thickness(0,8,8,18) };
        StatisticsContent.Children.Add(StudyOverviewSection);
    }
    private Border CreateStudyOverviewCard(StudyListing item)
    {
        var stack = new StackPanel();
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var date = StatsText(item.Date.ToString("yyyy.MM.dd dddd",Korean),true); date.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(date);
        var open = JournalAction("날짜로 이동 →", async () => await OpenStudyDateAsync(item.Date)); Grid.SetColumn(open,1); header.Children.Add(open); stack.Children.Add(header);
        stack.Children.Add(StatsText(StudyEntry.DurationLabel(item.DurationMinutes),true));
        var title = StatsText(item.Title,size: 15); title.FontWeight = FontWeights.SemiBold; stack.Children.Add(title);
        var summary = StatsText(string.IsNullOrWhiteSpace(item.Summary) ? "핵심 요약을 남기지 않았어요." : item.Summary,true);
        summary.MaxHeight = 68; summary.TextTrimming = TextTrimming.CharacterEllipsis; summary.ClipToBounds = true;
        summary.ToolTip = "아래 전체 내용 보기를 펼치면 요약과 본문을 모두 읽을 수 있어요."; stack.Children.Add(summary);
        var details = new Expander { Header = "전체 내용 보기", Margin = new Thickness(0,8,0,0) };
        var loaded = false;
        details.Expanded += async (_,args) =>
        {
            if (!ReferenceEquals(args.OriginalSource,details) || loaded || busy) return;
            loaded = await LoadStudyDetailAsync(details,item);
        };
        stack.Children.Add(details);
        return new Border { Tag = item, Child = stack, Background = ResourceBrush("Soft"), Padding = new Thickness(14),
            CornerRadius = new CornerRadius(12), Margin = new Thickness(0,8,0,4) };
    }
    private async Task<bool> LoadStudyDetailAsync(Expander details, StudyListing item)
    {
        busy = true; Body.IsEnabled = false; details.Content = StatsText("내용을 불러오는 중이에요.",true);
        try
        {
            var entry = await Task.Run(() => repository.LoadStudy(item.Date,item.Id));
            if (entry is null) { details.Content = StatsText("기록이 변경됐어요. 기간을 다시 선택해 목록을 갱신해 주세요.",true); return false; }
            var content = new StackPanel();
            content.Children.Add(StatsText("핵심 요약",true)); content.Children.Add(StatsText(string.IsNullOrWhiteSpace(entry.Summary) ? "작성한 요약이 없어요." : entry.Summary));
            content.Children.Add(StatsText("공부한 내용",true)); content.Children.Add(StatsText(string.IsNullOrWhiteSpace(entry.Content) ? "작성한 본문이 없어요." : entry.Content));
            if (entry.Attachments.Count>0) content.Children.Add(CreateStudyAttachmentList(entry.Attachments));
            details.Content = content; return true;
        }
        catch (Exception ex) { HandleError("학습 내용 조회",ex); details.Content = StatsText("내용을 불러오지 못했어요. 접었다가 다시 펼쳐 주세요.",true); return false; }
        finally { busy = false; Body.IsEnabled = true; }
    }
    private async Task ChangeStudyPageAsync(int delta)
    {
        if (busy || studyOverview is not { } report) return;
        studyPage = Math.Clamp(studyPage+delta,0,report.Pages-1); await RefreshStatisticsAsync();
        await Dispatcher.InvokeAsync(JumpToStudyOverview,DispatcherPriority.Loaded);
    }
    private async Task OpenStudyDateAsync(DateOnly date)
    {
        if (busy || !await ConfirmPendingAsync()) return;
        try
        {
            if (statisticsOpen) CloseStatistics();
            await SelectDateAsync(date); if (selected != date) return;
            month = new(date.Year,date.Month,1); await RefreshCalendarAsync(); SelectRecordTab("study"); StudyPane.ScrollToTop();
        }
        catch (Exception ex) { HandleError("학습 날짜 이동",ex); }
    }
}
