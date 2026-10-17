using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Pds.Desktop;

// Owns a private, invisible message window and only this instance's notification icon.
internal sealed class NativeTrayIcon : IDisposable
{
    private const int Callback = 0x8001;
    private const uint Message = 1, IconFlag = 2, Tip = 4, Info = 0x10, GuidFlag = 0x20, ShowTip = 0x80;
    private readonly HwndSource source;
    private readonly Icon icon;
    private readonly Guid identifier;
    private readonly string text;
    private readonly uint taskbarCreated;
    private readonly DispatcherTimer retry = new(DispatcherPriority.Background);
    private bool added, disposed;
    private int attempts;
    internal bool RemovedOnDispose { get; private set; }
    internal event EventHandler? Activated;
    internal Forms.ContextMenuStrip? ContextMenuStrip { get; set; }

    internal NativeTrayIcon(Icon original, string text, string storagePath)
    {
        icon = (Icon)original.Clone(); this.text = text;
        identifier = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes((storagePath + "|" + Environment.ProcessPath).ToUpperInvariant())).AsSpan(0, 16));
        source = new HwndSource(new HwndSourceParameters("PDS notification messages")
        { WindowStyle = unchecked((int)0x80000000), PositionX = -32000, PositionY = -32000, Width = 0, Height = 0 });
        source.AddHook(OnMessage);
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        retry.Interval = TimeSpan.FromSeconds(1);
        retry.Tick += (_, _) => { if (TryEnsureRegistered() || ++attempts >= 3) retry.Stop(); };
        if (!TryEnsureRegistered()) retry.Start();
    }

    internal bool IsRegistered
    {
        get
        {
            if (disposed || !added) return false;
            var id = new IconIdentifier { Size = (uint)Marshal.SizeOf<IconIdentifier>(), Window = source.Handle, Id = 1, Guid = identifier };
            return Shell_NotifyIconGetRect(ref id, out var rect) == 0 && rect.Right > rect.Left && rect.Bottom > rect.Top;
        }
    }
    internal bool TryEnsureRegistered()
    {
        if (disposed) return false;
        if (IsRegistered) return true;
        var data = Data();
        if (added && !Shell_NotifyIcon(1, ref data)) added = false;
        if (!added) added = Shell_NotifyIcon(0, ref data);
        if (!added) return false;
        data.Version = 4; Shell_NotifyIcon(4, ref data);
        return IsRegistered;
    }
    internal void ShowBalloonTip(int timeout, string title, string message, Forms.ToolTipIcon kind)
    {
        if (!IsRegistered) return;
        var data = Data(); data.Flags = GuidFlag | Info; data.InfoTitle = title; data.Info = message;
        data.InfoFlags = kind == Forms.ToolTipIcon.Warning ? 2u : kind == Forms.ToolTipIcon.Error ? 3u : 1u;
        Shell_NotifyIcon(1, ref data);
    }
    private NotifyData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyData>(), Window = source.Handle, Id = 1,
        Flags = Message | IconFlag | Tip | GuidFlag | ShowTip, CallbackMessage = Callback, Icon = icon.Handle,
        Tip = text, State = 0, StateMask = 0, Info = "", Version = 0, InfoTitle = "", InfoFlags = 0, Guid = identifier, BalloonIcon = IntPtr.Zero
    };
    private IntPtr OnMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (disposed) return IntPtr.Zero;
        if (taskbarCreated != 0 && message == taskbarCreated)
        {
            added = false; attempts = 0;
            if (!TryEnsureRegistered()) retry.Start();
        }
        else if (message == Callback)
        {
            var action = (int)(lParam.ToInt64() & 0xFFFF);
            if (action is 0x400 or 0x401 or 0x405) Activated?.Invoke(this, EventArgs.Empty);
            else if (action is 0x205 or 0x7B && ContextMenuStrip is { Visible: false } menu)
            {
                SetForegroundWindow(source.Handle);
                menu.Show(Forms.Cursor.Position);
            }
            handled = true;
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (disposed) return;
        retry.Stop();
        if (added) { var data = Data(); RemovedOnDispose = Shell_NotifyIcon(2, ref data); }
        disposed = true; ContextMenuStrip?.Dispose(); source.RemoveHook(OnMessage); source.Dispose(); icon.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags; public int CallbackMessage; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref IconIdentifier id, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
}
