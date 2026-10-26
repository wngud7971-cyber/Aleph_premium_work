using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    // Exercise the styled native controls in an isolated off-screen test window.
    private async Task VerifyModernScrollingAsync(List<string> checks, string theme)
    {
        static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message + "-" + theme);
            checks.Add(message + "-" + theme);
        }
        async Task Idle() => await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        ScrollBar Bar(ScrollViewer viewer, Orientation direction)
        {
            viewer.ApplyTemplate();
            return Descendants<ScrollBar>(viewer).First(b => b.Orientation == direction && b.IsVisible);
        }
        Track StyledTrack(ScrollBar bar)
        {
            bar.ApplyTemplate();
            return (Track)bar.Template.FindName("PART_Track", bar);
        }

        var horizontal = Bar(CalendarViewport, Orientation.Horizontal);
        var horizontalTrack = StyledTrack(horizontal);
        Check(horizontal.ActualHeight <= 12.5 && horizontalTrack.Thumb is not null,
            "modern-horizontal-scrollbar-has-slim-native-track");
        CalendarViewport.ScrollToLeftEnd(); await Idle();
        ScrollBar.PageRightCommand.Execute(null, horizontal); await Idle();
        Check(CalendarViewport.HorizontalOffset > 0, "horizontal-track-page-command-scrolls-calendar");
        ScrollBar.PageLeftCommand.Execute(null, horizontal); await Idle();
        Check(CalendarViewport.HorizontalOffset < 1, "horizontal-track-reverse-command-returns-left");
        horizontalTrack.Thumb!.RaiseEvent(new DragDeltaEventArgs(25, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        await Idle();
        Check(CalendarViewport.HorizontalOffset > 0, "horizontal-thumb-drag-updates-viewport");
        RevealSelectedDate(); await Idle();

        var composerWasExpanded = WorkoutComposer.IsExpanded;
        WorkoutComposer.IsExpanded = true;
        WorkoutPane.ScrollToTop(); await Idle();
        var vertical = Bar(WorkoutPane, Orientation.Vertical);
        var verticalTrack = StyledTrack(vertical);
        Check(vertical.ActualWidth <= 12.5 && verticalTrack.Thumb is not null,
            "modern-vertical-scrollbar-has-slim-native-track");
        ScrollBar.PageDownCommand.Execute(null, vertical); await Idle();
        Check(WorkoutPane.VerticalOffset > 0, "vertical-track-page-command-reaches-lower-inputs");
        WorkoutPane.ScrollToTop(); await Idle();
        verticalTrack.Thumb!.RaiseEvent(new DragDeltaEventArgs(0, 25) { RoutedEvent = Thumb.DragDeltaEvent });
        await Idle();
        Check(WorkoutPane.VerticalOffset > 0, "vertical-thumb-drag-updates-viewport");
        WorkoutPane.ScrollToTop(); await Idle();
        WorkoutPane.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = Mouse.MouseWheelEvent });
        await Idle();
        Check(WorkoutPane.VerticalOffset > 0, "wheel-routing-reaches-lower-workout-inputs");
        WorkoutComposer.IsExpanded = composerWasExpanded;
        WorkoutPane.ScrollToTop(); await Idle();
    }
}
