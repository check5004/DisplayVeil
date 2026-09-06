using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DisplayVeil
{
    internal sealed class VeilWindow : IDisposable
    {
        public readonly DisplayInfo Display;
        public readonly VeilSession Session = new VeilSession();
        public readonly CurtainForm Curtain;
        public readonly RevealBar Bar;
        public long LastMovement;
        public VeilWindow(DisplayInfo display, Action stop)
        {
            Display = display;
            Curtain = new CurtainForm(display);
            Bar = new RevealBar(display);
            Curtain.RevealRequested += Reveal;
            Curtain.StopRequested += stop;
            Bar.CoverRequested += Cover;
            Bar.PinRequested += delegate { Session.TogglePin(); Bar.UpdateStatus(Session.State, true); };
            Bar.StopRequested += stop;
        }
        public void Cover() { Session.Cover(); Bar.Hide(); Curtain.ShowCurtain(); }
        public void Reveal() { Session.Reveal(); Curtain.Hide(); Bar.UpdateStatus(Session.State, true); Bar.ShowBar(); }
        public void Dispose() { Curtain.Dispose(); Bar.Dispose(); }
    }

    internal sealed class AppController : ApplicationContext
    {
        public Settings Settings { get; private set; }
        public List<DisplayInfo> Displays { get; private set; }
        public bool Running { get; private set; }
        public MainForm Window { get; private set; }
        private readonly SettingsStore store;
        private readonly Func<List<DisplayInfo>> readDisplays;
        private readonly HotkeyManager hotkeys;
        private readonly NotifyIcon tray;
        private readonly Icon trayIcon = AppIcon.Load();
        private readonly ToolStripMenuItem toggleItem;
        private readonly Timer timer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<VeilWindow> veils = new List<VeilWindow>();
        private readonly List<DisplayBadge> badges = new List<DisplayBadge>();
        private Point previousPointer;
        private long nextTopologyPoll, nextTopmost, badgesUntil;
        private string signature;
        private bool disposed;
        private IntPtr previousApplication;
        private readonly uint ownProcessId = (uint)Process.GetCurrentProcess().Id;

        public AppController(string settingsDirectory, Func<List<DisplayInfo>> displayProvider = null)
        {
            readDisplays = displayProvider ?? Native.GetDisplays;
            store = new SettingsStore(settingsDirectory);
            string warning;
            Settings = store.Load(out warning);
            Displays = readDisplays();
            signature = DisplayLayout.Signature(Displays);
            Window = new MainForm(this);
            MainForm = Window;
            hotkeys = new HotkeyManager(Window.Handle);
            Window.HotkeyPressed += HandleHotkey;
            var menu = new ContextMenuStrip { BackColor = Theme.Surface, ForeColor = Theme.Text };
            toggleItem = new ToolStripMenuItem("鑑賞をはじめる", null, delegate { if (Running) Stop(null); else Start(); });
            menu.Items.Add(toggleItem);
            menu.Items.Add("画面を選ぶ / 設定", null, delegate { ShowUi(null); });
            menu.Items.Add("すべて解除", null, delegate { Stop(null); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Display Veil を終了", null, delegate { Window.Close(); });
            tray = new NotifyIcon { Icon = trayIcon, Text = "Display Veil — 待機中", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { ShowUi(null); };
            ApplyHotkeys();
            Window.RefreshDisplays(Displays);
            Window.ShowStatus(warning);
            timer = new Timer { Interval = 100 };
            timer.Tick += Tick;
            timer.Start();
            SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
            SystemEvents.SessionSwitch += SessionSwitch;
            SystemEvents.PowerModeChanged += PowerModeChanged;
            Window.Show();
        }

        public void SaveSettings()
        {
            string error;
            if (!store.Save(Settings, out error) && Window != null) Window.ShowStatus(error);
        }
        public void ChangeHotkey(int index, string key)
        {
            if (index == 0) Settings.ToggleKey = key;
            else if (index == 1) Settings.RevealKey = key;
            else Settings.StopKey = key;
            ApplyHotkeys();
            SaveSettings();
        }
        private void ApplyHotkeys()
        {
            var warnings = hotkeys.Apply(Settings);
            Window.ShowHotkeyStatus(warnings.Count == 0 ?
                "キーは変更・無効化できます。黒幕の右クリックからも、いつでも全解除できます。" :
                String.Join(" / ", warnings) + "。別のキーを選んでください。", warnings.Count > 0);
        }
        private void HandleHotkey(int id)
        {
            if (id == 1) { if (Running) Stop(null); else Start(); }
            else if (id == 2 && Running)
            {
                var veil = veils.FirstOrDefault(v => v.Display.Bounds.Contains(Cursor.Position));
                if (veil != null) { if (veil.Session.State == VeilState.Covered) veil.Reveal(); else veil.Cover(); }
            }
            else if (id == 3) Stop(null);
        }
        public void Start()
        {
            if (Running) return;
            if (!RefreshTopology(false)) return;
            var targets = DisplayLayout.Targets(Displays, Settings.ViewingId);
            if (targets.Count == 0)
            {
                ShowUi("映画を見る画面を選択し、拡張表示の画面を 2 台以上接続してください。");
                return;
            }
            CloseBadges();
            previousPointer = Cursor.Position;
            // When start is clicked in our settings, restore the previous external app.
            IntPtr foreground = Native.GetForegroundWindow();
            uint pid;
            Native.GetWindowThreadProcessId(foreground, out pid);
            IntPtr restore = pid == ownProcessId ? previousApplication : foreground;
            Window.Hide();
            if (restore != IntPtr.Zero && Native.IsWindow(restore)) Native.SetForegroundWindow(restore);
            try
            {
                foreach (var display in targets)
                {
                    var veil = new VeilWindow(display, delegate { Stop(null); });
                    veils.Add(veil);
                    veil.Curtain.HintRequested += delegate { veil.LastMovement = clock.ElapsedMilliseconds; };
                    veil.LastMovement = clock.ElapsedMilliseconds - 3000;
                    veil.Cover();
                }
                Running = true;
                nextTopmost = clock.ElapsedMilliseconds + 2000;
                toggleItem.Text = "すべて解除";
                tray.Text = "Display Veil — 鑑賞中";
            }
            catch (Exception ex)
            {
                Stop(null);
                ShowUi("黒幕を作成できなかったため、全解除しました: " + ex.Message);
            }
        }
        public void Stop(string reason)
        {
            Running = false;
            foreach (var veil in veils) veil.Dispose();
            veils.Clear();
            if (toggleItem != null) toggleItem.Text = "鑑賞をはじめる";
            if (tray != null) tray.Text = "Display Veil — 待機中";
            if (Window != null && !Window.IsDisposed) Window.ShowStatus(reason);
        }
        public void ShowUi(string message)
        {
            if (disposed || Window.IsDisposed) return;
            if (Window.InvokeRequired) { Window.BeginInvoke(new Action(delegate { ShowUi(message); })); return; }
            Stop(message);
            Window.RefreshDisplays(Displays);
            Window.ShowStatus(message);
            Window.Show();
            Window.WindowState = FormWindowState.Normal;
            // Recover settings after unplugging the screen that held the window.
            if (!Displays.Any(d => d.WorkArea.IntersectsWith(Window.Bounds)) && Displays.Count > 0)
                Window.Location = Displays[0].WorkArea.Location;
            Window.Activate();
        }
        public void Identify()
        {
            CloseBadges();
            foreach (var display in Displays)
            {
                var badge = new DisplayBadge(display);
                badges.Add(badge);
                badge.Place(badge.Bounds);
            }
            badgesUntil = clock.ElapsedMilliseconds + 2200;
        }
        private void CloseBadges()
        {
            foreach (var badge in badges) badge.Dispose();
            badges.Clear();
        }
        private void Tick(object sender, EventArgs e)
        {
            long now = clock.ElapsedMilliseconds;
            if (badges.Count > 0 && now >= badgesUntil) CloseBadges();
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground != IntPtr.Zero)
            {
                uint pid;
                Native.GetWindowThreadProcessId(foreground, out pid);
                if (pid != ownProcessId) previousApplication = foreground;
            }
            if (now >= nextTopologyPoll)
            {
                nextTopologyPoll = now + 2000;
                RefreshTopology(true);
            }
            if (!Running) return;
            Point pointer = Cursor.Position;
            bool moved = pointer != previousPointer;
            bool mouseDown = Control.MouseButtons != MouseButtons.None;
            foreach (var veil in veils)
            {
                bool inside = veil.Display.Bounds.Contains(pointer);
                if (inside && moved) veil.LastMovement = now;
                if (veil.Session.State == VeilState.Covered)
                {
                    // A right click always exposes the escape buttons, even with hints disabled.
                    if (Settings.ShowMouseHints) veil.Curtain.SetHint(inside && now - veil.LastMovement < 2500);
                    else if (!inside) veil.Curtain.SetHint(false);
                    if (now >= nextTopmost && !mouseDown) veil.Curtain.MaintainTopmost();
                }
                else
                {
                    if (veil.Session.Tick(inside, mouseDown, Settings.AutoCover, now)) veil.Cover();
                    else
                    {
                        veil.Bar.UpdateStatus(veil.Session.State, Settings.AutoCover);
                        if (now >= nextTopmost && !mouseDown) veil.Bar.MaintainTopmost();
                    }
                }
            }
            if (now >= nextTopmost) nextTopmost = now + 2000;
            previousPointer = pointer;
        }
        private bool RefreshTopology(bool notify)
        {
            try
            {
                var displays = readDisplays();
                string updated = DisplayLayout.Signature(displays);
                if (updated == signature) return true;
                bool wasRunning = Running;
                Stop(null);
                CloseBadges();
                Displays = displays;
                signature = updated;
                Window.RefreshDisplays(Displays);
                if (wasRunning && notify)
                    ShowUi("画面構成が変わったため、全解除しました。鑑賞画面を確認して再開してください。");
                return true;
            }
            catch (Exception ex)
            {
                Stop(null);
                Window.ShowStatus("画面情報を取得できないため、全解除しました: " + ex.Message);
                return false;
            }
        }
        private void OnUi(Action action)
        {
            if (disposed || Window.IsDisposed || !Window.IsHandleCreated) return;
            try { Window.BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }
        private void DisplaySettingsChanged(object sender, EventArgs e)
        {
            OnUi(delegate
            {
                bool wasRunning = Running;
                Stop(null);
                RefreshTopology(false);
                if (wasRunning) ShowUi("表示設定が変わったため、全解除しました。鑑賞画面を確認して再開してください。");
            });
        }
        private void SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.RemoteDisconnect || e.Reason == SessionSwitchReason.ConsoleDisconnect)
                OnUi(delegate { Stop("ロック・切断のため全解除しました。"); });
        }
        private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend || e.Mode == PowerModes.Resume)
                OnUi(delegate { Stop("スリープ・復帰のため全解除しました。"); });
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                timer.Stop();
                timer.Dispose();
                SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
                SystemEvents.SessionSwitch -= SessionSwitch;
                SystemEvents.PowerModeChanged -= PowerModeChanged;
                Stop(null);
                CloseBadges();
                hotkeys.Dispose();
                tray.Visible = false;
                var menu = tray.ContextMenuStrip;
                tray.Dispose();
                trayIcon.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
