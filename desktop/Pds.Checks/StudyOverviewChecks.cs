using Pds.Core;

static class StudyOverviewChecks
{
    public static void Run(string directory, Action<bool,string> check)
    {
        void Expect(Action action,string name)
        { try { action(); } catch (ArgumentException) { check(true,name); return; } throw new InvalidOperationException(name); }
        var path = Path.Combine(directory,"study-overview.db"); var repository = new PlanRepository(path);
        var day = new DateOnly(2026,10,18); var next = day.AddDays(1);
        var empty = repository.StudyOverview(DateRange.Month(day));
        check(empty.Records == 0 && empty.StudyDays == 0 && empty.Items.Count == 0 && empty.Pages == 1, "Study overview handles an empty period without fake activity");
        var first = Enumerable.Range(1,12).Select(i => StudyEntry.Create("학습 " + i,"본문\n" + new string('가',9990),"요약 " + i)).ToArray();
        var newer = Enumerable.Range(1,3).Select(i => StudyEntry.Create("다음 날 " + i,"다음 본문",i==1 ? "" : "새 요약")).ToArray();
        var saved = repository.Save(repository.Load(day) with { Studies = first });
        repository.Save(repository.Load(next) with { Studies = newer });
        repository.Save(repository.Load(new(2026,11,1)) with { Studies = [StudyEntry.Create("다른 달")] });
        var report = repository.StudyOverview(DateRange.Month(day));
        check(report.Records == 15 && report.StudyDays == 2 && report.Items.Count == 10 && report.Pages == 2, "Study overview counts records and distinct learning days across pages");
        check(report.Items[0].Id == newer[0].Id && report.Items[2].Id == newer[2].Id && report.Items[3].Id == first[0].Id, "Study list uses latest dates first and preserves daily entry order");
        var page2 = repository.StudyOverview(DateRange.Month(day),1);
        check(page2.Items.Count == 5 && !report.Items.Select(i=>i.Id).Intersect(page2.Items.Select(i=>i.Id)).Any()
            && report.Items.Concat(page2.Items).Select(i=>i.Id).Distinct().Count()==15, "Pagination never duplicates or skips saved study entries");
        check(repository.StudyOverview(DateRange.Week(day)).Records == 12 && repository.StudyOverview(DateRange.Week(next)).Records == 3, "Learning overview respects Sunday/Monday weekly boundaries");
        check(repository.StudyOverview(new(day,day)).Records == 12 && repository.StudyOverview(DateRange.Month(new(2026,11,1))).Records == 1, "Learning overview uses inclusive day and month boundaries");
        check(repository.StudyOverview(DateRange.Month(day),999).Page == 1 && repository.StudyOverview(DateRange.Month(day),0,50).Items.Count == 15, "Study page clamps after changes and respects bounded page size");
        Expect(()=>repository.StudyOverview(DateRange.Month(day),-1),"Negative study pages rejected");
        Expect(()=>repository.StudyOverview(DateRange.Month(day),0,51),"Unbounded study page sizes rejected");
        check(repository.LoadStudy(day,first[0].Id) == first[0] && repository.LoadStudy(next,first[0].Id) is null, "Lazy study body lookup preserves complete text and checks date plus identity");
        var renamed = first[0] with { Title = "수정 제목", Summary = "수정 요약" };
        saved = repository.Save(saved with { Studies = [renamed] });
        var afterEdit = repository.StudyOverview(DateRange.Month(day));
        check(afterEdit.Records == 4 && afterEdit.StudyDays == 2 && afterEdit.Pages == 1 && afterEdit.Items.Single(i=>i.Id==renamed.Id).Summary == "수정 요약", "Study edits and deletions refresh counts, headings and page totals");
        repository.Save(saved with { Studies = [] });
        var afterDelete = new PlanRepository(path).StudyOverview(DateRange.Month(day));
        check(afterDelete.Records == 3 && afterDelete.StudyDays == 1 && repository.LoadStudy(day,renamed.Id) is null, "Deleting a date removes it from learning days and full-body lookup after reopening");
    }
}
