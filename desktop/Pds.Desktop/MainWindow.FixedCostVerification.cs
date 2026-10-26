using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private sealed record FixedCostReopenFixture(DateOnly Date,MoneyEntry[] Money,FixedCostRule[] Rules);
    private async Task VerifyFixedCostsAsync(List<string> checks)
    {
        var start=checks.Count;
        void Check(bool value,string name) { if(!value)throw new InvalidOperationException(name);checks.Add(name); }
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var folder=Path.GetDirectoryName(repository.DatabasePath)!;var jan=new DateOnly(2026,1,31);var feb=new DateOnly(2026,2,28);
        pendingChangeDecision=()=>MessageBoxResult.No;
        try
        {
            if(statisticsOpen)CloseStatistics();ClosePlanReminder();CloseMeditation();
            await SelectDateAsync(jan);SelectRecordTab("money");MoneyKindChoice.SelectedIndex=0;MoneyCategoryChoice.SelectedItem="통신";
            MoneyTitle.Text="휴대폰 요금";MoneyAmount.Text="50,000";MoneyFixed.IsChecked=true;MoneyRepeat.IsChecked=true;
            Check(Pending && MoneyComposer.IsExpanded && repository.LoadFixedCosts().Count==0,"fixed-cost-draft-waits-for-save-and-shows-explicit-repeat");
            SelectRecordTab("study");settings.Dark=true;ApplyTheme();Close();RestoreWindow();
            Check(MoneyTitle.Text=="휴대폰 요금" && MoneyRepeat.IsChecked==true && Pending,"fixed-title-and-repeat-survive-tabs-theme-and-hide-restore");
            SelectRecordTab("money");MoneyTitle.Clear();
            Check(!await SaveAsync() && MoneyError.IsVisible && MoneyAmount.Text=="50,000" && repository.LoadFixedCosts().Count==0,"fixed-without-name-rejects-save-and-keeps-all-input");
            MoneyTitle.Text="휴대폰 요금";
            Check(await SaveAsync() && money.Single().FixedCostId==money.Single().Id && repository.LoadFixedCosts().Count==1,"fixed-save-registers-first-payment-and-future-template-once");
            var id=money.Single().Id;BeginMoneyEdit(id);MoneyAmount.Text="60,000";
            Check(!MoneyRepeat.IsEnabled && !MoneyKindChoice.IsEnabled && await SaveAsync() && repository.LoadFixedCosts().Single().Template.Won==50000,"dated-edit-keeps-future-template-and-record-kind-stable");
            await SelectDateAsync(feb);MoneyTitle.Text="입력 보호 시험";
            var dueNow=new DateTimeOffset(2026,2,28,12,0,0,TimeSpan.FromHours(9));
            await MaintainAsync(dueNow);
            Check(Pending && MoneyTitle.Text=="입력 보호 시험" && repository.Load(feb).Money.Count==0,"due-day-maintenance-defers-current-day-while-user-has-unsaved-input");
            ClearMoneyEditor();UpdateDraftStatus();await MaintainAsync(dueNow);
            Check(money.Count==1 && repository.ApplyFixedCosts(feb).Added==0,"due-day-maintenance-inserts-once-and-refreshes-day-after-draft-is-cleared");
            await SelectDateAsync(feb);SelectRecordTab("money");
            Check(money.Single().Title=="휴대폰 요금" && money.Single().Won==50000 && FixedCostHeading.Text.Contains("1개"),"generated-money-and-active-rule-name-visible-on-chosen-date");
            foreach(var item in new[]{("넷플릭스 구독",15000,"구독비"),("청년 적금",100000,"적금·저축")})
            {
                MoneyTitle.Text=item.Item1;MoneyCategoryChoice.SelectedItem=item.Item3;MoneyAmount.Text=item.Item2.ToString();MoneyFixed.IsChecked=true;MoneyRepeat.IsChecked=true;
                Check(await SaveAsync(),"save-fixed-"+item.Item3);
            }
            Check(string.Join("|",MoneyDaySummary.Children.OfType<Grid>().SelectMany(g=>g.Children.OfType<TextBlock>()).Select(t=>t.Text)).Contains("65,000원") && string.Join("|",MoneyDaySummary.Children.OfType<Grid>().SelectMany(g=>g.Children.OfType<TextBlock>()).Select(t=>t.Text)).Contains("100,000원") && money.Count==3,"day-summary-separates-consumption-and-savings-with-all-three-names");
            BeginMoneyEdit(money.Single(e=>e.IsSaving).Id);MoneyTitle.Text="취소할 이름";MoneyCancel_Click(this,new RoutedEventArgs());
            Check(!Pending && money.Single(e=>e.IsSaving).Title=="청년 적금","cancel-fixed-record-edit-retains-saved-name-and-rule");
            fixedCostStopDecision=()=>MessageBoxResult.No;
            Check(!await StopFixedCostAsync(id) && repository.LoadFixedCosts().All(r=>r.Enabled),"cancel-repeat-stop-retains-active-rules");
            fixedCostStopDecision=()=>MessageBoxResult.Yes;
            Check(await StopFixedCostAsync(id) && !repository.LoadFixedCosts().Single(r=>r.Id==id).Enabled && money.Count==3,"repeat-stop-persists-immediately-and-keeps-current-payments");
            foreach(var dark in new[]{false,true})
            {
                settings.Dark=dark;ApplyTheme();month=new DateOnly(feb.Year,feb.Month,1);await RefreshCalendarAsync();SelectRecordTab("money");
                System.Windows.Input.Keyboard.ClearFocus();MoneyPane.Focus();MoneyPane.UpdateLayout();MoneyPane.ScrollToTop();await Idle();MoneyPane.ScrollToTop();await Idle();
                Check(MoneyPane.VerticalOffset<1,"fixed-cost-saved-list-capture-starts-at-top-"+dark);
                Capture("fixed-costs-day-"+(dark?"dark":"light")+".png");
                BeginMoneyEdit(money.Single(e=>e.IsSaving).Id);await Idle();MoneyPane.ScrollToBottom();await Idle();
                Check(MoneyNote.TranslatePoint(new Point(0,MoneyNote.ActualHeight),MoneyPane).Y<=MoneyPane.ActualHeight+1 && SaveButton.TranslatePoint(new Point(0,SaveButton.ActualHeight),Details).Y<=Details.ActualHeight+1,"fixed-cost-editor-scroll-and-fixed-save-accessible-"+dark);
                Capture("fixed-costs-editor-"+(dark?"dark":"light")+".png");
                System.Windows.Input.Keyboard.ClearFocus();MoneyPane.Focus();var top=MoneyComposer.TranslatePoint(new Point(),MoneyPane).Y;MoneyPane.ScrollToVerticalOffset(MoneyPane.VerticalOffset+top);await Idle();
                Capture("fixed-costs-input-"+(dark?"dark":"light")+".png");ClearMoneyEditor();UpdateDraftStatus();
            }
            statisticsReport=null;statisticsMode="month";statisticsCustom=null;await OpenStatisticsAsync();
            Check(journalStatistics is { Expense:165000,Savings:100000,Consumption:65000,FixedRecords:3 } && journalStatistics.FixedLines.Any(l=>l.Title=="넷플릭스 구독"),"monthly-outflow-savings-and-fixed-named-details-reconcile");
            Check(journalStatistics!.Categories.Sum(c=>c.Won)==65000 && journalStatistics.Categories.All(c=>c.Category!="적금·저축"),"savings-excluded-from-consumption-percentage-denominator");
            foreach(var dark in new[]{false,true}) { settings.Dark=dark;ApplyTheme();DrawStatistics();StatisticsPane.ScrollToVerticalOffset(450);await Idle();Capture("fixed-costs-statistics-"+(dark?"dark":"light")+".png"); }
            CloseStatistics();
            var zip=Path.Combine(folder,"fixed-costs-export.zip");await ExportBackupAsync(zip);
            using(var archive=BackupArchive.Read(zip))Check(archive.Backup.Version==8 && archive.Backup.FixedCosts.Count==3 && archive.Backup.Attachments==2,"fixed-cost-zip-includes-rules-occurrences-and-prior-study-attachments");
            var before=repository.CreateBackup();backupReplacementDecision=_=>MessageBoxResult.No;
            Check(!await RestoreBackupAsync(zip) && repository.LoadFixedCosts().SequenceEqual(before.FixedCosts),"cancel-fixed-cost-restore-preserves-current-rules");
            backupReplacementDecision=_=>MessageBoxResult.Yes;
            Check(await RestoreBackupAsync(zip) && repository.LoadFixedCosts().SequenceEqual(before.FixedCosts) && repository.ApplyFixedCosts(feb).Added==0,"restore-fixed-cost-rules-and-no-duplicate-occurrences");
            File.WriteAllText(Path.Combine(folder,"anniversary-expected.json"),JsonSerializer.Serialize(repository.LoadAnniversaries()));
            File.WriteAllText(Path.Combine(folder,"fixed-costs-reopen-expected.json"),JsonSerializer.Serialize(new FixedCostReopenFixture(feb,money.ToArray(),repository.LoadFixedCosts().ToArray())));
            File.WriteAllText(Path.Combine(folder,"fixed-costs.json"),JsonSerializer.Serialize(new { success=true,count=checks.Count-start,checks=checks.Skip(start).ToArray(),personalDataUsed=false,schema=12,backupVersion=8 }));
            settings.Dark=false;ApplyTheme();
        }
        finally { pendingChangeDecision=null;fixedCostStopDecision=null;backupReplacementDecision=null; }
    }
    private async Task VerifyFixedCostReopenAsync(string folder)
    {
        var path=Path.Combine(folder,"fixed-costs-reopen-expected.json");if(!File.Exists(path))return;
        var expected=JsonSerializer.Deserialize<FixedCostReopenFixture>(File.ReadAllText(path))!;await SelectDateAsync(expected.Date);
        if(!money.SequenceEqual(expected.Money) || !repository.LoadFixedCosts().SequenceEqual(expected.Rules) || repository.ApplyFixedCosts(expected.Date).Added!=0 || Pending)
            throw new InvalidOperationException("Recurring costs did not survive process restart");
        File.WriteAllText(Path.Combine(folder,"fixed-costs-reopen.json"),JsonSerializer.Serialize(new {success=true,records=money.Count,rules=expected.Rules.Length,restoredInNewProcess=true,personalDataUsed=false}));
    }
}
