using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifySidebarMotionAsync(List<string> checks)
    {
        var begin=checks.Count;var original=CalmMotion.AnimationOverride;var oldDark=settings.Dark;
        var samples=new List<object>();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        try
        {
            pendingChangeDecision=()=>MessageBoxResult.No;ClosePlanReminder();CloseMeditation();if(statisticsOpen)CloseStatistics();
            foreach(var dark in new[]{false,true})
            {
                settings.Dark=dark;ApplyTheme();CalmMotion.AnimationOverride=false;
                await SelectDateAsync(new DateOnly(2026,10,18));month=new(2026,10,1);await RefreshCalendarAsync();
                await CloseDetailsAnimatedAsync();await Idle();UpdateLayout();
                var size=((Button)MonthGrid.Children[0]).RenderSize;var font=((Button)MonthGrid.Children[0]).FontSize;
                var revision=repository.Load(selected).Revision;CalmMotion.AnimationOverride=true;
                var watch=System.Diagnostics.Stopwatch.StartNew();
                var open=AnimateSidebarAsync(true);var widths=new List<double>{DetailColumn.ActualWidth};
                Check(!open.IsCompleted,"sidebar-opening-has-a-finite-transition-"+dark);
                for(var frame=0;frame<3;frame++)
                {
                    await Task.Delay(45);await Idle();UpdateLayout();widths.Add(DetailColumn.ActualWidth);
                    var cell=(Button)MonthGrid.Children[0];
                    File.AppendAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!,"slide-frames.jsonl"),JsonSerializer.Serialize(new{dark,frame,referenceWidth=size.Width,referenceHeight=size.Height,width=cell.ActualWidth,height=cell.ActualHeight,editor=Details.ActualWidth,column=DetailColumn.ActualWidth,viewport=CalendarViewport.ActualWidth})+"\n");
                    Check(Math.Abs(cell.ActualWidth-size.Width)<1.1 && Math.Abs(cell.ActualHeight-size.Height)<1.1 && cell.FontSize==font && Math.Abs(Details.ActualWidth-314)<1,"slide-keeps-calendar-squares-font-and-editor-width-frame-"+frame+"-"+dark);
                    if(frame==1)Capture("slide-opening-middle-"+dark+".png");
                }
                await open;var openingMs=watch.Elapsed.TotalMilliseconds;await Idle();UpdateLayout();widths.Add(DetailColumn.ActualWidth);
                Check(SidebarSlideMs==440 && openingMs>=200,"sidebar-opening-configured-440ms-is-not-instant-"+dark);
                Check(widths.Any(w=>w>1 && w<329) && widths.Zip(widths.Skip(1),(a,b)=>b>=a-.5).All(x=>x) && Math.Abs(DetailColumn.ActualWidth-330)<1,"sidebar-opens-monotonically-to-final-width-"+dark);
                RevealSelectedDate();await Idle();var chosen=MonthGrid.Children.OfType<Button>().Single(b=>b.Tag is DateOnly d && d==selected);var left=chosen.TranslatePoint(new Point(),CalendarViewport).X;
                Check(left>=-1 && left+chosen.ActualWidth<=CalendarViewport.ViewportWidth+1,"sliding-open-retains-selected-rightmost-date-"+dark);
                Capture("slide-open-final-"+dark+".png");
                watch.Restart();var close=CloseDetailsAnimatedAsync();await Task.Delay(50);await Idle();var midClose=DetailColumn.ActualWidth;
                Check(midClose>0 && midClose<330 && Details.Visibility==Visibility.Visible,"sidebar-remains-visible-during-slide-out-"+dark);
                Capture("slide-closing-middle-"+dark+".png");await close;var closingMs=watch.Elapsed.TotalMilliseconds;await Idle();UpdateLayout();
                Check(SidebarSlideMs==440 && closingMs>=200,"sidebar-closing-configured-440ms-is-not-instant-"+dark);
                Check(Details.Visibility==Visibility.Collapsed && DetailColumn.Width.Value==0 && Math.Abs(((Button)MonthGrid.Children[0]).ActualWidth-size.Width)<1.1,"sidebar-closes-without-resizing-calendar-cells-"+dark);
                var first=AnimateSidebarAsync(true);await Task.Delay(40);var firstWidth=DetailColumn.ActualWidth;var firstDone=first.IsCompleted;var reversed=AnimateSidebarAsync(false);await Task.Delay(40);var reversedWidth=DetailColumn.ActualWidth;var reversedDone=reversed.IsCompleted;var last=AnimateSidebarAsync(true);await Task.WhenAll(first,reversed,last);await Idle();
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!,"sidebar-reversal-diagnostics-"+dark+".json"),JsonSerializer.Serialize(new{first=first.Result,reversed=reversed.Result,last=last.Result,firstWidth,firstDone,reversedWidth,reversedDone,visible=Details.IsVisible,visibility=Details.Visibility.ToString(),width=DetailColumn.Width.Value,actualWidth=DetailColumn.ActualWidth,pending=sidebarCompletion is not null,animated=DetailColumn.HasAnimatedProperties,bodyVisible=Body.IsVisible,calendarVisible=CalendarArea.IsVisible},new JsonSerializerOptions{WriteIndented=true}));
                // A reversal can finish immediately when the first rendered frame is still closed.
                // Completed superseded requests are valid only if their requested endpoint was reached.
                var earlierRequestsSettled=(!first.Result || firstDone && Math.Abs(firstWidth-330)<1)
                    && (!reversed.Result || reversedDone && Math.Abs(reversedWidth)<1);
                Check(earlierRequestsSettled && last.Result && Details.IsVisible && DetailColumn.Width.Value==330 && sidebarCompletion is null && !DetailColumn.HasAnimatedProperties,"rapid-reversal-keeps-last-request-and-removes-clocks-"+dark);
                var interrupted=AnimateSidebarAsync(false);await Task.Delay(40);InterruptSidebarScroll();var manualOffset=CalendarViewport.HorizontalOffset;await interrupted;await Idle();
                Check(sidebarScrollInterrupted && !CalendarViewport.HasAnimatedProperties,"manual-scroll-stops-automatic-scroll-clock-"+dark);
                var hidden=AnimateSidebarAsync(true);await Task.Delay(40);Body.Visibility=Visibility.Collapsed;await hidden;Body.Visibility=Visibility.Visible;await Idle();
                Check(sidebarCompletion is null && !DetailColumn.HasAnimatedProperties && DetailColumn.Width.Value==330,"hidden-body-settles-sidebar-without-idle-work-"+dark);
                CalmMotion.AnimationOverride=false;await CloseDetailsAnimatedAsync();var instant=AnimateSidebarAsync(true);
                Check(instant.IsCompletedSuccessfully && sidebarCompletion is null && !DetailColumn.HasAnimatedProperties && Details.IsVisible,"reduced-motion-applies-final-sidebar-state-directly-"+dark);
                Check(repository.Load(selected).Revision==revision && !Pending,"sidebar-motion-does-not-write-or-dirty-records-"+dark);
                samples.Add(new{dark,widths,midClose,openingMs,closingMs,cellWidth=size.Width,editorWidth=Details.ActualWidth,manualOffset});
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!,"sidebar-slide.json"),JsonSerializer.Serialize(new{success=true,count=checks.Count-begin,milliseconds=SidebarSlideMs,windowsAnimationsEnabled=SystemParameters.ClientAreaAnimation,explicitOverrideTests=true,checks=checks.Skip(begin),samples,personalDataUsed=false,physicalPointerTested=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{CancelSidebarMotion(true);Body.Visibility=Visibility.Visible;CalmMotion.AnimationOverride=original;settings.Dark=oldDark;ApplyTheme();pendingChangeDecision=null;DrawCalendar();}
    }
}
