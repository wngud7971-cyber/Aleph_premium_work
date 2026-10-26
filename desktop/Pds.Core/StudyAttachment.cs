using System.Security.Cryptography;

namespace Pds.Core;

public sealed record StudyAttachment(Guid Id, string Name, long Length, string Sha256)
{
    public const long MaxBytes = 50L * 1024 * 1024;
    public const int MaxPerStudy = 10;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".hwp", ".hwpx", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".txt", ".md", ".csv", ".zip" };
    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name != Path.GetFileName(name)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\')
            || name.EndsWith('.') || name.EndsWith(' ') || !Extensions.Contains(Path.GetExtension(name)))
            throw new ArgumentException("지원하는 문서·이미지·ZIP 파일을 선택해 주세요. 파일명은 200자까지 가능해요.");
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem) || stem.Length==4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9')
            throw new ArgumentException("이 파일명은 사용할 수 없어요.");
    }
    public void Validate()
    {
        ValidateName(Name);
        if (Id==Guid.Empty || Length < 0 || Length > MaxBytes || Sha256 is null || Sha256.Length!=64 || Sha256.Any(c=>!Uri.IsHexDigit(c)))
            throw new ArgumentException("첨부 파일 정보나 크기를 확인해 주세요. 파일당 최대 50MB예요.");
    }
}

public sealed class StudyAttachmentStore
{
    public string Root { get; }
    public StudyAttachmentStore(string dataDirectory) => Root=Path.Combine(Path.GetFullPath(dataDirectory),"attachments");
    public string PathFor(StudyAttachment file)
    {
        file.Validate();
        var path=Path.GetFullPath(Path.Combine(Root,file.Id.ToString("N"),file.Name));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("첨부 파일 경로를 확인해 주세요.");
        return path;
    }
    public StudyAttachment Import(string source)
    {
        var full=Path.GetFullPath(source); var name=Path.GetFileName(full); StudyAttachment.ValidateName(name);
        using var input=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read);
        if (input.Length > StudyAttachment.MaxBytes) throw new ArgumentException("파일당 최대 50MB까지 첨부할 수 있어요.");
        var placeholder=new StudyAttachment(Guid.NewGuid(),name,input.Length,new string('0',64));
        var path=PathFor(placeholder); var directory=Path.GetDirectoryName(path)!; Directory.CreateDirectory(directory);
        try
        {
            using (var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) CopyBounded(input,output,placeholder.Length);
            using var check=File.OpenRead(path); var result=placeholder with { Sha256=Convert.ToHexString(SHA256.HashData(check)) };
            File.SetAttributes(path,File.GetAttributes(path)|FileAttributes.ReadOnly); return result;
        }
        catch { Remove(placeholder); throw; }
    }
    internal static void CopyBounded(Stream source,Stream target,long expected)
    {
        var buffer=new byte[64*1024]; long copied=0; int read;
        while ((read=source.Read(buffer,0,buffer.Length))>0)
        { copied+=read; if (copied>expected || copied>StudyAttachment.MaxBytes) throw new InvalidDataException("첨부 파일 크기가 올바르지 않아요."); target.Write(buffer,0,read); }
        if (copied!=expected) throw new InvalidDataException("첨부 파일이 변경됐거나 일부가 누락됐어요.");
    }
    public string Require(StudyAttachment file,bool verifyHash=false)
    {
        var path=PathFor(file); var info=new FileInfo(path);
        if (!info.Exists || info.Length!=file.Length) throw new FileNotFoundException("보관한 첨부 파일을 찾지 못했어요. 첨부가 포함된 백업으로 복원해 주세요.");
        if (verifyHash)
        {
            using var stream=File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("보관한 첨부 파일이 변경됐어요. 백업 파일을 확인해 주세요.");
        }
        return path;
    }
    internal void Install(StudyAttachment file,string staged)
    {
        var path=PathFor(file);
        if (File.Exists(path)) { Require(file,true); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Staged content is already validated; install under an unguessable immutable ID.
        File.Copy(staged,path,false);
        Require(file,true);
        File.SetAttributes(path,File.GetAttributes(path)|FileAttributes.ReadOnly);
    }
    internal void Remove(StudyAttachment file)
    {
        try
        {
            var path=PathFor(file);
            if (File.Exists(path)) { File.SetAttributes(path,File.GetAttributes(path)&~FileAttributes.ReadOnly); File.Delete(path); }
            var dir=Path.GetDirectoryName(path)!;
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A busy viewer may leave an unreferenced copy; saved records stay valid. */ }
    }
}

public sealed partial class PlanRepository
{
    public StudyAttachmentStore Attachments => new(Path.GetDirectoryName(DatabasePath)!);
    public void DiscardAttachmentIfUnused(StudyAttachment file)
    {
        try
        {
            using var connection=Open(); using var command=connection.CreateCommand();
            command.CommandText="SELECT (SELECT COUNT(*) FROM study_attachments WHERE id=$id)+(SELECT COUNT(*) FROM deleted_attachments WHERE file_id=$id)"; command.Parameters.AddWithValue("$id",file.Id.ToString());
            if (Convert.ToInt32(command.ExecuteScalar())==0) Attachments.Remove(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { }
    }
    private static IReadOnlyList<StudyAttachment> ReadStudyAttachments(Microsoft.Data.Sqlite.SqliteConnection connection,Microsoft.Data.Sqlite.SqliteTransaction? transaction,Guid studyId)
    {
        using var command=connection.CreateCommand(); command.Transaction=transaction;
        command.CommandText="SELECT id,name,length,sha256 FROM study_attachments WHERE study_id=$id ORDER BY position";
        command.Parameters.AddWithValue("$id",studyId.ToString()); var files=new List<StudyAttachment>();
        using var reader=command.ExecuteReader();
        while (reader.Read()) files.Add(new(Guid.Parse(reader.GetString(0)),reader.GetString(1),reader.GetInt64(2),reader.GetString(3)));
        return files;
    }
    private static IReadOnlyList<StudyAttachment> ReadAttachmentReferences(Microsoft.Data.Sqlite.SqliteConnection connection,Microsoft.Data.Sqlite.SqliteTransaction? transaction,DateOnly? date=null)
    {
        using var command=connection.CreateCommand(); command.Transaction=transaction;
        command.CommandText="SELECT a.id,a.name,a.length,a.sha256 FROM study_attachments a JOIN study_entries s ON s.id=a.study_id"+(date is null ? "" : " WHERE s.date=$date");
        if (date is DateOnly day) command.Parameters.AddWithValue("$date",Key(day));
        using var reader=command.ExecuteReader(); var files=new List<StudyAttachment>();
        while (reader.Read()) files.Add(new(Guid.Parse(reader.GetString(0)),reader.GetString(1),reader.GetInt64(2),reader.GetString(3)));
        return files;
    }
}
