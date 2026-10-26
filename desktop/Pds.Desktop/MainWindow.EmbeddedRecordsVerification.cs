using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyEmbeddedRecordsAsync(List<string> checks)
    {
        var start=checks.Count;var oldMotion=CalmMotion.AnimationOverride;var oldDark=settings.Dark;
        var folder=Path.GetDirectoryName(repository.DatabasePath)!;var day=new DateOnly(2023,8,18);
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        async Task Wait(Func<bool> condition)
        {for(var i=0;i<200 && !condition();i++){await Task.Delay(10);await Idle();}if(!condition())throw new InvalidOperationException("embedded record state timeout");}
        IEnumerable<T> Find<T>(DependencyObject root)where T:DependencyObject
        {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var item in Find<T>(child))yield return item;}}
        try
        {
            CalmMotion.AnimationOverride=false;pendingChangeDecision=()=>MessageBoxResult.No;CloseMeditation();ClosePlanReminder();if(statisticsOpen)CloseStatistics();
            foreach(var (month,minutes) in new[]{(1,(int?)60),(2,(int?)120),(3,(int?)null),(4,(int?)90),(8,(int?)180)})
            {
                var date=month==8?day:new DateOnly(2023,month,1);
                repository.Save(repository.Load(date)with{Studies=[StudyEntry.Create("화면통합 학습 "+month)with{DurationMinutes=minutes}]});
            }
            await SelectDateAsync(day);month=new(day.Year,day.Month,1);await RefreshCalendarAsync();
            var owned=OwnedWindows.Count;var size=(ActualWidth,ActualHeight);
            foreach(var dark in new[]{false,true})
            {
                repository.RemoveBudget(day);
                settings.Dark=dark;ApplyTheme();await OpenRecordToolsAsync("summary");await Idle();
                var view=recordToolsView!;
                Check(recordToolsOpen && Window.GetWindow(view)==this && RecordToolsPresenter.Content==view && OwnedWindows.Count==owned,"record-management-is-embedded-with-no-new-window-"+dark);
                Check(!statisticsOpen && !CalendarArea.IsVisible && RecordsHost.IsVisible && (ActualWidth,ActualHeight)==size,"embedded-screen-keeps-window-size-and-hides-calendar-"+dark);
                Check(RecordsNav.FontWeight==FontWeights.SemiBold && CalendarNav.FontWeight==FontWeights.Normal && StatisticsNav.FontWeight==FontWeights.Normal,"embedded-records-nav-marks-only-current-screen-"+dark);
                var wasPending=entryGuidancePending;entryGuidancePending=true;await ProcessEntryGuidanceAsync(true);
                Check(entryGuidancePending && MeditationCard.Visibility!=Visibility.Visible && ReminderOverlay.Visibility!=Visibility.Visible,"entry-guidance-waits-until-leaving-record-management-"+dark);entryGuidancePending=wasPending;
                view.AnnualMetricChoice.SelectedIndex=5;await Idle();
                var line=Find<AnnualLineChart>(view).Single();
                Check(line.Values.Count==12 && line.Values[0]==60 && line.Values[1]==120 && line.Values[2] is null && line.Values[4]==0 && line.Values[7]==180,"line-chart-has-twelve-months-and-preserves-unknown-versus-zero-"+dark);
                Check(line.SegmentCount==9 && line.ActualWidth>600 && line.ActualHeight==140,"line-chart-does-not-bridge-missing-months-and-fits-app-"+dark);
                Check(Find<System.Windows.Controls.Primitives.UniformGrid>(view).Single(g=>g.Columns==12).Children.Count==12,"line-chart-retains-all-twelve-month-labels-"+dark);
                var labels=Find<TextBlock>(view).Where(t=>Enumerable.Range(1,12).Any(m=>t.Text==m+"월")).ToArray();
                Check(labels.Length==12 && labels.All(t=>t.TranslatePoint(new Point(),view).Y>=line.TranslatePoint(new Point(0,line.ActualHeight),view).Y+4),"line-month-labels-have-space-below-points-"+dark);
                view.SummaryScroll.ScrollToBottom();await Idle();Capture("records-in-app-line-"+(dark?"dark":"light")+".png");
                view.QueryInput.Text="검색어 유지";view.BudgetInput.Text="40,000";
                Theme_Click(this,new RoutedEventArgs());await Idle();
                Check(view.BudgetInput.Text=="40,000" && view.QueryInput.Text=="검색어 유지" && view.BudgetDirty,"embedded-theme-switch-preserves-search-and-budget-drafts-"+dark);
                Check(((SolidColorBrush)view.FindResource("Ink")).Color==((SolidColorBrush)ResourceBrush("Ink")).Color && Find<TextBlock>(view).Where(t=>t.IsVisible).All(t=>t.FontFamily.Source=="Malgun Gothic"),"embedded-theme-and-malgun-font-stay-in-sync-"+dark);
                view.Confirmation=_=>false;
                Check(!await CloseRecordToolsAsync() && recordToolsView==view && view.BudgetInput.Text=="40,000","cancel-calendar-return-keeps-budget-draft-and-view-"+dark);
                await OpenStatisticsAsync();Check(recordToolsOpen && !statisticsOpen && view.BudgetDirty,"cancel-statistics-navigation-keeps-unsaved-budget-"+dark);
                await view.SaveBudgetAsync();Check(repository.BudgetFor(day).Budget==40000 && !view.BudgetDirty,"embedded-budget-save-writes-only-selected-month-"+dark);
                Close();Check(!IsVisible && recordToolsView==view,"tray-hide-retains-embedded-view-"+dark);RestoreWindow();await Idle();
                Check(recordToolsOpen && view.QueryInput.Text=="검색어 유지" && IsVisible,"tray-restore-keeps-record-management-screen-"+dark);
                view.Confirmation=_=>true;await CloseRecordToolsAsync();
                Check(!recordToolsOpen && recordToolsView is null && RecordToolsPresenter.Content is null && CalendarArea.IsVisible && selected==day,"leaving-records-detaches-view-and-restores-date-"+dark);
            }
            await OpenRecordToolsAsync("search");recordToolsView!.QueryInput.Text="화면통합 학습 3";await recordToolsView.SearchAsync();await Idle();
            var result=Find<Button>(recordToolsView).Single(b=>Equals(b.Content,"해당 날짜로 이동"));result.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(()=>!recordToolsOpen && !busy && selected==new DateOnly(2023,3,1) && StudyPane.IsVisible && month==new DateOnly(2023,3,1));
            Check(StudyPane.IsVisible && month==new DateOnly(2023,3,1),"embedded-search-result-returns-to-calendar-and-correct-study-date");
            await OpenRecordToolsAsync("summary");var before=recordToolsView!;before.BudgetInput.Text="12,000";before.Confirmation=_=>false;
            var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};before.BudgetInput.RaiseEvent(escape);await Idle();
            Check(recordToolsOpen && before.BudgetInput.Text=="12,000","embedded-escape-cancel-keeps-unsaved-budget");before.Confirmation=_=>true;
            before.BudgetInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});await Wait(()=>!recordToolsOpen && !busy);
            Check(CalendarArea.IsVisible,"embedded-escape-confirm-returns-home");
            CalmMotion.AnimationOverride=true;
            var open=OpenRecordToolsAsync("summary");await Wait(()=>recordToolsOpen && pageTurn is not null);
            Check(pageTurn is{Reverse:false,From:0,To:1} && PageTurnMs==1050,"calendar-to-records-uses-existing-forward-book-animation");await Task.Delay(180);Capture("records-book-forward.png");await open;
            Check(pageTurn is null && PageTurnLayer.Children.Count==0,"records-forward-animation-cleans-up-snapshots");
            var back=CloseRecordToolsAsync();await Wait(()=>pageTurn is{Reverse:true,TargetCaptured:true});
            Check(pageTurn is{From:1,To:0},"records-to-calendar-unwinds-the-same-book");await Task.Delay(180);Capture("records-book-reverse.png");await back;
            await OpenStatisticsAsync();open=OpenRecordToolsAsync("summary");await Wait(()=>recordToolsOpen && pageTurn is not null);
            Check(pageTurn is{Reverse:false} && !statisticsOpen,"statistics-to-records-uses-forward-turn");await open;
            var toStatistics=OpenStatisticsAsync();await Wait(()=>pageTurn is{Reverse:true,TargetCaptured:true});
            Check(!recordToolsOpen && statisticsOpen,"records-to-statistics-uses-reverse-turn-with-target-capture");await toStatistics;await CloseStatisticsAnimatedAsync();
            open=OpenRecordToolsAsync("search");await Wait(()=>recordToolsOpen && pageTurn is not null);Close();await open;
            Check(pageTurn is null && PageTurnLayer.Children.Count==0 && recordToolsOpen,"hide-cancels-records-book-animation-without-losing-screen");RestoreWindow();await CloseRecordToolsAsync(animate:false);
            open=OpenRecordToolsAsync("summary");await Wait(()=>recordToolsOpen && !busy && pageTurn is not null);back=CloseRecordToolsAsync();await Task.WhenAll(open,back);
            Check(!recordToolsOpen && pageTurn is null && PageTurnLayer.Children.Count==0,"rapid-records-return-leaves-no-orphan-animation");
            CalmMotion.AnimationOverride=false;await OpenRecordToolsAsync("summary");Check(recordToolsOpen && pageTurn is null && PageTurnLayer.Children.Count==0,"reduced-motion-records-navigation-skips-capture");
            recordToolsView!.QueryInput.Text="닫기 없이 검색";await CloseRecordToolsAsync();
            File.WriteAllText(Path.Combine(folder,"records-in-app.json"),JsonSerializer.Serialize(new{success=true,count=checks.Count-start,checks=checks.Skip(start).ToArray(),chart="line",months=12,pageTurnMs=PageTurnMs,newNativeWindows=false,personalDataUsed=false}));
        }
        finally
        {
            if(recordToolsView is not null){recordToolsView.Confirmation=_=>true;await CloseRecordToolsAsync(animate:false);}
            CancelPageTurn();CalmMotion.AnimationOverride=oldMotion;settings.Dark=oldDark;ApplyTheme();pendingChangeDecision=null;
        }
    }
}
