using System.IO;
using System.Text.Json;
using System.Windows;

namespace Pds.Desktop;

public partial class MainWindow
{
    private bool? trayAvailabilityOverride;
    private async Task VerifyTrayAsync()
    {
        var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); checks.Add(label); }
        try
        {
            await Task.Delay(3500);
            Check(tray is not null, "private-native-tray-created-at-startup");
            var registered = tray!.TryEnsureRegistered();
            Check(registered == tray.IsRegistered, "registration-result-matches-shell-icon-rectangle");
            TitleInput.Text = "트레이 복원 후 유지할 미저장 입력";
            var revision = snapshot.Revision;
            Close();
            Check(registered ? !IsVisible : IsVisible && WindowState == WindowState.Minimized && ShowInTaskbar,
                registered ? "registered-icon-allows-x-to-hide" : "missing-icon-keeps-taskbar-access");
            RestoreWindow();
            Check(IsVisible && WindowState == WindowState.Normal && TitleInput.Text == "트레이 복원 후 유지할 미저장 입력" && repository.Load(selected).Revision == revision, "restore-preserves-draft-without-saving");
            trayAvailabilityOverride = false; Close();
            Check(IsVisible && ShowInTaskbar && WindowState == WindowState.Minimized, "forced-unavailable-icon-minimizes-instead-of-hiding");
            RestoreWindow();
            Check(TitleInput.Text == "트레이 복원 후 유지할 미저장 입력" && !allowSystemStartup, "fallback-preserves-input-and-does-not-register-startup");
            tray.Dispose();
            Check(!tray.IsRegistered && (!registered || tray.RemovedOnDispose), "only-test-icon-is-removed-at-exit");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "tray.json"), JsonSerializer.Serialize(new
            { success = true, registered, checks, count = checks.Count, realTrayClickTested = false, explorerRestartTested = false, personalDataUsed = false }, new JsonSerializerOptions { WriteIndented = true }));
            FinishVerification(null);
        }
        catch (Exception ex) { FinishVerification(ex); }
        finally { trayAvailabilityOverride = null; }
    }
}
