using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace Pds.Desktop;

// A finite cylindrical fold, starting at the chosen bottom corner. Coordinates use screen Y downward.
internal sealed class CornerPageTurn : Animatable
{
    internal const int Columns=40,Rows=32;
    internal readonly MeshGeometry3D Mesh=new();
    private double width,height,nx,ny,low,high,radius;
    internal bool Forward {get;private set;}
    internal double Width=>width;
    internal double Height=>height;
    internal static readonly DependencyProperty ProgressProperty=DependencyProperty.Register(
        nameof(Progress),typeof(double),typeof(CornerPageTurn),new PropertyMetadata(0d,(o,e)=>((CornerPageTurn)o).Draw((double)e.NewValue)));
    internal double Progress {get=>(double)GetValue(ProgressProperty);set=>SetValue(ProgressProperty,value);}
    private CornerPageTurn(){}
    internal CornerPageTurn(double width,double height,bool forward)
    {
        this.width=width;this.height=height;Forward=forward;
        nx=(forward?1:-1)/Math.Sqrt(2);ny=1/Math.Sqrt(2);
        low=forward?0:nx*width;high=forward?nx*width+ny*height:ny*height;
        radius=Math.Clamp(Math.Min(width,height)*.085,24,64);
        var uv=new PointCollection();var triangles=new Int32Collection();
        for(var row=0;row<=Rows;row++)for(var col=0;col<=Columns;col++)uv.Add(new Point(col/(double)Columns,row/(double)Rows));
        for(var row=0;row<Rows;row++)for(var col=0;col<Columns;col++)
        {
            var a=row*(Columns+1)+col;var b=a+Columns+1;
            triangles.Add(a);triangles.Add(b);triangles.Add(b+1);
            triangles.Add(a);triangles.Add(b+1);triangles.Add(a+1);
        }
        uv.Freeze();triangles.Freeze();Mesh.TextureCoordinates=uv;Mesh.TriangleIndices=triangles;Draw(0);
    }
    internal Point3D FoldPoint(double x,double y,double progress)
    {
        var crease=high+.01-Math.Clamp(progress,0,1)*(high-low+Math.PI*radius+.02);
        var distance=nx*x+ny*y-crease;
        if(distance<=0)return new Point3D(x,height-y,0);
        var angle=Math.Min(Math.PI,distance/radius);
        var projected=distance>Math.PI*radius?-(distance-Math.PI*radius):radius*Math.Sin(angle);
        var shift=projected-distance;
        return new Point3D(x+nx*shift,height-(y+ny*shift),radius*(1-Math.Cos(angle)));
    }
    private void Draw(double progress)
    {
        if(width<1 || height<1)return;
        var positions=new Point3DCollection((Columns+1)*(Rows+1));
        for(var row=0;row<=Rows;row++)for(var col=0;col<=Columns;col++)positions.Add(FoldPoint(width*col/Columns,height*row/Rows,progress));
        positions.Freeze();Mesh.Positions=positions;
    }
    internal void Stop()
    {Progress=Progress;BeginAnimation(ProgressProperty,null);}
    protected override Freezable CreateInstanceCore()=>new CornerPageTurn();
}
