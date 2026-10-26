using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using Pds.Core;

static class AttachmentChecks
{
    public static void Run(string directory,Action<bool,string> check)
    {
        void Expect<T>(Action action,string label) where T:Exception
        { try { action(); } catch (T) { check(true,label); return; } throw new InvalidOperationException(label); }
        var root=Path.Combine(directory,"attachment-fixture"); Directory.CreateDirectory(root);
        var repository=new PlanRepository(Path.Combine(root,"records.db"));
        var target=new PlanRepository(Path.Combine(root,"restored","records.db"));
        var abandoned=new PlanRepository(Path.Combine(root,"abandoned","records.db"));
        try
        {
            var source=Path.Combine(root,"강의 자료.txt"); var bytes=Encoding.UTF8.GetBytes("첫 줄\n첨부 보관 시험 · 분석하지 않는 원문"); File.WriteAllBytes(source,bytes);
            var originalAttributes=File.GetAttributes(source); var file=repository.Attachments.Import(source);
            check(file.Name=="강의 자료.txt" && file.Length==bytes.Length && File.ReadAllBytes(repository.Attachments.Require(file,true)).SequenceEqual(bytes),"Attachment import copies exact Unicode bytes under managed local storage");
            check(File.GetAttributes(source)==originalAttributes && File.GetAttributes(repository.Attachments.PathFor(file)).HasFlag(FileAttributes.ReadOnly),"Source file attributes stay untouched; stored copy is read-only");
            var day=new DateOnly(2026,10,18); var study=StudyEntry.Create("첨부 학습","본문","직접 요약") with { Attachments=[file] };
            var saved=repository.Save(repository.Load(day) with { Studies=[study] });
            File.Delete(source);
            check(new PlanRepository(repository.DatabasePath).Load(day).Studies.Single()==study && File.Exists(repository.Attachments.Require(file,true)),"Attachment survives original deletion and reopening with structural study equality");
            var backup=repository.CreateBackup(); var zip=Path.Combine(root,"pds-backup.zip"); BackupArchive.Write(zip,backup,repository.Attachments);
            check(backup.Version==8 && backup.Attachments==1 && File.Exists(repository.Attachments.PathFor(file)),"ZIP backup v5 includes attachment metadata and preserves originals");
            using (var archive=ZipFile.OpenRead(zip)) check(archive.Entries.Count==2 && archive.GetEntry("manifest.json") is not null && archive.GetEntry("attachments/"+file.Id.ToString("N"))!.Length==file.Length,"ZIP contains one manifest and exact referenced payload only");
            Expect<InvalidDataException>(()=>BackupFile.Write(Path.Combine(root,"incomplete.json"),backup),"Plain JSON export cannot silently omit attachment payloads");
            Expect<FileNotFoundException>(()=>target.RestoreBackup(backup),"Restore without installed payload cannot overwrite existing database");
            using (var prepared=BackupArchive.Read(zip))
            {
                check(!Directory.Exists(target.Attachments.Root),"Reading a ZIP stages files without modifying the live data folder");
                prepared.Install(target); target.RestoreBackup(prepared.Backup);
            }
            check(target.Load(day).Studies.Single()==study && File.ReadAllBytes(target.Attachments.Require(file,true)).SequenceEqual(bytes),"ZIP restore round-trips study text, attachment name and exact bytes");
            using (var prepared=BackupArchive.Read(zip)) prepared.Install(abandoned);
            check(!File.Exists(abandoned.Attachments.PathFor(file)),"Uncommitted installed payload is cleaned without touching saved files");
            var stale=repository.Load(day);
            var changed=repository.Save(saved with { Studies=[study with { Title="수정 제목" }] });
            Expect<SaveConflictException>(()=>repository.Save(stale with { Studies=[] }),"Attachment removal cannot bypass stale revision protection");
            check(File.Exists(repository.Attachments.PathFor(file)),"Rejected stale deletion leaves stored attachment intact");
            var source2=Path.Combine(root,"같은 이름.txt"); File.WriteAllText(source2,"다른 원문");
            var draft=repository.Attachments.Import(source2); var draftPath=repository.Attachments.PathFor(draft); repository.DiscardAttachmentIfUnused(draft);
            check(!File.Exists(draftPath) && File.Exists(source2),"Cancelling an unsaved attachment removes only its managed copy");
            repository.DiscardAttachmentIfUnused(file);
            check(File.Exists(repository.Attachments.PathFor(file)),"Discard never removes a payload referenced by saved study");
            var collision=repository.Save(repository.Load(day.AddDays(1)) with { Studies=[StudyEntry.Create("보호할 기록")] });
            Expect<SqliteException>(()=>repository.Save(collision with { Studies=[StudyEntry.Create("충돌") with { Attachments=[file] }] }),"Cross-study attachment ID collision rolls back the entire day");
            check(repository.Load(day.AddDays(1)).Studies.Single().Title=="보호할 기록" && File.Exists(repository.Attachments.PathFor(file)),"Late collision preserves previous study and original attachment");
            Expect<ArgumentException>(()=>repository.Save(changed with { Studies=[study with { Attachments=[file,file] }] }),"Duplicate file links rejected before save");
            Expect<ArgumentException>(()=>StudyAttachment.ValidateName("../escape.txt"),"Attachment metadata rejects path traversal");
            Expect<ArgumentException>(()=>StudyAttachment.ValidateName("program.exe"),"Unsupported executable attachment rejected");
            Expect<ArgumentException>(()=>StudyAttachment.ValidateName("CON.txt"),"Reserved Windows filename rejected");
            Expect<ArgumentException>(()=>(file with { Length=StudyAttachment.MaxBytes+1 }).Validate(),"Oversized attachment metadata rejected");
            var huge=Path.Combine(root,"큰 자료.pdf");
            using (var stream=File.Create(huge)) stream.SetLength(StudyAttachment.MaxBytes+1);
            Expect<ArgumentException>(()=>repository.Attachments.Import(huge),"Oversized actual file rejected before managed copy");
            Expect<ArgumentException>(()=> { var invalid=StudyEntry.Create("파일 한도") with { Attachments=Enumerable.Range(0,11).Select(_=>file with { Id=Guid.NewGuid() }).ToArray() }; invalid.Validate(); },"Per-study attachment count is bounded");
            var bad=Path.Combine(root,"corrupt.zip");
            using (var archive=ZipFile.Open(bad,ZipArchiveMode.Create))
            {
                using (var writer=new StreamWriter(archive.CreateEntry("manifest.json").Open())) writer.Write(BackupFile.Serialize(backup));
                using var payload=archive.CreateEntry("attachments/"+file.Id.ToString("N")).Open(); payload.Write(new byte[bytes.Length]);
            }
            var revision=target.Load(day).Revision;
            Expect<InvalidDataException>(()=>BackupArchive.Read(bad),"Tampered ZIP bytes fail integrity validation before live restore");
            check(target.Load(day).Revision==revision && File.ReadAllBytes(target.Attachments.Require(file,true)).SequenceEqual(bytes),"Corrupt ZIP rejection preserves live records and attachment bytes");
            var missing=Path.Combine(root,"missing.zip");
            using (var archive=ZipFile.Open(missing,ZipArchiveMode.Create)) using (var writer=new StreamWriter(archive.CreateEntry("manifest.json").Open())) writer.Write(BackupFile.Serialize(backup));
            Expect<InvalidDataException>(()=>BackupArchive.Read(missing),"Missing ZIP attachment rejected");
            var traversal=Path.Combine(root,"traversal.zip"); File.Copy(zip,traversal);
            using (var archive=ZipFile.Open(traversal,ZipArchiveMode.Update)) archive.CreateEntry("../outside.txt");
            Expect<InvalidDataException>(()=>BackupArchive.Read(traversal),"Unexpected ZIP paths rejected without extracting outside staging");
            var noFiles=backup with { Version=4, Days=backup.Days.Select(d=>d with { Studies=d.Studies.Select(s=>s with { Attachments=[] }).ToArray() }).ToArray() };
            var old=Path.Combine(root,"legacy-v4.json"); BackupFile.Write(old,noFiles);
            using (var prepared=BackupArchive.Read(old)) { prepared.Install(target); target.RestoreBackup(prepared.Backup); }
            check(target.Load(day).Studies.Single().Attachments.Count==0 && !File.Exists(target.Attachments.PathFor(file)),"Legacy JSON v4 remains readable and full replacement removes saved file links/copies");
            using (var c=new SqliteConnection($"Data Source={target.DatabasePath};Pooling=False"))
            { c.Open(); using var command=c.CreateCommand(); command.CommandText="DROP TABLE study_attachments; PRAGMA user_version=8"; command.ExecuteNonQuery(); }
            var migrated=new PlanRepository(target.DatabasePath).Load(day);
            check(migrated.Studies.Single().Title==study.Title && migrated.Studies.Single().Attachments.Count==0,"Schema 8 migration adds attachments without changing old study text");
            var removed=repository.Save(changed with { Studies=[study with { Attachments=[] }] });
            check(removed.Studies.Single().Attachments.Count==0 && !File.Exists(repository.Attachments.PathFor(file)),"Saving attachment removal cleans unused managed file after commit");
        }
        finally
        {
            // Only this isolated fixture's managed copies; the outer runner removes its temporary root.
            foreach (var store in new[] { repository.Attachments,target.Attachments,abandoned.Attachments })
                if (Directory.Exists(store.Root)) foreach (var file in Directory.EnumerateFiles(store.Root,"*",SearchOption.AllDirectories)) File.SetAttributes(file,FileAttributes.Normal);
        }
    }
}
