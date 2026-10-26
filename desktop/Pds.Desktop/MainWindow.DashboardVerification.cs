using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyDashboardStructureAsync(List<string> checks, string theme)
    {
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name + "-" + theme);
            checks.Add(name + "-" + theme);
        }
        async Task Idle() => await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        ClearEditor(); ClearWorkoutEditor(); LoadReview(); SelectRecordTab("plan");
        await Idle();
        var expectedToday = repository.Counts(LocalToday, LocalToday).GetValueOrDefault(LocalToday) ?? new Pds.Core.DayCount(0, 0);
        Check(DashboardPlanValue.Text == $"{expectedToday.Done} / {expectedToday.Plans}" && DashboardWorkoutValue.Text == $"{expectedToday.Workouts}건",
            "dashboard-uses-saved-today-counts-independently-of-selected-day");
        var dashboardBottom = DashboardHeader.TranslatePoint(new Point(0, DashboardHeader.ActualHeight), this).Y;
        Check(DashboardHeader.IsVisible && CalendarArea.TranslatePoint(new Point(), this).Y >= dashboardBottom,
            "today-dashboard-precedes-month-calendar-without-overlap");
        Check(!PlanComposer.IsExpanded && !WorkoutComposer.IsExpanded && !ReviewComposer.IsExpanded && PlanList.IsVisible,
            "date-journal-starts-with-saved-list-and-collapsed-composers");
        Capture("structure-day-" + theme + ".png");
        var stored = repository.Load(selected);
        var peer = new ExpanderAutomationPeer(PlanComposer);
        var expansion = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse)!;
        expansion.Expand(); await Idle();
        Check(PlanComposer.IsExpanded && TitleInput.IsVisible, "plan-composer-expands-through-native-automation-pattern");
        const string title = "오늘의 우선순위 정리";
        TitleInput.Text = title; PlanPane.ScrollToBottom(); await Idle();
        Capture("structure-composer-" + theme + ".png");
        expansion.Collapse();
        Check(!PlanComposer.IsExpanded && TitleInput.Text == title && Pending && repository.Load(selected).Revision == stored.Revision,
            "collapsing-plan-keeps-draft-pending-without-saving");
        WorkoutMinutes.Text = "12";
        Check(WorkoutComposer.IsExpanded && !PlanComposer.IsExpanded && TitleInput.Text == title,
            "workout-editing-does-not-reopen-or-clear-collapsed-plan");
        ReviewGood.Text = "작은 계획을 하나 실천했다.";
        Check(ReviewComposer.IsExpanded, "review-editing-reveals-review-fields");
        WorkoutComposer.IsExpanded = ReviewComposer.IsExpanded = false;
        SelectRecordTab("review"); SelectRecordTab("workout"); SelectRecordTab("plan");
        Theme_Click(this, new RoutedEventArgs()); Theme_Click(this, new RoutedEventArgs());
        Check(!PlanComposer.IsExpanded && !WorkoutComposer.IsExpanded && !ReviewComposer.IsExpanded
            && TitleInput.Text == title && WorkoutMinutes.Text == "12" && ReviewGood.Text == "작은 계획을 하나 실천했다." && Pending,
            "collapsed-three-tab-drafts-survive-tab-and-theme-changes");
        Check(DashboardPlanValue.Text == $"{expectedToday.Done} / {expectedToday.Plans}" && repository.Load(selected).Revision == stored.Revision,
            "dashboard-and-database-do-not-count-unsaved-composer-drafts");
        pendingChangeDecision = () => MessageBoxResult.Cancel;
        CloseDetails_Click(this, new RoutedEventArgs()); await Idle(); pendingChangeDecision = null;
        Check(Details.IsVisible && TitleInput.Text == title && Pending, "collapsed-drafts-still-protect-close-details");
        ClearWorkoutEditor(); LoadReview(); PlanComposer.IsExpanded = false;
        Check(await SaveAsync() && !PlanComposer.IsExpanded && !Pending
            && repository.Load(selected).Plans.Count(p => p.Title == title) == 1,
            "save-includes-collapsed-plan-and-returns-to-saved-agenda");
        var added = plans.Single(p => p.Title == title);
        plans.RemoveAll(p => p.Id == added.Id); MarkDirty();
        Check(await SaveAsync() && repository.Load(selected).Plans.Select(p => p.Id).SequenceEqual(stored.Plans.Select(p => p.Id))
            && repository.Load(selected).Workouts.Select(w => w.Id).SequenceEqual(stored.Workouts.Select(w => w.Id))
            && repository.Load(selected).Review == stored.Review,
            "composer-verification-restores-original-synthetic-day-records");
        SelectRecordTab("plan"); PlanPane.ScrollToTop(); await Idle();
    }
}
