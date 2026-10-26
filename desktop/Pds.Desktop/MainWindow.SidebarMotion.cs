using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    internal const int SidebarSlideMs=440;
    private TaskCompletionSource<bool>? sidebarCompletion;
    private long sidebarGeneration;
    private double sidebarTargetWidth,sidebarTargetOffset;
    private bool sidebarScrollInterrupted,sidebarPanActive;
    private bool dateMouseSelectionPending;
    private readonly TranslateTransform calendarPan=new(),weekdayPan=new();
    private static readonly DependencyProperty SidebarOffsetProperty=DependencyProperty.RegisterAttached(
        "SidebarOffset",typeof(double),typeof(MainWindow),new PropertyMetadata(0d,(o,e)=>
        {if(Window.GetWindow(o) is MainWindow window)window.SynchronizeWeekdays();}));
    private double DisplayedCalendarOffset=>datePanActive?CalendarViewport.HorizontalOffset-calendarPan.X:sidebarPanActive?(double)CalendarViewport.GetValue(SidebarOffsetProperty):CalendarViewport.HorizontalOffset;
    private void ConfigureSidebarMotion()
    {
        MonthGrid.RenderTransform=calendarPan;WeekDays.RenderTransform=weekdayPan;
        CalendarViewport.PreviewMouseDown+=(_,e)=>
        {
            InterruptSidebarScroll();
            if(!IsCalendarDateElement(e.OriginalSource as DependencyObject))return;
            dateMouseSelectionPending=true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background,()=>dateMouseSelectionPending=false);
        };
        MonthGrid.RequestBringIntoView+=(_,e)=>
        {
            // Mouse focus must not queue a second, instant ScrollViewer.MakeVisible beside our pan.
            if((dateMouseSelectionPending || datePanActive) && IsCalendarDateElement(e.TargetObject))e.Handled=true;
        };
        CalendarViewport.PreviewMouseWheel+=(_,_)=>InterruptSidebarScroll();
        CalendarViewport.PreviewKeyDown+=(_,_)=>InterruptSidebarScroll();
        Body.IsVisibleChanged+=(_,e)=>{if(!(bool)e.NewValue)CancelSidebarMotion(true);};
        CalendarArea.IsVisibleChanged+=(_,e)=>{if(!(bool)e.NewValue)CancelSidebarMotion(true);};
        Closed+=(_,_)=>CancelSidebarMotion(true);
    }
    private bool IsCalendarDateElement(DependencyObject? element)
    {
        while(element is not null && element!=MonthGrid)
        {
            if(element is Button{Tag:DateOnly})return true;
            element=element is Visual?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
    private void CommitCalendarOffset(double offset)
    {
        StopDatePanClocks();
        // Set the base before removing the clock: removal must never expose the old starting offset.
        CalendarViewport.SetValue(SidebarOffsetProperty,offset);
        CalendarViewport.BeginAnimation(SidebarOffsetProperty,null);
        // The live ScrollViewer is updated once, after the render-only pan.
        CalendarViewport.ScrollToHorizontalOffset(UiPixels.RoundX(CalendarViewport,offset));
        CalendarViewport.UpdateLayout();
        sidebarPanActive=false;calendarPan.X=0;SynchronizeWeekdays();
    }
    private void InterruptSidebarScroll()
    {
        if(sidebarCompletion is null)return;
        var wasDatePan=datePanActive;
        var offset=DisplayedCalendarOffset;sidebarScrollInterrupted=true;CommitCalendarOffset(offset);
        if(wasDatePan)
        {
            var completion=sidebarCompletion;sidebarCompletion=null;++sidebarGeneration;
            completion?.TrySetResult(false);
        }
    }
    private void CancelSidebarMotion(bool settle=false)
    {
        if(sidebarCompletion is null)return;
        var completion=sidebarCompletion;sidebarCompletion=null;++sidebarGeneration;
        var width=settle?sidebarTargetWidth:DetailColumn.Width.Value;
        var offset=settle && !sidebarScrollInterrupted?sidebarTargetOffset:DisplayedCalendarOffset;
        DetailColumn.BeginAnimation(ColumnDefinition.WidthProperty,null);DetailColumn.Width=new GridLength(width);
        if(settle && width<.1)Details.Visibility=Visibility.Collapsed;
        UpdateLayout();CommitCalendarOffset(offset);completion.TrySetResult(false);
    }
    private double SidebarOffsetFor(double width)
    {
        if(width<.1)return 0;
        var viewport=Math.Max(1,CalendarViewport.ViewportWidth+DetailColumn.ActualWidth-width);
        var cell=MonthGrid.Children.OfType<Button>().FirstOrDefault(b=>b.Tag is DateOnly date && date==selected);
        if(cell is null)return 0;
        var left=cell.TranslatePoint(new Point(),MonthGrid).X;var right=left+cell.ActualWidth;
        var offset=DisplayedCalendarOffset;
        if(left<offset+4)offset=left-4;else if(right>offset+viewport-4)offset=right-viewport+4;
        return Math.Clamp(offset,0,Math.Max(0,MonthGrid.Width-viewport));
    }
    private Task<bool> AnimateSidebarAsync(bool open)
    {
        // A date change in an already-open editor has no geometry to remeasure.
        if(open && Details.Visibility==Visibility.Visible && Math.Abs(DetailColumn.ActualWidth-330)<.1 && !DetailColumn.HasAnimatedProperties && !fitQueued)
        {
            CancelSidebarMotion();sidebarTargetWidth=330;sidebarTargetOffset=SidebarOffsetFor(330);sidebarScrollInterrupted=false;
            var enabled=CalmMotion.AnimationsEnabled && (!verify || CalmMotion.AnimationOverride==true) && IsVisible && CalendarArea.IsVisible;
            if(enabled && Math.Abs(CalendarViewport.HorizontalOffset-sidebarTargetOffset)>=.5)return AnimateDatePanAsync(sidebarTargetOffset);
            if(Math.Abs(CalendarViewport.HorizontalOffset-sidebarTargetOffset)>=.01)CommitCalendarOffset(sidebarTargetOffset);
            return Task.FromResult(true);
        }
        CancelSidebarMotion();UpdateLayout();FitCalendar();UpdateLayout();
        var from=DetailColumn.ActualWidth;var target=open?330d:0;
        sidebarTargetWidth=target;sidebarTargetOffset=SidebarOffsetFor(target);sidebarScrollInterrupted=false;
        if(open)Details.Visibility=Visibility.Visible;
        var animate=CalmMotion.AnimationsEnabled && (!verify || CalmMotion.AnimationOverride==true) && IsVisible && CalendarArea.IsVisible;
        if(!animate || Math.Abs(from-target)<.1 && Math.Abs(CalendarViewport.HorizontalOffset-sidebarTargetOffset)<.5)
        {
            DetailColumn.Width=new GridLength(target);if(!open)Details.Visibility=Visibility.Collapsed;
            FitCalendar();UpdateLayout();CommitCalendarOffset(sidebarTargetOffset);return Task.FromResult(true);
        }
        if(open && Math.Abs(from-target)<.1)return AnimateDatePanAsync(sidebarTargetOffset);
        var serial=++sidebarGeneration;var completion=sidebarCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var offsetFrom=CalendarViewport.HorizontalOffset;
        var slide=new PixelGridLengthAnimation{From=from,To=target,Duration=TimeSpan.FromMilliseconds(SidebarSlideMs),FillBehavior=FillBehavior.Stop};
        var scroll=new DoubleAnimation(offsetFrom,sidebarTargetOffset,TimeSpan.FromMilliseconds(SidebarSlideMs))
        {EasingFunction=new SmoothRestEase{EasingMode=EasingMode.EaseIn},FillBehavior=FillBehavior.HoldEnd};
        slide.Completed+=async (_,_)=>
        {
            if(serial!=sidebarGeneration || sidebarCompletion is null)return;
            DetailColumn.BeginAnimation(ColumnDefinition.WidthProperty,null);DetailColumn.Width=new GridLength(target);
            var offset=sidebarScrollInterrupted?CalendarViewport.HorizontalOffset:sidebarTargetOffset;
            if(!open)Details.Visibility=Visibility.Collapsed;
            FitCalendar();UpdateLayout();CommitCalendarOffset(offset);
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Loaded);
            if(serial!=sidebarGeneration || sidebarCompletion is null)return;
            sidebarCompletion=null;completion.TrySetResult(true);
        };
        sidebarPanActive=true;
        CalendarViewport.SetValue(SidebarOffsetProperty,offsetFrom);
        // With an already-open editor only the render transform changes; no per-frame layout/scroll commands.
        DetailColumn.Width=new GridLength(target);
        DetailColumn.BeginAnimation(ColumnDefinition.WidthProperty,slide,HandoffBehavior.SnapshotAndReplace);
        CalendarViewport.BeginAnimation(SidebarOffsetProperty,scroll,HandoffBehavior.SnapshotAndReplace);
        return completion.Task;
    }
    private async Task CloseDetailsAnimatedAsync()
    {if(await AnimateSidebarAsync(false))UpdateCalendarSelection(false);}
}
