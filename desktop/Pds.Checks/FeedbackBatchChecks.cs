using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pds.Core;

internal static class FeedbackBatchChecks
{
    public static void Run(string directory,Action<bool,string> check)
    {
        void Reject<T>(Action action,string name)where T:Exception
        {try{action();}catch(T){check(true,name);return;}throw new InvalidOperationException(name);}
        check(StudyEntry.ParseDuration("","") is null && StudyEntry.ParseDuration(" "," ") is null,"Study duration blank stays unknown");
        check(StudyEntry.ParseDuration("1","30")==90 && StudyEntry.ParseDuration("","5")==5 && StudyEntry.ParseDuration("24","0")==1440,"Study duration accepts optional parts and daily upper boundary");
        foreach(var input in new[]{("0","0"),("-1","10"),("1","60"),("24","1"),("25",""),("1.5",""),("1e2",""),("999999999999",""),("","분")})
            Reject<ArgumentException>(()=>StudyEntry.ParseDuration(input.Item1,input.Item2),"Invalid study duration rejects "+input);
        var root=Path.Combine(directory,"feedback-batch");Directory.CreateDirectory(root);
        var path=Path.Combine(root,"records.db");var repo=new PlanRepository(path);var date=new DateOnly(2026,9,16);
        var study=StudyEntry.Create("학습 시간 검사","본문","요약") with {DurationMinutes=90};
        var unknown=StudyEntry.Create("기존 미입력");
        var filePath=Path.Combine(root,"첨부.txt");File.WriteAllText(filePath,"보존할 첨부");var file=repo.Attachments.Import(filePath);study=study with{Attachments=[file]};
        var plan=PlanEntry.Create("다른 달에 실제 완료") with{Done=true,ActualDate=new(2026,10,1)};
        var saved=repo.Save(repo.Load(date) with{Studies=[study,unknown],Plans=[plan,PlanEntry.Create("미완료")],Workouts=[WorkoutEntry.Create("walking",600,null,"")]});
        check(new PlanRepository(path).Load(date).Studies.SequenceEqual(saved.Studies),"Study duration and attached file survive reopening");
        var overview=repo.StudyOverview(new(date,date));
        check(overview.Minutes==90 && overview.TimedRecords==1 && overview.Records==2 && overview.Items.Single(s=>s.Id==study.Id).DurationMinutes==90 && repo.LoadStudy(date,study.Id)==study,"Study overview and lazy detail preserve nullable duration");
        check(study!=study with{DurationMinutes=91} && study==study with{Attachments=study.Attachments.ToArray()},"Study equality includes duration and attachment value equality");
        foreach(var minutes in new[]{0,-1,1441})Reject<ArgumentException>(()=>repo.Save(saved with{Studies=[study with{DurationMinutes=minutes}]}),"Study invalid stored duration rejects atomically "+minutes);
        check(repo.Load(date).Revision==saved.Revision && repo.Load(date).Studies[0].DurationMinutes==90,"Rejected time edits preserve day revision and records");
        var edited=repo.Save(saved with{Studies=[study with{DurationMinutes=125},unknown]});
        check(edited.Studies.Count==2 && edited.Studies[0].Id==study.Id && repo.Load(date).Studies[0].DurationMinutes==125,"Duration edit preserves record ID without duplication");
        var annual=repo.AnnualActivity(2026);var september=annual.Months[8];
        check(annual.Months.Count==12 && annual.Months.Select(m=>m.Month).SequenceEqual(Enumerable.Range(1,12)),"Annual report always has January through December");
        check(september.Plans==2 && september.Done==1 && september.Value(ActivityMetric.PlanRate)==50 && annual.Months[9].Done==0,"Annual plan basis matches assigned-date monthly digest even for another actual completion month");
        check(september.Workouts==1 && september.WorkoutSeconds==600 && september.Value(ActivityMetric.WorkoutMinutes)==10 && september.Studies==2 && september.TimedStudies==1 && september.StudyMinutes==125,"Annual exercise and study aggregates keep independent units and coverage");
        repo.Save(repo.Load(new(2026,10,2))with{Studies=[StudyEntry.Create("시간 없음")],Workouts=[WorkoutEntry.Create("pushups",null,10,"")]});
        var october=repo.AnnualActivity(2026).Months[9];
        check(october.Value(ActivityMetric.StudyMinutes) is null && october.Value(ActivityMetric.WorkoutMinutes) is null,"Annual time-only unknown records stay missing rather than fabricated zero");
        check(annual.Months[0].Value(ActivityMetric.PlanRate) is null && annual.Months[0].Value(ActivityMetric.StudyRecords)==0,"No plans has no completion percentage; actual empty counts remain zero");
        var jan=new DateOnly(2025,1,1);var dec=new DateOnly(2025,12,31);
        repo.Save(repo.Load(jan)with{Studies=[StudyEntry.Create("1월")with{DurationMinutes=1}]});repo.Save(repo.Load(dec)with{Studies=[StudyEntry.Create("12월")with{DurationMinutes=1440}]});
        var previous=repo.AnnualActivity(2025);check(previous.Months[0].StudyMinutes==1 && previous.Months[11].StudyMinutes==1440 && previous.Months.Sum(m=>m.Studies)==2,"Annual Jan 1 / Dec 31 inclusive boundaries exclude adjacent years");
        check(repo.AnnualActivity(1).Months.Count==12 && repo.AnnualActivity(9999).Months.Count==12,"Annual extreme years do not overflow");
        Reject<ArgumentException>(()=>repo.AnnualActivity(0),"Annual invalid year rejects");
        var digest=repo.MonthDigest(date);check(digest.StudyMinutes==125 && digest.TimedStudies==1 && digest.Activity.Done==september.Done,"Monthly digest agrees with annual time and plan aggregation");
        var backup=repo.CreateBackup();check(backup.Version==8,"Duration backup uses version eight");
        var zip=Path.Combine(root,"backup.zip");BackupArchive.Write(zip,backup,repo.Attachments);
        var target=new PlanRepository(Path.Combine(root,"restored","records.db"));using(var prepared=BackupArchive.Read(zip)){prepared.Install(target);target.RestoreBackup(prepared.Backup);}
        check(target.Load(date).Studies.SequenceEqual(edited.Studies) && File.ReadAllText(target.Attachments.Require(file,true))=="보존할 첨부","ZIP restore preserves duration, unknown state and copied attachment bytes");
        Reject<InvalidDataException>(()=>BackupFile.Serialize(backup with{Version=7}),"Duration backup cannot be mislabeled as version seven");
        repo.Save(repo.Load(date)with{Studies=[unknown]});var trashed=repo.DeletedRecords().Items.Single(d=>d.Study?.Id==study.Id);
        check(trashed.Study?.DurationMinutes==125 && trashed.Files.Count==1,"Trash preserves removed study duration and attachment");repo.RestoreDeleted(trashed.Id);
        check(repo.Load(date).Studies.Single(s=>s.Id==study.Id).DurationMinutes==125,"Trash restoration preserves edited study time");
        repo.Save(repo.Load(date)with{Studies=[unknown]});var trashBackup=repo.CreateBackup();
        Reject<InvalidDataException>(()=>BackupFile.Serialize(trashBackup with{Version=7}),"Older backup format cannot carry hidden trash duration");
        // A real v11 fixture: remove the newly added column and mark the original schema.
        var oldPath=Path.Combine(root,"legacy.db");var legacy=new PlanRepository(oldPath);legacy.Save(legacy.Load(date)with{Studies=[unknown],Plans=[PlanEntry.Create("기존 계획")]});
        using(var connection=new SqliteConnection($"Data Source={oldPath};Pooling=False"))
        {connection.Open();using var c=connection.CreateCommand();c.CommandText="ALTER TABLE study_entries DROP COLUMN duration_minutes;PRAGMA user_version=11";c.ExecuteNonQuery();}
        var migrated=new PlanRepository(oldPath);check(migrated.Load(date).Studies.Single().DurationMinutes is null && migrated.Load(date).Plans.Single().Title=="기존 계획","Real schema eleven migrates without changing existing study or plan records");
        check(new PlanRepository(oldPath).Load(date).Studies.Single()==unknown,"Schema migration is idempotent");
        var old=migrated.CreateBackup()with{Version=7};var node=JsonNode.Parse(BackupFile.Serialize(old))!;
        foreach(var day in node["Days"]!.AsArray())foreach(var entry in day!["Studies"]!.AsArray())entry!.AsObject().Remove("DurationMinutes");
        target.RestoreBackup(BackupFile.Parse(node.ToJsonString()));check(target.Load(date).Studies.Single().DurationMinutes is null,"Legacy version seven missing duration restores as unknown");
    }
}
