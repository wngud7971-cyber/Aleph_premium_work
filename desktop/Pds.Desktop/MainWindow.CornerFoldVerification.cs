using System.Windows.Media.Media3D;

namespace Pds.Desktop;

public partial class MainWindow
{
    private static void VerifyCornerFoldGeometry(Action<bool,string> check,bool dark)
    {
        const double w=844,h=716;
        foreach(var forward in new[]{true,false})
        {
            var curl=new CornerPageTurn(w,h,forward);var mirrored=new CornerPageTurn(w,h,!forward);
            var label=(forward?"right":"left")+"-"+dark;
            check(curl.Mesh.Positions.Count==(CornerPageTurn.Columns+1)*(CornerPageTurn.Rows+1) && curl.Mesh.Positions.All(p=>p.Z==0),"corner-fold-starts-as-flat-full-page-"+label);
            var x=forward?w:0;var corner=curl.FoldPoint(x,h,.28);var opposite=curl.FoldPoint(w-x,0,.28);
            check((forward?corner.X<w:corner.X>0) && corner.Y>0 && corner.Z>0 && opposite==new Point3D(w-x,h,0),"bottom-corner-curls-diagonally-upward-opposite-corner-stays-flat-"+label);
            var points=Enumerable.Range(0,CornerPageTurn.Rows+1).SelectMany(row=>Enumerable.Range(0,CornerPageTurn.Columns+1).Select(col=>curl.FoldPoint(w*col/CornerPageTurn.Columns,h*row/CornerPageTurn.Rows,1))).ToArray();
            check(points.All(p=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z) && (p.X<0 || p.X>w || p.Y<0 || p.Y>h)),"corner-fold-finishes-outside-page-without-final-pop-"+label);
            var a=curl.FoldPoint(w*.23,h*.81,.44);var b=mirrored.FoldPoint(w*.77,h*.81,.44);
            check(Math.Abs(a.X+b.X-w)<1e-6 && Math.Abs(a.Y-b.Y)<1e-6 && Math.Abs(a.Z-b.Z)<1e-6,"forward-and-backward-folds-mirror-bottom-corners-"+label);
        }
    }
}
