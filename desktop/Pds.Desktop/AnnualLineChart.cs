using System.Windows;
using System.Windows.Media;

namespace Pds.Desktop;

// Twelve monthly positions, with a gap at every unknown value. Never interpolate missing activity.
internal sealed class AnnualLineChart(IReadOnlyList<decimal?> values,decimal maximum,Brush accent,Brush paper,Brush grid) : FrameworkElement
{
    internal IReadOnlyList<decimal?> Values {get;}=values;
    internal int SegmentCount=>Enumerable.Range(1,Math.Max(0,Values.Count-1)).Count(i=>Values[i-1] is not null && Values[i] is not null);
    internal void SetPalette(Brush color,Brush background,Brush guides)
    {accent=color;paper=background;grid=guides;InvalidateVisual();}
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);if(ActualWidth<=0 || ActualHeight<=16)return;
        var height=ActualHeight-16;var baseline=ActualHeight-8;
        var guide=new Pen(grid,1);var stroke=new Pen(accent,2.5){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
        foreach(var fraction in new[]{0d,.5,1d})drawing.DrawLine(guide,new Point(0,baseline-height*fraction),new Point(ActualWidth,baseline-height*fraction));
        Point Position(int i,decimal value)=>new(ActualWidth*(i+.5)/Values.Count,baseline-height*(double)(value/maximum));
        for(var i=1;i<Values.Count;i++)if(Values[i-1] is decimal previous && Values[i] is decimal current)drawing.DrawLine(stroke,Position(i-1,previous),Position(i,current));
        for(var i=0;i<Values.Count;i++)if(Values[i] is decimal value)drawing.DrawEllipse(paper,stroke,Position(i,value),3.8,3.8);
    }
}
