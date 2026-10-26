using System.Globalization;

namespace Pds.Core;

public sealed record SearchHit(DateOnly Date,string Kind,Guid Id,string Title,string Preview);
public sealed record SearchPage(int Total,int Page,int PageSize,IReadOnlyList<SearchHit> Items)
{
    public int Pages=>Math.Max(1,(Total+PageSize-1)/PageSize);
}
public sealed record MonthlyBudget(DateOnly Month,long Won)
{
    public void Validate() { if(Month.Day!=1 || Won<1 || Won>MoneyEntry.MaxWon)throw new ArgumentException("월 예산은 1~999,999,999,999원으로 입력해 주세요."); }
}
public sealed record BudgetReport(DateOnly Month,long? Budget,long Consumption,long Savings)
{
    public long? Remaining=>Budget-Consumption;
    public decimal? Percent=>Budget is long value?(decimal)Consumption/value*100:null;
}
public sealed record MonthlyDigest(DateOnly Month,StatisticsReport Activity,JournalStatistics Money,int Studies,int StudyDays,BudgetReport Budget) {public long StudyMinutes {get;init;} public int TimedStudies {get;init;} }

public sealed partial class PlanRepository
{
    public SearchPage SearchRecords(string query,string kind="all",DateOnly? from=null,DateOnly? to=null,int page=0,int pageSize=20)
    {
        query=query.Trim();
        if(query.Length is <1 or >200 || !new[]{"all","plan","workout","review","money","study"}.Contains(kind) || from>to || page<0 || pageSize is <1 or >50)
            throw new ArgumentException("검색어는 1~200자이며 날짜 범위를 확인해 주세요.");
        using var connection=Open();using var transaction=connection.BeginTransaction(deferred:true);using var c=connection.CreateCommand();c.Transaction=transaction;
        c.Parameters.AddWithValue("$q",query);c.Parameters.AddWithValue("$kind",kind);c.Parameters.AddWithValue("$from",Key(from??DateOnly.MinValue));c.Parameters.AddWithValue("$to",Key(to??DateOnly.MaxValue));
        var names=new List<string>();var exerciseIndex=0;
        foreach(var exercise in ExerciseCatalog.All)
        {var id="$ex"+exerciseIndex;var name="$name"+exerciseIndex;names.Add($"({id},{name})");c.Parameters.AddWithValue(id,exercise.Id);c.Parameters.AddWithValue(name,exercise.Name);exerciseIndex++;}
        var prefix="WITH exercise_names(id,name) AS (VALUES "+string.Join(",",names)+") ";
        const string union="""
            SELECT date,'plan' kind,id,title,substr(note,1,240) preview FROM plans WHERE instr(lower(title||char(10)||note),lower($q))>0
            UNION ALL SELECT date,'money',m.id,CASE WHEN COALESCE(title,'')='' THEN category ELSE title END,substr(category||' · '||won||'원 · '||note,1,240) FROM money_entries m LEFT JOIN money_details d ON d.entry_id=m.id WHERE instr(lower(COALESCE(title,'')||char(10)||category||char(10)||note||char(10)||won),lower($q))>0
            UNION ALL SELECT date,'study',s.id,title,substr(CASE WHEN summary='' THEN content ELSE summary END,1,240) FROM study_entries s WHERE instr(lower(title||char(10)||content||char(10)||summary),lower($q))>0 OR EXISTS(SELECT 1 FROM study_attachments a WHERE a.study_id=s.id AND instr(lower(a.name),lower($q))>0)
            UNION ALL SELECT date,'workout',id,exercise_id,substr(note,1,240) FROM workouts WHERE instr(lower(exercise_id||char(10)||note),lower($q))>0 OR EXISTS(SELECT 1 FROM exercise_names n WHERE n.id=exercise_id AND instr(lower(n.name),lower($q))>0)
            UNION ALL SELECT date,'review','00000000-0000-0000-0000-000000000000','하루 회고',substr(good||char(10)||difficult||char(10)||next_action,1,240) FROM reviews WHERE instr(lower(good||char(10)||difficult||char(10)||next_action),lower($q))>0
            """;
        var sql=" FROM ("+union+") WHERE date >= $from AND date <= $to AND ($kind='all' OR kind=$kind)";
        c.CommandText=prefix+"SELECT COUNT(*)"+sql;var count=Convert.ToInt32(c.ExecuteScalar());page=Math.Min(page,Math.Max(0,(count-1)/pageSize));
        c.Parameters.AddWithValue("$limit",pageSize);c.Parameters.AddWithValue("$offset",page*pageSize);c.CommandText=prefix+"SELECT date,kind,id,title,preview"+sql+" ORDER BY date DESC,kind,id LIMIT $limit OFFSET $offset";
        var hits=new List<SearchHit>();using(var reader=c.ExecuteReader())while(reader.Read())hits.Add(new(DateOnly.ParseExact(reader.GetString(0),"yyyy-MM-dd",CultureInfo.InvariantCulture),reader.GetString(1),Guid.Parse(reader.GetString(2)),reader.GetString(3),reader.GetString(4)));
        transaction.Commit();return new(count,page,pageSize,hits);
    }
    private static IReadOnlyList<MonthlyBudget> ReadBudgets(Microsoft.Data.Sqlite.SqliteConnection connection,Microsoft.Data.Sqlite.SqliteTransaction transaction)
    {
        using var c=connection.CreateCommand();c.Transaction=transaction;c.CommandText="SELECT month,won FROM monthly_budgets ORDER BY month";
        var result=new List<MonthlyBudget>();using var reader=c.ExecuteReader();while(reader.Read())result.Add(new(DateOnly.ParseExact(reader.GetString(0),"yyyy-MM-dd",CultureInfo.InvariantCulture),reader.GetInt64(1)));return result;
    }
    public void SaveBudget(MonthlyBudget budget)
    {
        budget.Validate();using var connection=Open();using var c=connection.CreateCommand();
        c.CommandText="INSERT INTO monthly_budgets(month,won) VALUES($month,$won) ON CONFLICT(month) DO UPDATE SET won=$won";
        c.Parameters.AddWithValue("$month",Key(budget.Month));c.Parameters.AddWithValue("$won",budget.Won);c.ExecuteNonQuery();
    }
    public void RemoveBudget(DateOnly month)
    {
        using var connection=Open();using var c=connection.CreateCommand();c.CommandText="DELETE FROM monthly_budgets WHERE month=$month";c.Parameters.AddWithValue("$month",Key(new(month.Year,month.Month,1)));c.ExecuteNonQuery();
    }
    public BudgetReport BudgetFor(DateOnly month)
    {
        month=new(month.Year,month.Month,1);using var connection=Open();using var c=connection.CreateCommand();
        c.CommandText="SELECT won FROM monthly_budgets WHERE month=$month";c.Parameters.AddWithValue("$month",Key(month));var raw=c.ExecuteScalar();
        var journal=JournalStatistics(DateRange.Month(month));return new(month,raw is null?null:Convert.ToInt64(raw),journal.Consumption,journal.Savings);
    }
    public MonthlyDigest MonthDigest(DateOnly month)
    {
        month=new(month.Year,month.Month,1);var range=DateRange.Month(month);var studies=StudyOverview(range);
        return new(month,Statistics(range),JournalStatistics(range),studies.Records,studies.StudyDays,BudgetFor(month)) {StudyMinutes=studies.Minutes,TimedStudies=studies.TimedRecords};
    }
}
