using System.Globalization;

namespace Pds.Core;

public enum ActivityMetric { PlanDone, PlanRate, WorkoutRecords, WorkoutMinutes, StudyRecords, StudyMinutes }
public sealed record ActivityMonth(int Month,int Plans,int Done,int Workouts,int TimedWorkouts,long WorkoutSeconds,int Studies,int TimedStudies,long StudyMinutes)
{
    public bool HasRecords=>Plans+Workouts+Studies>0;
    public decimal? Value(ActivityMetric metric)=>metric switch
    {
        ActivityMetric.PlanDone=>Done,
        ActivityMetric.PlanRate=>Plans==0?null:(decimal)Done/Plans*100,
        ActivityMetric.WorkoutRecords=>Workouts,
        ActivityMetric.WorkoutMinutes=>Workouts>0 && TimedWorkouts==0?null:WorkoutSeconds/60m,
        ActivityMetric.StudyRecords=>Studies,
        ActivityMetric.StudyMinutes=>Studies>0 && TimedStudies==0?null:StudyMinutes,
        _=>throw new ArgumentOutOfRangeException(nameof(metric))
    };
}
public sealed record AnnualActivityReport(int Year,IReadOnlyList<ActivityMonth> Months);

public sealed partial class PlanRepository
{
    public AnnualActivityReport AnnualActivity(int year)
    {
        if(year is <1 or >9999)throw new ArgumentException("연도는 1~9999 사이로 선택해 주세요.");
        using var connection=Open();using var transaction=connection.BeginTransaction(deferred:true);
        using var command=connection.CreateCommand();command.Transaction=transaction;
        command.Parameters.AddWithValue("$from",Key(new(year,1,1)));command.Parameters.AddWithValue("$to",Key(new(year,12,31)));
        // Only small numeric aggregates are loaded. Study bodies and attachments stay on disk.
        command.CommandText="""
            SELECT substr(date,6,2),SUM(plans),SUM(done),SUM(workouts),SUM(timed_workouts),SUM(seconds),SUM(studies),SUM(timed_studies),SUM(minutes) FROM (
              SELECT date,COUNT(*) plans,SUM(done) done,0 workouts,0 timed_workouts,0 seconds,0 studies,0 timed_studies,0 minutes
                FROM plans WHERE date BETWEEN $from AND $to GROUP BY date
              UNION ALL
              SELECT date,0,0,COUNT(*),COUNT(duration_seconds),COALESCE(SUM(duration_seconds),0),0,0,0
                FROM workouts WHERE date BETWEEN $from AND $to GROUP BY date
              UNION ALL
              SELECT date,0,0,0,0,0,COUNT(*),COUNT(duration_minutes),COALESCE(SUM(duration_minutes),0)
                FROM study_entries WHERE date BETWEEN $from AND $to GROUP BY date
            ) GROUP BY substr(date,6,2)
            """;
        var months=Enumerable.Range(1,12).Select(m=>new ActivityMonth(m,0,0,0,0,0,0,0,0)).ToArray();
        using(var reader=command.ExecuteReader())while(reader.Read())
        {
            var m=int.Parse(reader.GetString(0),CultureInfo.InvariantCulture);
            months[m-1]=new(m,reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),reader.GetInt32(4),reader.GetInt64(5),reader.GetInt32(6),reader.GetInt32(7),reader.GetInt64(8));
        }
        transaction.Commit();return new(year,months);
    }
}
