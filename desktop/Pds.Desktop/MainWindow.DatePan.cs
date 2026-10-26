using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    internal const int DatePanMs=320;
    private static readonly ResponsiveArrivalEase DatePanCurve=new(){EasingMode=EasingMode.EaseIn};
    private bool datePanActive;
    private void StopDatePanClocks()
    {
        if(!datePanActive)return;
        // Preserve the visible frame while detaching clocks, before settling the real scroll position.
        calendarPan.X=calendarPan.X;weekdayPan.X=weekdayPan.X;
        calendarPan.BeginAnimation(TranslateTransform.XProperty,null);
        weekdayPan.BeginAnimation(TranslateTransform.XProperty,null);
        datePanActive=false;
    }
    private Task<bool> AnimateDatePanAsync(double target)
    {
        var serial=++sidebarGeneration;
        var completion=sidebarCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeOffset=CalendarViewport.HorizontalOffset;
        var centered=Math.Max(0,(CalendarViewport.ViewportWidth-MonthGrid.Width)/2);
        sidebarPanActive=datePanActive=true;
        var duration=TimeSpan.FromMilliseconds(DatePanMs);
        DoubleAnimation Move(double from,double to)=>new(from,to,duration)
        {EasingFunction=DatePanCurve,FillBehavior=FillBehavior.HoldEnd};
        var calendar=Move(calendarPan.X,nativeOffset-target);
        var weekdays=Move(weekdayPan.X,centered-target);
        calendar.Completed+=async (_,_)=>
        {
            if(serial!=sidebarGeneration || sidebarCompletion is null)return;
            CommitCalendarOffset(target);
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Loaded);
            if(serial!=sidebarGeneration || sidebarCompletion is null)return;
            sidebarCompletion=null;completion.TrySetResult(true);
        };
        // Independent transform clocks avoid a UI-thread scroll callback and pixel snapping on every frame.
        calendarPan.BeginAnimation(TranslateTransform.XProperty,calendar,HandoffBehavior.SnapshotAndReplace);
        weekdayPan.BeginAnimation(TranslateTransform.XProperty,weekdays,HandoffBehavior.SnapshotAndReplace);
        return completion.Task;
    }
}
