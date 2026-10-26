using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyFontStepAsync(List<string> checks)
    {
        var start=checks.Count;var oldDark=settings.Dark;var oldMotion=CalmMotion.AnimationOverride;
        var metrics=new List<object>();var failures=new List<string>();
        void Check(bool value,string label){if(!value)failures.Add(label);else checks.Add(label);}
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
        {
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            {var child=VisualTreeHelper.GetChild(root,i);if(child is T typed)yield return typed;foreach(var nested in Find<T>(child))yield return nested;}
        }
        try
        {
            // Explicit v1.77 values: this asserts the requested delta, not Grow against itself.
            var sizes=new[]{("small",UiTypography.Small,11.3333333333),("body",UiTypography.Body,13.3333333333),
                ("date",UiTypography.Date,15.3333333333),("subheading",UiTypography.Subheading,17.3333333333),
                ("heading",UiTypography.Heading,19.3333333333),("display",UiTypography.Display,21.3333333333),
                ("timer",UiTypography.Timer,25.3333333333),("selected-date",UiTypography.SelectedDate,29.3333333333)};
            foreach(var (name,current,previous) in sizes)Check(Math.Abs(current-previous-96d/72)<.00001,"font-adds-exactly-one-point-"+name);
            CalmMotion.AnimationOverride=false;pendingChangeDecision=()=>MessageBoxResult.No;
            CloseMeditation();ClosePlanReminder();if(statisticsOpen)CloseStatistics();
            foreach(var dark in new[]{false,true})
            {
                settings.Dark=dark;ApplyTheme();await SelectDateAsync(new DateOnly(2026,10,18));await Idle();
                foreach(var tab in new[]{"plan","workout","review","money","study"})
                {
                    SelectRecordTab(tab);await Idle();UpdateLayout();
                    var buttons=Find<Button>(this).Where(b=>b.IsVisible && b.Content is string).ToArray();
                    var clipped=new List<string>();
                    foreach(var button in buttons)
                    {
                        var text=(string)button.Content;var formatted=CalendarFormattedText(new TextBlock{FontFamily=button.FontFamily,FontSize=button.FontSize},text);
                        var width=button.ActualWidth-button.Padding.Left-button.Padding.Right-button.BorderThickness.Left-button.BorderThickness.Right;
                        var height=button.ActualHeight-button.Padding.Top-button.Padding.Bottom-button.BorderThickness.Top-button.BorderThickness.Bottom;
                        if(formatted.Width>width+1 || formatted.Height>height+1)clipped.Add(text+" "+formatted.Width+"x"+formatted.Height+" in "+width+"x"+height);
                    }
                    metrics.Add(new{dark,tab,buttons=buttons.Length,clipped});
                    Check(clipped.Count==0,"font-full-button-labels-"+tab+"-"+dark);
                    var inputs=Find<TextBox>(this).Where(t=>t.IsVisible && !t.AcceptsReturn).ToArray();
                    Check(inputs.All(t=>t.Template.FindName("PART_ContentHost",t) is ScrollViewer h && h.ViewportHeight>=CalendarFormattedText(new TextBlock{FontFamily=t.FontFamily,FontSize=t.FontSize},"한글 0123456789").Height-1),"font-single-line-input-height-"+tab+"-"+dark);
                    Capture("font-"+tab+"-"+dark+".png");
                }
                Check(Math.Abs(FontSize-14.6666666667)<.0001 && Math.Abs(Brand.FontSize-22.6666666667)<.0001 && Math.Abs(MeditationRemaining.FontSize-26.6666666667)<.0001,"font-actual-inherited-and-special-sizes-"+dark);
                OpenMeditation();await Idle();Capture("font-meditation-"+dark+".png");CloseMeditation();
            }
        }
        finally
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!,"font-step.json"),JsonSerializer.Serialize(new{success=failures.Count==0,count=checks.Count-start,metrics,failures,personalDataUsed=false},new JsonSerializerOptions{WriteIndented=true}));
            settings.Dark=oldDark;ApplyTheme();CalmMotion.AnimationOverride=oldMotion;pendingChangeDecision=null;
        }
        if(failures.Count>0)throw new InvalidOperationException("Font step verification: "+string.Join(", ",failures));
    }
}
