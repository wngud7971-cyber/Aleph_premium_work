using Microsoft.Data.Sqlite;

namespace Pds.Core;

public sealed partial class PlanRepository
{
    internal static void ValidateJournal(DaySnapshot day)
    {
        if (day.Money is null || day.Studies is null || day.Money.Count > 10000 || day.Studies.Count > 10000)
            throw new ArgumentException("하루 기록은 종류별 10,000건까지 저장할 수 있어요.");
        foreach (var entry in day.Money) entry.Validate();
        foreach (var entry in day.Studies) entry.Validate();
        var fileIds=day.Studies.SelectMany(s=>s.Attachments).Select(f=>f.Id).ToArray();
        if (fileIds.Distinct().Count()!=fileIds.Length) throw new ArgumentException("첨부 파일이 다른 학습에 중복 연결되어 있어요.");
        if (day.Money.Select(e => e.Id).Distinct().Count() != day.Money.Count || day.Studies.Select(e => e.Id).Distinct().Count() != day.Studies.Count)
            throw new ArgumentException("같은 가계부·학습 기록이 중복되어 있어요.");
    }
    private static DaySnapshot ReadJournal(SqliteConnection connection, SqliteTransaction transaction, DaySnapshot day)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.Parameters.AddWithValue("$date", Key(day.Date));
        command.CommandText = "SELECT m.id,kind,won,category,note,COALESCE(title,''),COALESCE(is_fixed,0),COALESCE(repeat_monthly,0),fixed_cost_id FROM money_entries m LEFT JOIN money_details d ON d.entry_id=m.id WHERE date=$date ORDER BY position";
        var money = new List<MoneyEntry>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) money.Add(new(Guid.Parse(reader.GetString(0)), (MoneyKind)reader.GetInt32(1), reader.GetInt64(2), reader.GetString(3), reader.GetString(4)) {
                Title=reader.GetString(5),IsFixed=reader.GetInt32(6)==1,RepeatMonthly=reader.GetInt32(7)==1,FixedCostId=reader.IsDBNull(8)?null:Guid.Parse(reader.GetString(8)) });
        command.CommandText = "SELECT id,title,content,summary,duration_minutes FROM study_entries WHERE date=$date ORDER BY position";
        var studies = new List<StudyEntry>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) studies.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3)) { DurationMinutes=reader.IsDBNull(4)?null:reader.GetInt32(4) });
        for (var i=0;i<studies.Count;i++) studies[i]=studies[i] with { Attachments=ReadStudyAttachments(connection,transaction,studies[i].Id) };
        return day with { Money = money, Studies = studies };
    }
    private static void WriteJournal(SqliteConnection connection, SqliteTransaction transaction, DaySnapshot day)
    {
        void Write(string sql, params (string Name, object Value)[] values)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
            command.Parameters.AddWithValue("$date", Key(day.Date));
            foreach (var v in values) command.Parameters.AddWithValue(v.Name, v.Value);
            command.ExecuteNonQuery();
        }
        Write("DELETE FROM money_entries WHERE date=$date; DELETE FROM study_entries WHERE date=$date;");
        for (var i = 0; i < day.Money.Count; i++)
        {
            var e = day.Money[i];
            InsertMoney(connection,transaction,day.Date,i,e);
            RegisterFixedMoney(connection,transaction,day.Date,e);
        }
        for (var i = 0; i < day.Studies.Count; i++)
        {
            var e = day.Studies[i];
            Write("INSERT INTO study_entries(id,date,position,title,content,summary,duration_minutes) VALUES($id,$date,$pos,$title,$content,$summary,$duration)",
                ("$id", e.Id.ToString()), ("$pos", i), ("$title", e.Title.Trim()), ("$content", e.Content.Trim()), ("$summary", e.Summary.Trim()), ("$duration", (object?)e.DurationMinutes??DBNull.Value));
            for (var j=0;j<e.Attachments.Count;j++)
            {
                var f=e.Attachments[j];
                Write("INSERT INTO study_attachments(id,study_id,position,name,length,sha256) VALUES($id,$study,$pos,$name,$length,$hash)",
                    ("$id",f.Id.ToString()),("$study",e.Id.ToString()),("$pos",j),("$name",f.Name),("$length",f.Length),("$hash",f.Sha256));
            }
        }
    }
    public JournalStatistics JournalStatistics(DateRange range)
    {
        range.Validate();
        using var connection = Open(); using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.Parameters.AddWithValue("$from", Key(range.From)); command.Parameters.AddWithValue("$to", Key(range.To));
        command.CommandText = "SELECT kind,COALESCE(SUM(won),0),COUNT(*) FROM money_entries WHERE date >= $from AND date <= $to GROUP BY kind";
        long income = 0, expense = 0; var count = 0;
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                if ((MoneyKind)reader.GetInt32(0) == MoneyKind.Income) income = reader.GetInt64(1); else expense = reader.GetInt64(1);
                count += reader.GetInt32(2);
            }
        command.CommandText = "SELECT category,SUM(won),COUNT(*) FROM money_entries WHERE date >= $from AND date <= $to AND kind=0 AND category<>'적금·저축' GROUP BY category ORDER BY SUM(won) DESC,category";
        var categories = new List<MoneyCategoryTotal>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) categories.Add(new(reader.GetString(0), reader.GetInt64(1), reader.GetInt32(2)));
        command.CommandText = "SELECT COUNT(*) FROM study_entries WHERE date >= $from AND date <= $to";
        var studies = Convert.ToInt32(command.ExecuteScalar());
        command.CommandText="SELECT COALESCE(SUM(CASE WHEN category='적금·저축' THEN won ELSE 0 END),0),COALESCE(SUM(CASE WHEN is_fixed=1 THEN won ELSE 0 END),0),COALESCE(SUM(CASE WHEN is_fixed=1 THEN 1 ELSE 0 END),0) FROM money_entries m LEFT JOIN money_details d ON d.entry_id=m.id WHERE date >= $from AND date <= $to AND kind=0";
        long savings=0,fixedTotal=0;int fixedRecords=0;
        using(var reader=command.ExecuteReader()) { reader.Read();savings=reader.GetInt64(0);fixedTotal=reader.GetInt64(1);fixedRecords=reader.GetInt32(2); }
        command.CommandText="SELECT date,title,category,won FROM money_entries m JOIN money_details d ON d.entry_id=m.id WHERE date >= $from AND date <= $to AND kind=0 AND is_fixed=1 ORDER BY date DESC,position LIMIT 200";
        var lines=new List<FixedCostLine>();
        using(var reader=command.ExecuteReader())while(reader.Read())lines.Add(new(DateOnly.ParseExact(reader.GetString(0),"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),reader.GetString(1),reader.GetString(2),reader.GetInt64(3),reader.GetString(2)=="적금·저축"));
        transaction.Commit();
        return new(range, income, expense, count, studies, categories) { Savings=savings,FixedTotal=fixedTotal,FixedRecords=fixedRecords,FixedLines=lines };
    }
}
