using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private void DrawJournalStatistics()
    {
        if (journalStatistics is not { } report) return;
        var content = new StackPanel();
        var cards = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 8) };
        foreach (var (title, value) in new[] { ("수입 합계", report.Income), ("지출·저축 합계", report.Expense), ("수입 − 지출·저축", report.Balance) })
        {
            var stack = new StackPanel(); stack.Children.Add(StatsText(title, true));
            var amount = StatsText(Won(value), size: 15); amount.FontWeight = FontWeights.SemiBold; UiLayout.Numeric(amount); stack.Children.Add(amount);
            cards.Children.Add(new Border { Child = stack, Padding = new Thickness(14), Margin = new Thickness(0, 0, 8, 0),
                Background = ResourceBrush("Soft"), CornerRadius = new CornerRadius(12) });
        }
        content.Children.Add(cards);
        var monthly=new Button { Content="월간 결산 · 소비 예산",HorizontalAlignment=HorizontalAlignment.Left };
        monthly.Click+=async(_,_)=>await OpenRecordToolsAsync("summary");content.Children.Add(monthly);
        content.Children.Add(StatsText($"저장된 가계부 {report.MoneyRecords}건", true));
        content.Children.Add(StatsText("조회 기간의 합계예요. 차액은 계좌 잔액이 아니며 운동 필터는 적용되지 않아요.", true));
        content.Children.Add(StatsText($"소비 {Won(report.Consumption)} · 적금·저축 {Won(report.Savings)} · 고정 항목 {Won(report.FixedTotal)}"));
        content.Children.Add(StatsText("고정 항목은 위 합계에 이미 포함돼요. 적금·저축은 출금에 포함하고 소비 그래프에서는 제외해요.",true));
        content.Children.Add(StatsText($"고정 지출·저축 내역 · {report.FixedRecords}건", size:15));
        if(report.FixedLines.Count==0)content.Children.Add(StatsText("조회 기간에 반영된 고정 항목이 없어요.",true));
        foreach(var row in report.FixedLines)
        {
            var detail=UiLayout.Values(StatsText($"{row.Date:MM.dd} · {row.Title}\n{row.Category}"),StatsText(Won(row.Won)));
            content.Children.Add(new Border { Child=detail,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,6),Background=ResourceBrush("Paper"),CornerRadius=new CornerRadius(8) });
        }
        if(report.FixedRecords>report.FixedLines.Count)content.Children.Add(StatsText("최근 200건을 표시해요. 전체 확인은 조회 기간을 줄여 주세요.",true));
        content.Children.Add(StatsText("소비 유형별 지출", size: 15));
        if (report.Categories.Count == 0) content.Children.Add(StatsText("이 기간에 저장된 지출이 없어요.", true));
        var max = report.Categories.Select(c => c.Won).DefaultIfEmpty(1).Max();
        foreach (var row in report.Categories)
        {
            var grid = new Grid { Margin = new Thickness(0, 6, 8, 6), ToolTip = $"{row.Category}: {Won(row.Won)} · {row.Records}건" };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            grid.Children.Add(StatsText(row.Category));
            var bar = new Grid { Height = 8, Background = ResourceBrush("Soft"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(row.Won, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(max - row.Won, GridUnitType.Star) });
            bar.Children.Add(new Border { Background = ResourceBrush("Ink"), CornerRadius = new CornerRadius(4) }); Grid.SetColumn(bar, 1); grid.Children.Add(bar);
            var value = StatsText($"{Won(row.Won)} · {(decimal)row.Won / report.Consumption * 100:0.#}% · {row.Records}건"); UiLayout.Numeric(value);
            Grid.SetColumn(value, 2); grid.Children.Add(value); content.Children.Add(grid);
        }
        StatisticsContent.Children.Add(new Expander { Header = "가계부 통계 · 현재 조회 기간", IsExpanded = true,
            Content = content, Margin = new Thickness(0, 8, 0, 18) });
    }
}
