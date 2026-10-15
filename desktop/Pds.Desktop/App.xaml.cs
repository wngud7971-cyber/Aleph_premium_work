using System.IO;
using System.Windows;
using Pds.Core;

namespace Pds.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? singleInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var verify = e.Args.Contains("--verify") || e.Args.Contains("--verify-reopen");
        var isolated = e.Args.FirstOrDefault(a => a.StartsWith("--data-dir=", StringComparison.Ordinal));
        var dataPath = isolated is not null ? Path.GetFullPath(isolated[11..])
            : verify ? Path.Combine(Path.GetTempPath(), "PdsVerification", Guid.NewGuid().ToString("N"))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PdsPrototype");
        if (!verify)
        {
            singleInstance = new Mutex(true, "Local\\PdsPrototype-" + Environment.UserName, out var ownsMutex);
            if (!ownsMutex) { System.Windows.MessageBox.Show("이미 실행 중이에요. 작업 표시줄 또는 트레이에서 PDS를 열어 주세요.", "PDS"); Shutdown(); return; }
        }
        try
        {
            var repository = new PlanRepository(Path.Combine(dataPath, "records.db"));
            var window = new MainWindow(repository, new SettingsStore(Path.Combine(dataPath, "settings.json")), verify, e.Args.Contains("--verify-reopen"));
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            if (verify) { Directory.CreateDirectory(dataPath); File.WriteAllText(Path.Combine(dataPath, "startup-error.txt"), ex.ToString()); }
            else System.Windows.MessageBox.Show("앱을 열지 못했어요. 기존 기록은 지우지 않습니다.\n\n" + ex.Message, "PDS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e) { singleInstance?.Dispose(); base.OnExit(e); }
}
