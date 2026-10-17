using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private Func<PdsBackup, MessageBoxResult>? backupReplacementDecision;
    private static UserSettings SettingsFromBackup(PdsBackup backup)
    {
        var result = backup.PreferencesJson is null ? new UserSettings() : JsonSerializer.Deserialize<UserSettings>(backup.PreferencesJson) ?? throw new InvalidDataException("설정이 비어 있어요.");
        result.Validate(); return result;
    }
    private async Task ExportBackupAsync(string? testPath = null)
    {
        if (busy || !await ConfirmPendingAsync()) return;
        busy = true; Body.IsEnabled = false;
        try
        {
            var path = testPath;
            if (path is null)
            {
                var picker = new SaveFileDialog { Title = "PDS 백업 내보내기", Filter = "PDS 백업 (*.json)|*.json", FileName = "PDS-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", OverwritePrompt = true };
                if (picker.ShowDialog(this) != true) return; path = picker.FileName;
            }
            var full = Path.GetFullPath(path);
            if (Path.GetDirectoryName(full)!.Equals(Path.GetDirectoryName(repository.DatabasePath), StringComparison.OrdinalIgnoreCase) &&
                new[] { "records.db", "records.db-wal", "records.db-shm", "records.db-journal", "settings.json" }.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("앱 기록·설정 파일과 다른 이름을 선택해 주세요.");
            RememberWindowPosition(); var preferences = JsonSerializer.Serialize(settings);
            var backup = await Task.Run(() => repository.CreateBackup(preferences));
            await Task.Run(() => BackupFile.Write(path, backup));
            Status.Text = "백업 파일을 내보냈어요. 앱의 기록은 그대로 남아 있어요.";
        }
        catch (Exception ex) { HandleError("백업 내보내기", ex); }
        finally { busy = false; Body.IsEnabled = true; QueueMaintenance(); }
    }
    private async Task<bool> RestoreBackupAsync(string? testPath = null)
    {
        if (busy) return false;
        PdsBackup backup; UserSettings replacement;
        try
        {
            var path = testPath;
            if (path is null)
            {
                var picker = new OpenFileDialog { Title = "PDS 백업 불러오기", Filter = "PDS 백업 (*.json)|*.json", CheckFileExists = true };
                if (picker.ShowDialog(this) != true) return false; path = picker.FileName;
            }
            backup = await Task.Run(() => BackupFile.Read(path)); replacement = SettingsFromBackup(backup);
        }
        catch (Exception ex) { HandleError("백업 파일 검사", ex); return false; }
        if (busy || !await ConfirmPendingAsync()) return false;
        busy = true; Body.IsEnabled = false;
        var committed = false;
        try
        {
            var answer = verify && backupReplacementDecision is not null ? backupReplacementDecision(backup)
                : System.Windows.MessageBox.Show(this, $"백업 생성: {backup.ExportedUtc.ToLocalTime():yyyy.MM.dd HH:mm}\n계획 {backup.Plans}건 · 운동 {backup.Workouts}건 · 회고 {backup.Reviews}일 · 체중 {backup.Weights.Count}건 · 기념일 {backup.Anniversaries.Count}건\n\n현재 기록과 설정 전체를 이 파일의 내용으로 교체합니다.\n필요한 현재 기록은 먼저 내보내 주세요. 계속할까요?", "백업 복원 · 전체 교체", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) { Status.Text = "복원을 취소했어요."; return false; }
            replacement.MeditationShownDates.UnionWith(settings.MeditationShownDates);
            if (settings.MeditationDate is string shown) replacement.MeditationShownDates.Add(shown);
            backup = backup with { PreferencesJson = JsonSerializer.Serialize(replacement) };
            await Task.Run(() => repository.RestoreBackup(backup));
            committed = true;
            settings.CopyFrom(replacement); if (statisticsOpen) CloseStatistics(); ApplyTheme(); ApplyWindowLayout();
            await LoadSelectedAsync(); busy = true; Body.IsEnabled = false; await RefreshCalendarAsync(); await RefreshBodyWeightAsync();
            if (allowSystemStartup)
            {
                try { StartupRegistration.Apply(settings.AutoStart); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
                { Status.Text = "기록은 복원했지만 자동 실행은 설정에서 다시 확인해 주세요."; return true; }
            }
            Status.Text = "복원 완료 · 기록과 설정을 불러왔어요."; return true;
        }
        catch (Exception ex)
        {
            if (committed) { Status.Text = "기록은 복원했지만 화면을 새로 불러오지 못했어요. 앱을 다시 실행해 주세요."; return true; }
            HandleError("백업 복원", ex); return false;
        }
        finally { busy = false; Body.IsEnabled = true; QueueMaintenance(); }
    }
}
