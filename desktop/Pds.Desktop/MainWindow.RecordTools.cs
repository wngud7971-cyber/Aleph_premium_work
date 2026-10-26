using System.Windows;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private bool recordToolsOpen,recordToolsHadDetails;
    private JournalToolsView? recordToolsView;
    private async void Records_Click(object sender,RoutedEventArgs e)=>await OpenRecordToolsAsync("search");
    private async Task OpenRecordToolsAsync(string start)
    {
        if(busy)return;
        if(recordToolsOpen)return;
        if(!await ConfirmPendingAsync())return;
        var turn=PreparePageTurn(true);
        try
        {
            busy=true;Body.IsEnabled=false;
            recordToolsHadDetails=statisticsOpen?statisticsHadDetails:Details.Visibility==Visibility.Visible;
            if(statisticsOpen)CloseStatistics(true);
            recordToolsView=new(repository,this,selected,verify,start);
            recordToolsView.CloseRequested+=RecordTools_CloseRequested;
            RecordToolsPresenter.Content=recordToolsView;recordToolsOpen=true;
            CalendarArea.Visibility=Details.Visibility=StatisticsPane.Visibility=DashboardHeader.Visibility=Visibility.Collapsed;
            DetailColumn.Width=new GridLength(0);RecordsHost.Visibility=Visibility.Visible;CloseMeditation();ClosePlanReminder();RefreshNavigationVisuals();
            await recordToolsView.InitializeAsync();
            busy=false;Body.IsEnabled=true;Status.Text="기록 관리 · 검색·휴지통·월간 결산과 연간 활동";
            await PlayPageTurnAsync(turn);
        }
        catch(Exception ex){RestoreCalendarFromRecords();HandleError("기록 관리",ex);}
        finally{busy=false;Body.IsEnabled=true;if(pageTurn==turn)CancelPageTurn();}
    }
    private async void RecordTools_CloseRequested()
    {
        var hit=recordToolsView?.NavigateTo;
        await CloseRecordToolsAsync(navigate:hit);
    }
    private void RestoreCalendarFromRecords()
    {
        if(recordToolsView is not null)recordToolsView.CloseRequested-=RecordTools_CloseRequested;
        RecordToolsPresenter.Content=null;recordToolsView=null;recordToolsOpen=false;RecordsHost.Visibility=Visibility.Collapsed;
        CalendarArea.Visibility=DashboardHeader.Visibility=Visibility.Visible;
        Details.Visibility=recordToolsHadDetails?Visibility.Visible:Visibility.Collapsed;DetailColumn.Width=new GridLength(recordToolsHadDetails?330:0);
        RefreshNavigationVisuals();
    }
    private async Task<bool> CloseRecordToolsAsync(bool animate=true,bool keepPageTurn=false,SearchHit? navigate=null)
    {
        if(!recordToolsOpen)return true;
        if(recordToolsView?.TryLeave()!=true)return false;
        var turn=animate?PreparePageTurn(false):null;
        try
        {
            RestoreCalendarFromRecords();
            if(navigate is not null)await NavigateSearchHitAsync(navigate);
            else {await LoadSelectedAsync();busy=true;await RefreshCalendarAsync();busy=false;}
            if(animate)await PlayPageTurnAsync(turn);
            return true;
        }
        catch(Exception ex){HandleError("기록 관리에서 돌아오기",ex);return false;}
        finally{busy=false;Body.IsEnabled=true;if(!keepPageTurn && pageTurn==turn)CancelPageTurn();ContinueEntryGuidance();}
    }
    private async Task NavigateSearchHitAsync(SearchHit hit)
    {
        if(busy || !await ConfirmPendingAsync())return;
        if(recordToolsOpen && !await CloseRecordToolsAsync())return;
        if(statisticsOpen)CloseStatistics();month=new DateOnly(hit.Date.Year,hit.Date.Month,1);await SelectDateAsync(hit.Date);await RefreshCalendarAsync();
        SelectRecordTab(hit.Kind);RevealSelectedDate();
        Status.Text=$"{hit.Date:yyyy.MM.dd} · {JournalToolsWindow.KindName(hit.Kind)} 검색 결과를 열었어요.";
    }
    private async Task EditFixedCostAsync(Guid id)
    {
        if(busy || !await ConfirmPendingAsync())return;
        try
        {
            var rule=await Task.Run(()=>repository.LoadFixedCosts().Single(r=>r.Id==id));
            var dialog=new FixedCostEditWindow(repository,rule,this,LocalToday);dialog.ShowDialog();
            if(dialog.Saved){DrawFixedCosts();SaveStatus.Text="반복 변경 저장 완료";}
        }
        catch(Exception ex){HandleError("반복 변경",ex);}
    }
}
