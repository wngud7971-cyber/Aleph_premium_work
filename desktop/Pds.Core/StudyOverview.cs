using System.Globalization;

namespace Pds.Core;

public sealed record StudyListing(DateOnly Date, Guid Id, string Title, string Summary) { public int? DurationMinutes {get;init;} }
public sealed record StudyOverviewReport(DateRange Range, int Records, int StudyDays, int Page, int PageSize, IReadOnlyList<StudyListing> Items)
{
    public long Minutes {get;init;}
    public int TimedRecords {get;init;}
    public int Pages => Math.Max(1, (int)Math.Ceiling((double)Records / PageSize));
}

public sealed partial class PlanRepository
{
    public StudyOverviewReport StudyOverview(DateRange range, int page = 0, int pageSize = 10)
    {
        range.Validate();
        if (page < 0 || pageSize is < 1 or > 50) throw new ArgumentException("학습 목록 페이지를 확인해 주세요.");
        using var connection = Open(); using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.Parameters.AddWithValue("$from", Key(range.From)); command.Parameters.AddWithValue("$to", Key(range.To));
        command.CommandText = "SELECT COUNT(*),COUNT(DISTINCT date),COUNT(duration_minutes),COALESCE(SUM(duration_minutes),0) FROM study_entries WHERE date >= $from AND date <= $to";
        int records, days, timed;long minutes;
        using (var reader = command.ExecuteReader()) { reader.Read(); records = reader.GetInt32(0); days = reader.GetInt32(1);timed=reader.GetInt32(2);minutes=reader.GetInt64(3); }
        var lastPage = Math.Max(0, (int)Math.Ceiling((double)records / pageSize) - 1);
        page = Math.Min(page, lastPage);
        // Query only one bounded page of headings/summaries; load the body on expansion.
        command.CommandText = "SELECT date,id,title,summary,duration_minutes FROM study_entries WHERE date >= $from AND date <= $to ORDER BY date DESC,position,id LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$limit", pageSize); command.Parameters.AddWithValue("$offset", page * pageSize);
        var items = new List<StudyListing>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) items.Add(new(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3)) {DurationMinutes=reader.IsDBNull(4)?null:reader.GetInt32(4)});
        transaction.Commit(); return new(range, records, days, page, pageSize, items) {Minutes=minutes,TimedRecords=timed};
    }
    public StudyEntry? LoadStudy(DateOnly date, Guid id)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT title,content,summary,duration_minutes FROM study_entries WHERE date=$date AND id=$id";
        command.Parameters.AddWithValue("$date", Key(date)); command.Parameters.AddWithValue("$id", id.ToString());
        StudyEntry? result;
        using (var reader = command.ExecuteReader()) result=reader.Read() ? new(id, reader.GetString(0), reader.GetString(1), reader.GetString(2)) {DurationMinutes=reader.IsDBNull(3)?null:reader.GetInt32(3)} : null;
        return result is null ? null : result with { Attachments=ReadStudyAttachments(connection,null,id) };
    }
}
