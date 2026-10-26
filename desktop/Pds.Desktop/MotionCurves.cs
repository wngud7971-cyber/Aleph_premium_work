using System.Windows;
using System.Windows.Media.Animation;

namespace Pds.Desktop;

internal sealed class SmoothRestEase : EasingFunctionBase
{
    internal static double Sample(double t)=>t*t*t*(10+t*(-15+6*t));
    protected override double EaseInCore(double t)=>Sample(t);
    protected override Freezable CreateInstanceCore()=>new SmoothRestEase();
}
internal sealed class ResponsiveArrivalEase : EasingFunctionBase
{
    // Starts promptly at a moderate speed; speed and acceleration both settle to zero.
    internal static double Sample(double t)=>t+t*t*t*(4+t*(-7+3*t));
    protected override double EaseInCore(double t)=>Sample(t);
    protected override Freezable CreateInstanceCore()=>new ResponsiveArrivalEase();
}
