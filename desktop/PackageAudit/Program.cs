using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pds.Core;

internal static class Program
{
    private static readonly List<string> Checks=[];
    private static string evidence="";
    private static Window? tools,editor,owner;
    private const BindingFlags All=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    [STAThread]
    private static int Main(string[] args)
    {
        evidence=Path.GetFullPath(args[0]);Directory.CreateDirectory(evidence);var result=1;
        var app=new Pds.Desktop.App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.BeginInvoke(async()=>
        {
            try { await AuditAsync(Path.GetFullPath(args[1]));result=0;File.WriteAllText(Path.Combine(evidence,"package-ui-audit.json"),JsonSerializer.Serialize(new{success=true,count=Checks.Count,checks=Checks,personalDataUsed=false,physicalMouseUsed=false,externalViewerLaunched=false},new JsonSerializerOptions{WriteIndented=true})); }
            catch(Exception ex){File.WriteAllText(Path.Combine(evidence,"package-ui-audit-error.txt"),ex.ToString());}
            finally
            {
                if(tools is not null){Set(tools,"Confirmation",new Func<string,bool>(_=>true));tools.Close();}
                if(editor is not null){Set(editor,"DiscardDecision",new Func<bool>(()=>true));editor.Close();}
                owner?.Close();Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        Dispatcher.Run();return result;
    }
    private static void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);Checks.Add(name);File.AppendAllText(Path.Combine(evidence,"audit-progress.log"),name+Environment.NewLine);}
    private static object? Get(object target,string name)=>target.GetType().GetProperty(name,All)?.GetValue(target)??target.GetType().GetField(name,All)?.GetValue(target);
    private static void Set(object target,string name,object value)=>target.GetType().GetProperty(name,All)!.SetValue(target,value);
    private static T Control<T>(object target,string name)=>(T)Get(target,name)!;
    private static async Task Call(object target,string name,params object?[] args)
    {
        try{await (Task)target.GetType().GetMethod(name,All)!.Invoke(target,args)!;}
        catch(TargetInvocationException ex){throw ex.InnerException??ex;}
    }
    private static async Task Idle()=>await Dispatcher.CurrentDispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Children<T>(DependencyObject root)where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T target)yield return target;foreach(var descendant in Children<T>(child))yield return descendant;}
    }
    private static async Task Wait(Window window,bool requireShown=false)
    {
        for(var i=0;i<150;i++){await Idle();if(Get(window,"busy") is not true)return;await Task.Delay(30);}throw new TimeoutException("Button operation did not finish");
    }
    private static async Task Click(Window window,string label,DependencyObject? scope=null)
    {
        window.UpdateLayout();await Idle();
        var candidates=Children<Button>(scope??window).Where(b=>b.Content as string==label && b.IsVisible).ToArray();
        if(candidates.Length!=1){Snapshot(window,"missing-button.png");File.WriteAllText(Path.Combine(evidence,"button-diagnostic.txt"),$"window={window.IsVisible}/{window.IsLoaded}/{window.WindowState}\n"+string.Join("\n",Children<Button>(window).Select(b=>$"{b.Content}:visible={b.IsVisible}/enabled={b.IsEnabled}")));throw new InvalidOperationException("Missing or duplicate button: "+label);}
        var button=candidates[0];button.BringIntoView();await Idle();
        Check(button.IsEnabled && button.IsVisible,"button-available: "+label);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,button));await Wait(window);
    }
    private static DependencyObject CardFor(Window window,string title)
        =>Children<Border>(window).First(b=>b.IsVisible && b.Child is StackPanel && Children<TextBlock>(b).Any(t=>t.Text==title));
    private static void Snapshot(Window window,string filename)
    {
        window.UpdateLayout();var content=(FrameworkElement)window.Content;var image=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(content);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(Path.Combine(evidence,filename));png.Save(output);
    }
    private static Window NewWindow(Type type,params object[] args)
    {
        var result=(Window)Activator.CreateInstance(type,All,null,args,null)!;result.ShowActivated=false;result.ShowInTaskbar=false;result.WindowStartupLocation=WindowStartupLocation.Manual;result.Left=-12000;result.Top=-12000;return result;
    }
    private static void Theme(bool dark)
    {
        var colors=dark?new[]{"#22222B","#F5F4FA","#C0BDCB","#3D3B49","#2A2A35","#FFACC0","#AAC7FF","#18181F"}:new[]{"#FFFFFF","#292831","#666371","#E7E5ED","#F3F3F7","#BE2F50","#315FA5","#F7F7FA"};
        var keys=new[]{"Paper","Ink","Muted","Line","Soft","Sunday","Saturday","Canvas"};
        for(var i=0;i<keys.Length;i++)owner!.Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        owner!.Resources["Note"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#2C252C":"#FBF2F6"));
    }
    private static async Task AuditAsync(string bundle)
    {
        var assembly=typeof(Pds.Desktop.App).Assembly;
        foreach(var (name,loaded) in new[]{("Pds.Desktop.dll",assembly.Location),("Pds.Core.dll",typeof(PlanRepository).Assembly.Location)})
            Check(SHA256.HashData(File.ReadAllBytes(loaded)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(bundle,"PDS",name)))),"uses-exact-delivered-assembly: "+name);
        owner=new Window{Style=(Style)Application.Current.FindResource(typeof(Window)),ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=-12000,Width=1,Height=1};owner.Show();Theme(false);
        var type=assembly.GetType("Pds.Desktop.JournalToolsWindow",true)!;var editType=assembly.GetType("Pds.Desktop.FixedCostEditWindow",true)!;
        var repo=new PlanRepository(Path.Combine(evidence,"data","records.db"));var date=new DateOnly(2026,5,10);var month=new DateOnly(2026,5,1);
        var source=Path.Combine(evidence,"심화검증 강의파일.txt");File.WriteAllText(source,"원본 보존 및 첨부 복구 바이트");var file=repo.Attachments.Import(source);
        var study=StudyEntry.Create("심화검증 학습","핵심본문 확인","직접 작성 요약")with{Attachments=[file]};var plan=PlanEntry.Create("심화검증 계획")with{Done=true,ActualDate=date};
        var bill=MoneyEntry.Create(MoneyKind.Expense,12000,"통신")with{Title="심화검증 통신비",IsFixed=true,RepeatMonthly=true};var saving=MoneyEntry.Create(MoneyKind.Expense,100000,"적금·저축");
        repo.Save(repo.Load(date)with{Plans=[plan],Studies=[study],Money=[bill,saving],Workouts=[WorkoutEntry.Create("walking",600,null)],Review=new("잘한 일","","다음 행동")});
        tools=NewWindow(type,repo,owner,date,true,"search");tools.Closing+=(_,_)=>File.AppendAllText(Path.Combine(evidence,"closing-diagnostic.txt"),Environment.StackTrace+"\n");Set(tools,"Confirmation",new Func<string,bool>(_=>false));tools.Show();
        File.WriteAllText(Path.Combine(evidence,"show-diagnostic.txt"),$"owner={owner.Visibility}/{owner.IsVisible}/{owner.IsLoaded};tools={tools.Visibility}/{tools.IsVisible}/{tools.IsLoaded};handle={new System.Windows.Interop.WindowInteropHelper(tools).Handle};shutdown={Application.Current.Dispatcher.HasShutdownStarted}");
        await Wait(tools,true);
        Control<TextBox>(tools,"QueryInput").Text="심화검증";await Click(tools,"검색");Check(Control<SearchPage>(tools,"SearchResult").Total==3,"search-button-finds-three-record-types");
        Control<ComboBox>(tools,"SearchKind").SelectedIndex=2;await Click(tools,"검색");Check(Control<SearchPage>(tools,"SearchResult").Items.Single().Id==study.Id,"search-kind-filter-button-path");
        Control<TextBox>(tools,"QueryInput").Text="핵심본문";await Click(tools,"검색");Check(Control<SearchPage>(tools,"SearchResult").Total==1,"search-matches-learning-body");
        Control<TextBox>(tools,"QueryInput").Text="심화검증 강의파일";Control<DatePicker>(tools,"SearchFrom").SelectedDate=date.AddDays(1).ToDateTime(TimeOnly.MinValue);await Click(tools,"검색");Check(Control<SearchPage>(tools,"SearchResult").Total==0,"search-date-filter-excludes-older-records");
        Control<DatePicker>(tools,"SearchFrom").SelectedDate=date.ToDateTime(TimeOnly.MinValue);Control<DatePicker>(tools,"SearchTo").SelectedDate=date.ToDateTime(TimeOnly.MinValue);await Click(tools,"검색");Check(Control<SearchPage>(tools,"SearchResult").Total==1,"search-inclusive-dates-and-filename-match");
        await Click(tools,"월간 결산·예산");Control<TextBox>(tools,"BudgetInput").Text="50,000";await Click(tools,"예산 저장");Check(repo.BudgetFor(date) is{Budget:50000,Consumption:12000,Savings:100000,Remaining:38000},"checklist-budget-50000-minus-12000-leaves-38000-and-excludes-savings");
        Control<TextBox>(tools,"BudgetInput").Text="60,000";await Click(tools,"통합 검색");await Click(tools,"월간 결산·예산");Check(Control<TextBox>(tools,"BudgetInput").Text=="60,000" && repo.BudgetFor(date).Budget==50000,"tab-buttons-preserve-unsaved-budget-without-writing");
        Control<TextBox>(tools,"MonthInput").Text="2026-06";await Click(tools,"선택한 달 조회");Check(Control<MonthlyDigest>(tools,"Digest").Month==month && Control<TextBox>(tools,"BudgetInput").Text=="60,000","cancel-month-change-preserves-budget-draft-and-selected-report");
        tools.Close();Check(tools.IsVisible,"cancel-window-close-protects-budget-draft");Control<TextBox>(tools,"BudgetInput").Text="50,000";
        await Click(tools,"예산 저장");Check(repo.BudgetFor(new(2026,6,1)).Budget is null && repo.BudgetFor(date).Budget==50000,"editing-month-text-cannot-silently-save-budget-into-wrong-month");Control<TextBox>(tools,"MonthInput").Text="2026-05";
        await Click(tools,"예산 해제");Check(repo.BudgetFor(date).Budget==50000,"cancel-budget-clear-retains-existing-budget");Set(tools,"Confirmation",new Func<string,bool>(_=>true));await Click(tools,"예산 해제");Check(repo.BudgetFor(date).Budget is null && repo.Load(date).Money.Count==2,"clear-budget-button-keeps-money-records");
        Control<TextBox>(tools,"BudgetInput").Text="50,000";await Click(tools,"예산 저장");await Click(tools,"다음 달 ›");Check(Control<MonthlyDigest>(tools,"Digest").Month==new DateOnly(2026,6,1) && repo.BudgetFor(new(2026,6,1)).Budget is null,"next-month-does-not-inherit-budget");await Click(tools,"‹ 이전 달");Check(Control<MonthlyDigest>(tools,"Digest").Budget.Remaining==38000,"previous-month-button-restores-selected-budget-report");
        Control<TextBox>(tools,"MonthInput").Text="2026-5";await Click(tools,"선택한 달 조회");
        Check(Control<TextBox>(tools,"MonthInput").Text=="2026-05" && Control<MonthlyDigest>(tools,"Digest").Budget.Remaining==38000,"un-padded-month-through-query-button-normalizes-and-preserves-budget");
        Control<TextBox>(tools,"MonthInput").Text="2026-13";await Click(tools,"선택한 달 조회");
        Check(Control<TextBox>(tools,"MonthInput").Text=="2026-13" && Control<MonthlyDigest>(tools,"Digest").Month==month,"invalid-month-button-keeps-input-and-last-valid-report");
        Control<TextBox>(tools,"MonthInput").Text="2026-05";await Click(tools,"선택한 달 조회");
        tools.Width=780;tools.Height=760;await Idle();
        var summaryViewport=Children<ScrollViewer>(tools).Single(s=>s.Content==Get(tools,"summaryView"));
        var exerciseHeading=Children<TextBlock>(tools).Single(t=>t.Text=="운동" && t.FontWeight==FontWeights.SemiBold && t.Parent is StackPanel);
        summaryViewport.ScrollToVerticalOffset(summaryViewport.VerticalOffset+exerciseHeading.TranslatePoint(new Point(),summaryViewport).Y-12);await Idle();Snapshot(tools,"feedback-monthly-exercise-group.png");
        var moneyHeading=Children<TextBlock>(tools).Single(t=>t.Text=="금전" && t.FontWeight==FontWeights.SemiBold && t.Parent is StackPanel);
        summaryViewport.ScrollToVerticalOffset(summaryViewport.VerticalOffset+moneyHeading.TranslatePoint(new Point(),summaryViewport).Y-12);await Idle();Snapshot(tools,"feedback-monthly-money-group.png");
        Check(exerciseHeading.Parent!=moneyHeading.Parent && summaryViewport.ScrollableHeight>0,"exercise-and-money-are-separate-accessible-groups");
        await Click(tools,"연간 활동");Control<ComboBox>(tools,"AnnualMetricChoice").SelectedIndex=3;await Idle();
        var annualTable=Children<Expander>(tools).Single(e=>Equals(e.Header,"월별 수치와 입력 상태 보기"));annualTable.IsExpanded=true;await Idle();
        Check(Children<TextBlock>(annualTable).Count(t=>t.Text.Contains("월 ·"))==12,"annual-accessible-table-has-all-twelve-month-values");
        annualTable.IsExpanded=false;
        repo.Save(repo.Load(date)with{Studies=[],Plans=[]});await Click(tools,"휴지통");Set(tools,"Confirmation",new Func<string,bool>(_=>false));await Click(tools,"복구",CardFor(tools,study.Title));Check(repo.Load(date).Studies.Count==0,"cancel-study-recovery-keeps-trash-entry");Set(tools,"Confirmation",new Func<string,bool>(_=>true));await Click(tools,"복구",CardFor(tools,study.Title));Check(repo.Load(date).Studies.Single()==study && File.ReadAllBytes(repo.Attachments.Require(file,true)).SequenceEqual(File.ReadAllBytes(source)),"recovery-button-restores-study-and-exact-file-bytes");await Click(tools,"복구",CardFor(tools,plan.Title));Check(repo.Load(date).Plans.Single()==plan,"recovery-button-restores-plan-completion");
        var second=repo.Attachments.Import(source);var disposable=StudyEntry.Create("영구 삭제 버튼 검증")with{Attachments=[second]};var next=date.AddDays(1);repo.Save(repo.Load(next)with{Studies=[disposable]});repo.Save(repo.Load(next)with{Studies=[]});await Call(tools,"LoadTrashAsync");
        Set(tools,"Confirmation",new Func<string,bool>(_=>false));await Click(tools,"영구 삭제",CardFor(tools,disposable.Title));Check(File.Exists(repo.Attachments.PathFor(second)) && repo.DeletedRecords().Total==1,"cancel-permanent-delete-keeps-file-and-record");Set(tools,"Confirmation",new Func<string,bool>(_=>true));await Click(tools,"영구 삭제",CardFor(tools,disposable.Title));Check(!File.Exists(repo.Attachments.PathFor(second)) && File.Exists(source) && File.Exists(repo.Attachments.PathFor(file)),"permanent-delete-button-removes-only-unreferenced-copy");
        var rule=repo.LoadFixedCosts().Single();editor=NewWindow(editType,repo,rule,owner,new DateOnly(2026,10,18),true);Set(editor,"DiscardDecision",new Func<bool>(()=>true));editor.Show();await Idle();Control<TextBox>(editor,"ApplyMonth").Text="2026-10";Control<TextBox>(editor,"AmountInput").Text="15000";await Click(editor,"변경 저장");Check(editor.IsVisible && repo.LoadFixedCosts().Single().Changes.Count==0 && Control<TextBox>(editor,"AmountInput").Text=="15000","invalid-apply-month-through-save-button-keeps-input-and-stored-rule");
        Control<TextBox>(editor,"ApplyMonth").Text="2026-11";Control<CheckBox>(editor,"EndEnabled").IsChecked=true;Control<TextBox>(editor,"EndMonth").Text="2026-12";await Click(editor,"변경 저장");Check(!editor.IsVisible && repo.Load(date).Money.Single(e=>e.Id==bill.Id).Won==12000,"future-change-save-button-closes-only-on-success-and-preserves-old-money");editor=null;
        Check(repo.ApplyFixedCosts(new(2027,1,31)).Added==7 && repo.Load(new(2026,11,10)).Money.Single().Won==15000 && repo.Load(new(2026,12,10)).Money.Single().Won==15000 && repo.Load(new(2027,1,10)).Money.Count==0,"end-month-is-inclusive-and-no-later-payments-are-generated");
        for(var pass=0;pass<2;pass++)
        {
            tools.Close();Theme(pass==1);tools=NewWindow(type,repo,owner,date,true,"summary");Set(tools,"Confirmation",new Func<string,bool>(_=>true));tools.Width=620;tools.Height=460;tools.Show();await Wait(tools);Check(Control<MonthlyDigest>(tools,"Digest").Studies==1,"minimum-window-monthly-digest-loads-theme-"+pass);
            var scroll=Children<ScrollViewer>(tools).Single(s=>s.Content==Get(tools,"summaryView"));scroll.ScrollToBottom();await Idle();
            var last=Children<TextBlock>(scroll).Single(t=>t.Text.StartsWith("연간 활동 안내:"));var bottom=last.TranslatePoint(new Point(0,last.ActualHeight),scroll).Y;Check(bottom<=scroll.ActualHeight+1 && bottom>0,"minimum-window-final-annual-guidance-is-reachable-theme-"+pass);Snapshot(tools,"audit-digest-minimum-"+(pass==0?"light":"dark")+".png");
            scroll.ScrollToTop();await Idle();Control<TextBox>(tools,"BudgetInput").Text="10,000";await Click(tools,"예산 저장");Check(Control<MonthlyDigest>(tools,"Digest").Budget.Remaining==-2000,"budget-overrun-through-button-keeps-negative-remainder-theme-"+pass);
            await Click(tools,"통합 검색");Control<TextBox>(tools,"QueryInput").Text="핵심본문";await Click(tools,"검색");Check(Control<SearchPage>(tools,"SearchResult").Total==1,"minimum-window-search-still-operates-theme-"+pass);Snapshot(tools,"audit-search-minimum-"+(pass==0?"light":"dark")+".png");
        }
        var archive=Path.Combine(evidence,"audit-backup.zip");BackupArchive.Write(archive,repo.CreateBackup(),repo.Attachments);var restored=new PlanRepository(Path.Combine(evidence,"restored","records.db"));using(var prepared=BackupArchive.Read(archive)){prepared.Install(restored);restored.RestoreBackup(prepared.Backup);}
        Check(restored.Load(date).Studies.Single()==study && restored.BudgetFor(date).Budget==10000 && restored.LoadFixedCosts().Single().Changes.Count==1,"independent-backup-roundtrip-preserves-budget-and-future-rule-change");
        await Click(tools,"해당 날짜로 이동");Check(!tools.IsVisible && Control<SearchHit>(tools,"NavigateTo").Date==date,"search-result-button-really-closes-dialog-and-returns-target-date");tools=null;
    }
}
