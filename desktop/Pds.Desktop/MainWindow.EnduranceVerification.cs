using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyEnduranceAsync()
    {
        var folder = Path.GetDirectoryName(repository.DatabasePath)!;
        var elapsed = Stopwatch.StartNew();
        var observations = new List<object>();
        var timings = new List<double>();
        void Progress(string stage) => File.WriteAllText(Path.Combine(folder, "progress.json"), JsonSerializer.Serialize(new { stage, elapsedSeconds = elapsed.Elapsed.TotalSeconds, observations }));
        async Task Measure(string stage, int seconds)
        {
            Progress(stage + "-warmup"); await Task.Delay(20000);
            using var process = Process.GetCurrentProcess(); process.Refresh();
            var cpuStart = process.TotalProcessorTime; var previousCpu = cpuStart;
            var timer = Stopwatch.StartNew(); var previousAt = timer.Elapsed; var peakCpu = 0d; var peakMemory = process.WorkingSet64; var samples = 0;
            var startingPrivate = process.PrivateMemorySize64;
            Progress(stage);
            while (timer.Elapsed < TimeSpan.FromSeconds(seconds))
            {
                await Task.Delay(1000); process.Refresh(); var at = timer.Elapsed; var cpu = process.TotalProcessorTime;
                peakCpu = Math.Max(peakCpu, (cpu - previousCpu).TotalMilliseconds / (at - previousAt).TotalMilliseconds / Environment.ProcessorCount * 100);
                peakMemory = Math.Max(peakMemory, process.WorkingSet64); previousAt = at; previousCpu = cpu; samples++;
                if (samples % 15 == 0) Progress(stage);
            }
            observations.Add(new { stage, sampleSeconds = timer.Elapsed.TotalSeconds, samples,
                averageCpuPercent = Math.Round((process.TotalProcessorTime - cpuStart).TotalMilliseconds / timer.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100, 3),
                peakOneSecondCpuPercent = Math.Round(peakCpu, 3), workingSetMiB = Math.Round(process.WorkingSet64 / 1048576d, 1),
                peakWorkingSetMiB = Math.Round(peakMemory / 1048576d, 1), privateMiB = Math.Round(process.PrivateMemorySize64 / 1048576d, 1),
                privateChangeMiB = Math.Round((process.PrivateMemorySize64 - startingPrivate) / 1048576d, 1) });
            Progress(stage + "-complete");
        }
        try
        {
            Progress("seed-500-days");
            var today = LocalToday;
            await Task.Run(() =>
            {
                for (var index = 0; index < 500; index++)
                {
                    var date = today.AddDays(-index);
                    var entries = Enumerable.Range(1, 10).Select(i => PlanEntry.Create($"검증 계획 {i}", new string('가', 80)) with { Done = true, ActualDate = date }).ToArray();
                    repository.Save(repository.Load(date) with { Plans = entries, Workouts = [WorkoutEntry.Create("walking", 1800, null), WorkoutEntry.Create("cycling-indoor", 1200, null), WorkoutEntry.Create("pushups", null, 30)], Review = new("검증 회고", "검증 메모", "다음 검증") });
                }
            });
            var totals = await Task.Run(() => repository.Statistics(new(today.AddDays(-499), today)));
            if (totals.Plans != 5000 || totals.Workout.Records != 1500 || totals.Workout.Seconds != 1500000 || totals.ReviewDays != 500) throw new InvalidOperationException("Seeded totals mismatch");
            settings.MeditationEnabled = false; CloseMeditation(); settings.Compact = false; ApplyWindowLayout(openDetails: true);
            await SelectDateAsync(today); await RefreshCalendarAsync();
            await Measure("general-500-days", 300);
            Details.Visibility = Visibility.Collapsed; DetailColumn.Width = new GridLength(0); DrawCalendar();
            await Measure("calendar-500-days", 300);
            var registered = tray?.TryEnsureRegistered() == true;
            Close();
            if (registered && IsVisible || !registered && (!IsVisible || WindowState != WindowState.Minimized)) throw new InvalidOperationException("Tray close fallback mismatch");
            await Measure(registered ? "native-tray-hidden" : "taskbar-minimized-fallback", 300);
            RestoreWindow(); settings.Compact = false; ApplyWindowLayout(openDetails: true); await OpenStatisticsAsync();
            statisticsMode = "weekday"; statisticsCustom = new(today.AddDays(-499), today); await RefreshStatisticsAsync();
            if (statisticsReport?.Workout.Records != 1500) throw new InvalidOperationException("Large-range UI report mismatch");
            await Measure("statistics-500-days", 300);
            CloseStatistics(); Progress("100-screen-transitions");
            using var transitionProcess = Process.GetCurrentProcess(); transitionProcess.Refresh(); var privateBeforeTransitions = transitionProcess.PrivateMemorySize64;
            for (var i = 0; i < 100; i++)
            {
                var timer = Stopwatch.StartNew();
                await SelectDateAsync(today.AddDays(-(i % 31))); month = new(selected.Year, selected.Month, 1); await RefreshCalendarAsync();
                await OpenStatisticsAsync(); CloseStatistics(); ApplyWindowLayout(); DrawCalendar();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                timer.Stop(); timings.Add(timer.Elapsed.TotalMilliseconds);
                if (Pending || snapshot.Plans.Count != 10 || snapshot.Workouts.Count != 3) throw new InvalidOperationException("Navigation damaged saved data");
                if (i % 10 == 0) Progress("screen-transition-" + (i + 1));
            }
            transitionProcess.Refresh(); var privateAfterTransitions = transitionProcess.PrivateMemorySize64;
            settings.Compact = false; ApplyWindowLayout(openDetails: true); await SelectDateAsync(today); await RefreshCalendarAsync();
            var marker = WorkoutEntry.Create("badminton", 1200, null);
            workouts.Add(marker); dirty = true;
            var save = Stopwatch.StartNew(); if (!await SaveAsync()) throw new InvalidOperationException("Large-data UI save failed"); save.Stop();
            if (!new PlanRepository(repository.DatabasePath).Load(today).Workouts.Any(w => w.Id == marker.Id)) throw new InvalidOperationException("Large-data save not durable");
            var backupPath = Path.Combine(folder, "synthetic-backup.json"); var export = Stopwatch.StartNew();
            await ExportBackupAsync(backupPath); export.Stop();
            var backup = BackupFile.Read(backupPath); var restored = new PlanRepository(Path.Combine(folder, "restore-check.db")); restored.RestoreBackup(backup);
            if (restored.Statistics(new(today.AddDays(-499), today)).Workout.Records != 1501) throw new InvalidOperationException("Large-data backup mismatch");
            Capture("stage2-calendar.png"); await OpenStatisticsAsync(); await RefreshStatisticsAsync();
            Capture("stage2-statistics.png"); CloseStatistics(); Close();
            var remaining = Math.Max(0, 1800 - (int)elapsed.Elapsed.TotalSeconds - 20);
            await Measure("remaining-hidden-soak", remaining);
            if (elapsed.Elapsed.TotalSeconds < 1800) await Task.Delay(TimeSpan.FromSeconds(1800) - elapsed.Elapsed);
            File.WriteAllText(Path.Combine(folder, "endurance.json"), JsonSerializer.Serialize(new
            {
                success = true, elapsedSeconds = elapsed.Elapsed.TotalSeconds, syntheticDays = 500, syntheticPlans = 5000, syntheticWorkouts = 1501,
                reviewDays = 500, transitions = timings.Count, transitionMedianMs = timings.Order().ElementAt(50), transitionMaximumMs = timings.Max(),
                privateBeforeTransitionsMiB = Math.Round(privateBeforeTransitions / 1048576d, 1), privateAfterTransitionsMiB = Math.Round(privateAfterTransitions / 1048576d, 1),
                saveMilliseconds = save.Elapsed.TotalMilliseconds, exportMilliseconds = export.Elapsed.TotalMilliseconds, exportBytes = new FileInfo(backupPath).Length,
                nativeTrayRegistered = registered, observations, logicalProcessors = Environment.ProcessorCount,
                inactiveOffscreenWindow = true, startupRegistrationChanged = false, maintenanceSchedulingEnabled = false, personalDataUsed = false,
                userExperienceTested = false, powerResumeTested = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            Progress("complete"); FinishVerification(null);
        }
        catch (Exception ex) { FinishVerification(ex); }
    }
}
