using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private sealed record JournalReopenFixture(DateOnly Date, MoneyEntry[] Money, StudyEntry[] Studies);
    private async Task VerifyJournalFeaturesAsync(List<string> checks)
    {
        var start = checks.Count;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        async Task Idle() => await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var folder = Path.GetDirectoryName(repository.DatabasePath)!;
        var day = new DateOnly(2026,10,12);
        pendingChangeDecision = () => MessageBoxResult.No;
        try
        {
            if (statisticsOpen) CloseStatistics(); ClosePlanReminder(); CloseMeditation();
            await SelectDateAsync(day); SelectRecordTab("money");
            Check(money.Count == 0 && studies.Count == 0 && !Pending && !MoneyComposer.IsExpanded, "journal-new-date-starts-empty-with-collapsed-editor");
            MoneyAmount.Text = "12,000"; MoneyNote.Text = "점심";
            Check(Pending && MoneyComposer.IsExpanded && repository.Load(day).Money.Count == 0, "money-input-opens-editor-and-waits-for-explicit-save");
            MoneyComposer.IsExpanded = false; SelectRecordTab("study"); StudyTitle.Text = "C# 학습";
            StudyContent.Text = "자료형을 복습했다.\n직접 작성한 학습 기록"; StudySummary.Text = "값 형식과 참조 형식의 차이";
            StudyComposer.IsExpanded = false;
            Check(await SaveAsync() && SaveStatus.Text == "저장 완료" && !Pending && money.Count == 1 && studies.Count == 1, "save-persists-hidden-finance-and-study-drafts-together");
            var expenseId = money[0].Id; var studyId = studies[0].Id;
            MoneyKindChoice.SelectedIndex = 1; MoneyCategoryChoice.SelectedItem = "급여"; MoneyAmount.Text = "100,000";
            Check(await SaveAsync() && money.Count == 2 && money.Sum(e => e.Won) == 112000, "second-finance-entry-appends-income-with-correct-category");
            BeginMoneyEdit(expenseId); MoneyAmount.Text = "9,000";
            Check(await SaveAsync() && money.Count == 2 && money.Single(e => e.Id == expenseId).Won == 9000, "money-edit-preserves-id-and-replaces-instead-of-duplicating");
            BeginStudyEdit(studyId); StudySummary.Text = "직접 수정한 핵심 요약";
            Check(await SaveAsync() && studies.Count == 1 && studies[0].Id == studyId && studies[0].Summary == "직접 수정한 핵심 요약", "study-edit-preserves-body-title-and-identity");
            var revision = snapshot.Revision;
            foreach (var amount in new[] { "0", "-10", "1.5", "12,34" })
            {
                MoneyAmount.Text = amount;
                Check(!await SaveAsync() && MoneyAmount.Text == amount && repository.Load(day).Revision == revision && MoneyError.IsVisible,
                    "invalid-money-keeps-input-and-entire-saved-day-" + amount);
            }
            ClearMoneyEditor(); StudyContent.Text = "제목 없이 내용만";
            Check(!await SaveAsync() && StudyContent.Text == "제목 없이 내용만" && StudyError.IsVisible && repository.Load(day).Revision == revision,
                "study-without-title-is-rejected-inline-without-partial-write");
            ClearStudyEditor();
            MoneyKindChoice.SelectedIndex = 0; MoneyAmount.Text = "2,000"; MoneyNote.Text = "아직 저장하지 않은 지출";
            StudyTitle.Text = "아직 저장하지 않은 학습";
            SelectRecordTab("plan"); Theme_Click(this, new RoutedEventArgs()); Close(); RestoreWindow();
            Check(MoneyAmount.Text == "2,000" && StudyTitle.Text == "아직 저장하지 않은 학습" && Pending, "new-tab-drafts-survive-theme-hide-and-restore");
            pendingChangeDecision = () => MessageBoxResult.Cancel; await SelectDateAsync(day.AddDays(1));
            Check(selected == day && MoneyAmount.Text == "2,000" && StudyTitle.Text == "아직 저장하지 않은 학습", "cancel-date-navigation-protects-finance-and-study-drafts");
            pendingChangeDecision = () => MessageBoxResult.No; await SelectDateAsync(day.AddDays(1));
            Check(!Pending && money.Count == 0 && studies.Count == 0 && StudyTitle.Text == "", "discard-pending-new-tabs-before-changing-date");
            await SelectDateAsync(day);
            Check(money.Count == 2 && studies.Single().Summary == "직접 수정한 핵심 요약", "journal-returns-by-chosen-date");
            journalRemovalDecision = () => MessageBoxResult.No; DeleteMoney(expenseId); DeleteStudy(studyId);
            Check(money.Count == 2 && studies.Count == 1 && !Pending, "cancel-delete-keeps-both-record-types-clean");
            journalRemovalDecision = () => MessageBoxResult.Yes; DeleteMoney(expenseId); DeleteStudy(studyId);
            Check(Pending && repository.Load(day).Money.Count == 2 && repository.Load(day).Studies.Count == 1, "journal-delete-waits-for-save");
            Check(await SaveAsync() && money.Count == 1 && studies.Count == 0, "journal-delete-save-updates-database");
            MoneyKindChoice.SelectedIndex = 0; MoneyCategoryChoice.SelectedItem = "식비·카페"; MoneyAmount.Text = "12,000"; MoneyNote.Text = "점심";
            StudyTitle.Text = "자료형과 컬렉션"; StudyContent.Text = "기본 자료형을 복습하고 컬렉션을 비교했다.\n예제를 직접 작성했다.";
            StudySummary.Text = "상황에 맞는 자료구조를 선택하자."; Check(await SaveAsync(), "journal-final-fixture-saved");
            MoneyCategoryChoice.SelectedItem = "교통"; MoneyAmount.Text = "1,500"; MoneyNote.Text = "버스";
            Check(await SaveAsync(), "journal-second-consumption-category-saved");
            var expected = repository.Load(day);
            foreach (var dark in new[] { false, true })
            {
                settings.Dark = dark; ApplyTheme(); DrawCalendar(); SelectRecordTab("money"); MoneyPane.ScrollToTop(); await Idle();
                Check(MoneyDaySummary.Children.OfType<Grid>().SelectMany(g=>g.Children.OfType<TextBlock>()).Any(t=>t.Text=="100,000원") && MoneyDaySummary.Children.OfType<Grid>().SelectMany(g=>g.Children.OfType<TextBlock>()).Any(t=>t.Text=="13,500원"), "journal-day-summary-exact-" + dark);
                foreach (var tab in new[] { PlanTab,WorkoutTab,ReviewTab,MoneyTab,StudyTab })
                {
                    var text = (string)tab.Content; var formatted = CalendarFormattedText(new TextBlock { FontFamily = tab.FontFamily, FontSize = tab.FontSize }, text);
                    Check(formatted.Width <= tab.ActualWidth - tab.Padding.Left - tab.Padding.Right + 1, "five-journal-tab-label-fits-" + text + "-" + dark);
                }
                Capture("journal-money-" + (dark ? "dark" : "light") + ".png");
                BeginMoneyEdit(money.Single(e => e.Note == "점심").Id); await Idle(); MoneyPane.ScrollToBottom(); await Idle();
                Check(MoneyNote.TranslatePoint(new Point(0,MoneyNote.ActualHeight), MoneyPane).Y <= MoneyPane.ActualHeight + 1,
                    "money-last-field-reachable-" + dark);
                Capture("journal-money-editor-" + (dark ? "dark" : "light") + ".png"); ClearMoneyEditor(); UpdateDraftStatus();
                SelectRecordTab("study"); StudyPane.ScrollToTop(); await Idle(); Capture("journal-study-" + (dark ? "dark" : "light") + ".png");
                BeginStudyEdit(studies[0].Id); await Idle(); StudyPane.ScrollToBottom(); await Idle();
                var last = StudySummary.TranslatePoint(new Point(0,StudySummary.ActualHeight), StudyPane).Y;
                Check(last <= StudyPane.ActualHeight + 1 && SaveButton.TranslatePoint(new Point(0,SaveButton.ActualHeight), Details).Y <= Details.ActualHeight + 1,
                    "study-last-field-and-save-reachable-" + dark);
                Capture("journal-study-editor-" + (dark ? "dark" : "light") + ".png"); ClearStudyEditor(); UpdateDraftStatus();
            }
            await SelectDateAsync(day); statisticsReport = null; statisticsMode = "week"; statisticsCustom = null; await OpenStatisticsAsync();
            Check(statisticsAnchor == day && journalStatistics?.Range == DateRange.Week(day), "journal-statistics-open-on-selected-week");
            Check(journalStatistics?.Income == 100000 && journalStatistics.Expense == 13500 && journalStatistics.Studies == 1,
                "journal-weekly-statistics-use-saved-date-records");
            statisticsFilter = new("사이클", "cycling-indoor"); await RefreshStatisticsAsync();
            Check(journalStatistics?.Income == 100000 && journalStatistics.Expense == 13500, "exercise-filter-does-not-filter-money");
            statisticsMode = "month"; await RefreshStatisticsAsync();
            Check(journalStatistics?.Range == DateRange.Month(day) && journalStatistics.Income == 100000 && journalStatistics.Expense == 13500, "journal-monthly-statistics-reconcile");
            foreach (var dark in new[] { false,true })
            {
                settings.Dark = dark; ApplyTheme(); DrawStatistics(); StatisticsPane.ScrollToTop(); await Idle();
                Capture("journal-statistics-" + (dark ? "dark" : "light") + ".png");
            }
            CloseStatistics();
            var backupPath = Path.Combine(folder,"journal-manual-export.json"); await ExportBackupAsync(backupPath);
            var backup = BackupFile.Read(backupPath);
            Check(backup.Version == 8 && backup.MoneyRecords == 3 && backup.Studies == 1 && repository.Load(day).Money.Count == 3, "journal-export-v5-keeps-originals");
            StudyTitle.Text = "복원으로 교체할 임시 기록"; Check(await SaveAsync(), "journal-fixture-before-backup-restore");
            backupReplacementDecision = _ => MessageBoxResult.No;
            Check(!await RestoreBackupAsync(backupPath) && studies.Count == 2, "cancel-journal-restore-keeps-current-records");
            backupReplacementDecision = _ => MessageBoxResult.Yes;
            Check(await RestoreBackupAsync(backupPath) && money.SequenceEqual(expected.Money) && studies.SequenceEqual(expected.Studies), "confirmed-v4-restore-updates-new-tabs-and-keeps-existing-day-records");
            var restoredAnniversaries = repository.LoadAnniversaries();
            Check(restoredAnniversaries.Select(a => a with { Revision = 0 }).SequenceEqual(backup.Anniversaries.Select(a => a with { Revision = 0 })),
                "journal-backup-restore-preserves-anniversary-rules-and-origin-dates");
            // Full restore advances conflict revisions; restart checks must expect the restored revisions.
            File.WriteAllText(Path.Combine(folder,"anniversary-expected.json"), JsonSerializer.Serialize(restoredAnniversaries));
            var fixture = new JournalReopenFixture(day, money.ToArray(), studies.ToArray());
            File.WriteAllText(Path.Combine(folder,"journal-reopen-expected.json"), JsonSerializer.Serialize(fixture));
            File.WriteAllText(Path.Combine(folder,"journal.json"), JsonSerializer.Serialize(new { success = true, count = checks.Count-start,
                checks = checks.Skip(start).ToArray(), personalDataUsed = false, schema = 12, backupVersion = 8 }));
            SelectRecordTab("study"); settings.Dark = false; ApplyTheme();
        }
        finally { pendingChangeDecision = null; journalRemovalDecision = null; backupReplacementDecision = null; }
    }
    private async Task VerifyJournalReopenAsync(string folder)
    {
        var path = Path.Combine(folder,"journal-reopen-expected.json");
        if (!File.Exists(path)) return;
        var expected = JsonSerializer.Deserialize<JournalReopenFixture>(File.ReadAllText(path))!;
        await SelectDateAsync(expected.Date);
        if (!money.SequenceEqual(expected.Money) || !studies.SequenceEqual(expected.Studies) || Pending)
            throw new InvalidOperationException("Finance or study did not restore after process restart");
        File.WriteAllText(Path.Combine(folder,"journal-reopen.json"), JsonSerializer.Serialize(new { success = true, restoredInNewProcess = true, money = money.Count, studies = studies.Count, personalDataUsed = false }));
    }
}
