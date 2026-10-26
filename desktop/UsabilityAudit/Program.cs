using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pds.Core;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using Application=System.Windows.Application;

static class Program
{
    const BindingFlags All=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static string folder="";static readonly List<string> checks=[];static readonly List<object> metrics=[];static Window? owner,tools;static PlanRepository repo=null!;
    static readonly DateOnly date=new(2026,10,18);
    static object? Get(object o,string n)=>o.GetType().GetProperty(n,All)?.GetValue(o)??o.GetType().GetField(n,All)?.GetValue(o);
    static T C<T>(string n)=>(T)Get(tools!,n)!;
    static async Task Call(string n,params object[] a)=>await (Task)tools!.GetType().GetMethod(n,All)!.Invoke(tools,a)!;
    static Task Idle()=>Dispatcher.CurrentDispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle).Task;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);File.AppendAllText(Path.Combine(folder,"progress.log"),name+"\n");}
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T t)yield return t;foreach(var t2 in Find<T>(child))yield return t2;}}
    static void Capture(string n,double dpi=1)
    {tools!.UpdateLayout();var root=(FrameworkElement)tools.Content;var image=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth*dpi),(int)Math.Ceiling(root.ActualHeight*dpi),96*dpi,96*dpi,PixelFormats.Pbgra32);image.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(Path.Combine(folder,n+".png"));png.Save(output);}
    static async Task Click(string label)
    {var b=Find<Button>(tools!).Single(x=>x.IsVisible && x.Content as string==label);b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,b));for(var i=0;i<100 && Get(tools!,"busy") is true;i++)await Task.Delay(10);await Idle();}
    static void Theme(bool dark)
    {foreach(var (key,value) in new[]{("Paper",dark?"#202027":"#FFFFFF"),("Canvas",dark?"#18181F":"#F7F7FA"),("Soft",dark?"#292934":"#F3F3F7"),("Ink",dark?"#F0EEF4":"#292831"),("Muted",dark?"#C3BECB":"#666371"),("Line",dark?"#494653":"#E7E5ED"),("Accent","#E3BACA"),("AccentInk","#24232B"),("Note",dark?"#392D39":"#F5E7EC"),("Sunday",dark?"#FF9DB1":"#C93656"),("Saturday",dark?"#A2C7FF":"#396AB8")})owner!.Resources[key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));}
    static async Task Open(bool dark,bool visible=false)
    {
        tools?.Close();Theme(dark);tools=(Window)Activator.CreateInstance(typeof(Pds.Desktop.App).Assembly.GetType("Pds.Desktop.JournalToolsWindow")!,All,null,[repo,owner!,date,true,"search"],null)!;
        tools.ShowActivated=visible;tools.ShowInTaskbar=visible;tools.WindowStartupLocation=WindowStartupLocation.Manual;tools.Left=visible?90:-12000;tools.Top=visible?50:-12000;tools.Width=620;tools.Height=460;
        tools.GetType().GetProperty("Confirmation",All)!.SetValue(tools,new Func<string,bool>(_=>true));
        tools.Show();await Idle();
    }
    [STAThread]static int Main(string[] args)
    {
        folder=Path.GetFullPath(args[1]);Directory.CreateDirectory(folder);int code=1;
        var app=new Pds.Desktop.App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.BeginInvoke(async()=>{try{
            repo=new(Path.Combine(folder,"synthetic.db"));var money=MoneyEntry.Create(MoneyKind.Expense,12000,MoneyEntry.ExpenseCategories[0],"종합사용성 내역");
            repo.Save(repo.Load(date)with{Plans=[PlanEntry.Create("종합사용성 계획")with{Done=true,ActualDate=date}],Studies=[StudyEntry.Create("종합사용성 학습")],Money=[money]});
            owner=new Window{Width=1,Height=1,Left=-12000,Top=-12000,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false,ShowInTaskbar=false,Style=(Style)app.FindResource(typeof(Window))};owner.Show();await Open(false,args[0]=="interactive");
            if(args[0]=="interactive"){
                tools!.Title="PDS · 사용성 시험";tools.Width=700;tools.Height=660;
                var actions=new List<string>();tools.AddHandler(Button.ClickEvent,new RoutedEventHandler((_,e)=>{if(e.OriginalSource is Button b)actions.Add("click:"+b.Content);}));tools.PreviewKeyDown+=(_,e)=>actions.Add("key:"+Keyboard.Modifiers+"+"+e.Key);
                tools.Closed+=(_,_)=>{File.WriteAllText(Path.Combine(folder,"interactive.json"),JsonSerializer.Serialize(new{success=true,actions,budget=repo.BudgetFor(date).Budget,query=C<TextBox>("QueryInput").Text,search=C<SearchPage>("SearchResult")?.Total,actualInputInjection=true,personalDataUsed=false},new JsonSerializerOptions{WriteIndented=true}));owner.Close();app.Shutdown();};code=0;return;
            }
            if(args[0]=="placement")await PlacementAsync();else await AuditAsync(args[0]!="layout");code=0;File.WriteAllText(Path.Combine(folder,"usability.json"),JsonSerializer.Serialize(new{success=true,mode=args[0],count=checks.Count,checks,metrics,monitors=System.Windows.Forms.Screen.AllScreens.Select(s=>new{s.DeviceName,s.Primary,area=s.WorkingArea.ToString()}),dpiTests=args[0]=="placement"?"actual current-monitor bounds and source-initialized recovery":"WPF visual DPI simulation; no OS setting changed",actualMultipleMonitors=false,personalDataUsed=false},new JsonSerializerOptions{WriteIndented=true}));
        }catch(Exception ex){File.WriteAllText(Path.Combine(folder,"error.txt"),ex.ToString());if(args[0]=="interactive")Dispatcher.CurrentDispatcher.InvokeShutdown();}finally{if(args[0]!="interactive"){tools?.Close();owner?.Close();Dispatcher.CurrentDispatcher.InvokeShutdown();}}});
        Dispatcher.Run();return code;
    }
    static async Task AuditAsync(bool includeSoak=true)
    {
        foreach(var dark in new[]{false,true})
        {
            await Open(dark);C<TextBox>("QueryInput").Text="종합사용성";await Call("SearchAsync",0);tools!.UpdateLayout();
            var results=Find<ScrollViewer>(tools).Single(v=>v.IsVisible && v.Content==Get(tools,"searchRows"));
            metrics.Add(new{dark,searchViewport=results.ViewportHeight});Capture("minimum-search-"+dark);
            Check(results.ViewportHeight>=160,"collapsed-filter-keeps-at-least-160dip-results-"+dark);
            C<Expander>("SearchFilters").IsExpanded=true;await Idle();C<DatePicker>("SearchFrom").SelectedDate=new DateTime(2026,10,1);C<Expander>("SearchFilters").IsExpanded=false;await Idle();
            Check(C<DatePicker>("SearchFrom").SelectedDate==new DateTime(2026,10,1) && C<Expander>("SearchFilters").Header.ToString()!.Contains("2026.10.01"),"closed-filter-keeps-and-describes-active-date-"+dark);
            C<Expander>("SearchFilters").IsExpanded=true;await Click("필터 초기화");Check(C<DatePicker>("SearchFrom").SelectedDate is null,"clear-filter-resets-dates-"+dark);C<Expander>("SearchFilters").IsExpanded=false;
            foreach(var scale in new[]{1d,1.25,1.5,2})
            {
                VisualTreeHelper.SetRootDpi(tools,new DpiScale(scale,scale));await Idle();tools.UpdateLayout();
                var host=(ScrollViewer)C<TextBox>("QueryInput").Template.FindName("PART_ContentHost",C<TextBox>("QueryInput"));
                Check(Math.Abs(VisualTreeHelper.GetDpi(C<TextBox>("QueryInput")).DpiScaleX-scale)<.001 && host.ActualHeight>=16,"dpi-input-line-fit-"+scale+"-"+dark);
                Capture("search-"+scale+"-"+dark,scale);
                await Call("SwitchAsync","summary");await Click("전월 비교");var view=C<ScrollViewer>("summaryScroll");
                if(scale==1)Check(C<TextBlock>("status").Text.Contains("결산"),"summary-footer-describes-current-screen-"+dark);
                Check(view.VerticalOffset>0 && view.ViewportHeight>=160,"summary-shortcut-reaches-comparison-"+scale+"-"+dark);Capture("comparison-"+scale+"-"+dark,scale);
                await Click("월·예산 설정 ↑");await Idle();Check(view.VerticalOffset<2,"summary-shortcut-returns-to-editing-"+scale+"-"+dark);await Call("SwitchAsync","search");
                metrics.Add(new{dark,scale,resultsHeight=results.ViewportHeight,textHeight=host.ActualHeight});
            }
            VisualTreeHelper.SetRootDpi(tools,new DpiScale(1,1));
            C<TextBox>("QueryInput").Focus();await Idle();var focus=new List<string>();
            for(var i=0;i<35;i++){var active=Keyboard.FocusedElement as UIElement;active?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));await Idle();var next=Keyboard.FocusedElement as FrameworkElement;if(next is not null)focus.Add(next.GetType().Name);}
            Check(focus.Contains("Button") && focus.Count>=30,"tab-cycle-visits-controls-without-losing-window-"+dark);
        }
        var placement=typeof(Pds.Desktop.App).Assembly.GetType("Pds.Desktop.WindowPlacement")!.GetMethod("Fit",BindingFlags.Static|BindingFlags.NonPublic)!;
        foreach(var area in new[]{new Rect(-1920,0,1920,1040),new Rect(1920,-200,1706,920),new Rect(0,0,853,480)})
        {
            var width=Math.Min(900,area.Width);var height=Math.Min(880,area.Height);
            var p=((double Left,double Top))placement.Invoke(null,new object[]{-30000d,30000d,width,height,area.Left,area.Top,area.Width,area.Height})!;
            Check(p.Left>=area.Left && p.Top>=area.Top && p.Left+width<=area.Right && p.Top+height<=area.Bottom,"synthetic-monitor-restores-entire-window-"+area);
        }
        if(includeSoak)await SoakAsync();
    }
    static async Task SoakAsync()
    {
        await Open(false);C<TextBox>("QueryInput").Text="종합사용성";await Call("SearchAsync",0);await Call("SwitchAsync","summary");C<TextBox>("BudgetInput").Text="50000";await Call("SaveBudgetAsync");
        var watch=Stopwatch.StartNew();using var process=Process.GetCurrentProcess();process.Refresh();var cpu=process.TotalProcessorTime;var before=process.PrivateMemorySize64;var samples=new List<object>();
        for(var i=0;i<100;i++){await Call("SwitchAsync","search");await Call("SearchAsync",0);await Call("SwitchAsync","trash");await Call("SwitchAsync","summary");if(i%10==0)await Idle();}
        Check(repo.BudgetFor(date).Budget==50000 && repo.Load(date).Money.Count==1,"100-management-roundtrips-preserve-budget-and-records");
        for(var i=0;i<60;i++)
        {await Task.Delay(5000);process.Refresh();if(i%6==0){samples.Add(new{seconds=watch.Elapsed.TotalSeconds,privateMiB=process.PrivateMemorySize64/1048576d,workingMiB=process.WorkingSet64/1048576d,handles=process.HandleCount});File.WriteAllText(Path.Combine(folder,"soak-progress.json"),JsonSerializer.Serialize(new{seconds=watch.Elapsed.TotalSeconds,samples}));}if(i==20)tools!.Hide();if(i==40)tools!.Show();}
        process.Refresh();Check(watch.Elapsed.TotalSeconds>=300 && repo.Load(date).Plans.Count==1 && repo.BudgetFor(date).Budget==50000,"five-minute-shown-hidden-session-keeps-records");
        metrics.Add(new{sessionSeconds=watch.Elapsed.TotalSeconds,transitions=100,privateBeforeMiB=before/1048576d,privateAfterMiB=process.PrivateMemorySize64/1048576d,cpuSeconds=(process.TotalProcessorTime-cpu).TotalSeconds,samples});
    }
    static async Task PlacementAsync()
    {
        var environment=typeof(Pds.Desktop.App).Assembly.GetType("Pds.Desktop.WindowEnvironment")!;
        var flags=BindingFlags.Static|BindingFlags.NonPublic;
        foreach(var location in new[]{new Point(-30000,-30000),new Point(30000,30000)})
        {
            var recovery=new Window{Width=900,Height=880,Left=location.X,Top=location.Y,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false,ShowInTaskbar=false,Opacity=0};
            environment.GetMethod("Watch",flags)!.Invoke(null,new object?[]{recovery,null});recovery.Show();await Idle();await Task.Delay(100);recovery.UpdateLayout();
            var area=(Rect)environment.GetMethod("WorkArea",flags)!.Invoke(null,[recovery])!;
            Check(recovery.Left>=area.Left-1 && recovery.Top>=area.Top-1 && recovery.Left+recovery.ActualWidth<=area.Right+1 && recovery.Top+recovery.ActualHeight<=area.Bottom+1,"source-initialized-recovers-offscreen-window-"+location);
            Check(recovery.ActualWidth<=area.Width+1 && recovery.ActualHeight<=area.Height+1,"recovered-window-fits-current-workarea-"+location);
            recovery.Close();await Idle();Check(!recovery.IsVisible,"recovery-hook-detaches-on-close-"+location);
        }
    }
}
