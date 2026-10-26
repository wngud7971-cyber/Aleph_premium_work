using System.Windows;
using System.Windows.Controls;
using Pds.Core;

namespace Pds.Desktop;

// A test harness for the reusable view; the shipping navigation embeds JournalToolsView directly.
internal sealed class JournalToolsWindow : Window
{
    private readonly JournalToolsView view;
    internal JournalToolsWindow(PlanRepository repository,Window owner,DateOnly month,bool verification=false,string start="search")
    {
        Owner=owner;Title="PDS · 기록 관리";Width=780;Height=Math.Min(760,SystemParameters.WorkArea.Height-24);MinWidth=620;MinHeight=460;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        foreach(var key in new[]{"Paper","Canvas","Soft","Ink","Muted","Line","Accent","AccentInk","Note","Sunday","Saturday"})Resources[key]=owner.FindResource(key);
        Style=(Style)owner.FindResource(typeof(Window));if(!verification)WindowEnvironment.Watch(this);
        view=new(repository,owner,month,verification,start);Content=view;
        view.CloseRequested+=Close;Loaded+=async(_,_)=>await view.InitializeAsync();Closing+=(_,e)=>{if(!view.TryLeave())e.Cancel=true;};
    }
    internal TextBox QueryInput=>view.QueryInput;
    internal ComboBox SearchKind=>view.SearchKind;
    internal DatePicker SearchFrom=>view.SearchFrom;
    internal DatePicker SearchTo=>view.SearchTo;
    internal TextBox MonthInput=>view.MonthInput;
    internal TextBox BudgetInput=>view.BudgetInput;
    internal ComboBox AnnualMetricChoice=>view.AnnualMetricChoice;
    internal Expander SearchFilters=>view.SearchFilters;
    internal SearchPage? SearchResult=>view.SearchResult;
    internal DeletedPage? TrashResult=>view.TrashResult;
    internal MonthlyDigest? Digest=>view.Digest;
    internal SearchHit? NavigateTo=>view.NavigateTo;
    internal Func<string,bool>? Confirmation {get=>view.Confirmation;set=>view.Confirmation=value;}
    internal bool BudgetDirty=>view.BudgetDirty;
    private bool busy=>view.IsBusy;
    private ScrollViewer summaryScroll=>view.SummaryScroll;
    private StackPanel summaryView=>view.SummaryView;
    private StackPanel searchRows=>view.SearchRows;
    private TextBlock status=>view.StatusLabel;
    internal Task SwitchAsync(string tab)=>view.SwitchAsync(tab);
    internal Task SearchAsync(int page=0)=>view.SearchAsync(page);
    internal Task LoadSummaryAsync(DateOnly month)=>view.LoadSummaryAsync(month);
    internal Task SaveBudgetAsync()=>view.SaveBudgetAsync();
    internal Task LoadTrashAsync()=>view.LoadTrashAsync();
    internal Task RestoreAsync(Guid id)=>view.RestoreAsync(id);
    internal Task DeleteForeverAsync(Guid id)=>view.DeleteForeverAsync(id);
    internal static DateOnly ParseMonth(string text)=>JournalToolsView.ParseMonth(text);
    internal static string KindName(string kind)=>JournalToolsView.KindName(kind);
}
