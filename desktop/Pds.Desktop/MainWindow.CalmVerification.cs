using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyCalmPresentationAsync(List<string> checks, string theme)
    {
        var originalOverride = CalmMotion.AnimationOverride;
        var originalMonth = month;
        var revision = repository.Load(selected).Revision;
        var originalDate = selected;
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException(label + "-" + theme);
            checks.Add(label + "-" + theme);
        }
        static IEnumerable<FrameworkElement> Elements(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement element) yield return element;
                foreach (var nested in Elements(child)) yield return nested;
            }
        }
        async Task Idle() => await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        try
        {
            CalmMotion.Settle(this); CalmMotion.AnimationOverride = true;
            var size = Details.RenderSize;
            CalmMotion.Reveal(Details, CalmMotion.ScreenMs);
            Check(CalmMotion.ActiveCount == 0 && Details.Opacity == 1 && Details.RenderSize == size, "context-transition-keeps-text-opaque-without-changing-layout");
            await Task.Delay(CalmMotion.ScreenMs + 100); await Idle();
            Check(Math.Abs(Details.Opacity - 1) < 0.001 && CalmMotion.ActiveCount == 0,
                "context-transition-finishes-and-removes-animation-clocks");
            Check(CalmMotion.HoverEnterMs < CalmMotion.HoverLeaveMs && CalmMotion.TabMs < CalmMotion.DialogMs,
                "hover-tabs-and-dialogs-use-context-specific-timing");

            var hover = Elements(TodayButton).Single(CalmMotion.GetHoverWash);
            var buttonBounds = TodayButton.RenderSize;
            TodayButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
            await Task.Delay(CalmMotion.HoverEnterMs + 80); await Idle();
            Check(hover.Opacity > 0.04 && hover.Opacity < 0.05 && TodayButton.RenderSize == buttonBounds,
                "subtle-hover-tint-preserves-button-hit-area");
            TodayButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
            await Task.Delay(CalmMotion.HoverLeaveMs + 80); await Idle();
            Check(hover.Opacity < 0.001 && CalmMotion.ActiveCount == 0, "hover-leave-restores-clear-surface-without-idle-clock");
            for (var i = 0; i < 5; i++)
            {
                TodayButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                TodayButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
            }
            Check(CalmMotion.ActiveCount == 1, "rapid-hover-replaces-one-animation-instead-of-stacking");
            CalmMotion.Settle(this);
            CalmMotion.AnimationOverride = false;
            CalmMotion.Reveal(Details, CalmMotion.DialogMs);
            TodayButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
            Check(CalmMotion.ActiveCount == 0 && Details.Opacity == 1 && Math.Abs(hover.Opacity - 0.045) < 0.001,
                "reduced-motion-renders-final-state-with-static-hover-feedback");
            TodayButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
            CalmMotion.AnimationOverride = true;
            SelectRecordTab("workout"); SelectRecordTab("review"); SelectRecordTab("plan");
            CalmMotion.Reveal(Details, CalmMotion.ScreenMs); Details.Visibility = Visibility.Collapsed;
            Check(Details.Opacity == 1, "hidden-panel-cancels-its-reveal-clock");
            Details.Visibility = Visibility.Visible; await Idle(); CalmMotion.Settle(this);
            Check(CalmMotion.ActiveCount == 0 && selected == originalDate && month == originalMonth
                && repository.Load(selected).Revision == revision && !Pending,
                "motion-does-not-change-selected-date-or-saved-records");
            var expectedShadow = !SystemParameters.HighContrast && RenderCapability.Tier >= 0x10000;
            Check(Details.Effect is null && (expectedShadow
                ? CalendarSurface.Effect is DropShadowEffect { IsFrozen: true, BlurRadius: <= 18, ShadowDepth: <= 2, Opacity: <= 0.18 }
                    && DetailsShadow.Effect is DropShadowEffect { IsFrozen: true }
                : CalendarSurface.Effect is null && DetailsShadow.Effect is null),
                "soft-shadows-are-frozen-and-limited-to-surfaces");
            Check(MonthGrid.Children.OfType<Button>().All(b => b.Effect is null), "calendar-day-buttons-do-not-create-individual-shadows");
            PlanPane.ScrollToTop(); await Idle(); Capture("calm-day-" + theme + ".png");
            CloseDetails_Click(this, new RoutedEventArgs()); await Idle(); Capture("calm-calendar-" + theme + ".png");
            await SelectDateAsync(originalDate); SelectRecordTab("plan"); await Idle(); CalmMotion.Settle(this);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "calm-motion-" + theme + ".json"),
                JsonSerializer.Serialize(new { success = true, systemAnimationsEnabled = SystemParameters.ClientAreaAnimation,
                    hoverEnterMs = CalmMotion.HoverEnterMs, hoverLeaveMs = CalmMotion.HoverLeaveMs, tabMs = CalmMotion.TabMs,
                    screenMs = CalmMotion.ScreenMs, composerMs = CalmMotion.ComposerMs, dialogMs = CalmMotion.DialogMs,
                    physicalPointerTested = false, inactiveWhenSettled = CalmMotion.ActiveCount == 0,
                    shadowsEnabled = expectedShadow, subjectiveSmoothnessTested = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { CalmMotion.Settle(this); CalmMotion.AnimationOverride = originalOverride; }
    }
}
