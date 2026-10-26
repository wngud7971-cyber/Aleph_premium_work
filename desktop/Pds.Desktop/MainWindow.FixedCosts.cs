using System.Windows;
using System.Windows.Controls;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private Func<MessageBoxResult>? fixedCostStopDecision;
    private void FixedCost_Changed(object sender,RoutedEventArgs e)
    {
        if(!ready || suppress || MoneyRepeat is null)return;
        if(MoneyFixed.IsChecked!=true)MoneyRepeat.IsChecked=false;
        MoneyRepeat.IsEnabled=MoneyFixed.IsChecked==true && moneyEditing is null;
        if(HasMoneyInput)MoneyComposer.IsExpanded=true;
        MoneyError.Visibility=Visibility.Collapsed;UpdateDraftStatus();
    }
    private void DrawFixedCosts()
    {
        if(FixedCostList is null)return;
        FixedCostList.Children.Clear();
        var rules=repository.LoadFixedCosts().Where(r=>r.Enabled).OrderBy(r=>r.FirstDate.Day).ThenBy(r=>r.Template.Title).ToArray();
        FixedCostHeading.Text=$"매월 자동 반영 · {rules.Length}개";
        if(rules.Length==0)FixedCostList.Children.Add(StatsText("고정 지출을 작성하고 ‘매월 반복’을 켜 보세요.",true));
        foreach(var rule in rules)
        {
            var content=new StackPanel();
            var effective=rule.TemplateIn(new DateOnly(LocalToday.Year,LocalToday.Month,1));
            var title=StatsText(effective.Title);title.FontWeight=FontWeights.SemiBold;content.Children.Add(title);
            content.Children.Add(UiLayout.Values(StatsText("현재 적용",true),StatsText(Won(effective.Won))));
            content.Children.Add(StatsText($"{effective.Category} · 매월 {rule.FirstDate.Day}일\n없는 날짜는 말일에 반영",true));
            if(rule.EndMonth is DateOnly end)content.Children.Add(StatsText($"{end:yyyy.MM}까지 반영",true));
            foreach(var change in rule.Changes)content.Children.Add(UiLayout.Values(StatsText($"{change.Month:yyyy.MM}부터\n{change.Template.Title}",true),StatsText(Won(change.Template.Won),true)));
            content.Children.Add(JournalAction("변경·종료 월 설정",()=>_=EditFixedCostAsync(rule.Id)));
            var stop=JournalAction("반복 중단",()=>_=StopFixedCostAsync(rule.Id));
            System.Windows.Automation.AutomationProperties.SetName(stop,rule.Template.Title+" 매월 반복 중단");
            content.Children.Add(stop);FixedCostList.Children.Add(JournalCard(content));
        }
    }
    private async Task<bool> StopFixedCostAsync(Guid id)
    {
        if(busy || !await ConfirmPendingAsync())return false;
        var decision=verify && fixedCostStopDecision is not null ? fixedCostStopDecision() : System.Windows.MessageBox.Show(this,
            "앞으로 자동 반영을 중단할까요?\n이미 저장된 금액과 이전 달의 내역은 그대로 남아요.\n이 설정은 즉시 저장돼요.","고정 지출 반복 중단",MessageBoxButton.YesNo,MessageBoxImage.Question);
        if(decision!=MessageBoxResult.Yes)return false;
        busy=true;Body.IsEnabled=false;
        try { await Task.Run(()=>repository.StopFixedCost(id));DrawFixedCosts();SaveStatus.Text="반복 중단 완료";return true; }
        catch(Exception ex) { HandleError("반복 중단",ex);return false; }
        finally { busy=false;Body.IsEnabled=true; }
    }
}
