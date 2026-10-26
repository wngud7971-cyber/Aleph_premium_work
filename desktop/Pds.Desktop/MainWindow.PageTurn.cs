using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    internal const int PageTurnMs=BookTurnVisuals.DurationMs;
    private PageTurnState? pageTurn;
    private sealed class PageTurnState(CornerPageTurn curl,GeometryModel3D face,bool reverse)
    {
        public CornerPageTurn Curl {get;}=curl;
        public GeometryModel3D Face {get;}=face;
        public bool Reverse {get;}=reverse;
        public bool TargetCaptured {get;set;}
        public double From=>Reverse?1:0;
        public double To=>Reverse?0:1;
        public TaskCompletionSource<bool> Completion {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private void ConfigurePageTurn()
    {
        IsVisibleChanged+=(_,e)=>{if(!(bool)e.NewValue)CancelPageTurn();};
        Closed+=(_,_)=>CancelPageTurn();
        SizeChanged+=(_,_)=>CancelPageTurn();
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized)CancelPageTurn();};
    }
    private void CancelPageTurn()
    {
        var previous=pageTurn;pageTurn=null;
        PageTurnLayer.Children.Clear();PageTurnLayer.Visibility=Visibility.Hidden;
        previous?.Curl.Stop();previous?.Completion.TrySetResult(false);
    }
    private RenderTargetBitmap CapturePageImage(bool excludeTurn=false)
    {
        var oldVisibility=PageTurnLayer.Visibility;
        try
        {
            if(excludeTurn)PageTurnLayer.Visibility=Visibility.Hidden;
            UpdateLayout();
            var w=PageTurnLayer.ActualWidth;var h=PageTurnLayer.ActualHeight;
            var origin=PageTurnLayer.TranslatePoint(new Point(),PageRoot);var dpi=VisualTreeHelper.GetDpi(this);
            var drawing=new DrawingVisual();
            using(var dc=drawing.RenderOpen())
            {
                dc.DrawRectangle(ResourceBrush("Canvas"),null,new Rect(0,0,w,h));
                var brush=new VisualBrush(PageRoot){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(origin,new Size(w,h)),Stretch=Stretch.Fill};
                dc.DrawRectangle(brush,null,new Rect(0,0,w,h));
            }
            var image=new RenderTargetBitmap((int)Math.Ceiling(w*dpi.DpiScaleX),(int)Math.Ceiling(h*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
            image.Render(drawing);image.Freeze();return image;
        }
        finally{if(excludeTurn)PageTurnLayer.Visibility=oldVisibility;}
    }
    private PageTurnState? PreparePageTurn(bool forward)
    {
        if(!CalmMotion.AnimationsEnabled || verify && CalmMotion.AnimationOverride!=true || !IsVisible)
        {CancelPageTurn();return null;}
        UpdateLayout();var w=PageTurnLayer.ActualWidth;var h=PageTurnLayer.ActualHeight;
        if(w<1 || h<1){CancelPageTurn();return null;}
        var image=CapturePageImage();CancelPageTurn();
        var scene=BookTurnVisuals.Create(image,w,h,((SolidColorBrush)ResourceBrush("Paper")).Color,forward);
        var curl=scene.Curl;var face=scene.Face;var viewport=scene.Viewport;
        if(!forward)PageTurnLayer.Children.Add(new Image{Source=image,Stretch=Stretch.Fill,IsHitTestVisible=false});
        PageTurnLayer.Children.Add(viewport);PageTurnLayer.Visibility=Visibility.Visible;
        return pageTurn=new(curl,face,!forward);
    }
    private async Task PlayPageTurnAsync(PageTurnState? state)
    {
        if(state is null || pageTurn!=state)return;
        if(state.Reverse)
        {
            // The statistics cover remains in place while the calendar settles into its restored editor width.
            await Dispatcher.InvokeAsync(()=>{UpdateLayout();if(CalendarArea.IsVisible)FitCalendar();UpdateLayout();},DispatcherPriority.Loaded);
            if(pageTurn!=state)return;
            // Capture the restored live calendar without the outgoing statistics cover. No UI frame is yielded while hidden.
            state.Face.Material=new DiffuseMaterial(new ImageBrush(CapturePageImage(true)){Stretch=Stretch.Fill});
            state.TargetCaptured=true;
        }
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Loaded);
        if(pageTurn!=state)return;
        var animation=new DoubleAnimation(state.From,state.To,TimeSpan.FromMilliseconds(PageTurnMs))
        {EasingFunction=new SmoothRestEase{EasingMode=EasingMode.EaseIn},FillBehavior=FillBehavior.HoldEnd};
        animation.Completed+=(_,_)=>
        {
            if(pageTurn!=state)return;
            pageTurn=null;PageTurnLayer.Children.Clear();PageTurnLayer.Visibility=Visibility.Hidden;
            state.Curl.Stop();state.Completion.TrySetResult(true);
        };
        state.Curl.BeginAnimation(CornerPageTurn.ProgressProperty,animation,HandoffBehavior.SnapshotAndReplace);
        await state.Completion.Task;
    }
    private async Task CloseStatisticsAnimatedAsync()
    {
        if(!statisticsOpen)return;
        var turn=PreparePageTurn(false);
        try{CloseStatistics(true);await PlayPageTurnAsync(turn);}
        finally{if(pageTurn==turn)CancelPageTurn();}
    }
}
