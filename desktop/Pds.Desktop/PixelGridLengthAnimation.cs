using System.Windows;
using System.Windows.Media.Animation;

namespace Pds.Desktop;

internal sealed class PixelGridLengthAnimation:AnimationTimeline
{
    public static readonly DependencyProperty FromProperty=DependencyProperty.Register(nameof(From),typeof(double),typeof(PixelGridLengthAnimation));
    public static readonly DependencyProperty ToProperty=DependencyProperty.Register(nameof(To),typeof(double),typeof(PixelGridLengthAnimation));
    public double From {get=>(double)GetValue(FromProperty);set=>SetValue(FromProperty,value);}
    public double To {get=>(double)GetValue(ToProperty);set=>SetValue(ToProperty,value);}
    public override Type TargetPropertyType=>typeof(GridLength);
    protected override Freezable CreateInstanceCore()=>new PixelGridLengthAnimation();
    public override object GetCurrentValue(object origin,object destination,AnimationClock clock)
    {var p=Math.Clamp(clock.CurrentProgress??0,0,1);return new GridLength(Math.Max(0,From+(To-From)*SmoothRestEase.Sample(p)));}
}
