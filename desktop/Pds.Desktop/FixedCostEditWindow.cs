using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pds.Core;

namespace Pds.Desktop;

internal sealed class FixedCostEditWindow : Window
{
    private readonly PlanRepository repository;
    private readonly FixedCostRule original;
    private readonly DateOnly today;
    private readonly StackPanel body=new(){Margin=new Thickness(22)};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(22,8,22,12)};
    private readonly Button save;
    private bool busy,finished;
    internal CheckBox ChangeEnabled { get; }=new(){Content="적용 월부터 이름·금액·유형 변경",IsChecked=true,Margin=new Thickness(0,6,0,10)};
    internal CheckBox EndEnabled { get; }=new(){Content="종료 월 지정 · 그 달까지 반영",Margin=new Thickness(0,10,0,8)};
    internal TextBox ApplyMonth { get; }=new(){MaxLength=7};
    internal TextBox EndMonth { get; }=new(){MaxLength=7};
    internal TextBox NameInput { get; }=new(){MaxLength=200};
    internal TextBox AmountInput { get; }=new(){MaxLength=18};
    internal ComboBox CategoryInput { get; }=new();
    internal TextBox NoteInput { get; }=new(){MaxLength=2000,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=72,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    internal bool Saved { get; private set; }
    internal Func<bool>? DiscardDecision { get; set; }
    private readonly string initial;
    private string State=>$"{ChangeEnabled.IsChecked}|{EndEnabled.IsChecked}|{ApplyMonth.Text}|{EndMonth.Text}|{NameInput.Text}|{AmountInput.Text}|{CategoryInput.SelectedItem}|{NoteInput.Text}";
    internal FixedCostEditWindow(PlanRepository repository,FixedCostRule rule,Window owner,DateOnly today)
        :this(repository,rule,owner,today,false){}
    internal FixedCostEditWindow(PlanRepository repository,FixedCostRule rule,Window owner,DateOnly today,bool verification)
    {
        this.repository=repository;original=rule;this.today=today;Owner=owner;Title="PDS · 고정 지출 변경";Width=480;Height=Math.Min(760,SystemParameters.WorkArea.Height-24);MinWidth=400;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        foreach(var key in new[]{"Paper","Soft","Ink","Muted","Line","Accent","AccentInk","Note","Sunday"})Resources[key]=owner.FindResource(key);
        Style=(Style)owner.FindResource(typeof(Window));
        if(!verification)WindowEnvironment.Watch(this);
        System.Windows.Input.KeyboardNavigation.SetTabNavigation(this,System.Windows.Input.KeyboardNavigationMode.Cycle);
        PreviewKeyDown+=async(_,e)=>{if(CategoryInput.IsDropDownOpen)return;if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;Close();}else if(e.Key==System.Windows.Input.Key.S && System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control){e.Handled=true;try{await SaveChangesAsync();}catch(Exception ex){status.Text=ex.Message;}}};
        foreach(var (control,name) in new(Control,string)[]{(ApplyMonth,"변경 적용 월 YYYY-MM"),(NameInput,"매월 반복 내역명"),(AmountInput,"매월 반복 금액 원"),(CategoryInput,"매월 반복 유형"),(NoteInput,"매월 반복 메모 선택 사항"),(EndMonth,"그 달까지 반영할 종료 월 YYYY-MM")})System.Windows.Automation.AutomationProperties.SetName(control,name);
        void Label(string text)=>body.Children.Add(new TextBlock{Text=text,Foreground=(Brush)FindResource("Muted"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4)});
        body.Children.Add(new TextBlock{Text="고정 지출 변경·종료",FontSize=UiTypography.Grow(20),Margin=new Thickness(0,0,0,10)});
        Label("과거 거래는 유지하고 다음 달 이후부터 변경해요. 종료 월까지 반영하며 아래 저장으로 확정해요.");
        foreach(var control in new Control[]{ApplyMonth,EndMonth,NameInput,AmountInput,CategoryInput})UiLayout.Compact(control);
        ApplyMonth.TextAlignment=EndMonth.TextAlignment=TextAlignment.Center;AmountInput.TextAlignment=TextAlignment.Right;
        body.Children.Add(ChangeEnabled);Label("변경 적용 월 · YYYY-MM");body.Children.Add(ApplyMonth);Label("내역명");body.Children.Add(NameInput);
        var fields=new Grid();fields.ColumnDefinitions.Add(new());fields.ColumnDefinitions.Add(new());
        foreach(var (control,label,index) in new(Control,string,int)[]{(AmountInput,"금액 · 원",0),(CategoryInput,"유형",1)}){var field=new StackPanel{Margin=new Thickness(index==0?0:6,0,index==0?6:0,0)};field.Children.Add(new TextBlock{Text=label,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(0,8,0,4)});field.Children.Add(control);Grid.SetColumn(field,index);fields.Children.Add(field);}body.Children.Add(fields);
        Label("메모 · 선택");body.Children.Add(NoteInput);body.Children.Add(EndEnabled);Label("종료 월 · YYYY-MM · 체크하지 않으면 기한 없음");body.Children.Add(EndMonth);
        void UpdateEnabled(){foreach(var control in new Control[]{ApplyMonth,NameInput,AmountInput,CategoryInput,NoteInput})control.IsEnabled=ChangeEnabled.IsChecked==true;EndMonth.IsEnabled=EndEnabled.IsChecked==true;}
        ChangeEnabled.Checked+=(_,_)=>UpdateEnabled();ChangeEnabled.Unchecked+=(_,_)=>UpdateEnabled();EndEnabled.Checked+=(_,_)=>UpdateEnabled();EndEnabled.Unchecked+=(_,_)=>UpdateEnabled();UpdateEnabled();
        var next=new DateOnly(today.Year,today.Month,1).AddMonths(1);var first=new DateOnly(rule.FirstDate.Year,rule.FirstDate.Month,1);if(next<=first)next=first.AddMonths(1);
        var template=rule.TemplateIn(next);ApplyMonth.Text=next.ToString("yyyy-MM",CultureInfo.InvariantCulture);NameInput.Text=template.Title;AmountInput.Text=template.Won.ToString("N0",CultureInfo.GetCultureInfo("ko-KR"));CategoryInput.ItemsSource=MoneyEntry.ExpenseCategories;CategoryInput.SelectedItem=template.Category;NoteInput.Text=template.Note;
        EndEnabled.IsChecked=rule.EndMonth is not null;EndMonth.Text=(rule.EndMonth??next).ToString("yyyy-MM",CultureInfo.InvariantCulture);initial=State;UpdateEnabled();
        var root=new Grid{Background=(Brush)FindResource("Paper")};root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Grid.SetRow(status,1);root.Children.Add(status);
        save=new Button{Content="변경 저장",Background=(Brush)FindResource("Accent"),Foreground=(Brush)FindResource("AccentInk"),Margin=new Thickness(22,4,22,14)};save.Click+=async(_,_)=>{try{await SaveChangesAsync();}catch(Exception ex){status.Text=ex.Message;}};Grid.SetRow(save,2);root.Children.Add(save);Content=root;
        Closing+=(_,e)=>{if(busy){e.Cancel=true;return;}if(!finished && initial!=State && !(DiscardDecision?.Invoke()??System.Windows.MessageBox.Show(this,"저장하지 않은 변경을 버리고 닫을까요?","입력 확인",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes))e.Cancel=true;};
    }
    internal async Task SaveChangesAsync()
    {
        if(busy)return;
        FixedCostChange? change=null;
        if(ChangeEnabled.IsChecked==true)change=new(JournalToolsWindow.ParseMonth(ApplyMonth.Text),original.Template with {Title=NameInput.Text.Trim(),Won=MoneyEntry.ParseWon(AmountInput.Text),Category=CategoryInput.SelectedItem as string??"",Note=NoteInput.Text.Trim()});
        var end=EndEnabled.IsChecked==true?JournalToolsWindow.ParseMonth(EndMonth.Text):(DateOnly?)null;
        busy=true;body.IsEnabled=save.IsEnabled=false;
        try{await Task.Run(()=>repository.ChangeFixedCost(original.Id,original.Revision,change,end,today));Saved=finished=true;status.Text="변경 저장 완료";}
        finally{busy=false;body.IsEnabled=save.IsEnabled=true;}
        if(IsVisible)Close();
    }
}
