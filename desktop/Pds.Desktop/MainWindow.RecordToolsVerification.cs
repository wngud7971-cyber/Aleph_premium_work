using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private sealed record ToolsFixture(DateOnly Date,Guid StudyId,Guid DeletedId,long Budget,Guid RuleId);
    private void CaptureUtility(Window window,string name)
    {
        CalmMotion.Settle(window);window.UpdateLayout();var content=(FrameworkElement)window.Content;
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!,name));encoder.Save(stream);
    }
    private async Task VerifyRecordToolsAsync(List<string> checks)
    {
        var start=checks.Count;void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var folder=Path.GetDirectoryName(repository.DatabasePath)!;var day=new DateOnly(2026,6,12);JournalToolsWindow? tools=null;FixedCostEditWindow? editor=null;
        var source=Path.Combine(folder,"검증 학습 첨부.txt");await File.WriteAllTextAsync(source,"보관·복구 시험 원문");var file=repository.Attachments.Import(source);
        try
        {
            if(statisticsOpen)CloseStatistics();CloseMeditation();ClosePlanReminder();
            var study=StudyEntry.Create("모아보기검증 학습","본문 검색확인","직접 핵심 요약") with {Attachments=[file]};var plan=PlanEntry.Create("모아보기검증 계획") with {Done=true,ActualDate=day};
            var expense=MoneyEntry.Create(MoneyKind.Expense,30000,"식비·카페") with {Title="모아보기검증 식비"};var savings=MoneyEntry.Create(MoneyKind.Expense,100000,"적금·저축");
            repository.Save(repository.Load(day) with {Plans=[plan],Studies=[study],Money=[expense,savings],Workouts=[WorkoutEntry.Create("walking",600,null)],Review=new("잘한 일","","다음 행동")});
            var rule=repository.LoadFixedCosts().First(r=>r.Enabled);
            editor=new(repository,rule,this,new DateOnly(2026,10,18)){ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=-12000,DiscardDecision=()=>true};editor.Show();await Idle();
            editor.ApplyMonth.Text="2026-10";editor.AmountInput.Text="120000";
            try{await editor.SaveChangesAsync();throw new InvalidOperationException("Retroactive editor save unexpectedly succeeded");}catch(ArgumentException){Check(editor.IsVisible && editor.AmountInput.Text=="120000" && repository.LoadFixedCosts().Single(r=>r.Id==rule.Id).Revision==rule.Revision,"recurring-editor-rejects-past-apply-month-and-preserves-input");}
            editor.ApplyMonth.Text="2026-11";editor.NameInput.Text="변경할 반복 내역";editor.EndEnabled.IsChecked=true;editor.EndMonth.Text="2026-12";CaptureUtility(editor,"tools-fixed-change-light.png");await editor.SaveChangesAsync();
            Check(editor.Saved && repository.LoadFixedCosts().Single(r=>r.Id==rule.Id).Changes.Single().Month==new DateOnly(2026,11,1) && repository.LoadFixedCosts().Single(r=>r.Id==rule.Id).EndMonth==new DateOnly(2026,12,1),"recurring-editor-saves-future-amount-and-inclusive-end-month");editor=null;
            settings.Dark=false;ApplyTheme();DrawCalendar();tools=new(repository,this,day,true){ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=-12000,Confirmation=_=>false};tools.Show();await Idle();
            tools.QueryInput.Text="모아보기검증";await tools.SearchAsync();Check(tools.SearchResult?.Total==3,"tools-search-renders-plan-study-and-money-results");CaptureUtility(tools,"tools-search-light.png");
            tools.QueryInput.Text="검증 학습 첨부";await tools.SearchAsync();Check(tools.SearchResult?.Items.Single().Id==study.Id,"tools-search-matches-attached-filename-without-file-analysis");
            var hit=tools.SearchResult!.Items.Single();tools.QueryInput.Text=" ";try{await tools.SearchAsync();throw new InvalidOperationException("Empty query accepted");}catch(ArgumentException){Check(tools.QueryInput.Text==" ","tools-invalid-search-keeps-query-for-correction");}
            await tools.LoadSummaryAsync(day);await tools.SwitchAsync("summary");
            Check(tools.Digest is {Studies:1,StudyDays:1} && tools.Digest.Activity.Done==1 && tools.Digest.Money.Savings==100000,"monthly-digest-view-reconciles-study-workout-plan-and-savings");
            tools.BudgetInput.Text="0";try{await tools.SaveBudgetAsync();throw new InvalidOperationException("Zero budget accepted");}catch(ArgumentException){Check(repository.BudgetFor(day).Budget is null && tools.BudgetInput.Text=="0","budget-invalid-input-does-not-save-or-erase-draft");}
            tools.BudgetInput.Text="25,000";await tools.SaveBudgetAsync();Check(tools.Digest?.Budget.Remaining==-5000 && tools.Digest.Budget.Percent==120,"budget-view-renders-overrun-and-excludes-savings");
            tools.BudgetInput.Text="26,000";await tools.SwitchAsync("search");await tools.SwitchAsync("summary");Check(tools.BudgetDirty && tools.BudgetInput.Text=="26,000" && repository.BudgetFor(day).Budget==25000,"budget-draft-survives-tab-switch-without-auto-save");
            tools.Close();Check(tools.IsVisible && tools.BudgetInput.Text=="26,000","cancel-close-protects-unsaved-budget-draft");tools.BudgetInput.Text="25,000";
            var saved=repository.Load(day);repository.Save(saved with {Plans=[],Studies=[]});await tools.SwitchAsync("trash");
            var trashedStudy=tools.TrashResult!.Items.Single(i=>i.Study?.Id==study.Id);Check(File.Exists(repository.Attachments.PathFor(file)),"trash-view-keeps-deleted-study-attachment");
            await tools.RestoreAsync(trashedStudy.Id);Check(repository.Load(day).Studies.Count==0,"cancel-individual-restore-keeps-trash-and-current-day");tools.Confirmation=_=>true;await tools.RestoreAsync(trashedStudy.Id);
            Check(repository.Load(day).Studies.Single()==study && repository.Attachments.Require(file,true)==repository.Attachments.PathFor(file),"confirmed-individual-study-restore-recovers-text-and-file");
            var trashedPlan=repository.DeletedRecords().Items.Single(i=>i.Plan?.Id==plan.Id);await tools.RestoreAsync(trashedPlan.Id);Check(repository.Load(day).Plans.Single()==plan,"individual-plan-recovery-keeps-completion-and-original-date");
            var dummyDate=day.AddDays(1);var dummy=PlanEntry.Create("영구 삭제 시험") with {Done=true,ActualDate=dummyDate};repository.Save(repository.Load(dummyDate) with {Plans=[dummy]});repository.Save(repository.Load(dummyDate) with {Plans=[]});await tools.LoadTrashAsync();var dummyTrash=tools.TrashResult!.Items.Single(i=>i.Plan?.Id==dummy.Id);
            tools.Confirmation=_=>false;await tools.DeleteForeverAsync(dummyTrash.Id);Check(repository.DeletedRecords().Items.Any(i=>i.Id==dummyTrash.Id),"cancel-permanent-delete-retains-recoverable-record");tools.Confirmation=_=>true;await tools.DeleteForeverAsync(dummyTrash.Id);Check(repository.DeletedRecords().Items.All(i=>i.Id!=dummyTrash.Id),"confirmed-permanent-delete-removes-only-selected-trash-item");
            foreach(var dark in new[]{false,true})
            {
                tools.Close();tools=null;settings.Dark=dark;ApplyTheme();DrawCalendar();tools=new(repository,this,day,true){ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=-12000,Confirmation=_=>true};tools.Show();await Idle();
                Check(tools.FontFamily.Source==FontFamily.Source && tools.FontSize==FontSize,"tools-native-window-preserves-app-font-in-theme-"+dark);
                Check(((System.Windows.Controls.Grid)((JournalToolsView)tools.Content).Content).Background is SolidColorBrush paper && paper.Color.A==255 && paper.Color==((SolidColorBrush)FindResource("Paper")).Color,"tools-content-has-opaque-theme-paper-background-"+dark);
                tools.QueryInput.Text="모아보기검증";await tools.SearchAsync();Check(tools.SearchResult?.Total==3 && tools.ActualWidth<=SystemParameters.WorkArea.Width,"tools-search-viewport-and-content-fit-"+dark);CaptureUtility(tools,"tools-search-"+(dark?"dark":"light")+".png");
                await tools.SwitchAsync("trash");Check(tools.TrashResult is not null,"trash-pagination-view-loads-"+dark);CaptureUtility(tools,"tools-trash-"+(dark?"dark":"light")+".png");
                await tools.SwitchAsync("summary");Check(tools.Digest?.Budget.Budget==25000 && !tools.BudgetDirty,"monthly-digest-budget-renders-clean-in-theme-"+dark);CaptureUtility(tools,"tools-digest-"+(dark?"dark":"light")+".png");
                await VerifyToolsAlignmentAsync(tools,checks,dark);
            }
            tools.Close();tools=null;await NavigateSearchHitAsync(hit);Check(selected==day && month==new DateOnly(2026,6,1) && StudyPane.IsVisible && !Pending,"search-result-navigation-opens-correct-calendar-month-date-and-record-tab");
            var second=repository.Attachments.Import(source);var deletedStudy=StudyEntry.Create("휴지통 첨부 보관") with {Attachments=[second]};var trashDate=day.AddDays(2);repository.Save(repository.Load(trashDate) with {Studies=[deletedStudy]});repository.Save(repository.Load(trashDate) with {Studies=[]});var toRecover=repository.DeletedRecords().Items.Single(i=>i.Study?.Id==deletedStudy.Id);
            var zip=Path.Combine(folder,"record-tools-backup.zip");await ExportBackupAsync(zip);using(var prepared=BackupArchive.Read(zip))Check(prepared.Backup.Version==8 && prepared.Backup.Deleted.Any(d=>d.Id==toRecover.Id) && prepared.Backup.Attachments==4 && prepared.Backup.Budgets.Count==1,"tools-backup-includes-budget-rule-changes-and-live-plus-trashed-attachment-payloads");
            repository.RemoveBudget(day);repository.PermanentlyDelete(toRecover.Id);backupReplacementDecision=_=>MessageBoxResult.No;
            Check(!await RestoreBackupAsync(zip) && repository.BudgetFor(day).Budget is null,"cancel-tools-full-restore-keeps-current-state");backupReplacementDecision=_=>MessageBoxResult.Yes;
            Check(await RestoreBackupAsync(zip) && repository.BudgetFor(day).Budget==25000 && repository.DeletedRecords().Items.Any(i=>i.Id==toRecover.Id) && File.Exists(repository.Attachments.PathFor(second)),"confirmed-tools-full-restore-recovers-budget-and-trash-only-file");
            File.WriteAllText(Path.Combine(folder,"anniversary-expected.json"),JsonSerializer.Serialize(repository.LoadAnniversaries()));
            var prior=JsonSerializer.Deserialize<FixedCostReopenFixture>(File.ReadAllText(Path.Combine(folder,"fixed-costs-reopen-expected.json")))!;
            File.WriteAllText(Path.Combine(folder,"fixed-costs-reopen-expected.json"),JsonSerializer.Serialize(prior with {Rules=repository.LoadFixedCosts().ToArray()}));
            File.WriteAllText(Path.Combine(folder,"record-tools-reopen-expected.json"),JsonSerializer.Serialize(new ToolsFixture(day,study.Id,toRecover.Id,25000,rule.Id)));
            File.WriteAllText(Path.Combine(folder,"record-tools.json"),JsonSerializer.Serialize(new {success=true,count=checks.Count-start,checks=checks.Skip(start).ToArray(),personalDataUsed=false,schema=12,backupVersion=8}));
        }
        finally{if(tools is not null){tools.Confirmation=_=>true;tools.Close();}if(editor is not null){editor.DiscardDecision=()=>true;editor.Close();}backupReplacementDecision=null;settings.Dark=false;ApplyTheme();DrawCalendar();}
    }
    private async Task VerifyRecordToolsReopenAsync(string folder)
    {
        var path=Path.Combine(folder,"record-tools-reopen-expected.json");if(!File.Exists(path))return;var expected=JsonSerializer.Deserialize<ToolsFixture>(File.ReadAllText(path))!;await SelectDateAsync(expected.Date);
        var trash=repository.DeletedRecords().Items.Single(i=>i.Id==expected.DeletedId);
        if(repository.BudgetFor(expected.Date).Budget!=expected.Budget || studies.Single().Id!=expected.StudyId || repository.LoadFixedCosts().Single(r=>r.Id==expected.RuleId).Changes.Count!=1 || Pending)throw new InvalidOperationException("Record tools state did not survive process restart");
        foreach(var file in trash.Files)repository.Attachments.Require(file,true);
        var feedbackPath=Path.Combine(folder,"feedback-reopen-expected.json");
        if(File.Exists(feedbackPath))
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(feedbackPath));var root=doc.RootElement;
            var date=root.GetProperty("Date").Deserialize<DateOnly>();var id=root.GetProperty("Id").GetGuid();
            if(repository.LoadStudy(date,id)?.DurationMinutes!=root.GetProperty("Minutes").GetInt32())throw new InvalidOperationException("Study duration did not survive a separate process restart");
            File.WriteAllText(Path.Combine(folder,"feedback-reopen.json"),JsonSerializer.Serialize(new{success=true,minutes=125,separateProcess=true,personalDataUsed=false}));
        }
        File.WriteAllText(Path.Combine(folder,"record-tools-reopen.json"),JsonSerializer.Serialize(new{success=true,budget=expected.Budget,trashAttachmentVerified=true,restoredInNewProcess=true,personalDataUsed=false}));
    }
}
