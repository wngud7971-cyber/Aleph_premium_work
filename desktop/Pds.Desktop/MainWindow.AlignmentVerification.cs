using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyToolsAlignmentAsync(JournalToolsWindow tools,List<string> checks,bool dark)
    {
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name+"-"+dark);}
        IEnumerable<T> Find<T>(DependencyObject parent) where T:DependencyObject
        {
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            {var child=VisualTreeHelper.GetChild(parent,i);if(child is T typed)yield return typed;foreach(var nested in Find<T>(child))yield return nested;}
        }
        tools.Width=620;tools.Height=460;await tools.SwitchAsync("search");await Idle();tools.UpdateLayout();
        var scroll=Find<ScrollViewer>(tools).Single(s=>s.IsVisible && s.Content is StackPanel stack && stack.Children.OfType<Border>().Any());
        CaptureUtility(tools,"alignment-search-minimum-"+(dark?"dark":"light")+".png");
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(repository.DatabasePath)!,"alignment-metrics.txt"),$"dark={dark} results={scroll.ViewportHeight} window={tools.ActualWidth}x{tools.ActualHeight}\n");
        Check(scroll.ViewportHeight>=110,"minimum-search-keeps-readable-results-viewport");
        var textHost=(ScrollViewer)tools.QueryInput.Template.FindName("PART_ContentHost",tools.QueryInput);
        Check(textHost.ActualHeight>=tools.QueryInput.FontSize*1.2,"query-input-renders-full-text-line-height");
        tools.SearchFilters.IsExpanded=true;await Idle();tools.UpdateLayout();
        var kindTop=tools.SearchKind.TranslatePoint(new Point(),tools).Y;
        Check(new FrameworkElement[]{tools.SearchFrom,tools.SearchTo}.All(c=>Math.Abs(c.TranslatePoint(new Point(),tools).Y-kindTop)<=1 && Math.Abs(c.ActualHeight-tools.SearchKind.ActualHeight)<=1),"search-filter-control-tops-and-heights-align");
        var from=tools.SearchFrom;from.ApplyTemplate();var input=(DatePickerTextBox)from.Template.FindName("PART_TextBox",from);
        Check(input.ActualHeight>=28 && input.ActualHeight<=38,"date-picker-has-no-blank-native-tall-frame");
        from.IsDropDownOpen=true;await Idle();var popup=(Popup)from.Template.FindName("PART_Popup",from);
        var calendar=popup.Child as Calendar??Find<Calendar>(popup.Child).Single();
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(repository.DatabasePath)!,"popup-font-metrics.txt"),
            "calendar="+calendar.FontFamily.Source+"; mismatched="+string.Join(" | ",Find<TextBlock>(calendar).Concat(Find<TextBlock>(tools).Where(t=>t.IsVisible)).Where(t=>t.FontFamily.Source!="Malgun Gothic").Select(t=>t.Text+":"+t.FontFamily.Source))+"\n");
        Check(calendar.FontFamily.Source=="Malgun Gothic" && Find<TextBlock>(calendar).All(t=>t.FontFamily.Source=="Malgun Gothic")
            && Find<TextBlock>(tools).Where(t=>t.IsVisible).All(t=>t.FontFamily.Source=="Malgun Gothic"),"native-calendar-popup-and-record-tools-use-malgun-gothic");
        calendar.SelectedDate=new DateTime(2026,6,1);await Idle();from.IsDropDownOpen=false;
        Check(from.SelectedDate==new DateTime(2026,6,1) && input.Text.Length>0,"styled-date-picker-calendar-updates-real-input");from.SelectedDate=null;
        tools.SearchFilters.IsExpanded=false;await Idle();
        CaptureUtility(tools,"alignment-search-minimum-"+(dark?"dark":"light")+".png");
        await tools.SwitchAsync("summary");await Idle();
        var pairs=Find<Grid>(tools).Where(g=>g.IsVisible && g.Children.Count==2 && g.Children.OfType<TextBlock>().Count()==2 && Grid.GetColumn(g.Children[1])==1).ToArray();
        Check(pairs.Length>=14 && pairs.All(g=>{var value=(TextBlock)g.Children[1];var label=(TextBlock)g.Children[0];return Math.Abs(label.TranslatePoint(new Point(0,label.ActualHeight/2),g).Y-value.TranslatePoint(new Point(0,value.ActualHeight/2),g).Y)<=1;}),"digest-labels-and-values-share-row-centres");
        var longRow=UiLayout.Values(new TextBlock{Text="소비"},new TextBlock{Text="999,999,999,999원",TextWrapping=TextWrapping.Wrap,FontFamily=tools.FontFamily,FontSize=tools.FontSize});
        longRow.Measure(new Size(480,double.PositiveInfinity));longRow.Arrange(new Rect(0,0,480,longRow.DesiredSize.Height));
        Check(longRow.Children.OfType<TextBlock>().All(t=>t.ActualWidth>0 && t.ActualHeight+t.Margin.Top+t.Margin.Bottom>=t.DesiredSize.Height-1),"large-money-values-retain-full-height-without-clipping");
        CaptureUtility(tools,"alignment-digest-minimum-"+(dark?"dark":"light")+".png");tools.Width=780;tools.Height=760;await Idle();
    }
}
