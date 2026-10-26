using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Pds.Core;

public sealed record FixedCostRule(Guid Id, DateOnly FirstDate, MoneyEntry Template, bool Enabled)
{
    public long Revision { get; init; }
    public DateOnly? EndMonth { get; init; }
    public IReadOnlyList<FixedCostChange> Changes { get; init; } = [];
    public bool Equals(FixedCostRule? other)=>other is not null && Id==other.Id && FirstDate==other.FirstDate && Template==other.Template && Enabled==other.Enabled && Revision==other.Revision && EndMonth==other.EndMonth && Changes.SequenceEqual(other.Changes);
    public override int GetHashCode()=>HashCode.Combine(Id,FirstDate,Template,Enabled,Revision,EndMonth);
    public MoneyEntry TemplateIn(DateOnly month)=>Changes.Where(c=>c.Month<=month).OrderBy(c=>c.Month).LastOrDefault()?.Template ?? Template;
    public void Validate()
    {
        Template.Validate();
        if (Id == Guid.Empty || Template.Id!=Id || FirstDate.Year < 1900 || !Template.IsFixed || Template.Kind != MoneyKind.Expense
            || Template.RepeatMonthly || Template.FixedCostId is not null)
            throw new ArgumentException("매월 고정 지출의 이름·날짜·금액을 확인해 주세요.");
        if(Revision<0 || Revision==long.MaxValue || EndMonth is DateOnly end && (end.Day!=1 || end<new DateOnly(FirstDate.Year,FirstDate.Month,1))
            || Changes is null || Changes.Count>1000 || Changes.Select(c=>c.Month).Distinct().Count()!=Changes.Count)
            throw new ArgumentException("반복 변경 월과 종료 월을 확인해 주세요.");
        foreach(var change in Changes)
        {
            change.Template.Validate();
            if(change.Month.Day!=1 || change.Month<=new DateOnly(FirstDate.Year,FirstDate.Month,1) || change.Template.Id!=Id || !change.Template.IsFixed || change.Template.Kind!=MoneyKind.Expense || change.Template.RepeatMonthly || change.Template.FixedCostId is not null)
                throw new ArgumentException("반복 변경 내역을 확인해 주세요.");
        }
    }
    public DateOnly DueIn(DateOnly month) => new(month.Year, month.Month, Math.Min(FirstDate.Day, DateTime.DaysInMonth(month.Year, month.Month)));
}
public sealed record FixedCostChange(DateOnly Month,MoneyEntry Template);
public sealed record FixedCostOccurrence(Guid RuleId, DateOnly Month, Guid EntryId);
public sealed record FixedCostApplication(int Added, int Deferred);
public sealed record FixedCostLine(DateOnly Date, string Title, string Category, long Won, bool Saving);

public sealed partial class PlanRepository
{
    private static IReadOnlyList<FixedCostRule> ReadFixedCosts(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var c=connection.CreateCommand(); c.Transaction=transaction; c.CommandText="SELECT json FROM fixed_costs ORDER BY id";
        var result=new List<FixedCostRule>(); using var reader=c.ExecuteReader();
        while(reader.Read()) result.Add(JsonSerializer.Deserialize<FixedCostRule>(reader.GetString(0))!);
        return result;
    }
    private static IReadOnlyList<FixedCostOccurrence> ReadFixedOccurrences(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var c=connection.CreateCommand(); c.Transaction=transaction; c.CommandText="SELECT rule_id,month,entry_id FROM fixed_cost_occurrences ORDER BY rule_id,month";
        var result=new List<FixedCostOccurrence>(); using var reader=c.ExecuteReader();
        while(reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)),DateOnly.ParseExact(reader.GetString(1),"yyyy-MM-dd",CultureInfo.InvariantCulture),Guid.Parse(reader.GetString(2))));
        return result;
    }
    public IReadOnlyList<FixedCostRule> LoadFixedCosts()
    {
        using var connection=Open();using var transaction=connection.BeginTransaction(deferred:true);
        var result=ReadFixedCosts(connection,transaction);transaction.Commit();return result;
    }
    public void StopFixedCost(Guid id)
    {
        using var connection=Open();using var transaction=connection.BeginTransaction();
        var rule=ReadFixedCosts(connection,transaction).SingleOrDefault(r=>r.Id==id) ?? throw new ArgumentException("반복 지출을 찾지 못했어요.");
        WriteFixedRule(connection,transaction,rule with { Enabled=false,Revision=rule.Revision+1 });transaction.Commit();
    }
    public FixedCostRule ChangeFixedCost(Guid id,long expectedRevision,FixedCostChange? change,DateOnly? endMonth,DateOnly today)
    {
        using var connection=Open();using var transaction=connection.BeginTransaction();
        var rule=ReadFixedCosts(connection,transaction).SingleOrDefault(r=>r.Id==id) ?? throw new ArgumentException("반복 지출을 찾지 못했어요.");
        if(rule.Revision!=expectedRevision)throw new SaveConflictException();
        var current=new DateOnly(today.Year,today.Month,1);
        if(!rule.Enabled || rule.EndMonth is DateOnly ended && ended<current || endMonth is DateOnly end && (end.Day!=1 || end<current)
            || change is not null && (change.Month.Day!=1 || change.Month<=current || endMonth is DateOnly last && change.Month>last))
            throw new ArgumentException("변경 적용은 다음 달부터, 종료는 이번 달 이후로 선택해 주세요.");
        var changes=rule.Changes.ToList();
        if(change is not null) { changes.RemoveAll(c=>c.Month==change.Month);changes.Add(change); }
        rule=rule with { Changes=changes.OrderBy(c=>c.Month).ToArray(),EndMonth=endMonth,Revision=rule.Revision+1 };
        WriteFixedRule(connection,transaction,rule);transaction.Commit();return rule;
    }
    private static void WriteFixedRule(SqliteConnection connection,SqliteTransaction transaction,FixedCostRule rule)
    {
        rule.Validate();using var c=connection.CreateCommand();c.Transaction=transaction;
        c.CommandText="INSERT INTO fixed_costs(id,json) VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=$json";
        c.Parameters.AddWithValue("$id",rule.Id.ToString());c.Parameters.AddWithValue("$json",JsonSerializer.Serialize(rule));c.ExecuteNonQuery();
    }
    private static void InsertMoney(SqliteConnection connection,SqliteTransaction transaction,DateOnly date,int position,MoneyEntry entry)
    {
        using var c=connection.CreateCommand();c.Transaction=transaction;
        c.CommandText="INSERT INTO money_entries(id,date,position,kind,won,category,note) VALUES($id,$date,$pos,$kind,$won,$category,$note)";
        c.Parameters.AddWithValue("$id",entry.Id.ToString());c.Parameters.AddWithValue("$date",Key(date));c.Parameters.AddWithValue("$pos",position);
        c.Parameters.AddWithValue("$kind",(int)entry.Kind);c.Parameters.AddWithValue("$won",entry.Won);c.Parameters.AddWithValue("$category",entry.Category);c.Parameters.AddWithValue("$note",entry.Note.Trim());c.ExecuteNonQuery();
        c.CommandText="INSERT INTO money_details(entry_id,title,is_fixed,repeat_monthly,fixed_cost_id) VALUES($id,$title,$fixed,$repeat,$source)";
        c.Parameters.AddWithValue("$title",entry.Title.Trim());c.Parameters.AddWithValue("$fixed",entry.IsFixed?1:0);c.Parameters.AddWithValue("$repeat",entry.RepeatMonthly?1:0);
        c.Parameters.AddWithValue("$source",entry.FixedCostId is Guid source?source.ToString():DBNull.Value);c.ExecuteNonQuery();
    }
    private static void RegisterFixedMoney(SqliteConnection connection,SqliteTransaction transaction,DateOnly date,MoneyEntry entry)
    {
        if(entry.FixedCostId is not Guid id) return;
        using var c=connection.CreateCommand();c.Transaction=transaction;
        if(entry.RepeatMonthly)
        {
            c.CommandText="SELECT COUNT(*) FROM fixed_costs WHERE id=$rule";c.Parameters.AddWithValue("$rule",id.ToString());
            if(Convert.ToInt32(c.ExecuteScalar())==0)
            {
                c.CommandText="SELECT COUNT(*) FROM fixed_costs";
                if(Convert.ToInt32(c.ExecuteScalar())>=500) throw new ArgumentException("반복 지출은 최대 500개까지 보관할 수 있어요.");
                WriteFixedRule(connection,transaction,new(id,date,entry with { RepeatMonthly=false,FixedCostId=null },true));
            }
        }
        c.CommandText="SELECT entry_id FROM fixed_cost_occurrences WHERE rule_id=$rule AND month=$month";
        if(!c.Parameters.Contains("$rule"))c.Parameters.AddWithValue("$rule",id.ToString());
        c.Parameters.AddWithValue("$month",Key(new(date.Year,date.Month,1)));
        var previous=c.ExecuteScalar() as string;
        if(previous is not null && previous!=entry.Id.ToString()) throw new ArgumentException("같은 달의 고정 지출이 이미 반영되어 있어요.");
        c.CommandText="INSERT OR IGNORE INTO fixed_cost_occurrences(rule_id,month,entry_id) VALUES($rule,$month,$entry)";
        c.Parameters.AddWithValue("$entry",entry.Id.ToString());c.ExecuteNonQuery();
    }
    public FixedCostApplication ApplyFixedCosts(DateOnly asOf,IReadOnlySet<DateOnly>? protectedDates=null)
    {
        using var connection=Open();using var transaction=connection.BeginTransaction();
        var rules=ReadFixedCosts(connection,transaction).Where(r=>r.Enabled).ToArray();
        var applied=ReadFixedOccurrences(connection,transaction).Select(o=>(o.RuleId,o.Month)).ToHashSet();
        var added=0;var deferred=0;
        foreach(var rule in rules)
        {
            // Start after the explicitly saved first payment. Replays never recreate a deleted payment.
            var firstMonth=new DateOnly(rule.FirstDate.Year,rule.FirstDate.Month,1);
            if(firstMonth.Year==9999 && firstMonth.Month==12)continue;
            for(var month=firstMonth.AddMonths(1);month<=asOf && (rule.EndMonth is null || month<=rule.EndMonth);)
            {
                var due=rule.DueIn(month);
                if(due<=asOf && !applied.Contains((rule.Id,month)))
                {
                    if(protectedDates?.Contains(due)==true)deferred++;
                    else
                    {
                        var bytes=SHA256.HashData(Encoding.UTF8.GetBytes($"PDS-fixed:{rule.Id:N}:{month:yyyy-MM}"));
                        var entry=rule.TemplateIn(month) with { Id=new Guid(bytes.AsSpan(0,16)),RepeatMonthly=false,FixedCostId=rule.Id };
                        using var c=connection.CreateCommand();c.Transaction=transaction;
                        c.Parameters.AddWithValue("$date",Key(due));
                        c.CommandText="INSERT INTO days(date,revision) VALUES($date,1) ON CONFLICT(date) DO UPDATE SET revision=revision+1";c.ExecuteNonQuery();
                        c.CommandText="SELECT COALESCE(MAX(position)+1,0) FROM money_entries WHERE date=$date";var pos=Convert.ToInt32(c.ExecuteScalar());
                        InsertMoney(connection,transaction,due,pos,entry);RegisterFixedMoney(connection,transaction,due,entry);added++;
                    }
                }
                if(month.Year==9999 && month.Month==12)break;
                month=month.AddMonths(1);
            }
        }
        transaction.Commit();return new(added,deferred);
    }
}
