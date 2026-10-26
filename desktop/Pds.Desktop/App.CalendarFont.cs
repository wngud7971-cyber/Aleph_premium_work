using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pds.Desktop;

public partial class App
{
    private void CalendarFont_Loaded(object sender, RoutedEventArgs args)
    {
        // The Windows CalendarItem template hardcodes Verdana on its weekday titles.
        // Keep the native template/selection behavior, replacing only its text font.
        var family = (FontFamily)FindResource("BodyFont");
        void Apply(DependencyObject root)
        {
            if (root is TextBlock text) text.SetCurrentValue(TextBlock.FontFamilyProperty, family);
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                Apply(VisualTreeHelper.GetChild(root, i));
        }
        Apply((DependencyObject)sender);
    }
}
