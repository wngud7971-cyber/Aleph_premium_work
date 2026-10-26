using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Pds.Core;

public static class BackupArchive
{
    public const long MaxPayloadBytes=1024L*1024*1024;
    private static StudyAttachment[] Files(PdsBackup backup)
    {
        var files=BackupFile.Files(backup).ToArray();
        if (files.Length>10000 || files.Sum(f=>f.Length)>MaxPayloadBytes) throw new InvalidDataException("첨부 백업은 총 1GB,10,000개 파일까지 지원해요.");
        return files;
    }
    public static void Write(string path,PdsBackup backup,StudyAttachmentStore store)
    {
        var json=BackupFile.Serialize(backup); var bytes=Encoding.UTF8.GetBytes(json);
        if (bytes.Length>BackupFile.MaxBytes) throw new InvalidDataException("백업 기록이 32MB를 초과해요.");
        var files=Files(backup); var full=Path.GetFullPath(path);
        var temp=Path.Combine(Path.GetDirectoryName(full)!,".pds-export-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using (var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using (var archive=new ZipArchive(output,ZipArchiveMode.Create))
            {
                using (var manifest=archive.CreateEntry("manifest.json",CompressionLevel.Fastest).Open()) manifest.Write(bytes);
                foreach (var file in files)
                {
                    var source=store.Require(file,true);
                    using var input=File.OpenRead(source);
                    using var target=archive.CreateEntry("attachments/"+file.Id.ToString("N"),CompressionLevel.Fastest).Open();
                    StudyAttachmentStore.CopyBounded(input,target,file.Length);
                }
            }
            File.Move(temp,full,true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static PreparedBackup Read(string path)
    {
        if (!Path.GetExtension(path).Equals(".zip",StringComparison.OrdinalIgnoreCase)) return new(BackupFile.Read(path),null,[]);
        if (new FileInfo(path).Length>2*MaxPayloadBytes) throw new InvalidDataException("백업 파일이 너무 커요.");
        using var archive=ZipFile.OpenRead(path);
        if (archive.Entries.Count>10001) throw new InvalidDataException("백업 파일 수가 제한을 초과해요.");
        var manifest=archive.GetEntry("manifest.json") ?? throw new InvalidDataException("PDS 백업의 기록 파일이 없어요.");
        if (manifest.Length>BackupFile.MaxBytes) throw new InvalidDataException("백업 기록이 32MB를 초과해요.");
        PdsBackup backup;
        using (var source=manifest.Open())
        using (var bounded=new MemoryStream())
        {
            var buffer=new byte[64*1024]; int size; long read=0;
            while ((size=source.Read(buffer))>0) { read+=size; if (read>BackupFile.MaxBytes) throw new InvalidDataException("백업 기록이 너무 커요."); bounded.Write(buffer,0,size); }
            backup=BackupFile.Parse(Encoding.UTF8.GetString(bounded.ToArray()));
        }
        var files=Files(backup);
        var expected=files.Select(f=>"attachments/"+f.Id.ToString("N")).Append("manifest.json").ToHashSet(StringComparer.Ordinal);
        if (archive.Entries.Count!=expected.Count || archive.Entries.Select(e=>e.FullName).Distinct(StringComparer.Ordinal).Count()!=expected.Count
            || archive.Entries.Any(e=>!expected.Contains(e.FullName))) throw new InvalidDataException("ZIP 백업에 누락되거나 알 수 없는 항목이 있어요.");
        var stage=Path.Combine(Path.GetTempPath(),"PdsAttachmentRestore-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage); var prepared=new PreparedBackup(backup,stage,[]);
        try
        {
            foreach (var file in files)
            {
                var entry=archive.GetEntry("attachments/"+file.Id.ToString("N"))!;
                if (entry.Length!=file.Length) throw new InvalidDataException("첨부 파일 크기가 백업 기록과 달라요.");
                var target=Path.Combine(stage,file.Id.ToString("N"));
                using (var source=entry.Open()) using (var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    StudyAttachmentStore.CopyBounded(source,output,file.Length);
                using var check=File.OpenRead(target);
                if (!Convert.ToHexString(SHA256.HashData(check)).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("백업 첨부 파일이 손상됐어요. 기존 기록은 유지돼요.");
                prepared.Staged[file.Id]=target;
            }
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
    }
}

public sealed class PreparedBackup : IDisposable
{
    public PdsBackup Backup { get; }
    internal Dictionary<Guid,string> Staged { get; }
    private readonly string? stageDirectory;
    private readonly List<StudyAttachment> installed=[];
    private PlanRepository? repository;
    internal PreparedBackup(PdsBackup backup,string? stageDirectory,Dictionary<Guid,string> staged)
    { Backup=backup; this.stageDirectory=stageDirectory; Staged=staged; }
    public void Install(PlanRepository target)
    {
        repository=target;
        foreach (var file in BackupFile.Files(Backup))
        {
            if (!Staged.TryGetValue(file.Id,out var path)) throw new InvalidDataException("복원할 첨부 파일이 없어요.");
            if (!File.Exists(target.Attachments.PathFor(file))) installed.Add(file);
            target.Attachments.Install(file,path);
        }
    }
    public void Dispose()
    {
        if (repository is not null) foreach (var file in installed) repository.DiscardAttachmentIfUnused(file);
        if (stageDirectory is null) return;
        var full=Path.GetFullPath(stageDirectory); var temp=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))+Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("PdsAttachmentRestore-",StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected attachment staging path");
        try { if (Directory.Exists(full)) Directory.Delete(full,true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
