using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyUnifiedMotionAsync(List<string> checks)
    {
        var start=checks.Count;var oldMotion=CalmMotion.AnimationOverride;var oldDark=settings.Dark;var oldAccent=settings.Accent;
        var samples=new List<object>();
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        async Task Wait(Func<bool> ready)
        {for(var i=0;i<150 && !ready();i++){await Task.Delay(10);await Idle();}if(!ready())throw new InvalidOperationException("unified motion timeout");}
        IEnumerable<T> Find<T>(DependencyObject root)where T:DependencyObject
        {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var item in Find<T>(child))yield return item;}}
        void CheckThumbs(string suffix,bool require=true)
        {
            var bars=Find<ScrollBar>(this).Where(b=>b.IsVisible).ToArray();
            Check((bars.Length>0 || !require) && bars.All(b=>{b.ApplyTemplate();var track=(Track)b.Template.FindName("PART_Track",b);return track.Thumb.Background is SolidColorBrush brush && brush.Color==((SolidColorBrush)b.FindResource("Accent")).Color;}),"all-visible-scrollbars-use-accent-"+suffix);
        }
        try
        {
            var points=Enumerable.Range(0,1001).Select(i=>i/1000d).ToArray();
            Check(points.Zip(points.Skip(1),(a,b)=>SmoothRestEase.Sample(a)<=SmoothRestEase.Sample(b)).All(x=>x) && SmoothRestEase.Sample(0)==0 && SmoothRestEase.Sample(1)==1,"shared-book-and-panel-curve-is-bounded-and-monotonic");
            var h=.001;var acceleration=(SmoothRestEase.Sample(2*h)-2*SmoothRestEase.Sample(h))/(h*h);
            Check(Math.Abs(acceleration)<.1 && Math.Abs(SmoothRestEase.Sample(h))<1e-7,"shared-rest-curve-has-soft-speed-and-acceleration-at-endpoints");
            Check(points.Zip(points.Skip(1),(a,b)=>ResponsiveArrivalEase.Sample(a)<=ResponsiveArrivalEase.Sample(b)).All(x=>x) && ResponsiveArrivalEase.Sample(.1)>.08 && ResponsiveArrivalEase.Sample(.5)>.6,"date-arrival-curve-starts-promptly-and-remains-monotonic");
            Check(Math.Abs((ResponsiveArrivalEase.Sample(1)-ResponsiveArrivalEase.Sample(1-h))/h)<.001 && DatePanMs==320 && SidebarSlideMs==440,"date-pan-settles-with-zero-speed-and-new-contextual-durations");
            pendingChangeDecision=()=>MessageBoxResult.No;CloseMeditation();ClosePlanReminder();if(statisticsOpen)CloseStatistics();
            foreach(var dark in new[]{false,true})
            {
                CalmMotion.AnimationOverride=false;settings.Dark=dark;settings.Accent=oldAccent;ApplyTheme();
                month=new(2026,10,1);await RefreshCalendarAsync();await SelectDateAsync(new(2026,10,25));await CloseDetailsAnimatedAsync();await Idle();UpdateLayout();
                var cell=((Button)MonthGrid.Children[0]).RenderSize;var height=CalendarViewport.ViewportHeight;
                var templateRoot=(Grid)VisualTreeHelper.GetChild(CalendarViewport,0);
                Check(templateRoot.RowDefinitions.Count==2 && templateRoot.RowDefinitions[1].Height.Value==12,"calendar-reserves-scrollbar-gutter-before-overflow-"+dark);
                var bar=Find<ScrollBar>(CalendarViewport).Single(b=>b.Orientation==Orientation.Horizontal);
                Check(!bar.IsVisible,"calendar-full-width-does-not-show-unused-scrollbar-"+dark);
                CalmMotion.AnimationOverride=true;var opening=AnimateSidebarAsync(true);await Wait(()=>bar.IsVisible);await Idle();
                var surface=(FrameworkElement)VisualTreeHelper.GetChild(bar,0);
                Check(CalmMotion.GetSoftScrollAppearance(surface) && surface.HasAnimatedProperties && surface.Opacity>=0 && surface.Opacity<1,"calendar-scrollbar-fades-in-when-first-needed-"+dark);
                Check(Math.Abs(CalendarViewport.ViewportHeight-height)<1.1 && Math.Abs(((Button)MonthGrid.Children[0]).ActualHeight-cell.Height)<1.1,"scrollbar-appearance-keeps-viewport-height-and-date-squares-"+dark);
                Capture("unified-scrollbar-appearance-"+dark+".png");await opening;await Idle();
                Check(Math.Abs(CalendarViewport.ViewportHeight-height)<1.1 && !DetailColumn.HasAnimatedProperties,"opening-settles-without-reserving-space-late-"+dark);
                SelectRecordTab("study");StudyComposer.IsExpanded=true;await Idle();
                foreach(var accent in new[]{"#E3BACA","#DE9D3E"}){settings.Accent=accent;ApplyTheme();await Idle();CheckThumbs("calendar-editor-"+dark+accent);}
                StudyComposer.IsExpanded=false;settings.Accent=oldAccent;ApplyTheme();
                CalmMotion.AnimationOverride=false;await OpenRecordToolsAsync("search");var view=recordToolsView!;var revision=repository.Load(selected).Revision;
                await view.LoadSummaryAsync(selected);view.QueryInput.Text="모션 중 유지";view.BudgetInput.Text="47,000";
                CalmMotion.AnimationOverride=true;
                foreach(var next in new[]{"trash","summary","trash","search"})
                {
                    var previous=view.CurrentTab;var forward=Array.IndexOf(new[]{"search","trash","summary"},next)>Array.IndexOf(new[]{"search","trash","summary"},previous);
                    var change=view.SwitchAsync(next);await Wait(()=>view.TabTurnActive && !view.IsBusy && (forward || view.TabTargetCaptured));
                    Check(view.TabTurnReverse==!forward && BookTurnVisuals.DurationMs==PageTurnMs,"record-subtab-uses-same-book-direction-and-duration-"+previous+"-"+next+"-"+dark);
                    if(!forward)Check(view.TabTargetCaptured,"record-subtab-reverse-captures-incoming-content-"+next+"-"+dark);
                    await Task.Delay(120);Capture("unified-subtab-"+previous+"-"+next+"-"+dark+".png");await change;
                    Check(!view.TabTurnActive && view.TabOverlayChildren==0 && view.QueryInput.Text=="모션 중 유지" && view.BudgetInput.Text=="47,000","record-subtab-cleans-clocks-and-preserves-drafts-"+next+"-"+dark);
                    CheckThumbs("record-subtab-"+next+"-"+dark,require:false);
                }
                var first=view.SwitchAsync("trash");await Wait(()=>view.TabTurnActive && !view.IsBusy);var second=view.SwitchAsync("summary");await Wait(()=>view.TabTurnActive && !view.IsBusy);var last=view.SwitchAsync("search");await Task.WhenAll(first,second,last);
                Check(view.CurrentTab=="search" && view.TabOverlayChildren==0 && view.BudgetDirty,"rapid-record-tab-reversal-finishes-latest-request-with-budget-intact-"+dark);
                var hidden=view.SwitchAsync("trash");await Wait(()=>view.TabTurnActive);Hide();await hidden;RestoreWindow();await Idle();
                Check(view.CurrentTab=="trash" && !view.TabTurnActive && view.TabOverlayChildren==0 && view.QueryInput.Text=="모션 중 유지","hiding-record-tab-cancels-only-animation-"+dark);
                CalmMotion.AnimationOverride=false;await view.SwitchAsync("summary");
                Check(!view.TabTurnActive && view.TabOverlayChildren==0 && view.BudgetInput.Text=="47,000","reduced-motion-record-tabs-preserve-final-screen-and-budget-"+dark);
                foreach(var accent in new[]{"#E3BACA","#DE9D3E"}){settings.Accent=accent;ApplyTheme();await Idle();CheckThumbs("record-manager-"+dark+accent);}
                Check(repository.Load(selected).Revision==revision,"animation-and-scroll-color-do-not-write-records-"+dark);
                samples.Add(new{dark,viewportHeight=height,cellHeight=cell.Height,gutter=12,bookMs=PageTurnMs,dateMs=DatePanMs,panelMs=SidebarSlideMs});
                view.Confirmation=_=>true;await CloseRecordToolsAsync(animate:false);
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!,"unified-motion.json"),JsonSerializer.Serialize(new{success=true,count=checks.Count-start,checks=checks.Skip(start).ToArray(),samples,physicalSmoothnessConfirmed=false,personalDataUsed=false}));
        }
        finally
        {
            if(recordToolsView is not null){recordToolsView.Confirmation=_=>true;await CloseRecordToolsAsync(animate:false);}
            CancelPageTurn();CancelSidebarMotion(true);CalmMotion.AnimationOverride=oldMotion;settings.Dark=oldDark;settings.Accent=oldAccent;ApplyTheme();pendingChangeDecision=null;
        }
    }
}
