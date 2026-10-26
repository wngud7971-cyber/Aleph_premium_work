using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifySteadyUiAsync(List<string> checks)
    {
        var start=checks.Count;var oldDark=settings.Dark;var oldMotion=CalmMotion.AnimationOverride;
        var results=new List<object>();
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
        {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T t)yield return t;foreach(var x in Find<T>(child))yield return x;}}
        try
        {
            CloseMeditation();ClosePlanReminder();if(statisticsOpen)CloseStatistics();pendingChangeDecision=()=>MessageBoxResult.No;
            foreach(var dark in new[]{false,true})
            {
                settings.Dark=dark;ApplyTheme();month=new(2026,10,1);await RefreshCalendarAsync();
                CalmMotion.AnimationOverride=false;await CloseDetailsAnimatedAsync();await Idle();
                var cells=MonthGrid.Children.Cast<UIElement>().ToArray();var headers=WeekDays.Children.Cast<UIElement>().ToArray();
                var disabledEvents=0;DependencyPropertyChangedEventHandler enabled=(_,e)=>{if(!(bool)e.NewValue)disabledEvents++;};Body.IsEnabledChanged+=enabled;
                try
                {
                    CalmMotion.AnimationOverride=true;
                    for(var round=0;round<2;round++)
                    {
                        await SelectDateAsync(new DateOnly(2026,10,18+round));await Idle();
                        Check(cells.SequenceEqual(MonthGrid.Children.Cast<UIElement>()) && headers.SequenceEqual(WeekDays.Children.Cast<UIElement>()),"record-open-retains-calendar-and-weekday-nodes-"+dark+"-"+round);
                        Check(Body.IsEnabled && Body.IsHitTestVisible && Details.Opacity==1 && disabledEvents==0,"record-load-never-dims-body-and-restores-input-"+dark+"-"+round);
                        var chosen=MonthGrid.Children.OfType<Button>().Single(b=>b.Tag is DateOnly d && d==selected);
                        Check(((SolidColorBrush)chosen.Background).Color==((SolidColorBrush)ResourceBrush("Accent")).Color,"record-open-updates-selected-cell-without-rebuild-"+dark+"-"+round);
                        await CloseDetailsAnimatedAsync();await Idle();
                        Check(cells.SequenceEqual(MonthGrid.Children.Cast<UIElement>()) && headers.SequenceEqual(WeekDays.Children.Cast<UIElement>()) && Details.Visibility==Visibility.Collapsed,"record-close-retains-nodes-and-removes-selection-"+dark+"-"+round);
                    }
                }
                finally{Body.IsEnabledChanged-=enabled;}
                await ShowTodayAsync();await Idle();
                Check(cells.SequenceEqual(MonthGrid.Children.Cast<UIElement>()) && headers.SequenceEqual(WeekDays.Children.Cast<UIElement>()) && selected==LocalToday,"today-record-button-reuses-current-month-calendar-"+dark);
                await CloseDetailsAnimatedAsync();await Idle();
                Check(cells.SequenceEqual(MonthGrid.Children.Cast<UIElement>()),"today-record-close-does-not-rebuild-calendar-"+dark);
                month=new DateOnly(LocalToday.Year,LocalToday.Month,1).AddMonths(-1);await RefreshCalendarAsync();await ShowTodayAsync();await Idle();
                Check(month==new DateOnly(LocalToday.Year,LocalToday.Month,1) && selected==LocalToday && Details.IsVisible && !Pending,"today-record-from-other-month-restores-month-and-opens-"+dark);
                await CloseDetailsAnimatedAsync();await Idle();
                Check(DashboardPlanLabel.FontSize==DashboardWorkoutLabel.FontSize && DashboardPlanValue.FontSize==DashboardWorkoutValue.FontSize
                    && Math.Abs(DashboardPlanLabel.TranslatePoint(new Point(),DashboardHeader).Y-DashboardWorkoutLabel.TranslatePoint(new Point(),DashboardHeader).Y)<1
                    && Math.Abs(DashboardPlanValue.TranslatePoint(new Point(),DashboardHeader).Y-DashboardWorkoutValue.TranslatePoint(new Point(),DashboardHeader).Y)<1,"dashboard-plan-workout-labels-values-same-size-and-rows-"+dark);
                Check(Find<TextBlock>(this).All(t=>t.FontFamily.Source=="Malgun Gothic") && Find<Control>(this).Where(c=>c is Button or TextBox or ComboBox).All(c=>c.FontFamily.Source=="Malgun Gothic")
                    && Brand.FontStyle==FontStyles.Italic && MonthTitle.FontStyle==FontStyles.Normal,"all-app-text-use-malgun-with-italic-brand-only-"+dark);
                var captions=MonthGrid.Children.OfType<Button>().SelectMany(b=>Find<TextBlock>(b)).Where(t=>t.Tag as string=="record-count-caption").ToArray();
                Check(captions.Length>0 && captions.All(t=>t.FontSize==UiTypography.Body),"calendar-plan-complete-workout-share-summary-size-"+dark);
                CalmMotion.Reveal(CalendarArea);CalmMotion.Reveal(Details);
                Check(CalendarArea.Opacity==1 && Details.Opacity==1 && CalmMotion.ActiveCount==0,"text-reveal-keeps-full-opacity-without-fade-clocks-"+dark);
                results.Add(new{dark,disabledEvents,calendarNodesRetained=true,summarySize=UiTypography.Body,bodyFont=FontFamily.Source});
                Capture("malgun-steady-calendar-"+dark+".png");
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!,"steady-ui.json"),JsonSerializer.Serialize(new{success=true,count=checks.Count-start,results,physicalFlickerConfirmed=false,personalDataUsed=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{CalmMotion.AnimationOverride=oldMotion;settings.Dark=oldDark;ApplyTheme();pendingChangeDecision=null;}
    }
}
