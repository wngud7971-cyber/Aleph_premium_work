using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyCrispSpacingAsync(List<string> checks)
    {
        var start=checks.Count;var originalDark=settings.Dark;var originalDpi=VisualTreeHelper.GetDpi(this);
        var originalMotion=CalmMotion.AnimationOverride;var samples=new List<object>();
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        try
        {
            CalmMotion.AnimationOverride=false;CloseMeditation();ClosePlanReminder();pendingChangeDecision=()=>MessageBoxResult.No;
            foreach(var dark in new[]{false,true})
            foreach(var scale in new[]{1d,1.25,1.5,1.75,2})
            {
                settings.Dark=dark;ApplyTheme();VisualTreeHelper.SetRootDpi(this,new DpiScale(scale,scale));
                await CloseDetailsAnimatedAsync();await Idle();UpdateLayout();FitCalendar();
                var label=dark+"-"+scale;
                Check(Math.Abs(VisualTreeHelper.GetDpi(MonthTitle).DpiScaleX-scale)<.001 && TextOptions.GetTextHintingMode(this)==TextHintingMode.Fixed && TextOptions.GetTextRenderingMode(this)==TextRenderingMode.ClearType
                    && TextOptions.GetTextHintingMode(MonthTitle)==TextHintingMode.Fixed,"text-fixed-quality-applied-at-dpi-"+label);
                Check(CalendarViewport.Background is SolidColorBrush {Color.A:255} && WeekDays.Background is SolidColorBrush {Color.A:255}
                    && RenderOptions.GetClearTypeHint(CalendarViewport)==ClearTypeHint.Enabled
                    && RenderOptions.GetClearTypeHint(Details)==ClearTypeHint.Enabled,"cleartype-hints-have-opaque-calendar-and-detail-backgrounds-"+label);
                var gaps=new List<double>();
                foreach(var date in new[]{new DateOnly(2026,10,3),new DateOnly(2026,10,9),new DateOnly(2026,10,5),new DateOnly(2026,8,15),new DateOnly(2026,12,25)})
                {
                    month=new(date.Year,date.Month,1);await RefreshCalendarAsync();await Idle();UpdateLayout();
                    var cell=MonthGrid.Children.OfType<Button>().Single(b=>b.Tag is DateOnly d && d==date);var grid=(Grid)cell.Content;
                    var number=grid.Children.OfType<TextBlock>().First();var caption=grid.Children.OfType<TextBlock>().Single(t=>t.Tag as string=="holiday-caption");
                    var numberInk=CalendarFormattedText(number,number.Text).BuildGeometry(new Point()).Bounds;
                    var captionInk=CalendarFormattedText(caption,caption.Text.Split('\n')[0]).BuildGeometry(new Point()).Bounds;
                    var gap=caption.TranslatePoint(new Point(captionInk.Left,0),grid).X-number.TranslatePoint(new Point(numberInk.Right,0),grid).X;
                    gaps.Add(gap);samples.Add(new{dark,scale,date=date.ToString(),gap,numberSlot=grid.ColumnDefinitions[0].ActualWidth,caption=caption.Text});
                    if(date==new DateOnly(2026,10,5))
                    {
                        Check(caption.Text=="대체" && caption.ToolTip?.ToString()=="대체공휴일(개천절)" && cell.ToolTip.ToString()!.Contains("대체공휴일(개천절)"),"substitute-caption-short-with-full-name-retained-"+label);
                        if(scale==1)Capture("substitute-calendar-"+dark+".png");
                    }
                }
                Check(gaps.All(g=>g>=4 && g<=8),"one-and-two-digit-holiday-visible-gaps-stay-compact-"+label);
                Check(gaps.Max()-gaps.Min()<=2.5,"holiday-gap-does-not-reserve-unused-second-digit-"+label);
                await SelectDateAsync(new DateOnly(2026,10,18));await Idle();
                var headerX=((TranslateTransform)WeekDays.RenderTransform).X*scale;
                var snapped=UiPixels.RoundX(CalendarViewport,12.345)*scale;
                Check(Math.Abs(headerX-Math.Round(headerX))<.0001 && Math.Abs(snapped-Math.Round(snapped))<.0001,"weekday-and-scroll-offset-use-physical-pixels-"+label);
                CalmMotion.Settle(this);Capture("crisp-calendar-"+label+".png");
            }
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!,"crisp-spacing.json"),JsonSerializer.Serialize(new{success=true,count=checks.Count-start,samples,simulatedDpiOnly=true,physicalTextClarityConfirmed=false,personalDataUsed=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            VisualTreeHelper.SetRootDpi(this,originalDpi);settings.Dark=originalDark;ApplyTheme();CalmMotion.AnimationOverride=originalMotion;pendingChangeDecision=null;
            month=new(2026,10,1);await RefreshCalendarAsync();
        }
    }
}
