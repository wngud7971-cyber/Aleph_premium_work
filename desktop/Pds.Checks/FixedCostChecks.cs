using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pds.Core;

internal static class FixedCostChecks
{
    public static void Run(string directory,Action<bool,string> check)
    {
        var root=Path.Combine(directory,"fixed-costs");Directory.CreateDirectory(root);
        var path=Path.Combine(root,"records.db");var repo=new PlanRepository(path);
        var jan=new DateOnly(2024,1,31);var feb=new DateOnly(2024,2,29);var mar=new DateOnly(2024,3,31);var apr=new DateOnly(2024,4,30);
        MoneyEntry Cost(string title,long amount,string category) => MoneyEntry.Create(MoneyKind.Expense,amount,category,"자동 반영 시험") with { Title=title,IsFixed=true,RepeatMonthly=true };
        void Expect<T>(Action action,string name)where T:Exception { try{action();}catch(T){check(true,name);return;}throw new InvalidOperationException(name); }
        var phone=Cost("휴대폰 통신비",60000,"통신");var streaming=Cost("넷플릭스 구독비",15000,"구독비");var savings=Cost("청년 적금",300000,"적금·저축");
        var saved=repo.Save(repo.Load(jan) with { Money=[phone,streaming,savings] });
        check(saved.Money.All(e=>e.FixedCostId==e.Id) && repo.LoadFixedCosts().Count==3,"Fixed costs create rules only with explicit day save and stable source IDs");
        check(new PlanRepository(path).Load(jan).Money.SequenceEqual(saved.Money),"Names, fixed flags and recurrence survive database reopen");
        check(repo.ApplyFixedCosts(jan).Added==0 && repo.ApplyFixedCosts(feb.AddDays(-1)).Added==0,"First payment is not duplicated and future due days are excluded");
        check(repo.ApplyFixedCosts(feb,new HashSet<DateOnly>{feb}).Deferred==3 && repo.Load(feb).Money.Count==0,"Unsaved protected dates defer recurring payments without a partial write");
        check(repo.ApplyFixedCosts(feb).Added==3 && repo.Load(feb).Money.Count==3,"January 31 rules use February 29 in a leap year");
        check(repo.ApplyFixedCosts(feb).Added==0 && repo.Load(feb).Money.Count==3,"Repeated launches do not duplicate a monthly occurrence");
        var stale=repo.Load(mar);check(repo.ApplyFixedCosts(mar).Added==3,"App-off catch-up inserts all due monthly payments");
        Expect<SaveConflictException>(()=>repo.Save(stale),"Automatic insertion advances day revision and blocks a stale overwrite");
        check(repo.Load(mar).Money.All(e=>e.FixedCostId is not null && !e.RepeatMonthly && e.Title.Length>0),"Generated entries carry exact names and rule links without creating new rules");
        var march=repo.Load(mar);var marchPhone=march.Money.Single(e=>e.Title==phone.Title);
        repo.Save(march with { Money=march.Money.Where(e=>e.Id!=marchPhone.Id).ToArray() });
        check(new PlanRepository(path).ApplyFixedCosts(mar).Added==0 && repo.Load(mar).Money.Count==2,"Deleting a generated payment retains a durable no-recreation marker");
        var first=repo.Load(jan);repo.Save(first with { Money=first.Money.Select(e=>e.Id==phone.Id?e with { Won=90000,Title="해당 날짜만 수정" }:e).ToArray() });
        check(repo.LoadFixedCosts().Single(r=>r.Id==phone.Id).Template.Won==60000,"Editing a dated payment preserves the already registered future template");
        repo.StopFixedCost(phone.Id);check(repo.LoadFixedCosts().Single(r=>r.Id==phone.Id).Enabled==false && repo.Load(feb).Money.Count==3,"Stopping a rule retains all already saved history");
        check(repo.ApplyFixedCosts(apr).Added==2 && repo.Load(apr).Money.All(e=>e.Title!=phone.Title),"Stopped rule generates no further payments while other rules continue");
        var totals=repo.JournalStatistics(DateRange.Month(apr));
        check(totals.Expense==315000 && totals.Savings==300000 && totals.Consumption==15000 && totals.Balance==-315000,"Monthly outflow includes savings while consumption excludes it");
        check(totals.FixedTotal==315000 && totals.FixedRecords==2 && totals.FixedLines.Any(l=>l.Title=="청년 적금" && l.Saving),"Monthly fixed detail lines contain names, categories, dates and exact amounts");
        check(totals.Categories.Single()==new MoneyCategoryTotal("구독비",15000,1),"Consumption categories omit savings and reconcile to actual consumption");
        check(repo.JournalStatistics(DateRange.Week(apr)).Expense==315000,"Weekly totals include the generated payments on their due date");
        check(repo.ApplyFixedCosts(new(2025,2,28)).Added==20 && repo.Load(new(2025,2,28)).Money.Count==2,"Catch-up handles multiple missed months and non-leap February 28");
        var backup=repo.CreateBackup();var archive=Path.Combine(root,"fixed.zip");BackupArchive.Write(archive,backup,repo.Attachments);
        check(backup.Version==8 && backup.FixedCosts.Count==3 && backup.FixedCostOccurrences.Count>backup.MoneyRecords,"Backup v6 preserves stopped rules and tombstones for deleted payments");
        var target=new PlanRepository(Path.Combine(root,"restored.db"));
        using(var prepared=BackupArchive.Read(archive)) { prepared.Install(target);target.RestoreBackup(prepared.Backup); }
        check(target.LoadFixedCosts().SequenceEqual(repo.LoadFixedCosts()) && target.Load(apr).Money.SequenceEqual(repo.Load(apr).Money),"ZIP restore preserves rule templates, names and historical payments");
        check(target.ApplyFixedCosts(new(2025,2,28)).Added==0 && target.Load(mar).Money.Count==2,"Restored occurrences do not regenerate a deleted payment or duplicate history");
        var invalid=backup with { FixedCostOccurrences=backup.FixedCostOccurrences.Concat([backup.FixedCostOccurrences[0]]).ToArray() };
        Expect<InvalidDataException>(()=>target.RestoreBackup(invalid),"Duplicate recurring month rejects entire restore");
        var wrong=backup with { FixedCosts=[] };
        Expect<InvalidDataException>(()=>target.RestoreBackup(wrong),"Missing recurring rule rejects restore before current records change");
        check(target.Load(apr).Money.Count==2,"Rejected backup leaves current fixed records intact");
        Expect<ArgumentException>(()=>repo.Save(repo.Load(apr) with { Money=[Cost("",1000,"통신")] }),"Fixed cost requires a readable name and preserves saved day on failure");
        Expect<ArgumentException>(()=>(phone with { Kind=MoneyKind.Income,Category="급여" }).Validate(),"Income cannot silently become a recurring fixed expense");
        Expect<ArgumentException>(()=>(phone with { IsFixed=false }).Validate(),"Monthly repeat cannot be enabled without fixed classification");
        var oldRepo=new PlanRepository(Path.Combine(root,"legacy.db"));var oldMoney=MoneyEntry.Create(MoneyKind.Expense,5000,"통신");oldRepo.Save(oldRepo.Load(jan) with { Money=[oldMoney] });
        var legacy=oldRepo.CreateBackup() with { Version=5 };var node=JsonNode.Parse(BackupFile.Serialize(legacy))!.AsObject();
        node.Remove("FixedCosts");node.Remove("FixedCostOccurrences");
        foreach(var day in node["Days"]!.AsArray())foreach(var e in day!["Money"]!.AsArray())
            foreach(var key in new[]{"Title","IsFixed","RepeatMonthly","FixedCostId","IsSaving"})e!.AsObject().Remove(key);
        target.RestoreBackup(BackupFile.Parse(node.ToJsonString()));
        check(target.LoadFixedCosts().Count==0 && target.Load(jan).Money.Single()==oldMoney && target.Load(apr).Money.Count==0,"Legacy v5 restore initializes empty rules and preserves original ordinary money");
        using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=oldRepo.DatabasePath}.ToString()))
        { c.Open();using var q=c.CreateCommand();q.CommandText="DROP TABLE money_details;DROP TABLE fixed_cost_occurrences;DROP TABLE fixed_costs;PRAGMA user_version=9";q.ExecuteNonQuery(); }
        var migrated=new PlanRepository(oldRepo.DatabasePath);
        check(migrated.Load(jan).Money.Single()==oldMoney && migrated.LoadFixedCosts().Count==0,"Schema 9 to 10 migration preserves ordinary money and initializes optional metadata");
    }
}
