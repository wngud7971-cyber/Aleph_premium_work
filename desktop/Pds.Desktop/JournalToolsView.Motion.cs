using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Pds.Desktop;

internal sealed partial class JournalToolsView
{
    private readonly Grid tabOverlay=new(){Visibility=Visibility.Hidden,ClipToBounds=true,Background=Brushes.Transparent};
    private string? currentTab;
    private long tabGeneration;
    private bool tabLoading;
    private TabTurn? tabTurn;
    internal bool TabTurnActive=>tabTurn is not null;
    internal string? CurrentTab=>currentTab;
    internal bool? TabTurnReverse=>tabTurn?.Reverse;
    internal bool TabTargetCaptured=>tabTurn?.TargetCaptured==true;
    internal int TabOverlayChildren=>tabOverlay.Children.Count;
    private sealed record TabTurn(BookTurnVisuals.Scene Scene,bool Reverse)
    {
        internal TaskCompletionSource<bool> Completion {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool TargetCaptured {get;set;}
    }
    private void ConfigureTabMotion()
    {
        content.ClipToBounds=true;Panel.SetZIndex(tabOverlay,10);content.Children.Add(tabOverlay);
        Unloaded+=(_,_)=>CancelTabTurn();IsVisibleChanged+=(_,e)=>{if(!(bool)e.NewValue)CancelTabTurn();};
        content.SizeChanged+=(_,_)=>CancelTabTurn();
    }
    private TabTurn? PrepareTabTurn(string next)
    {
        if(currentTab is null || currentTab==next || !CalmMotion.AnimationsEnabled || verification && CalmMotion.AnimationOverride!=true || !IsVisible)
        {CancelTabTurn();return null;}
        content.UpdateLayout();if(content.ActualWidth<1 || content.ActualHeight<1){CancelTabTurn();return null;}
        // Keep the current composition when reversing an unfinished turn.
        var image=CaptureTabImage(includeOverlay:true);CancelTabTurn();
        var order=new[]{"search","trash","summary"};var forward=Array.IndexOf(order,next)>Array.IndexOf(order,currentTab);
        var scene=BookTurnVisuals.Create(image,content.ActualWidth,content.ActualHeight,((SolidColorBrush)B("Paper")).Color,forward);
        if(!forward)tabOverlay.Children.Add(new Image{Source=image,Stretch=Stretch.Fill,IsHitTestVisible=false});
        tabOverlay.Children.Add(scene.Viewport);tabOverlay.Visibility=Visibility.Visible;return tabTurn=new(scene,!forward);
    }
    private BitmapSource CaptureTabImage(bool includeOverlay=false)
    {
        return BookTurnVisuals.Capture(content,tabOverlay,B("Paper"),!includeOverlay);
    }
    private async Task PlayTabTurnAsync(TabTurn? turn)
    {
        if(turn is null || tabTurn!=turn)return;
        await Dispatcher.InvokeAsync(()=>content.UpdateLayout(),DispatcherPriority.Loaded);if(tabTurn!=turn)return;
        if(turn.Reverse)
        {turn.Scene.Face.Material=new DiffuseMaterial(new ImageBrush(CaptureTabImage()){Stretch=Stretch.Fill});turn.TargetCaptured=true;}
        var animation=new DoubleAnimation(turn.Reverse?1:0,turn.Reverse?0:1,TimeSpan.FromMilliseconds(BookTurnVisuals.DurationMs))
        {EasingFunction=new SmoothRestEase{EasingMode=EasingMode.EaseIn},FillBehavior=FillBehavior.HoldEnd};
        animation.Completed+=(_,_)=>{if(tabTurn!=turn)return;tabTurn=null;tabOverlay.Children.Clear();tabOverlay.Visibility=Visibility.Hidden;turn.Scene.Curl.Stop();turn.Completion.TrySetResult(true);};
        turn.Scene.Curl.BeginAnimation(CornerPageTurn.ProgressProperty,animation,HandoffBehavior.SnapshotAndReplace);await turn.Completion.Task;
    }
    internal void CancelTabTurn()
    {
        var old=tabTurn;tabTurn=null;tabOverlay.Children.Clear();tabOverlay.Visibility=Visibility.Hidden;
        old?.Scene.Curl.Stop();old?.Completion.TrySetResult(false);
    }
    private async Task NavigateTabAsync(string next)
    {
        if(busy || tabLoading)return;
        try{await SwitchAsync(next);}catch(Exception ex){status.Text=ex.Message;}
    }
}
