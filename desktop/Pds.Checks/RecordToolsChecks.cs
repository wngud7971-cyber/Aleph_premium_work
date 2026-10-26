using Microsoft.Data.Sqlite;
using Pds.Core;

internal static class RecordToolsChecks
{
    public static void Run(string directory,Action<bool,string> check)
    {
        var root=Path.Combine(directory,"record-tools");Directory.CreateDirectory(root);var repo=new PlanRepository(Path.Combine(root,"records.db"));var day=new DateOnly(2026,10,10);
        void Expect<T>(Action action,string name)where T:Exception{try{action();}catch(T){check(true,name);return;}throw new InvalidOperationException(name);}
        var source=Path.Combine(root,"강의자료 첨부.txt");File.WriteAllText(source,"첨부 내용만의 키워드: 검색하지 않음");var file=repo.Attachments.Import(source);
        try
        {
            var plan=PlanEntry.Create("검색시험 계획","메모 속 UNIQUE_SQL ' % _") with {Done=true,ActualDate=day};
            var study=StudyEntry.Create("학습 검색시험","본문 깊은키워드","핵심 요약") with {Attachments=[file]};
            var money=MoneyEntry.Create(MoneyKind.Expense,30000,"통신","메모요금") with {Title="휴대폰 검색시험",IsFixed=true,RepeatMonthly=true};
            var workout=WorkoutEntry.Create("walking",600,null,"저녁 산책");var review=new DayReview("검토키워드","","다음");
            var first=repo.Save(repo.Load(day) with {Plans=[plan],Studies=[study],Money=[money],Workouts=[workout],Review=review});
            check(repo.SearchRecords("검색시험").Total==3,"Unified search covers plan, study and named money on their stored date");
            check(repo.SearchRecords("깊은키워드").Items.Single().Id==study.Id && repo.SearchRecords("강의자료").Items.Single().Id==study.Id,"Study body and attachment filenames are searchable without reading file content");
            check(repo.SearchRecords("검색하지 않음").Total==0,"Unified search never analyzes attachment contents");
            check(repo.SearchRecords("unique_sql").Total==1 && repo.SearchRecords("% _").Total==1 && repo.SearchRecords("' OR 1=1 --").Total==0,"Search treats quotes and wildcard-looking characters literally with bound parameters");
            check(repo.SearchRecords("검색시험","money",day,day).Total==1 && repo.SearchRecords("검색시험",from:day.AddDays(1)).Total==0,"Record type and inclusive date filters isolate results");
            check(repo.SearchRecords("걷기","workout").Total==1 && repo.SearchRecords("검토키워드","review").Total==1,"Korean exercise names and review fields are included");
            var next=day.AddDays(1);repo.Save(repo.Load(next) with {Plans=Enumerable.Range(0,25).Select(i=>PlanEntry.Create("페이지 시험 "+i)).ToArray()});
            var p1=repo.SearchRecords("페이지 시험");var p2=repo.SearchRecords("페이지 시험",page:1);check(p1.Total==25 && p1.Items.Count==20 && p2.Items.Count==5 && !p1.Items.Select(i=>i.Id).Intersect(p2.Items.Select(i=>i.Id)).Any(),"Bounded search pages contain no duplicates or missing rows");
            check(repo.SearchRecords("없는키워드",page:999).Page==0 && repo.SearchRecords("페이지 시험",page:999).Page==1,"Empty results and page clamping remain operable");
            Expect<ArgumentException>(()=>repo.SearchRecords(" "),"Empty search preserves stored data");
            Expect<ArgumentException>(()=>repo.SearchRecords("a",from:next,to:day),"Reversed search dates are rejected");
            repo.SaveBudget(new(new(2026,10,1),20000));var budget=repo.BudgetFor(day);
            check(budget.Budget==20000 && budget.Consumption==30000 && budget.Remaining==-10000 && budget.Percent==150,"Budget reports an exact overrun without clipping the numeric percent");
            var saving=MoneyEntry.Create(MoneyKind.Expense,100000,"적금·저축");repo.Save(repo.Load(day) with {Money=first.Money.Append(saving).ToArray()});
            check(repo.BudgetFor(day).Consumption==30000 && repo.BudgetFor(day).Savings==100000,"Savings never consumes the consumption budget");
            check(repo.BudgetFor(new(2026,11,1)).Budget is null && repo.BudgetFor(new(2026,11,1)).Percent is null,"No implicit budget inheritance or divide-by-zero for an unset month");
            repo.SaveBudget(new(new(2026,10,1),40000));check(repo.BudgetFor(day).Remaining==10000,"Budget updates replace only the selected month");
            Expect<ArgumentException>(()=>repo.SaveBudget(new(new(2026,10,1),0)),"Zero budget is rejected without overwriting a valid value");
            var digest=repo.MonthDigest(day);check(digest.Activity.Done==1 && digest.Activity.Workout.Seconds==600 && digest.Studies==1 && digest.StudyDays==1 && digest.Money.Savings==100000 && digest.Budget.Budget==40000,"Monthly digest reconciles existing plan, workout, study, finance and budget reports");
            repo.RemoveBudget(day);check(repo.BudgetFor(day).Budget is null && repo.Load(day).Money.Count==2,"Clearing a budget leaves all underlying finance records");repo.SaveBudget(new(new(2026,10,1),40000));
            var rule=repo.LoadFixedCosts().Single();var template=rule.Template with {Title="새 요금",Won=50000};
            var changed=repo.ChangeFixedCost(rule.Id,rule.Revision,new(new(2026,11,1),template),new(2026,12,1),new(2026,10,18));
            check(changed.Revision==rule.Revision+1 && repo.Load(day).Money.Single(m=>m.Id==money.Id).Won==30000,"Scheduling a future change advances revision and leaves old payments untouched");
            Expect<SaveConflictException>(()=>repo.ChangeFixedCost(rule.Id,rule.Revision,null,null,new(2026,10,18)),"Stale recurring-rule editor cannot overwrite a newer schedule");
            Expect<ArgumentException>(()=>repo.ChangeFixedCost(rule.Id,changed.Revision,new(new(2026,10,1),template),null,new(2026,10,18)),"Applying a recurring change retroactively is rejected");
            check(repo.ApplyFixedCosts(new(2027,1,31)).Added==2 && repo.Load(new(2026,11,10)).Money.Single().Title=="새 요금" && repo.Load(new(2026,12,10)).Money.Single().Won==50000 && repo.Load(new(2027,1,10)).Money.Count==0,"Changed template applies from November through the inclusive December end month");
            Expect<ArgumentException>(()=>repo.ChangeFixedCost(rule.Id,changed.Revision,null,null,new(2027,1,1)),"Expired schedules cannot silently backfill a skipped past interval by reopening");
            var current=repo.Load(day);repo.Save(current with {Plans=[],Studies=[],Money=[],Workouts=[],Review=null});var trash=repo.DeletedRecords();
            check(trash.Total==6 && File.Exists(repo.Attachments.PathFor(file)),"Saving deletions moves all five record kinds and study attachments into durable trash");
            check(repo.SearchRecords("검색시험").Total==0 && repo.MonthDigest(day).Studies==0,"Deleted records are excluded from search and statistics");
            var trashedStudy=trash.Items.Single(t=>t.Study is not null);repo.RestoreDeleted(trashedStudy.Id);
            check(repo.Load(day).Studies.Single()==study && repo.Attachments.Require(file,true)==repo.Attachments.PathFor(file) && repo.DeletedRecords().Total==5,"Individual study restore preserves attachment bytes and removes only its trash item");
            var trashedPlan=repo.DeletedRecords().Items.Single(t=>t.Plan is not null);repo.RestoreDeleted(trashedPlan.Id);
            check(repo.Load(day).Plans.Single()==plan && repo.LoadPlanHistory(plan.Id).Origin==trashedPlan.Origin,"Plan recovery preserves its original identity, completion and origin metadata");
            var recurringTrash=repo.DeletedRecords().Items.Single(t=>t.Money?.FixedCostId is not null);repo.RestoreDeleted(recurringTrash.Id);
            check(repo.Load(day).Money.Count==1 && repo.ApplyFixedCosts(new(2027,1,31)).Added==0,"Recurring payment recovery keeps tombstones and never duplicates generated months");
            var reviewTrash=repo.DeletedRecords().Items.Single(t=>t.Review is not null);repo.Save(repo.Load(day) with {Review=new("새 회고","","")});
            Expect<ArgumentException>(()=>repo.RestoreDeleted(reviewTrash.Id),"Recovery refuses to overwrite an existing review");
            check(repo.DeletedRecords().Items.Any(i=>i.Id==reviewTrash.Id),"A failed recovery keeps its trash entry intact");
            var studyAgain=repo.Save(repo.Load(day) with {Studies=[]});var studyTrash=repo.DeletedRecords().Items.Single(t=>t.Study is not null);
            var backup=repo.CreateBackup();var zip=Path.Combine(root,"tools.zip");BackupArchive.Write(zip,backup,repo.Attachments);
            check(backup.Version==8 && backup.Deleted.Count>=3 && backup.Budgets.Count==1 && backup.Attachments==1,"Backup v7 includes budgets, rule changes, trash and trash-only file payloads");
            var target=new PlanRepository(Path.Combine(root,"restored","records.db"));using(var prepared=BackupArchive.Read(zip)){prepared.Install(target);target.RestoreBackup(prepared.Backup);}
            check(target.BudgetFor(day).Budget==40000 && target.LoadFixedCosts().Single()==changed && target.DeletedRecords().Total==backup.Deleted.Count,"ZIP restore round-trips budget, versioned rules and trash together");
            var copied=target.DeletedRecords().Items.Single(i=>i.Id==studyTrash.Id);target.RestoreDeleted(copied.Id);
            check(target.Load(day).Studies.Single()==study && File.ReadAllText(target.Attachments.Require(file,true))==File.ReadAllText(source),"Trash-only attachments can be recovered after backup on a different data folder");
            repo.PermanentlyDelete(studyTrash.Id);check(!File.Exists(repo.Attachments.PathFor(file)) && repo.Load(day).Plans.Count==1,"Permanent deletion cleans an unreferenced file while preserving other records");
            Expect<ArgumentException>(()=>repo.RestoreDeleted(studyTrash.Id),"A permanently deleted item cannot be restored");
            var invalid=backup with {Budgets=[new(new(2026,10,1),-1)]};Expect<ArgumentException>(()=>target.RestoreBackup(invalid),"Malformed budget backup fails before replacing live records");
            var dup=backup with {Deleted=backup.Deleted.Append(backup.Deleted[0]).ToArray()};Expect<InvalidDataException>(()=>target.RestoreBackup(dup),"Duplicate trash IDs are rejected before a full restore");
            var reopened=new PlanRepository(target.DatabasePath);check(reopened.Load(day).Studies.Single()==study && reopened.BudgetFor(day).Budget==40000,"Budget and individual recoveries survive repository reopen");
            var legacy=new PlanRepository(Path.Combine(root,"legacy.db"));legacy.Save(legacy.Load(day) with {Money=[saving],Studies=[StudyEntry.Create("이전 학습")]});
            var old=legacy.CreateBackup() with {Version=6};target.RestoreBackup(BackupFile.Parse(BackupFile.Serialize(old)));
            check(target.DeletedRecords().Total==0 && target.BudgetFor(day).Budget is null && target.Load(day).Money.Single()==saving,"Legacy v6 backup initializes empty budgets/trash while retaining old records");
            using(var c=new SqliteConnection($"Data Source={legacy.DatabasePath};Pooling=False")){c.Open();using var q=c.CreateCommand();q.CommandText="DROP TABLE deleted_attachments;DROP TABLE deleted_records;DROP TABLE monthly_budgets;PRAGMA user_version=10";q.ExecuteNonQuery();}
            var migrated=new PlanRepository(legacy.DatabasePath);check(migrated.Load(day).Studies.Count==1 && migrated.DeletedRecords().Total==0 && migrated.BudgetFor(day).Budget is null,"Schema 10 to 11 migration preserves finance/study without inventing trash or budgets");
            var history=new PlanRepository(Path.Combine(root,"history","records.db"),new FrozenToolsClock());var original=PlanEntry.Create("이월 이력 복구");history.Save(history.Load(day) with {Plans=[original]});
            history.Rollover(new DateTimeOffset(2026,10,10,23,5,0,TimeSpan.Zero),zone:TimeZoneInfo.Utc);var moved=history.Load(day.AddDays(1));var moves=history.LoadPlanHistory(original.Id);
            history.Save(moved with {Plans=[]});history.RestoreDeleted(history.DeletedRecords().Items.Single().Id);
            check(history.LoadPlanHistory(original.Id).Origin==moves.Origin && history.LoadPlanHistory(original.Id).Moves.SequenceEqual(moves.Moves) && moves.Moves.Count==1,"Individual plan recovery preserves original rollover history rather than creating a new plan");
            var energyRepo=new PlanRepository(Path.Combine(root,"historic-energy","records.db"));var now=DateOnly.FromDateTime(DateTime.Today);var weight=energyRepo.SaveBodyWeight(70m,now);var input=new EnergyInput("simple","17190",null,70m,true);
            var walk=WorkoutEntry.Create("walking",1800,null) with {Calculation=input};walk=walk with {Energy=WorkoutEnergy.Calculate(walk,input,weight,now).Snapshot};energyRepo.Save(energyRepo.Load(now) with {Workouts=[walk]});energyRepo.Save(energyRepo.Load(now) with {Workouts=[]});energyRepo.SaveBodyWeight(70m,now.AddDays(-20));
            energyRepo.RestoreDeleted(energyRepo.DeletedRecords().Items.Single().Id);check(energyRepo.Load(now).Workouts.Single()==walk,"Recovering a historical workout preserves its kcal snapshot even after weight expiry");
            energyRepo.Save(energyRepo.Load(now) with {Workouts=[]});var energyBackup=energyRepo.CreateBackup();
            var badEnergy=energyBackup with {Deleted=energyBackup.Deleted.Select(i=>i with {Workout=i.Workout! with {Energy=i.Workout!.Energy! with {Code="unknown"}}}).ToArray()};
            Expect<ArgumentException>(()=>BackupFile.Serialize(badEnergy),"Invalid historical energy metadata in trash cannot enter a backup");
        }
        finally
        {
            foreach(var folder in new[]{repo.Attachments.Root,Path.Combine(root,"restored","attachments")})if(Directory.Exists(folder))foreach(var path in Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories))File.SetAttributes(path,FileAttributes.Normal);
        }
    }
    private sealed class FrozenToolsClock : TimeProvider
    { public override DateTimeOffset GetUtcNow()=>new(2026,10,10,12,0,0,TimeSpan.Zero); }
}
