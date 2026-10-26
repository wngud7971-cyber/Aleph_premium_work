using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyPageTurnAsync(List<string> checks)
    {
        var start=checks.Count;var oldMotion=CalmMotion.AnimationOverride;var oldDark=settings.Dark;
        var samples=new List<object>();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        async Task WaitFor(Func<bool> condition)
        {for(var i=0;i<100&&!condition();i++){await Task.Delay(10);await Idle();}if(!condition())throw new InvalidOperationException("motion test state timeout");}
        try
        {
            pendingChangeDecision=()=>MessageBoxResult.No;CloseMeditation();ClosePlanReminder();if(statisticsOpen)CloseStatistics();
            foreach(var dark in new[]{false,true})
            {
                settings.Dark=dark;ApplyTheme();CalmMotion.AnimationOverride=false;
                month=new(2026,10,1);await RefreshCalendarAsync();await SelectDateAsync(new(2026,10,19));await Idle();UpdateLayout();
                var nodes=MonthGrid.Children.Cast<object>().ToArray();var height=DashboardHeader.ActualHeight;
                var surface=CalendarSurface.TranslatePoint(new Point(),PageRoot);var header=DashboardHeader.TranslatePoint(new Point(),PageRoot);
                Check(Math.Abs(surface.X-header.X)<1 && Math.Abs(CalendarSurface.ActualWidth-DashboardHeader.ActualWidth)<1,"dashboard-matches-calendar-surface-edges-with-editor-"+dark);
                Check(YearTitle.TranslatePoint(new Point(0,YearTitle.ActualHeight),MonthHeader).Y<=MonthTitle.TranslatePoint(new Point(),MonthHeader).Y+1,"year-above-month-"+dark);
                Check(Brand.FontStyle==FontStyles.Italic && MonthTitle.FontStyle==FontStyles.Normal,"brand-italic-month-upright-malgun-"+dark);
                CalmMotion.AnimationOverride=true;
                foreach(var date in new[]{new DateOnly(2026,10,24),new DateOnly(2026,10,19),new DateOnly(2026,10,25),new DateOnly(2026,10,19)})
                {
                    var clicked=MonthGrid.Children.OfType<Button>().Single(b=>b.Tag is DateOnly d && d==date);
                    var beforeFocus=CalendarViewport.HorizontalOffset;
                    clicked.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});
                    clicked.BringIntoView();await Idle();
                    Check(Math.Abs(CalendarViewport.HorizontalOffset-beforeFocus)<.01,"mouse-focus-does-not-queue-a-competing-instant-scroll-"+date+"-"+dark);
                    var displayedBefore=snapshot.Date;var click=System.Diagnostics.Stopwatch.StartNew();
                    var pan=SelectDateAsync(date);var callbackMs=click.Elapsed.TotalMilliseconds;await WaitFor(()=>sidebarCompletion is not null || pan.IsCompleted);
                    Check(!pan.IsCompleted && sidebarPanActive && datePanActive && calendarPan.HasAnimatedProperties && !DetailColumn.HasAnimatedProperties && DatePanMs==320,"weekday-pan-starts-render-only-"+date+"-"+dark);
                    Check(callbackMs<150 && DatePanCurve.Ease(.1)>.08 && DatePanCurve.Ease(.5)>.6,"date-click-yields-promptly-and-moves-from-the-start-"+date+"-"+dark);
                    var actual=CalendarViewport.HorizontalOffset;var positions=new List<double>{DisplayedCalendarOffset};
                    for(var i=0;i<3;i++)
                    {
                        await Task.Delay(45);await Idle();positions.Add(DisplayedCalendarOffset);
                        Check((!datePanActive || Math.Abs(CalendarViewport.HorizontalOffset-actual)<.1 && snapshot.Date==displayedBefore) && Math.Abs(DetailColumn.ActualWidth-330)<1 && DashboardHeader.ActualHeight==height,"weekday-pan-keeps-scroll-layout-and-header-height-frame-"+date+"-"+i+"-"+dark);
                    }
                    Capture("weekday-pan-middle-"+date.Day+"-"+dark+".png");await pan;await Idle();
                    var cell=MonthGrid.Children.OfType<Button>().Single(b=>b.Tag is DateOnly d && d==date);var x=cell.TranslatePoint(new Point(),CalendarViewport).X;
                    Check(x>=-1 && x+cell.ActualWidth<=CalendarViewport.ViewportWidth+1 && !sidebarPanActive && calendarPan.X==0 && !CalendarViewport.HasAnimatedProperties,"weekday-pan-final-commit-no-extra-reveal-"+date+"-"+dark);
                    Check(nodes.SequenceEqual(MonthGrid.Children.Cast<object>()) && !Pending && snapshot.Date==date && !busy && Body.IsHitTestVisible,"weekday-pan-retains-calendar-nodes-and-clean-records-"+date+"-"+dark);
                    var decreasing=date.Day==19;
                    Check(positions.Zip(positions.Skip(1),(a,b)=>decreasing?b<=a+.01:b>=a-.01).All(x=>x) && !calendarPan.HasAnimatedProperties && !weekdayPan.HasAnimatedProperties,"weekend-monday-pan-monotonic-and-transform-clocks-clean-"+date+"-"+dark);
                    samples.Add(new{dark,date,callbackMs,positions,actualScrollDuringPan=actual,finalScroll=CalendarViewport.HorizontalOffset});
                }
                var interrupted=SelectDateAsync(new(2026,10,25));await WaitFor(()=>datePanActive);await Task.Delay(70);InterruptSidebarScroll();await interrupted;await Idle();
                Check(sidebarCompletion is null && !datePanActive && !calendarPan.HasAnimatedProperties && !weekdayPan.HasAnimatedProperties,"manual-interruption-completes-native-date-pan-without-orphan-task-"+dark);
                var returnToMonday=SelectDateAsync(new(2026,10,19));await returnToMonday;
                var keyboardTarget=MonthGrid.Children.OfType<Button>().Single(b=>b.Tag is DateOnly d && d==new DateOnly(2026,10,25));
                keyboardTarget.BringIntoView();await Idle();
                Check(CalendarViewport.HorizontalOffset>1 && !dateMouseSelectionPending,"keyboard-bring-into-view-still-works-outside-mouse-selection-"+dark);
                CommitCalendarOffset(0);await Idle();
                VerifyCornerFoldGeometry(Check,dark);
                var watch=System.Diagnostics.Stopwatch.StartNew();
                var open=OpenStatisticsAsync();await WaitFor(()=>pageTurn is not null && pageTurn.Curl.Progress>=.18 && !busy);
                await Idle();var turn=pageTurn;
                Check(statisticsOpen && turn is not null && turn.Curl.Forward && turn.Curl.Progress>0 && turn.Curl.Progress<1 && PageTurnLayer.IsVisible,"calendar-to-statistics-bottom-right-diagonal-curl-"+dark);
                Capture("book-forward-middle-"+dark+".png");await open;await Idle();
                Check(pageTurn is null && PageTurnLayer.Children.Count==0 && PageTurnLayer.Visibility==Visibility.Hidden && StatisticsPane.Opacity==1 && watch.Elapsed.TotalMilliseconds>=950 && PageTurnMs==1050,"page-turn-forward-cleans-snapshot-and-clocks-"+dark);
                var back=CloseStatisticsAnimatedAsync();await WaitFor(()=>pageTurn is not null && pageTurn.Curl.Progress<=.82 && pageTurn.Curl.Progress>0 || back.IsCompleted);await Idle();
                Check(!statisticsOpen && pageTurn is not null && pageTurn.Reverse && pageTurn.Curl.Forward && pageTurn.Curl.Progress>0 && pageTurn.Curl.Progress<1,"statistics-to-calendar-rewinds-same-bottom-right-curl-"+dark);
                var returning=pageTurn!;
                Check(returning.TargetCaptured && returning.From==1 && returning.To==0 && PageTurnLayer.Children.OfType<Image>().Single().Source is not null && returning.Face.Material is DiffuseMaterial material && material.Brush is ImageBrush incoming && !ReferenceEquals(incoming.ImageSource,PageTurnLayer.Children.OfType<Image>().Single().Source),"returning-sheet-is-incoming-calendar-above-outgoing-statistics-"+dark);
                var pose=returning.Curl.Progress;var a=turn!.Curl.FoldPoint(turn.Curl.Width,turn.Curl.Height,pose);var b=returning.Curl.FoldPoint(returning.Curl.Width,returning.Curl.Height,pose);
                Check((a-b).Length<.00001 && !turn.Reverse && returning.Reverse,"returning-sheet-follows-exact-forward-fold-path-in-reverse-"+dark);
                Capture("book-backward-middle-"+dark+".png");await back;await Idle();
                Check(returning.Curl.Progress<.00001 && returning.Curl.Mesh.Positions.All(p=>Math.Abs(p.Z)<.00001) && !returning.Curl.HasAnimatedProperties,"returning-calendar-ends-fully-unfolded-without-clocks-"+dark);
                Check(pageTurn is null && PageTurnLayer.Children.Count==0 && CalendarArea.IsVisible && CalendarArea.Opacity==1,"page-turn-back-restores-crisp-live-calendar-"+dark);
                var first=OpenStatisticsAsync();await WaitFor(()=>pageTurn is not null && !busy);await Task.Delay(50);
                var reverse=CloseStatisticsAnimatedAsync();await Task.WhenAll(first,reverse);await Idle();
                Check(!statisticsOpen && pageTurn is null && PageTurnLayer.Children.Count==0,"rapid-navigation-cancels-page-turn-latest-state-wins-"+dark);
                var captured=PreparePageTurn(true);var cancel=PlayPageTurnAsync(captured);CancelPageTurn();await cancel;
                Check(captured is not null && !captured.Curl.HasAnimatedProperties && pageTurn is null && PageTurnLayer.Children.Count==0,"cancel-releases-bitmap-model-and-curl-clock-"+dark);
                CalmMotion.AnimationOverride=false;await OpenStatisticsAsync();await CloseStatisticsAnimatedAsync();
                Check(pageTurn is null && PageTurnLayer.Children.Count==0 && !statisticsOpen,"reduced-motion-navigation-has-no-snapshot-or-clock-"+dark);
                await CloseDetailsAnimatedAsync();await Idle();
                surface=CalendarSurface.TranslatePoint(new Point(),PageRoot);header=DashboardHeader.TranslatePoint(new Point(),PageRoot);
                Check(Math.Abs(surface.X-header.X)<1 && Math.Abs(CalendarSurface.ActualWidth-DashboardHeader.ActualWidth)<1 && DashboardHeader.ActualHeight==height,"dashboard-matches-calendar-surface-edges-without-editor-"+dark);
                Capture("page-turn-calendar-final-"+dark+".png");
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!,"page-turn.json"),JsonSerializer.Serialize(new{success=true,count=checks.Count-start,pageTurnMs=PageTurnMs,datePanMs=DatePanMs,sidebarMs=SidebarSlideMs,checks=checks.Skip(start),samples,physicalSmoothnessConfirmed=false,personalDataUsed=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{CancelPageTurn();CancelSidebarMotion(true);CalmMotion.AnimationOverride=oldMotion;settings.Dark=oldDark;ApplyTheme();pendingChangeDecision=null;}
    }
}
