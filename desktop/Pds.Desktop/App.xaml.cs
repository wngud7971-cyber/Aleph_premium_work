using System.IO;
using System.Windows;
using Pds.Core;

namespace Pds.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? singleInstance;
    private InstanceActivation? activation;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var verify = e.Args.Contains("--verify") || e.Args.Contains("--verify-reopen") || e.Args.Contains("--verify-features") || e.Args.Contains("--verify-review-reopen") || e.Args.Contains("--verify-tray") || e.Args.Contains("--verify-endurance");
        var isolated = e.Args.FirstOrDefault(a => a.StartsWith("--data-dir=", StringComparison.Ordinal));
        var dataPath = isolated is not null ? Path.GetFullPath(isolated[11..])
            : verify ? Path.Combine(Path.GetTempPath(), "PdsVerification", Guid.NewGuid().ToString("N"))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PdsPrototype");
        if (!verify)
        {
            var name = InstanceActivation.NameFor(dataPath);
            singleInstance = new Mutex(true, isolated is null ? "Local\\PdsPrototype-" + Environment.UserName : "Local\\" + name, out var ownsMutex);
            activation = new InstanceActivation(name);
            if (!ownsMutex)
            {
                if (!await activation.RequestAsync(TimeSpan.FromSeconds(3)))
                    System.Windows.MessageBox.Show("기존 PDS 창을 열지 못했어요. 이전 버전이 실행 중이면 트레이에서 열어 주세요. 저장하지 않은 입력이 있을 수 있으니 강제로 종료하지 마세요.", "PDS");
                Shutdown(); return;
            }
        }
        try
        {
            var repository = new PlanRepository(Path.Combine(dataPath, "records.db"));
            var window = new MainWindow(repository, new SettingsStore(Path.Combine(dataPath, "settings.json"), repository), verify, e.Args.Contains("--verify-reopen"), e.Args.Contains("--verify-features"), e.Args.Contains("--verify-review-reopen"), isolated is null && !verify, e.Args.Contains("--verify-tray"), e.Args.Contains("--verify-endurance"));
            MainWindow = window;
            if (verify) { window.ShowActivated = false; window.ShowInTaskbar = false; window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -30000; window.Top = -30000; }
            activation?.Listen(() => Dispatcher.InvokeAsync(window.RestoreWindow).Task,
                _ => Dispatcher.BeginInvoke(() => window.ReportActivationUnavailable()));
            window.Show();
        }
        catch (Exception ex)
        {
            if (verify) { Directory.CreateDirectory(dataPath); File.WriteAllText(Path.Combine(dataPath, "startup-error.txt"), ex.ToString()); }
            else System.Windows.MessageBox.Show("앱을 열지 못했어요. 기존 기록은 지우지 않습니다.\n\n" + ex.Message, "PDS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e) { activation?.Dispose(); singleInstance?.Dispose(); base.OnExit(e); }
}
