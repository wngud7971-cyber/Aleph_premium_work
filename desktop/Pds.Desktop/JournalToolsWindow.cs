using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pds.Core;

namespace Pds.Desktop;

internal sealed partial class JournalToolsView : UserControl
{
    private readonly Window owner;
    private readonly string initialTab;
    private readonly PlanRepository repository;
    private readonly bool verification;
    private readonly Grid content=new();
    private readonly StackPanel searchRows=new(),trashRows=new(),summaryRows=new();
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(22,2,22,6)};
    private readonly TextBlock searchPageLabel=new(),trashPageLabel=new();
    private readonly StackPanel summaryView=new();
    private readonly ScrollViewer summaryScroll;
    private readonly Grid searchView=new(),trashView=new();
    private readonly Grid summaryPane=new();
    internal Expander SearchFilters {get;}=new(){IsExpanded=false,Margin=new Thickness(0,0,0,6)};
    private FrameworkElement? budgetSection,activitySection,compareSection,annualSection;
    internal TextBox QueryInput { get; }=new(){MaxLength=200,MinWidth=180};
    internal ComboBox SearchKind { get; }=new(){MinWidth=100};
    internal DatePicker SearchFrom { get; }=new(){MinWidth=130};
    internal DatePicker SearchTo { get; }=new(){MinWidth=130};
    internal TextBox MonthInput { get; }=new(){Width=110,MaxLength=7};
    internal TextBox BudgetInput { get; }=new(){MaxLength=18};
    internal SearchPage? SearchResult { get; private set; }
    internal DeletedPage? TrashResult { get; private set; }
    internal MonthlyDigest? Digest { get; private set; }
    internal SearchHit? NavigateTo { get; private set; }
    internal Func<string,bool>? Confirmation { get; set; }
    private bool busy,loadingBudget,discardClosing;
    internal bool IsBusy=>busy || tabLoading;
    internal event Action? CloseRequested;
    internal ScrollViewer SummaryScroll=>summaryScroll;
    internal StackPanel SummaryView=>summaryView;
    internal StackPanel SearchRows=>searchRows;
    internal TextBlock StatusLabel=>status;
    private Window DialogOwner=>Window.GetWindow(this)??owner;
    internal Task InitializeAsync()=>RunAsync(()=>SwitchAsync(initialTab));
    internal bool TryLeave()
    {
        if(discardClosing)return true;
        if(busy || tabLoading)return false;
        if(BudgetDirty && !Ask("저장하지 않은 예산 입력을 버리고 화면을 이동할까요?"))return false;
        if(BudgetDirty){loadingBudget=true;BudgetInput.Text=savedBudgetText;loadingBudget=false;}
        CancelTabTurn();return true;
    }
    internal void RefreshTheme()
    {
        var replacements=new Dictionary<Brush,Brush>(ReferenceEqualityComparer.Instance);
        foreach(var key in new[]{"Paper","Canvas","Soft","Ink","Muted","Line","Accent","AccentInk","Note","Sunday","Saturday"})
        {
            if(Resources[key] is Brush previous && owner.FindResource(key) is SolidColorBrush source)
            {var next=source.CloneCurrentValue();replacements[previous]=next;Resources[key]=next;}
        }
        var visited=new HashSet<DependencyObject>();UpdateBrushes(this);
        void UpdateBrushes(DependencyObject node)
        {
            if(!visited.Add(node))return;
            // Rendering can freeze brushes. Replace references instead of mutating a frozen resource.
            var values=node.GetLocalValueEnumerator();var changed=new List<(DependencyProperty Property,Brush Brush)>();
            while(values.MoveNext())if(values.Current.Value is Brush old && replacements.TryGetValue(old,out var next))changed.Add((values.Current.Property,next));
            foreach(var item in changed)node.SetValue(item.Property,item.Brush);
            if(node is AnnualLineChart chart)chart.SetPalette(B("Accent"),B("Paper"),B("Line"));
            if(node is Visual or System.Windows.Media.Media3D.Visual3D)
                for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)UpdateBrushes(VisualTreeHelper.GetChild(node,i));
            foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())UpdateBrushes(child);
        }
    }
    private string savedBudgetText="";
    private int trashPage;
    private readonly Dictionary<string,Button> tabs=[];
    internal bool BudgetDirty=>!loadingBudget && BudgetInput.Text!=savedBudgetText;
    private Brush B(string key)=>(Brush)FindResource(key);
    private TextBlock T(string text,bool muted=false,double size=12)=>new(){Text=text,Foreground=B(muted?"Muted":"Ink"),FontSize=UiTypography.Grow(size),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,2)};
    private Button ActionButton(string text,Func<Task> action)
    { var button=new Button{Content=text,Height=38,Padding=new Thickness(12,4,12,4),Margin=new Thickness(4,4,0,8),VerticalAlignment=VerticalAlignment.Center};button.Click+=async(_,_)=>await RunAsync(action);return button; }
    private bool Ask(string text)=>Confirmation?.Invoke(text)??System.Windows.MessageBox.Show(DialogOwner,text,"확인",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    private Border Card(UIElement child)=>new(){Child=child,Background=B("Soft"),CornerRadius=new CornerRadius(12),Padding=new Thickness(14),Margin=new Thickness(0,4,0,8)};
    internal JournalToolsView(PlanRepository repository,Window owner,DateOnly month,bool verification=false,string start="search")
    {
        this.owner=owner;initialTab=start;this.repository=repository;this.verification=verification;HorizontalContentAlignment=HorizontalAlignment.Stretch;VerticalContentAlignment=VerticalAlignment.Stretch;
        foreach(var key in new[]{"Paper","Canvas","Soft","Ink","Muted","Line","Accent","AccentInk","Note","Sunday","Saturday"})Resources[key]=((SolidColorBrush)owner.FindResource(key)).CloneCurrentValue();
        System.Windows.Automation.AutomationProperties.SetName(QueryInput,"검색어 제목 내용 내역 첨부 파일명");
        System.Windows.Automation.AutomationProperties.SetName(SearchKind,"검색 기록 종류");System.Windows.Automation.AutomationProperties.SetName(SearchFrom,"검색 시작일 선택 사항");System.Windows.Automation.AutomationProperties.SetName(SearchTo,"검색 종료일 선택 사항");
        System.Windows.Automation.AutomationProperties.SetName(MonthInput,"결산과 예산의 선택 월. 예: 2026-9 또는 2026-09");System.Windows.Automation.AutomationProperties.SetName(BudgetInput,"선택한 월의 소비 예산 원");
        var root=new Grid{Background=B("Paper")};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        foreach(var control in new Control[]{QueryInput,SearchKind,SearchFrom,SearchTo,MonthInput,BudgetInput})UiLayout.Compact(control);
        QueryInput.Margin=new Thickness(0,3,0,2);
        MonthInput.TextAlignment=TextAlignment.Center;MonthInput.HorizontalAlignment=HorizontalAlignment.Left;BudgetInput.TextAlignment=TextAlignment.Right;
        var heading=new Grid{Margin=new Thickness(22,4,22,4)};heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});heading.ColumnDefinitions.Add(new());var title=T("기록 관리",size:20);title.Margin=new Thickness(0,0,18,0);title.VerticalAlignment=VerticalAlignment.Center;heading.Children.Add(title);
        var nav=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};foreach(var (key,label) in new[]{("search","통합 검색"),("trash","휴지통"),("summary","월간 결산·예산")}) { var button=new Button{Content=label,Padding=new Thickness(12,4,12,4)};button.Click+=async(_,_)=>await NavigateTabAsync(key);button.Height=34;button.Margin=new Thickness(0,2,4,4);tabs[key]=button;nav.Children.Add(button); }Grid.SetColumn(nav,1);heading.Children.Add(nav);root.Children.Add(heading);
        content.Margin=new Thickness(22,0,22,0);Grid.SetRow(content,1);root.Children.Add(content);Grid.SetRow(status,2);root.Children.Add(status);Content=root;
        BuildSearch();BuildTrash();
        summaryView.Children.Add(T("선택 월 · 예: 2026-9 또는 2026-09",true));
        summaryView.Children.Add(ControlRow(MonthInput,ActionButton("선택한 달 조회",()=>LoadSummaryAsync(ParseMonth(MonthInput.Text))),ActionButton("‹ 이전 달",()=>LoadSummaryAsync((Digest?.Month??month).AddMonths(-1))),ActionButton("다음 달 ›",()=>LoadSummaryAsync((Digest?.Month??month).AddMonths(1)))));
        summaryView.Children.Add(T("월 소비 예산 · 원",true));summaryView.Children.Add(ControlRow(BudgetInput,ActionButton("예산 저장",SaveBudgetAsync),ActionButton("예산 해제",ClearBudgetAsync)));
        summaryView.Children.Add(T("예산은 월별로 따로 저장하며 적금·저축을 제외한 소비액과 비교해요.",true));summaryView.Children.Add(summaryRows);
        summaryScroll=new ScrollViewer{Content=summaryView,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Focusable=false};
        summaryPane.RowDefinitions.Add(new(){Height=GridLength.Auto});summaryPane.RowDefinitions.Add(new());
        var jumps=new WrapPanel();foreach(var (label,target) in new(string,Func<FrameworkElement?>)[]{("월·예산 설정 ↑",()=>MonthInput),("예산·소비",()=>budgetSection),("기록 요약",()=>activitySection),("전월 비교",()=>compareSection),("연간 활동",()=>annualSection)})
        {var jump=ActionButton(label,()=>{var section=target();if(section is not null){section.BringIntoView();section.Focus();if(section==MonthInput)summaryScroll.ScrollToTop();}return Task.CompletedTask;});jump.Padding=new Thickness(8,3,8,3);jumps.Children.Add(jump);}summaryPane.Children.Add(jumps);Grid.SetRow(summaryScroll,1);summaryPane.Children.Add(summaryScroll);
        content.Children.Add(searchView);content.Children.Add(trashView);content.Children.Add(summaryPane);status.MinHeight=44;ConfigureTabMotion();
        KeyboardNavigation.SetTabNavigation(root,KeyboardNavigationMode.Continue);
        root.AddHandler(Keyboard.GotKeyboardFocusEvent,new KeyboardFocusChangedEventHandler((_,e)=>{if(e.NewFocus is FrameworkElement element)element.BringIntoView();}));
        root.PreviewKeyDown+=async(_,e)=>await HandleShortcutAsync(e);
        MonthInput.Text=month.ToString("yyyy-MM",CultureInfo.InvariantCulture);
    }
    internal async Task HandleShortcutAsync(KeyEventArgs e)
    {
        if(e.Handled || SearchFrom.IsDropDownOpen || SearchTo.IsDropDownOpen || SearchKind.IsDropDownOpen || AnnualMetricChoice.IsDropDownOpen)return;
        if(e.Key==Key.Escape){e.Handled=true;CloseRequested?.Invoke();return;}
        if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.F){e.Handled=true;await NavigateTabAsync("search");QueryInput.Focus();QueryInput.SelectAll();}
        else if(Keyboard.Modifiers==ModifierKeys.Control && e.Key==Key.S && summaryPane.IsVisible){e.Handled=true;await RunAsync(SaveBudgetAsync);}
    }
    private async Task RunAsync(Func<Task> action)
    {
        if(busy || tabLoading || TabTurnActive)return;busy=true;content.IsHitTestVisible=false;
        try{await action();}catch(Exception ex){status.Text=ex.Message;}finally{busy=false;content.IsHitTestVisible=true;}
    }
    internal async Task SwitchAsync(string tab)
    {
        if(tab is not ("search" or "trash" or "summary"))throw new ArgumentException("기록 관리 화면을 확인해 주세요.");
        var serial=++tabGeneration;var turn=PrepareTabTurn(tab);currentTab=tab;content.IsHitTestVisible=false;
        try
        {
            searchView.Visibility=tab=="search"?Visibility.Visible:Visibility.Collapsed;trashView.Visibility=tab=="trash"?Visibility.Visible:Visibility.Collapsed;summaryPane.Visibility=tab=="summary"?Visibility.Visible:Visibility.Collapsed;
            foreach(var pair in tabs)pair.Value.Background=B(pair.Key==tab?"Note":"Paper");
            tabLoading=true;
            if(tab=="trash"){await LoadTrashAsync();status.Text=$"휴지통 · {TrashResult?.Total??0}건 · 복구하거나 확인 후 영구 삭제할 수 있어요.";}
            else if(tab=="summary"){if(Digest is null)await LoadSummaryAsync(ParseMonth(MonthInput.Text));else status.Text=$"{Digest.Month:yyyy년 M월} 결산 · 저장된 기록 기준. 위 바로가기로 구역을 이동해요.";}
            else status.Text=SearchResult is null?"검색 버튼이나 Enter를 누르면 저장된 기록을 찾아요. 첨부는 파일명만 검색해요.":$"검색 결과 {SearchResult.Total}건 · 필터를 접어도 선택한 조건은 유지돼요.";
            tabLoading=false;if(serial!=tabGeneration)return;
            await PlayTabTurnAsync(turn);
        }
        finally
        {
            if(serial==tabGeneration){tabLoading=false;content.IsHitTestVisible=true;if(tabTurn==turn)CancelTabTurn();}
        }
    }

    private static Grid ControlRow(FrameworkElement input,params Button[] buttons)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.Children.Add(input);
        foreach(var button in buttons){grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});Grid.SetColumn(button,grid.ColumnDefinitions.Count-1);grid.Children.Add(button);}return grid;
    }
    private Grid Pair(string label,string value,bool muted=false)=>UiLayout.Values(T(label,muted),T(value,muted));
    private static void PageLabel(TextBlock label){UiLayout.Numeric(label);label.Margin=new Thickness(16,4,12,8);}
    private void BuildSearch()
    {
        searchView.RowDefinitions.Add(new(){Height=GridLength.Auto});searchView.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});searchView.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var controls=new StackPanel();controls.Children.Add(T("검색어 · 제목/내용/내역/첨부 파일명",true));var query=new Grid();query.ColumnDefinitions.Add(new());query.ColumnDefinitions.Add(new(){Width=GridLength.Auto});query.Children.Add(QueryInput);
        var search=ActionButton("검색",()=>SearchAsync());search.Margin=new Thickness(4,4,0,2);Grid.SetColumn(search,1);query.Children.Add(search);controls.Children.Add(query);QueryInput.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await RunAsync(()=>SearchAsync());}};
        SearchKind.ItemsSource=new[]{"전체","계획","학습","가계부","운동","회고"};SearchKind.SelectedIndex=0;
        var filters=new Grid();
        foreach(var (control,label) in new (Control,string)[]{(SearchKind,"기록 종류"),(SearchFrom,"시작일 · 선택"),(SearchTo,"종료일 · 선택")})
        {var index=filters.ColumnDefinitions.Count;filters.ColumnDefinitions.Add(new(){Width=new GridLength(index==0?1:1.5,GridUnitType.Star)});var field=new StackPanel{Margin=new Thickness(0,0,index<2?12:0,0)};field.Children.Add(T(label,true));field.Children.Add(control);Grid.SetColumn(field,index);filters.Children.Add(field);}
        var filterBody=new StackPanel();filterBody.Children.Add(filters);filterBody.Children.Add(ActionButton("필터 초기화",()=>{SearchKind.SelectedIndex=0;SearchFrom.SelectedDate=SearchTo.SelectedDate=null;return Task.CompletedTask;}));SearchFilters.Content=filterBody;
        void DescribeFilters()=>SearchFilters.Header=$"필터 · {SearchKind.SelectedItem} · {(SearchFrom.SelectedDate is null && SearchTo.SelectedDate is null?"기간 미지정":$"{SearchFrom.SelectedDate:yyyy.MM.dd} ~ {SearchTo.SelectedDate:yyyy.MM.dd}")}";
        SearchKind.SelectionChanged+=(_,_)=>DescribeFilters();SearchFrom.SelectedDateChanged+=(_,_)=>DescribeFilters();SearchTo.SelectedDateChanged+=(_,_)=>DescribeFilters();DescribeFilters();controls.Children.Add(SearchFilters);searchView.Children.Add(controls);
        var scroll=new ScrollViewer{Content=searchRows,Padding=new Thickness(0,0,6,0),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);searchView.Children.Add(scroll);
        PageLabel(searchPageLabel);var pages=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center};var previous=ActionButton("이전",()=>SearchAsync(Math.Max(0,(SearchResult?.Page??0)-1)));var next=ActionButton("다음",()=>SearchAsync((SearchResult?.Page??0)+1));previous.Margin=next.Margin=new Thickness(4,2,0,4);pages.Children.Add(previous);pages.Children.Add(searchPageLabel);pages.Children.Add(next);Grid.SetRow(pages,2);searchView.Children.Add(pages);
    }
    internal async Task SearchAsync(int page=0)
    {
        var query=QueryInput.Text;var kind=new[]{"all","plan","study","money","workout","review"}[Math.Max(0,SearchKind.SelectedIndex)];var from=SearchFrom.SelectedDate is DateTime f?DateOnly.FromDateTime(f):(DateOnly?)null;var to=SearchTo.SelectedDate is DateTime t?DateOnly.FromDateTime(t):(DateOnly?)null;
        SearchResult=await Task.Run(()=>repository.SearchRecords(query,kind,from,to,page));searchRows.Children.Clear();
        if(SearchResult.Total==0)searchRows.Children.Add(T("일치하는 기록이 없어요. 검색어나 기간을 바꿔 보세요.",true));
        foreach(var hit in SearchResult.Items)
        {
            var row=new StackPanel();var header=new Grid();header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var date=T($"{hit.Date:yyyy.MM.dd} · {KindName(hit.Kind)}",true);date.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(date);
            var open=ActionButton("해당 날짜로 이동",()=>{if(BudgetDirty && !Ask("저장하지 않은 예산 입력을 버리고 날짜로 이동할까요?"))return Task.CompletedTask;NavigateTo=hit;discardClosing=true;CloseRequested?.Invoke();return Task.CompletedTask;});Grid.SetColumn(open,1);header.Children.Add(open);row.Children.Add(header);row.Children.Add(T(hit.Kind=="workout"?ExerciseCatalog.Get(hit.Title).Name:hit.Title,size:15));row.Children.Add(T(hit.Preview,true));searchRows.Children.Add(Card(row));
        }
        searchPageLabel.Text=$"{SearchResult.Page+1}/{SearchResult.Pages} · {SearchResult.Total}건";status.Text=$"검색 완료 · {SearchResult.Total}건 (저장된 기록 기준)";
    }
    internal static string KindName(string kind)=>kind switch {"plan"=>"계획","study"=>"학습","money"=>"가계부","workout"=>"운동",_=>"회고"};
    private void BuildTrash()
    {
        trashView.RowDefinitions.Add(new(){Height=GridLength.Auto});trashView.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});trashView.RowDefinitions.Add(new(){Height=GridLength.Auto});
        trashView.Children.Add(T("저장 후 삭제된 기록을 개별 복구해요. 자동으로 비우지 않으며, 영구 삭제하면 복구할 수 없어요. 학습 첨부도 함께 보관해요.",true));
        var scroll=new ScrollViewer{Content=trashRows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);trashView.Children.Add(scroll);PageLabel(trashPageLabel);var pages=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center};
        pages.Children.Add(ActionButton("이전",async()=>{trashPage=Math.Max(0,trashPage-1);await LoadTrashAsync();}));pages.Children.Add(trashPageLabel);pages.Children.Add(ActionButton("다음",async()=>{trashPage++;await LoadTrashAsync();}));Grid.SetRow(pages,2);trashView.Children.Add(pages);
    }
    internal async Task LoadTrashAsync()
    {
        TrashResult=await Task.Run(()=>repository.DeletedRecords(trashPage));trashPage=TrashResult.Page;trashRows.Children.Clear();
        if(TrashResult.Total==0)trashRows.Children.Add(T("휴지통이 비어 있어요.",true));
        foreach(var item in TrashResult.Items)
        {
            var row=new StackPanel();row.Children.Add(T($"{item.Date:yyyy.MM.dd} · {KindName(item.Kind)} · 삭제 {item.DeletedUtc.ToLocalTime():yyyy.MM.dd HH:mm}",true));row.Children.Add(T(item.Title,size:15));
            if(item.Files.Count>0)row.Children.Add(T($"첨부 {item.Files.Count}개 함께 복구",true));var actions=new WrapPanel();actions.Children.Add(ActionButton("복구",()=>RestoreAsync(item.Id)));actions.Children.Add(ActionButton("영구 삭제",()=>DeleteForeverAsync(item.Id)));row.Children.Add(actions);trashRows.Children.Add(Card(row));
        }
        trashPageLabel.Text=$"{TrashResult.Page+1}/{TrashResult.Pages} · {TrashResult.Total}건";
    }
    internal async Task RestoreAsync(Guid id)
    { if(!Ask("이 기록을 원래 날짜에 복구할까요? 현재 다른 기록은 유지돼요."))return;await Task.Run(()=>repository.RestoreDeleted(id));Digest=null;await LoadTrashAsync();status.Text="복구 완료 · 원래 날짜에서 확인할 수 있어요."; }
    internal async Task DeleteForeverAsync(Guid id)
    { if(!Ask("이 항목과 보관된 첨부를 영구 삭제할까요? 이 작업은 되돌릴 수 없어요."))return;await Task.Run(()=>repository.PermanentlyDelete(id));await LoadTrashAsync();status.Text="영구 삭제 완료"; }
    internal static DateOnly ParseMonth(string text)
    {
        var value=text.Trim();
        if(!System.Text.RegularExpressions.Regex.IsMatch(value,@"^[0-9]{4}-[0-9]{1,2}$")
            || !DateOnly.TryParseExact(value+"-01",new[]{"yyyy-M-dd","yyyy-MM-dd"},CultureInfo.InvariantCulture,DateTimeStyles.None,out var month))
            throw new ArgumentException("연도와 월을 입력해 주세요. 예: 2026-9 또는 2026-09 (월: 1~12)");
        return month;
    }
    internal async Task LoadSummaryAsync(DateOnly month)
    {
        if(BudgetDirty && !Ask("저장하지 않은 예산 입력을 버리고 월간 결산을 새로 볼까요?"))return;
        month=new(month.Year,month.Month,1);if(!verification)await Task.Run(()=>repository.ApplyFixedCosts(DateOnly.FromDateTime(DateTime.Today)));
        var result=await Task.Run(()=>(Current:repository.MonthDigest(month),Previous:month.Year==1&&month.Month==1?null:repository.MonthDigest(month.AddMonths(-1)),Annual:repository.AnnualActivity(month.Year)));Digest=result.Current;annualReport=result.Annual;
        MonthInput.Text=month.ToString("yyyy-MM",CultureInfo.InvariantCulture);loadingBudget=true;BudgetInput.Text=Digest.Budget.Budget?.ToString("N0",CultureInfo.GetCultureInfo("ko-KR"))??"";savedBudgetText=BudgetInput.Text;loadingBudget=false;
        DrawDigest(result.Previous);status.Text=$"{month:yyyy년 M월} 결산 · 저장된 기록 기준. 진행 중인 달은 미완료 기간이 포함돼요.";
    }
    internal async Task SaveBudgetAsync()
    {
        var month=Digest?.Month??ParseMonth(MonthInput.Text);if(ParseMonth(MonthInput.Text)!=month)throw new ArgumentException("먼저 선택한 달 조회를 눌러 예산을 저장할 월을 선택해 주세요.");var value=MoneyEntry.ParseWon(BudgetInput.Text);await Task.Run(()=>repository.SaveBudget(new(month,value)));savedBudgetText=BudgetInput.Text;await LoadSummaryAsync(month);status.Text="예산 저장 완료";
    }
    private async Task ClearBudgetAsync()
    { if(Digest is null || !Ask("이 달의 예산 설정을 해제할까요? 가계부 기록은 유지돼요."))return;await Task.Run(()=>repository.RemoveBudget(Digest.Month));savedBudgetText=BudgetInput.Text;await LoadSummaryAsync(Digest.Month);status.Text="예산 해제 완료"; }
    private static string Won(long value)=>value.ToString("N0",CultureInfo.GetCultureInfo("ko-KR"))+"원";
    private void DrawDigest(MonthlyDigest? previous)
    {
        var d=Digest!;summaryRows.Children.Clear();summaryRows.Children.Add(T($"{d.Month:yyyy년 M월} 월간 결산",size:20));
        var budget=new StackPanel();budget.Children.Add(T("소비 예산",size:15));budget.Children.Add(Pair("소비",Won(d.Money.Consumption)));budget.Children.Add(Pair("적금·저축",Won(d.Money.Savings)));
        if(d.Budget.Budget is long amount)
        {
            budget.Children.Add(Pair("월 소비 예산",Won(amount)));budget.Children.Add(Pair("사용률",$"{d.Budget.Percent:0.#}%"));var remaining=d.Budget.Remaining!.Value;
            var balance=Pair(remaining>=0?"남은 예산":"예산 초과",Won(Math.Abs(remaining)));if(remaining<0)foreach(var label in balance.Children.OfType<TextBlock>())label.Foreground=B("Sunday");budget.Children.Add(balance);
            budget.Children.Add(new ProgressBar{Minimum=0,Maximum=100,Value=(double)Math.Min(100,d.Budget.Percent!.Value),Height=8,Foreground=B("Accent"),Background=B("Paper"),Margin=new Thickness(0,8,0,8)});
        }
        else budget.Children.Add(T("이 달의 예산이 아직 없어요.",true));budgetSection=Card(budget);budgetSection.Focusable=true;summaryRows.Children.Add(budgetSection);
        var activity=new StackPanel();activity.Children.Add(T("한 달의 기록",size:15));
        StackPanel Group(string name)
        {
            var panel=new StackPanel{Margin=new Thickness(0,12,0,0)};
            var heading=T(name,size:14);heading.FontWeight=FontWeights.SemiBold;
            panel.Children.Add(heading);
            activity.Children.Add(new Border{BorderBrush=B("Line"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,4,0,0),Child=panel});
            return panel;
        }
        var plans=Group("계획");plans.Children.Add(Pair("계획 완료",$"{d.Activity.Done} / {d.Activity.Plans}건"));plans.Children.Add(Pair("계획 완료율",Rate(d.Activity.CompletionRate)));
        var exercise=Group("운동");
        exercise.Children.Add(Pair("운동 기록",$"{d.Activity.Workout.Records}건 · {d.Activity.Workout.Days}일"));
        exercise.Children.Add(Pair("입력된 운동 시간",$"{d.Activity.Workout.Seconds/3600}시간 {d.Activity.Workout.Seconds%3600/60}분"));
        exercise.Children.Add(Pair("시간 입력",$"{d.Activity.Workout.TimedRecords} / {d.Activity.Workout.Records}건",true));
        exercise.Children.Add(Pair("예상 kcal",d.Activity.Workout.Kcal is decimal energy?"약 "+Math.Round(energy,0,MidpointRounding.AwayFromZero).ToString("N0"):"계산된 기록 없음"));
        exercise.Children.Add(Pair("kcal 계산",$"{d.Activity.Workout.Calculated} / {d.Activity.Workout.Records}건",true));
        var study=Group("학습");study.Children.Add(Pair("학습 기록",$"{d.Studies}건 · {d.StudyDays}일"));
        study.Children.Add(Pair("입력된 학습 시간",d.TimedStudies==0 && d.Studies>0?"시간 미입력":$"{d.StudyMinutes/60:N0}시간 {d.StudyMinutes%60}분"));
        study.Children.Add(Pair("시간 입력",$"{d.TimedStudies} / {d.Studies}건",true));
        Group("하루 회고").Children.Add(Pair("회고한 날",$"{d.Activity.ReviewDays}일"));
        var money=Group("금전");money.Children.Add(Pair("수입",Won(d.Money.Income)));money.Children.Add(Pair("소비",Won(d.Money.Consumption)));money.Children.Add(Pair("적금·저축",Won(d.Money.Savings)));money.Children.Add(Pair("지출·저축 합계",Won(d.Money.Expense)));money.Children.Add(Pair("차액",Won(d.Money.Balance)));money.Children.Add(T("차액은 계좌 잔액이 아니에요.",true));
        activitySection=Card(activity);activitySection.Focusable=true;summaryRows.Children.Add(activitySection);compareSection=null;
        if(previous is not null)
        {
            var compare=new StackPanel();compare.Children.Add(T($"전월 비교 · {previous.Month:yyyy.MM} → {d.Month:yyyy.MM}",size:15));
            compare.Children.Add(UiLayout.Values(T("항목",true),T($"{previous.Month:yyyy.MM}",true),T($"{d.Month:yyyy.MM}",true)));
            foreach(var (label,before,after) in new[]{("계획 완료율",Rate(previous.Activity.CompletionRate),Rate(d.Activity.CompletionRate)),("운동 시간",$"{previous.Activity.Workout.Seconds/60}분",$"{d.Activity.Workout.Seconds/60}분"),("소비",Won(previous.Money.Consumption),Won(d.Money.Consumption)),("적금·저축",Won(previous.Money.Savings),Won(d.Money.Savings)),("학습",$"{previous.Studies}건",$"{d.Studies}건")})compare.Children.Add(UiLayout.Values(T(label),T(before),T(after)));
            compare.Children.Add(T("두 달 전체에 저장된 기록을 비교해요. 진행 중인 달과 지난 달의 사용 기간은 다를 수 있어요.",true));compareSection=Card(compare);compareSection.Focusable=true;summaryRows.Children.Add(compareSection);
        }
        DrawAnnualActivity();
        static string Rate(decimal? value)=>value is decimal rate?rate.ToString("0.#")+"%":"계획 없음";
    }
}
