using System.Windows;
using System.Windows.Controls;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private IReadOnlyList<Anniversary> anniversaries = [];
    private async void Anniversaries_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            var dialog = new AnniversaryWindow(repository, this, selected); dialog.ShowDialog();
            await RefreshCalendarAsync(); DrawSelectedAnniversaries();
        }
        catch (Exception ex) { HandleError("기념일", ex); }
    }
    private void DrawSelectedAnniversaries()
    {
        AnniversaryDayList.Children.Clear();
        var occurrences = AnniversaryDates.Between(anniversaries, selected, selected);
        AnniversaryDayList.Visibility = occurrences.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var occurrence in occurrences)
        {
            var entry = occurrence.Entry;
            AnniversaryDayList.Children.Add(new TextBlock { Text = entry.DisplayLabel, TextWrapping = TextWrapping.Wrap });
            AnniversaryDayList.Children.Add(new TextBlock { Text = entry.DateLabel + (occurrence.Adjusted ? " · 대체 규칙 적용" : ""), Foreground = ResourceBrush("Muted"), TextWrapping = TextWrapping.Wrap });
            if (entry.Origin is not null) AnniversaryDayList.Children.Add(new TextBlock { Text = entry.Origin.Label, Foreground = ResourceBrush("Muted"), TextWrapping = TextWrapping.Wrap });
            if (entry.Note.Length > 0) AnniversaryDayList.Children.Add(new TextBlock { Text = entry.Note, TextWrapping = TextWrapping.Wrap });
        }
        if (occurrences.Count > 0) AnniversaryDayList.Children.Add(new TextBlock { Text = "기념일 메뉴에서 수정·삭제할 수 있어요.", Foreground = ResourceBrush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
    }
}
