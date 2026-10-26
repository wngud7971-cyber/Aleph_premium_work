using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private sealed record AttachmentReopenFixture(DateOnly Date,StudyEntry Study);
    private async Task VerifyAttachmentsAsync(List<string> checks)
    {
        var start=checks.Count;
        void Check(bool value,string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        async Task Idle() => await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var folder=Path.GetDirectoryName(repository.DatabasePath)!;
        var day=new DateOnly(2026,10,11);
        var source=Path.Combine(folder,"첨부 보관 시험.txt"); var pdf=Path.Combine(folder,"학습 자료 예시.pdf");
        await File.WriteAllTextAsync(source,"보관할 첨부 원문\n두 번째 줄"); await File.WriteAllTextAsync(pdf,"Synthetic attachment payload; no PDF analysis performed.");
        try
        {
            await SelectDateAsync(day); SelectRecordTab("study");
            Check(await ImportStudyAttachmentsAsync([source]) && Pending && studyDraftAttachments.Count==1 && repository.Load(day).Studies.Count==0,"attachment-import-is-a-pending-local-copy-until-save");
            var discarded=studyDraftAttachments[0];
            Check(!await SaveAsync() && studyDraftAttachments.Count==1 && File.Exists(repository.Attachments.PathFor(discarded)),"file-only-draft-requires-title-and-keeps-copy-on-validation-error");
            StudyCancel_Click(this,new RoutedEventArgs());
            Check(!Pending && !File.Exists(repository.Attachments.PathFor(discarded)) && File.Exists(source),"cancel-attachment-draft-removes-managed-copy-and-preserves-source");
            var revision=repository.Load(day).Revision;
            Check(!await ImportStudyAttachmentsAsync([source,Path.Combine(folder,"missing.txt")]) && studyDraftAttachments.Count==0 && repository.Load(day).Revision==revision,"failed-multi-file-import-rolls-back-only-new-copies");
            Check(!await ImportStudyAttachmentsAsync(Enumerable.Repeat(source,11).ToArray()) && studyDraftAttachments.Count==0,"attachment-count-limit-does-not-partially-import");
            Check(await ImportStudyAttachmentsAsync([source,pdf]),"import-multiple-supported-study-files");
            var draftIds=studyDraftAttachments.Select(f=>f.Id).ToArray(); Theme_Click(this,new RoutedEventArgs());
            var draftText=((System.Windows.Controls.StackPanel)StudyAttachmentDraftList.Children[0]).Children.OfType<System.Windows.Controls.TextBlock>().First();
            Check(Pending && studyDraftAttachments.Select(f=>f.Id).SequenceEqual(draftIds) && ReferenceEquals(draftText.Foreground,ResourceBrush("Ink")),"attachment-draft-and-text-colors-follow-theme-switch");
            StudyTitle.Text="첨부 자료와 함께 복습"; StudyContent.Text="오늘 정리한 내용과 참고 자료를 함께 보관했다."; StudySummary.Text="필요할 때 첨부를 열어 다시 읽기";
            Check(await SaveAsync() && !Pending && studies.Single().Attachments.Count==2,"study-save-commits-two-file-links-with-text");
            var stored=studies.Single(); var textFile=stored.Attachments.Single(f=>f.Name.EndsWith(".txt")); var pdfFile=stored.Attachments.Single(f=>f.Name.EndsWith(".pdf"));
            File.Delete(source);
            string? opened=null; attachmentOpenAction=path=>opened=path;
            Check(await OpenStudyAttachmentAsync(textFile) && opened is not null && opened==repository.Attachments.PathFor(textFile) && File.ReadAllText(opened).Contains("두 번째 줄"),"attachment-open-resolves-managed-copy-after-original-deletion-without-launching-viewer-in-tests");
            Check(File.GetAttributes(opened!).HasFlag(FileAttributes.ReadOnly),"opened-study-copy-is-read-only");
            BeginStudyEdit(stored.Id); RemoveStudyAttachmentDraft(pdfFile.Id);
            Check(Pending && File.Exists(repository.Attachments.PathFor(pdfFile)) && repository.Load(day).Studies.Single().Attachments.Count==2,"removing-saved-file-waits-for-save-and-preserves-undo");
            StudyCancel_Click(this,new RoutedEventArgs());
            Check(!Pending && studies.Single().Attachments.Count==2 && File.Exists(repository.Attachments.PathFor(pdfFile)),"cancel-edit-keeps-both-saved-attachments");
            var backupPath=Path.Combine(folder,"attachments-backup.zip"); await ExportBackupAsync(backupPath);
            using (var prepared=BackupArchive.Read(backupPath)) Check(prepared.Backup.Attachments==2 && prepared.Backup.Version==8,"ui-export-zip-includes-all-attachment-payloads");
            BeginStudyEdit(stored.Id); RemoveStudyAttachmentDraft(pdfFile.Id);
            Check(await SaveAsync() && studies.Single().Attachments.Count==1 && !File.Exists(repository.Attachments.PathFor(pdfFile)),"saving-file-removal-cleans-unused-copy-and-keeps-study");
            backupReplacementDecision=_=>MessageBoxResult.No;
            Check(!await RestoreBackupAsync(backupPath) && studies.Single().Attachments.Count==1 && !File.Exists(repository.Attachments.PathFor(pdfFile)),"cancel-zip-restore-does-not-install-new-payloads");
            backupReplacementDecision=_=>MessageBoxResult.Yes;
            Check(await RestoreBackupAsync(backupPath) && studies.Single()==stored && File.Exists(repository.Attachments.Require(pdfFile,true)),"confirmed-zip-restore-recovers-deleted-file-and-study-links");
            var managed=repository.Attachments.PathFor(textFile); File.SetAttributes(managed,FileAttributes.Normal); File.Delete(managed);
            Check(!await OpenStudyAttachmentAsync(textFile) && studies.Single().Attachments.Count==2,"missing-managed-file-shows-error-without-deleting-record");
            Check(await RestoreBackupAsync(backupPath) && await OpenStudyAttachmentAsync(textFile),"valid-zip-restores-missing-managed-file");
            File.WriteAllText(Path.Combine(folder,"anniversary-expected.json"),JsonSerializer.Serialize(repository.LoadAnniversaries()));
            foreach (var dark in new[] { false,true })
            {
                settings.Dark=dark; ApplyTheme(); DrawCalendar(); SelectRecordTab("study"); StudyPane.ScrollToTop(); await Idle();
                Capture("study-attachments-"+(dark?"dark":"light")+".png");
                BeginStudyEdit(stored.Id); await Idle(); StudyPane.ScrollToBottom(); await Idle();
                Check(AttachStudyFileButton.TranslatePoint(new Point(0,AttachStudyFileButton.ActualHeight),StudyPane).Y<=StudyPane.ActualHeight+1,
                    "attachment-controls-reachable-in-both-themes-"+dark);
                Capture("study-attachments-editor-"+(dark?"dark":"light")+".png");
                ClearStudyEditor(); UpdateDraftStatus();
            }
            await SelectDateAsync(day); statisticsMode="month"; statisticsReport=null; await OpenStatisticsAsync();
            var found=studyOverview!.Items.Single(i=>i.Id==stored.Id);
            var row=StudyOverviewList!.Children.OfType<System.Windows.Controls.Border>().Single(b=>(b.Tag as StudyListing)?.Id==found.Id);
            var expander=((System.Windows.Controls.StackPanel)row.Child).Children.OfType<System.Windows.Controls.Expander>().Single();
            expander.IsExpanded=true;
            for (var i=0;i<100 && busy;i++) { await Task.Delay(10); await Idle(); }
            Check(expander.Content is System.Windows.Controls.StackPanel content && content.Children.OfType<System.Windows.Controls.StackPanel>().Any(),"study-overview-full-detail-includes-attachment-opening-list");
            JumpToStudyOverview(); await Idle(); Capture("study-attachments-overview.png"); CloseStatistics();
            File.WriteAllText(Path.Combine(folder,"attachments-reopen-expected.json"),JsonSerializer.Serialize(new AttachmentReopenFixture(day,stored)));
            File.WriteAllText(Path.Combine(folder,"attachments.json"),JsonSerializer.Serialize(new { success=true,count=checks.Count-start,checks=checks.Skip(start).ToArray(),
                viewerActuallyLaunched=false,sourceFilesAreSynthetic=true,personalDataUsed=false,schema=12,backupVersion=8 }));
            settings.Dark=false; ApplyTheme(); DrawCalendar();
        }
        finally { attachmentOpenAction=null; backupReplacementDecision=null; }
    }
    private async Task VerifyAttachmentReopenAsync(string folder)
    {
        var path=Path.Combine(folder,"attachments-reopen-expected.json"); if (!File.Exists(path)) return;
        var expected=JsonSerializer.Deserialize<AttachmentReopenFixture>(File.ReadAllText(path))!;
        await SelectDateAsync(expected.Date);
        if (studies.Single()!=expected.Study || Pending) throw new InvalidOperationException("Attachment links did not survive restart");
        foreach (var file in studies.Single().Attachments) repository.Attachments.Require(file,true);
        File.WriteAllText(Path.Combine(folder,"attachments-reopen.json"),JsonSerializer.Serialize(new { success=true,restoredInNewProcess=true,files=studies.Single().Attachments.Count,viewerActuallyLaunched=false,personalDataUsed=false }));
    }
}
