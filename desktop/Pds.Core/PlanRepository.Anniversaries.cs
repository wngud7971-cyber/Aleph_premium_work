using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Pds.Core;

public sealed partial class PlanRepository
{
    private static IReadOnlyList<Anniversary> ReadAnniversaries(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT json FROM anniversaries ORDER BY name,id";
        var results = new List<Anniversary>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(JsonSerializer.Deserialize<Anniversary>(reader.GetString(0)) ?? throw new InvalidDataException("기념일을 읽지 못했어요."));
        return results;
    }
    public IReadOnlyList<Anniversary> LoadAnniversaries()
    { using var connection = Open(); return ReadAnniversaries(connection); }

    public Anniversary SaveAnniversary(Anniversary input)
    {
        input.Validate(); var entry = input with { Name = input.Name.Trim(), Note = input.Note.Trim(), Revision = input.Revision + 1 };
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", input.Id.ToString());
        command.CommandText = "SELECT revision FROM anniversaries WHERE id=$id";
        var current = command.ExecuteScalar();
        if (current is null && input.Revision != 0 || current is not null && Convert.ToInt64(current) != input.Revision)
            throw new SaveConflictException();
        command.CommandText = "INSERT INTO anniversaries(id,revision,name,json) VALUES($id,$revision,$name,$json) ON CONFLICT(id) DO UPDATE SET revision=$revision,name=$name,json=$json";
        command.Parameters.AddWithValue("$revision", entry.Revision); command.Parameters.AddWithValue("$name", entry.Name);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(entry)); command.ExecuteNonQuery(); transaction.Commit(); return entry;
    }
    public void DeleteAnniversary(Anniversary entry)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM anniversaries WHERE id=$id AND revision=$revision";
        command.Parameters.AddWithValue("$id", entry.Id.ToString()); command.Parameters.AddWithValue("$revision", entry.Revision);
        if (command.ExecuteNonQuery() != 1) throw new SaveConflictException();
    }
}
