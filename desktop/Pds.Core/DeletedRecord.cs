using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace Pds.Core;

public sealed record DeletedRecord(Guid Id,DateOnly Date,DateTimeOffset DeletedUtc)
{
    public PlanEntry? Plan { get; init; }
    public WorkoutEntry? Workout { get; init; }
    public MoneyEntry? Money { get; init; }
    public StudyEntry? Study { get; init; }
    public DayReview? Review { get; init; }
    public PlanOrigin? Origin { get; init; }
    public IReadOnlyList<PlanMove> Moves { get; init; }=[];
    [JsonIgnore] public string Kind=>Plan is not null?"plan":Workout is not null?"workout":Money is not null?"money":Study is not null?"study":"review";
    [JsonIgnore] public Guid RecordId=>Plan?.Id??Workout?.Id??Money?.Id??Study?.Id??Guid.Empty;
    [JsonIgnore] public string Title=>Plan?.Title??(Workout is not null?ExerciseCatalog.Get(Workout.ExerciseId).Name:null)??(Money is not null?(Money.Title.Length>0?Money.Title:Money.Category):null)??Study?.Title??"하루 회고";
    [JsonIgnore] public IReadOnlyList<StudyAttachment> Files=>Study?.Attachments??[];
    public void Validate()
    {
        if(Id==Guid.Empty || new[]{Plan is not null,Workout is not null,Money is not null,Study is not null,Review is not null}.Count(v=>v)!=1 || Moves is null || Moves.Count>36600)
            throw new ArgumentException("휴지통 항목이 올바르지 않아요.");
        Plan?.Validate();Workout?.Validate();Money?.Validate();Study?.Validate();Review?.Validate();
        if(Review is not null && Review.Normalize()!=Review)throw new ArgumentException("삭제한 회고 형식이 올바르지 않아요.");
        foreach(var move in Moves)PlanEntry.Create("시간 확인",start:move.PreviousStart,end:move.PreviousEnd).Validate();
        if(Plan is null && (Origin is not null || Moves.Count>0) || Plan is not null && (Origin is null || Origin.PlanId!=Plan.Id)
            || Moves.Any(m=>m.PlanId!=Plan?.Id || m.To.DayNumber-m.From.DayNumber<m.MissedCutoffs || m.MissedCutoffs<1 || m.To.DayNumber-m.CutoffDate.DayNumber!=1 || m.Reason is not("23:00" or "catch-up"))
            || Moves.Select(m=>m.CutoffDate).Distinct().Count()!=Moves.Count)throw new ArgumentException("삭제한 계획의 이력이 올바르지 않아요.");
        if(Workout?.Energy is EnergySnapshot energy)
        {
            var preset=EnergyCatalog.Resolve(Workout.ExerciseId,Workout.Calculation!);
            if(preset is null || preset.Code!=energy.Code || preset.Met!=energy.Met || preset.Label!=energy.Label || preset.SourceUrl!=energy.SourceUrl || energy.SourceVersion!=EnergyCatalog.Version)
                throw new ArgumentException("삭제한 운동의 kcal 기준이 올바르지 않아요.");
        }
    }
}
public sealed record DeletedPage(int Total,int Page,IReadOnlyList<DeletedRecord> Items)
{
    public int Pages=>Math.Max(1,(Total+19)/20);
}

public sealed partial class PlanRepository
{
    private static IReadOnlyList<DeletedRecord> ReadDeleted(SqliteConnection connection,SqliteTransaction transaction)
    {
        using var c=connection.CreateCommand();c.Transaction=transaction;c.CommandText="SELECT json FROM deleted_records ORDER BY deleted_utc DESC,id";
        var result=new List<DeletedRecord>();using var reader=c.ExecuteReader();while(reader.Read())result.Add(JsonSerializer.Deserialize<DeletedRecord>(reader.GetString(0))!);return result;
    }
    private static void InsertDeleted(SqliteConnection connection,SqliteTransaction transaction,DeletedRecord entry)
    {
        entry.Validate();using var c=connection.CreateCommand();c.Transaction=transaction;
        c.CommandText="SELECT COUNT(*) FROM deleted_records";if(Convert.ToInt32(c.ExecuteScalar())>=10000)throw new ArgumentException("휴지통이 10,000건에 도달했어요. 필요 없는 항목을 영구 삭제한 뒤 저장해 주세요.");
        c.CommandText="INSERT INTO deleted_records(id,date,deleted_utc,json) VALUES($id,$date,$time,$json)";
        c.Parameters.AddWithValue("$id",entry.Id.ToString());c.Parameters.AddWithValue("$date",Key(entry.Date));c.Parameters.AddWithValue("$time",entry.DeletedUtc.ToString("O"));c.Parameters.AddWithValue("$json",JsonSerializer.Serialize(entry));c.ExecuteNonQuery();
        foreach(var file in entry.Files)
        {
            using var a=connection.CreateCommand();a.Transaction=transaction;a.CommandText="INSERT INTO deleted_attachments(trash_id,file_id,name,length,sha256) VALUES($trash,$id,$name,$length,$sha)";
            a.Parameters.AddWithValue("$trash",entry.Id.ToString());a.Parameters.AddWithValue("$id",file.Id.ToString());a.Parameters.AddWithValue("$name",file.Name);a.Parameters.AddWithValue("$length",file.Length);a.Parameters.AddWithValue("$sha",file.Sha256);a.ExecuteNonQuery();
        }
    }
    private void CaptureRemoved(SqliteConnection connection,SqliteTransaction transaction,DaySnapshot next)
    {
        var old=ReadDay(connection,transaction,next.Date);
        DeletedRecord Item()=>new(Guid.NewGuid(),next.Date,clock.GetUtcNow());
        foreach(var p in old.Plans.Where(p=>next.Plans.All(n=>n.Id!=p.Id)))
            InsertDeleted(connection,transaction,Item() with { Plan=p,Origin=ReadOrigins(connection,transaction).Single(o=>o.PlanId==p.Id),Moves=ReadMoves(connection,transaction).Where(m=>m.PlanId==p.Id).ToArray() });
        foreach(var w in old.Workouts.Where(p=>next.Workouts.All(n=>n.Id!=p.Id)))InsertDeleted(connection,transaction,Item() with {Workout=w});
        foreach(var m in old.Money.Where(p=>next.Money.All(n=>n.Id!=p.Id)))InsertDeleted(connection,transaction,Item() with {Money=m});
        foreach(var s in old.Studies.Where(p=>next.Studies.All(n=>n.Id!=p.Id)))InsertDeleted(connection,transaction,Item() with {Study=s});
        if(old.Review is not null && next.Review?.Normalize() is null)InsertDeleted(connection,transaction,Item() with {Review=old.Review});
    }
    public DeletedPage DeletedRecords(int page=0)
    {
        if(page<0)throw new ArgumentException("휴지통 페이지를 확인해 주세요.");
        using var connection=Open();using var transaction=connection.BeginTransaction(deferred:true);using var c=connection.CreateCommand();c.Transaction=transaction;
        c.CommandText="SELECT COUNT(*) FROM deleted_records";var count=Convert.ToInt32(c.ExecuteScalar());page=Math.Min(page,Math.Max(0,(count-1)/20));
        c.CommandText="SELECT json FROM deleted_records ORDER BY deleted_utc DESC,id LIMIT 20 OFFSET $offset";c.Parameters.AddWithValue("$offset",page*20);
        var rows=new List<DeletedRecord>();using(var reader=c.ExecuteReader())while(reader.Read())rows.Add(JsonSerializer.Deserialize<DeletedRecord>(reader.GetString(0))!);
        transaction.Commit();return new(count,page,rows);
    }
    private DeletedRecord GetDeleted(Guid id)
    {
        using var connection=Open();using var c=connection.CreateCommand();c.CommandText="SELECT json FROM deleted_records WHERE id=$id";c.Parameters.AddWithValue("$id",id.ToString());
        var json=c.ExecuteScalar() as string ?? throw new ArgumentException("이미 복구하거나 삭제한 항목이에요.");return JsonSerializer.Deserialize<DeletedRecord>(json)!;
    }
    public DaySnapshot RestoreDeleted(Guid id)
    {
        var item=GetDeleted(id);item.Validate();foreach(var file in item.Files)Attachments.Require(file,true);
        var day=Load(item.Date);
        if(item.Plan is not null)day=day with {Plans=day.Plans.Append(item.Plan).ToArray()};
        if(item.Workout is not null)day=day with {Workouts=day.Workouts.Append(item.Workout).ToArray()};
        if(item.Money is not null)day=day with {Money=day.Money.Append(item.Money).ToArray()};
        if(item.Study is not null)day=day with {Studies=day.Studies.Append(item.Study).ToArray()};
        if(item.Review is not null) { if(day.Review is not null)throw new ArgumentException("그 날짜에 회고가 있어요. 현재 회고를 유지하기 위해 복구를 중단했어요.");day=day with {Review=item.Review}; }
        return SaveCore(day,item);
    }
    private static void CompleteDeletedRestore(SqliteConnection connection,SqliteTransaction transaction,DeletedRecord item)
    {
        using var c=connection.CreateCommand();c.Transaction=transaction;
        if(item.Plan is not null)
        {
            c.CommandText="UPDATE plan_metadata SET original_date=$original,created_utc=$created WHERE plan_id=$plan";
            c.Parameters.AddWithValue("$original",Key(item.Origin!.OriginalDate));c.Parameters.AddWithValue("$created",item.Origin.CreatedUtc is DateTimeOffset created?created.ToString("O"):DBNull.Value);c.Parameters.AddWithValue("$plan",item.Plan.Id.ToString());c.ExecuteNonQuery();
            foreach(var m in item.Moves)
            {
                using var move=connection.CreateCommand();move.Transaction=transaction;move.CommandText="INSERT INTO plan_rollovers(plan_id,from_date,to_date,cutoff_date,processed_utc,previous_start,previous_end,reason,missed_cutoffs) VALUES($id,$from,$to,$cutoff,$time,$start,$end,$reason,$missed)";
                foreach(var (name,value) in new(string,object?)[]{("$id",m.PlanId.ToString()),("$from",Key(m.From)),("$to",Key(m.To)),("$cutoff",Key(m.CutoffDate)),("$time",m.ProcessedUtc.ToString("O")),("$start",m.PreviousStart),("$end",m.PreviousEnd),("$reason",m.Reason),("$missed",m.MissedCutoffs)})move.Parameters.AddWithValue(name,value??DBNull.Value);
                move.ExecuteNonQuery();
            }
        }
        c.CommandText="DELETE FROM deleted_records WHERE id=$id";c.Parameters.AddWithValue("$id",item.Id.ToString());c.ExecuteNonQuery();
    }
    public void PermanentlyDelete(Guid id)
    {
        using var connection=Open();using var transaction=connection.BeginTransaction();using var c=connection.CreateCommand();c.Transaction=transaction;c.CommandText="SELECT json FROM deleted_records WHERE id=$id";c.Parameters.AddWithValue("$id",id.ToString());
        var json=c.ExecuteScalar() as string ?? throw new ArgumentException("이미 삭제한 항목이에요.");var item=JsonSerializer.Deserialize<DeletedRecord>(json)!;
        c.CommandText="DELETE FROM deleted_records WHERE id=$id";c.ExecuteNonQuery();transaction.Commit();foreach(var file in item.Files)DiscardAttachmentIfUnused(file);
    }
}
