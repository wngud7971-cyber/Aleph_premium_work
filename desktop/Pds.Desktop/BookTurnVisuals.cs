using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Pds.Desktop;

internal static class BookTurnVisuals
{
    internal const int DurationMs=1050;
    internal sealed record Scene(CornerPageTurn Curl,GeometryModel3D Face,Viewport3D Viewport);
    internal static Scene Create(BitmapSource image,double w,double h,Color paper,bool forward)
    {
        var curl=new CornerPageTurn(w,h,true){Progress=forward?0:1};
        var back=new LinearGradientBrush();back.GradientStops.Add(new GradientStop(paper,0));
        back.GradientStops.Add(new GradientStop(Color.FromArgb(255,(byte)(paper.R*.90),(byte)(paper.G*.90),(byte)(paper.B*.90)),1));
        var face=new GeometryModel3D(curl.Mesh,new DiffuseMaterial(new ImageBrush(image){Stretch=Stretch.Fill})){BackMaterial=new DiffuseMaterial(back)};
        var model=new Model3DGroup();model.Children.Add(new AmbientLight(Color.FromScRgb(1,.82f,.82f,.82f)));
        model.Children.Add(new DirectionalLight(Color.FromScRgb(1,.18f,.18f,.18f),new Vector3D(0,0,-1)));model.Children.Add(face);
        var viewport=new Viewport3D{Camera=new OrthographicCamera(new Point3D(w/2,h/2,4*w),new Vector3D(0,0,-1),new Vector3D(0,1,0),w),IsHitTestVisible=false};
        viewport.Children.Add(new ModelVisual3D{Content=model});return new(curl,face,viewport);
    }
    internal static RenderTargetBitmap Capture(FrameworkElement source,Grid overlay,Brush background,bool excludeOverlay=true)
    {
        var visibility=overlay.Visibility;
        try
        {
            if(excludeOverlay)overlay.Visibility=Visibility.Hidden;source.UpdateLayout();var dpi=VisualTreeHelper.GetDpi(source);
            var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())
            {
                var bounds=new Rect(0,0,source.ActualWidth,source.ActualHeight);dc.DrawRectangle(background,null,bounds);
                dc.DrawRectangle(new VisualBrush(source){Stretch=Stretch.Fill},null,bounds);
            }
            var image=new RenderTargetBitmap((int)Math.Ceiling(source.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(source.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
            image.Render(drawing);image.Freeze();return image;
        }
        finally{overlay.Visibility=visibility;}
    }
}
