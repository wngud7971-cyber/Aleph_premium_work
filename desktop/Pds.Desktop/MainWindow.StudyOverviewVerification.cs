using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyStudyOverviewAsync(List<string> checks)
    {
        var start = checks.Count;
        void Check(bool value,string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        async Task Idle() => await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var day = new DateOnly(2026,10,13); var next = day.AddDays(1);
        var folder = Path.GetDirectoryName(repository.DatabasePath)!;
        var originals = new[] { repository.Load(day),repository.Load(next) };
        try
        {
            repository.Save(originals[0] with { Studies = Enumerable.Range(1,12).Select(i=>StudyEntry.Create("자료구조 복습 " + i,"예제 코드를 직접 작성했다.\n두 번째 줄도 원문 그대로 유지.","배열과 리스트의 차이 " + i)).ToArray() });
            repository.Save(originals[1] with { Studies = [StudyEntry.Create("영어 읽기","짧은 글을 읽고 문장을 정리했다.","새 표현을 문장으로 기억하기"),StudyEntry.Create("수학 복습")] });
            statisticsReport = null; statisticsMode = "week"; statisticsCustom = null;
            await OpenStatisticsAsync(); await Idle();
            Check(studyOverview is { Records:15,StudyDays:3,Pages:2 } && StudyOverviewList!.Children.Count==10, "study-overview-shows-total-records-distinct-days-and-bounded-page");
            Check(studyOverview!.Items[0].Date == next && studyOverview.Items[2].Date == day, "study-overview-sorts-recent-dates-first");
            var storedRevision = repository.Load(day).Revision;
            var item = StudyOverviewList!.Children.OfType<Border>().First();
            var details = ((StackPanel)item.Child).Children.OfType<Expander>().Single();
            Check(details.Content is null && !details.IsExpanded, "study-overview-defers-body-loading-until-expansion");
            details.IsExpanded = true;
            for (var i=0;i<100 && busy;i++) { await Task.Delay(10); await Idle(); }
            Check(!busy && details.Content is StackPanel detailContent && detailContent.Children.OfType<TextBlock>().Any(t=>t.Text=="짧은 글을 읽고 문장을 정리했다."),
                "study-overview-expansion-loads-complete-stored-body");
            Check(repository.Load(day).Revision == storedRevision && !Pending, "reading-study-overview-never-mutates-records-or-creates-drafts");
            await ChangeStudyPageAsync(1); await Idle();
            Check(studyOverview!.Page==1 && studyOverview.Items.Count==5 && StudyPageLabel!.Text.Contains("2 / 2"), "study-overview-next-page-shows-remaining-records");
            await ChangeStudyPageAsync(-1); await Idle();
            Check(studyOverview!.Page==0 && StudyOverviewList!.Children.Count==10, "study-overview-previous-page-restores-first-ten");
            statisticsFilter = new("사이클","cycling-indoor"); await RefreshStatisticsAsync();
            Check(studyOverview!.Records==15 && studyOverview.StudyDays==3, "exercise-filter-never-filters-study-overview");
            statisticsMode = "month"; await RefreshStatisticsAsync();
            Check(studyOverview!.Range==DateRange.Month(day) && studyOverview.Records==15, "study-overview-shares-monthly-period-controls");
            foreach (var dark in new[] { false,true })
            {
                settings.Dark=dark; ApplyTheme(); DrawStatistics(); await Idle(); JumpToStudyOverview(); await Idle();
                Check(Math.Abs(StudyOverviewSection!.TranslatePoint(new Point(),StatisticsPane).Y-StatisticsPane.Padding.Top) <= 2 && StudyOverviewList!.Children.Count==10,
                    "study-shortcut-reveals-overview-heading-"+dark);
                Capture("study-overview-"+(dark?"dark":"light")+".png");
                var first = StudyOverviewList.Children.OfType<Border>().First();
                var expand = ((StackPanel)first.Child).Children.OfType<Expander>().Single();
                expand.IsExpanded=true;
                for (var i=0;i<100 && busy;i++) { await Task.Delay(10); await Idle(); }
                expand.BringIntoView(); await Idle();
                Check(expand.Content is StackPanel && expand.IsExpanded,"study-details-readable-in-both-themes-"+dark);
                Capture("study-overview-expanded-"+(dark?"dark":"light")+".png");
            }
            await OpenStudyDateAsync(day); await Idle();
            Check(!statisticsOpen && selected==day && month==new DateOnly(day.Year,day.Month,1) && StudyPane.IsVisible && studies.Count==12 && !Pending,
                "study-result-opens-correct-date-and-study-tab-without-edit-draft");
            await OpenStatisticsAsync(); studyPage=1; statisticsAnchor=new(2026,12,1); statisticsMode="month"; await RefreshStatisticsAsync();
            Check(studyOverview is { Records:0,StudyDays:0,Page:0 } && StudyOverviewList!.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("없어요")),
                "empty-study-period-resets-pagination-and-explains-no-records");
            CloseStatistics();
            // Preserve the previous journal/restart fixture after the independent list test.
            foreach (var original in originals) repository.Save(original with { Revision=repository.Load(original.Date).Revision });
            await SelectDateAsync(new(2026,10,12));
            File.WriteAllText(Path.Combine(folder,"study-overview.json"),JsonSerializer.Serialize(new { success=true,count=checks.Count-start,
                checks=checks.Skip(start).ToArray(),pageSize=10,bodyLoadedOnExpansion=true,personalDataUsed=false }));
        }
        finally
        {
            if (statisticsOpen) CloseStatistics();
            settings.Dark=false; ApplyTheme(); DrawCalendar();
        }
    }
}
