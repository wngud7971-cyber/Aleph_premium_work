using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Pds.Desktop;

public partial class MainWindow
{
    private async Task VerifyStatisticsPerformanceAsync()
    {
        var load = Stopwatch.StartNew(); await OpenStatisticsAsync(); load.Stop();
        await Task.Delay(20000);
        var process = Process.GetCurrentProcess(); process.Refresh(); var start = process.TotalProcessorTime; var sample = Stopwatch.StartNew();
        await Task.Delay(10000); process.Refresh(); sample.Stop();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "statistics-performance.json"), JsonSerializer.Serialize(new
        {
            success = true, period = statisticsMode, savedPlans = statisticsReport?.Plans, savedWorkouts = statisticsReport?.Workout.Records,
            firstOpenMilliseconds = Math.Round(load.Elapsed.TotalMilliseconds, 1), workingSetMB = Math.Round(process.WorkingSet64 / 1048576d, 1),
            privateMB = Math.Round(process.PrivateMemorySize64 / 1048576d, 1),
            idleCpuPercent = Math.Round((process.TotalProcessorTime - start).TotalMilliseconds / sample.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100, 3),
            sampleSeconds = sample.Elapsed.TotalSeconds, warmupSeconds = 20, logicalProcessors = Environment.ProcessorCount,
            inactiveOffscreenWindow = true, personalDataUsed = false, representativeLongTermDataset = false
        }, new JsonSerializerOptions { WriteIndented = true }));
        CloseStatistics();
    }
}
