using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyFeedbackBatchAsync(List<string> checks)
    {
        var start=checks.Count;var oldMotion=CalmMotion.AnimationOverride;var oldDark=settings.Dark;var oldAccent=settings.Accent;
        var folder=Path.GetDirectoryName(repository.DatabasePath)!;var day=new DateOnly(2024,9,16);JournalToolsWindow? tools=null;
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        IEnumerable<T> Find<T>(DependencyObject parent)where T:DependencyObject
        {for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T t)yield return t;foreach(var nested in Find<T>(child))yield return nested;}}
        try
        {
            CalmMotion.AnimationOverride=false;pendingChangeDecision=()=>MessageBoxResult.No;CloseMeditation();ClosePlanReminder();if(statisticsOpen)CloseStatistics();
            await SelectDateAsync(day);month=new(day.Year,day.Month,1);await RefreshCalendarAsync();SelectRecordTab("study");
            StudyTitle.Text="피드백 학습 시간";StudyContent.Text="내용과 분리된 시간";StudyHours.Text="1";StudyMinutes.Text="30";
            Check(HasStudyInput && Pending,"study-duration-fields-participate-in-unsaved-protection");
            Check(await SaveAsync() && repository.Load(day).Studies.Single().DurationMinutes==90,"study-ui-saves-one-hour-thirty-minutes");
            var id=studies.Single().Id;BeginStudyEdit(id);Check(StudyHours.Text=="1" && StudyMinutes.Text=="30","study-edit-loads-duration-parts");
            StudyHours.Text="2";StudyMinutes.Text="5";Check(await SaveAsync() && repository.Load(day).Studies.Single().Id==id && repository.Load(day).Studies.Single().DurationMinutes==125,"study-ui-edits-time-without-duplicating-record");
            BeginStudyEdit(id);StudyMinutes.Text="60";
            Check(!await SaveAsync() && StudyMinutes.Text=="60" && repository.Load(day).Studies.Single().DurationMinutes==125 && StudyError.IsVisible,"invalid-study-minutes-preserve-editor-and-stored-time");ClearStudyEditor();
            StudyMinutes.Text="15";pendingChangeDecision=()=>MessageBoxResult.Cancel;await SelectDateAsync(day.AddDays(1));
            Check(selected==day && StudyMinutes.Text=="15","duration-only-draft-is-protected-by-date-change-cancel");ClearStudyEditor();pendingChangeDecision=()=>MessageBoxResult.No;
            StudyTitle.Text="시간 미입력 학습";Check(await SaveAsync() && repository.Load(day).Studies.Last().DurationMinutes is null,"study-ui-allows-optional-duration-without-zero-filling");
            await SelectDateAsync(day.AddDays(1));await SelectDateAsync(day);BeginStudyEdit(id);
            Check(StudyHours.Text=="2" && StudyMinutes.Text=="5","study-duration-survives-date-navigation-and-editor-reopen");
            await Idle();StudyHours.BringIntoView();await Idle();
            Check(StudyHours.ActualHeight>=32 && Math.Abs(StudyHours.TranslatePoint(new Point(),StudyPane).Y-StudyMinutes.TranslatePoint(new Point(),StudyPane).Y)<1,"study-hours-and-minutes-align-and-have-readable-height");
            Capture("feedback-study-duration-light.png");ClearStudyEditor();
            foreach(var dark in new[]{false,true})
            {
                settings.Dark=dark;
                foreach(var accent in new[]{"#E3BACA","#DE9D3E"})
                {
                    settings.Accent=accent;ApplyTheme();await Idle();CalendarViewport.UpdateLayout();
                    var horizontal=Find<ScrollBar>(CalendarViewport).Single(b=>b.Orientation==Orientation.Horizontal);horizontal.ApplyTemplate();
                    var track=(Track)horizontal.Template.FindName("PART_Track",horizontal);track.Thumb.ApplyTemplate();
                    Check(((SolidColorBrush)track.Thumb.Background).Color==((SolidColorBrush)ResourceBrush("Accent")).Color,"horizontal-thumb-follows-accent-live-"+dark+accent);
                    var surface=(Border)track.Thumb.Template.FindName("ThumbSurface",track.Thumb);
                    Check(surface.Background==track.Thumb.Background,"horizontal-thumb-template-renders-selected-brush-"+dark+accent);
                }
                var vertical=Find<ScrollBar>(StudyPane).First(b=>b.Orientation==Orientation.Vertical);vertical.ApplyTemplate();
                var vtrack=(Track)vertical.Template.FindName("PART_Track",vertical);
                Check(((SolidColorBrush)vtrack.Thumb.Background).Color==((SolidColorBrush)ResourceBrush("Accent")).Color,"vertical-scroll-thumb-follows-accent-"+dark);
                settings.Accent=oldAccent;ApplyTheme();
                tools=new(repository,this,day,true,"search"){ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=-12000,Confirmation=_=>true};
                tools.Width=620;tools.Height=460;tools.Show();await Idle();await tools.LoadSummaryAsync(day);await tools.SwitchAsync("summary");await Idle();
                Check(JournalToolsWindow.ParseMonth("2026-9")==new DateOnly(2026,9,1) && JournalToolsWindow.ParseMonth("2026-09")==new DateOnly(2026,9,1),"single-and-double-digit-months-accepted-"+dark);
                tools.MonthInput.Text="2024-9";await tools.LoadSummaryAsync(JournalToolsWindow.ParseMonth(tools.MonthInput.Text));
                Check(tools.MonthInput.Text=="2024-09" && Find<Button>(tools).Any(b=>Equals(b.Content,"선택한 달 조회")),"month-normalizes-and-query-button-has-clear-name-"+dark);
                foreach(var invalid in new[]{"2026-0","2026-13","26-9","2026-009","2026/9","2026-9-extra"})
                {try{JournalToolsWindow.ParseMonth(invalid);throw new InvalidOperationException("accepted "+invalid);}catch(ArgumentException){Check(true,"invalid-month-retains-validation-"+invalid+dark);}}
                var texts=Find<TextBlock>(tools).ToArray();var groups=new[]{"계획","운동","학습","하루 회고","금전"}.Select(label=>texts.Single(t=>t.Text==label && t.FontWeight==FontWeights.SemiBold && t.Parent is StackPanel)).ToArray();
                Check(groups.All(t=>t.FontWeight==FontWeights.SemiBold) && groups.Select(t=>t.Parent).Distinct().Count()==5,"monthly-records-have-five-independent-labeled-groups-"+dark);
                Check(tools.Digest is{StudyMinutes:125,TimedStudies:1,Studies:2},"monthly-digest-exposes-only-known-study-time-and-coverage-"+dark);
                // Locate the actual summary scroll by its content, avoiding unrelated nested scroll viewers.
                var scroll=Find<ScrollViewer>(tools).Single(s=>s.Content is StackPanel panel && Find<TextBlock>(panel).Any(t=>t.Text.StartsWith("연간 활동 안내:")));
                var activityHeading=Find<TextBlock>(tools).Single(t=>t.Text=="한 달의 기록");activityHeading.BringIntoView();await Idle();CaptureUtility(tools,"feedback-monthly-groups-"+(dark?"dark":"light")+".png");
                foreach(var metric in Enumerable.Range(0,6))
                {
                    tools.AnnualMetricChoice.SelectedIndex=metric;await Idle();
                    var chart=Find<UniformGrid>(tools).Single(g=>g.Columns==12);
                    Check(chart.Children.Count==12 && chart.Children.Cast<Grid>().All(g=>g.ActualWidth>25),"annual-twelve-month-columns-fit-minimum-window-metric-"+metric+"-"+dark);
                }
                tools.AnnualMetricChoice.SelectedIndex=5;await tools.LoadSummaryAsync(day.AddMonths(1));
                Check(tools.AnnualMetricChoice.SelectedIndex==5,"annual-metric-selection-survives-month-change-"+dark);
                scroll=Find<ScrollViewer>(tools).Single(s=>s.Content is StackPanel panel && Find<TextBlock>(panel).Any(t=>t.Text.StartsWith("연간 활동 안내:")));scroll.ScrollToBottom();await Idle();
                var note=Find<TextBlock>(tools).Single(t=>t.Text.StartsWith("연간 활동 안내:"));Check(note.TranslatePoint(new Point(0,note.ActualHeight),scroll).Y<=scroll.ActualHeight+1,"annual-final-guidance-reachable-at-minimum-size-"+dark);
                tools.Height=760;await Idle();scroll.ScrollToBottom();await Idle();CaptureUtility(tools,"feedback-annual-"+(dark?"dark":"light")+".png");
                await tools.LoadSummaryAsync(new DateOnly(9999,12,1));await Idle();
                Check(Find<UniformGrid>(tools).Single(g=>g.Columns==12).Children.Cast<Grid>().All(g=>g.ToolTip is string detail && detail.Contains("미도래")),"future-empty-months-are-not-labeled-zero-"+dark);
                tools.Close();tools=null;
            }
            settings.Dark=oldDark;settings.Accent=oldAccent;ApplyTheme();
            StudyTitle.Text="홈 이동 보호";pendingChangeDecision=()=>MessageBoxResult.Cancel;await OpenStatisticsAsync();
            Check(!statisticsOpen && StudyTitle.Text=="홈 이동 보호","statistics-navigation-still-protects-unsaved-study-draft");ClearStudyEditor();pendingChangeDecision=()=>MessageBoxResult.No;
            await OpenStatisticsAsync();Check(statisticsOpen,"logo-home-fixture-starts-on-statistics");HomeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle();
            Check(!statisticsOpen && CalendarArea.IsVisible && selected==day && Brand.FontStyle==FontStyles.Italic,"pds-home-button-returns-calendar-with-date-and-brand-preserved");
            Check(HomeButton.Focusable && HomeButton.IsTabStop && System.Windows.Automation.AutomationProperties.GetName(HomeButton)=="캘린더 홈","home-logo-supports-keyboard-and-accessible-name");
            StudyTitle.Text="홈 버튼 입력 유지";HomeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Idle();Check(StudyTitle.Text=="홈 버튼 입력 유지" && Pending,"already-home-logo-does-not-discard-input");ClearStudyEditor();
            var backup=Path.Combine(folder,"feedback-batch-backup.zip");await ExportBackupAsync(backup);using(var prepared=BackupArchive.Read(backup))Check(prepared.Backup.Version==8 && prepared.Backup.Days.Single(d=>d.Date==day).Studies.Single(s=>s.Id==id).DurationMinutes==125,"ui-backup-manifest-includes-study-duration-v8");
            File.WriteAllText(Path.Combine(folder,"feedback-reopen-expected.json"),JsonSerializer.Serialize(new{Date=day,Id=id,Minutes=125}));
            File.WriteAllText(Path.Combine(folder,"feedback-batch.json"),JsonSerializer.Serialize(new{success=true,count=checks.Count-start,checks=checks.Skip(start).ToArray(),schema=12,backupVersion=8,personalDataUsed=false}));
        }
        finally
        {
            if(tools is not null){tools.Confirmation=_=>true;tools.Close();}ClearStudyEditor();pendingChangeDecision=null;
            CalmMotion.AnimationOverride=oldMotion;settings.Dark=oldDark;settings.Accent=oldAccent;ApplyTheme();
        }
    }
}
