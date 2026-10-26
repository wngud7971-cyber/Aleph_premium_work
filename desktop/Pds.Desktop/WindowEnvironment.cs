using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pds.Desktop;

internal static class WindowEnvironment
{
    internal static Rect WorkArea(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(handle==IntPtr.Zero)return SystemParameters.WorkArea;
        var pixels=System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var fromDevice=PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice??Matrix.Identity;
        return new Rect(pixels.Left*fromDevice.M11,pixels.Top*fromDevice.M22,pixels.Width*fromDevice.M11,pixels.Height*fromDevice.M22);
    }
    internal static void Watch(Window window,Action? fit=null)
    {
        if(window.Owner is MainWindow main && main.VerificationMode)return;
        HwndSource? source=null;bool queued=false,closed=false;
        void Schedule()
        {
            if(queued || closed)return;queued=true;
            window.Dispatcher.BeginInvoke(()=>{queued=false;if(closed || window.WindowState!=WindowState.Normal)return;
                if(fit is not null){fit();return;}
                var area=WorkArea(window);window.MinWidth=Math.Min(window.MinWidth,area.Width);window.MinHeight=Math.Min(window.MinHeight,area.Height);
                window.Width=Math.Min(window.Width,area.Width);window.Height=Math.Min(window.Height,area.Height);
                var point=WindowPlacement.Fit(window.Left,window.Top,window.Width,window.Height,area.Left,area.Top,area.Width,area.Height);window.Left=point.Left;window.Top=point.Top;
            },DispatcherPriority.Loaded);
        }
        IntPtr Hook(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam,ref bool handled)
        {if(message is 0x007E or 0x001A or 0x02E0)Schedule();return IntPtr.Zero;}
        window.SourceInitialized+=(_,_)=>{source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);source?.AddHook(Hook);Schedule();};
        window.Closed+=(_,_)=>{closed=true;source?.RemoveHook(Hook);};
    }
}
