using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DisplayVeil
{
    internal sealed class MonitorMap : Control
    {
        public IList<DisplayInfo> Displays = new List<DisplayInfo>();
        public string SelectedId;
        public event Action<string> Selected;
        public MonitorMap()
        {
            DoubleBuffered = true;
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            AccessibleName = "ディスプレイ配置。画面一覧からも選択できます。";
            SetStyle(ControlStyles.ResizeRedraw, true);
        }
        private List<RectangleF> Rectangles()
        {
            return DisplayLayout.Fit(Displays, new RectangleF(24, 16, Math.Max(0, Width - 48), Math.Max(0, Height - 48)));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rectangles = Rectangles();
            for (int i = 0; i < rectangles.Count; i++)
            {
                var r = rectangles[i];
                r.Inflate(-3, -3);
                if (r.Width < 1 || r.Height < 1) continue;
                bool selected = Displays[i].Id == SelectedId;
                using (var brush = new SolidBrush(selected ? Color.FromArgb(38, 67, 62) : Color.FromArgb(33, 40, 52)))
                    e.Graphics.FillRectangle(brush, r);
                using (var pen = new Pen(selected ? Theme.Accent : Theme.Border, selected ? 2 : 1))
                    e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                using (var font = Theme.Font(Math.Max(8, Math.Min(23, r.Height / 3)), FontStyle.Bold))
                using (var brush = new SolidBrush(selected ? Theme.Accent : Theme.Text))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    e.Graphics.DrawString(Displays[i].Number.ToString() + (selected && r.Width > 90 && r.Height > 65 ? "\n鑑賞" : ""), font, brush, r, format);
            }
            TextRenderer.DrawText(e.Graphics, "配置図・番号はこのアプリ内の表示です  /  クリックで鑑賞画面を選択", Font,
                new Rectangle(16, Height - 27, Width - 32, 22), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            var rectangles = Rectangles();
            for (int i = 0; i < rectangles.Count; i++)
                if (rectangles[i].Contains(e.Location) && Selected != null) { Selected(Displays[i].Id); return; }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly AppController controller;
        private readonly Icon applicationIcon = AppIcon.Load();
        private readonly MonitorMap map;
        private readonly ListBox list;
        private readonly Label selection, status, hotkeyStatus;
        private readonly Button start;
        private readonly Label updateStatus;
        private readonly Button openUpdates;
        private UpdateForm updateDialog;
        public bool UpdateDialogOpen { get { return updateDialog != null && !updateDialog.IsDisposed; } }
        private bool refreshing;
        public event Action<int> HotkeyPressed;
        public MainForm(AppController controller)
        {
            this.controller = controller;
            Text = "Display Veil";
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Font(10, FontStyle.Regular);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 890);
            MinimumSize = new Size(800, 790);
            Icon = applicationIcon;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8,
                Padding = new Padding(26, 20, 26, 16), BackColor = Theme.Background };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 134));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            Controls.Add(root);

            var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Size = new Size(848, 76) };
            var title = Theme.Label("Display Veil", 25, Theme.Text, FontStyle.Bold);
            title.Location = new Point(0, 0);
            var subtitle = Theme.Label("映画のある画面だけを、明るく。", 10, Theme.Muted, FontStyle.Regular);
            subtitle.Location = new Point(2, 47);
            var identify = Theme.Button("画面番号を表示", false);
            identify.SetBounds(650, 8, 196, 38);
            identify.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            identify.Click += delegate { controller.Identify(); };
            header.Controls.AddRange(new Control[] { title, subtitle, identify });
            root.Controls.Add(header, 0, 0);

            map = new MonitorMap { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            map.Selected += SelectDisplay;
            root.Controls.Add(map, 0, 1);
            selection = Theme.Label("01  鑑賞する画面を選択", 10, Theme.Text, FontStyle.Bold);
            selection.Dock = DockStyle.Fill;
            selection.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(selection, 0, 2);
            list = new ListBox { Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 48,
                IntegralHeight = false, Margin = new Padding(0, 0, 0, 8), AccessibleName = "鑑賞する画面", TabIndex = 0 };
            list.DrawItem += DrawDisplay;
            list.SelectedIndexChanged += delegate
            {
                if (!refreshing && list.SelectedItem is DisplayInfo) SelectDisplay(((DisplayInfo)list.SelectedItem).Id);
            };
            root.Controls.Add(list, 0, 3);

            var behaviors = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Margin = new Padding(0, 8, 0, 4) };
            var autoCover = new CheckBox { Text = "操作中の画面からマウスが離れたら、1.5 秒後に暗く戻す", AutoSize = true,
                Checked = controller.Settings.AutoCover, Margin = new Padding(0, 4, 0, 8), ForeColor = Theme.Text };
            autoCover.CheckedChanged += delegate { controller.Settings.AutoCover = autoCover.Checked; controller.SaveSettings(); };
            var hints = new CheckBox { Text = "黒い画面でマウスを動かしたとき、操作ボタンを短く表示する", AutoSize = true,
                Checked = controller.Settings.ShowMouseHints, ForeColor = Theme.Text };
            hints.CheckedChanged += delegate { controller.Settings.ShowMouseHints = hints.Checked; controller.SaveSettings(); };
            behaviors.Controls.AddRange(new Control[] { autoCover, hints });
            root.Controls.Add(behaviors, 0, 4);

            var shortcuts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Margin = Padding.Empty };
            for (int i = 0; i < 3; i++) shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            shortcuts.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            shortcuts.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            string[] labels = { "開始 / 全解除", "マウスの画面を開閉", "全解除のみ" };
            string[] values = { controller.Settings.ToggleKey, controller.Settings.RevealKey, controller.Settings.StopKey };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var label = Theme.Label(labels[i], 9, Theme.Muted, FontStyle.Regular);
                label.Dock = DockStyle.Fill;
                label.TextAlign = ContentAlignment.BottomLeft;
                shortcuts.Controls.Add(label, i, 0);
                var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 6, 0, 0) };
                var modifiers = Theme.Label("Ctrl + Alt + Shift +", 9, Theme.Text, FontStyle.Regular);
                modifiers.Margin = new Padding(0, 5, 5, 0);
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 86,
                    FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface, ForeColor = Theme.Text,
                    AccessibleName = labels[i] + "のショートカット" };
                combo.Items.AddRange(HotkeyManager.KeyNames);
                combo.SelectedItem = HotkeyManager.ValidName(values[i]) ? values[i] : "無効";
                combo.SelectedIndexChanged += delegate { controller.ChangeHotkey(index, (string)combo.SelectedItem); };
                row.Controls.AddRange(new Control[] { modifiers, combo });
                shortcuts.Controls.Add(row, i, 1);
            }
            hotkeyStatus = Theme.Label("", 9, Theme.Muted, FontStyle.Regular);
            hotkeyStatus.AutoSize = false;
            hotkeyStatus.Dock = DockStyle.Fill;
            shortcuts.Controls.Add(hotkeyStatus, 0, 2);
            shortcuts.SetColumnSpan(hotkeyStatus, 3);
            root.Controls.Add(shortcuts, 0, 5);

            var updates = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            updates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            updates.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            updates.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            updates.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            updateStatus = Theme.Label("", 9, Theme.Muted, FontStyle.Regular);
            updateStatus.AutoSize = false;
            updateStatus.Dock = DockStyle.Fill;
            updateStatus.TextAlign = ContentAlignment.MiddleLeft;
            openUpdates = Theme.Button("更新とリリースノート", false);
            openUpdates.Dock = DockStyle.Fill;
            openUpdates.Margin = new Padding(0, 0, 0, 2);
            openUpdates.Click += delegate { ShowUpdateDialog(); };
            updates.Controls.Add(openUpdates, 1, 0);
            var currentVersion = Theme.Label("現在のバージョン " + UpdateClient.CurrentVersion.ToString(3), 9, Theme.Muted, FontStyle.Regular);
            currentVersion.AutoSize = false;
            currentVersion.Dock = DockStyle.Fill;
            currentVersion.TextAlign = ContentAlignment.MiddleLeft;
            updates.Controls.Add(currentVersion, 0, 0);
            updates.Controls.Add(updateStatus, 0, 1);
            updates.SetColumnSpan(updateStatus, 2);
            root.Controls.Add(updates, 0, 6);
            RefreshUpdates();

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 222));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            status = Theme.Label("", 10, Theme.Muted, FontStyle.Regular);
            status.AutoSize = false;
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            start = Theme.Button("鑑賞をはじめる", true);
            start.Dock = DockStyle.Fill;
            start.Margin = new Padding(0, 2, 0, 2);
            start.Click += delegate { controller.Start(); };
            var help = Theme.Label("黒幕をダブルクリック → 一時操作  /  右クリック → 解除ボタン  /  × → アプリ終了", 9, Theme.Muted, FontStyle.Regular);
            help.AutoSize = false;
            help.Dock = DockStyle.Fill;
            help.TextAlign = ContentAlignment.BottomLeft;
            footer.Controls.Add(status, 0, 0);
            footer.Controls.Add(start, 1, 0);
            footer.Controls.Add(help, 0, 1);
            footer.SetColumnSpan(help, 2);
            root.Controls.Add(footer, 0, 7);
            FormClosing += delegate { controller.Stop(null); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            Shown += delegate
            {
                var work = Screen.FromControl(this).WorkingArea;
                if (Height > work.Height || Width > work.Width)
                {
                    MinimumSize = new Size(Math.Min(MinimumSize.Width, work.Width), Math.Min(MinimumSize.Height, work.Height));
                    Bounds = new Rectangle(work.X, work.Y, Math.Min(Width, work.Width), Math.Min(Height, work.Height));
                }
            };
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) applicationIcon.Dispose();
        }
        private void SelectDisplay(string id)
        {
            controller.Settings.ViewingId = id;
            controller.SaveSettings();
            RefreshDisplays(controller.Displays);
        }
        public void RefreshDisplays(IList<DisplayInfo> displays)
        {
            refreshing = true;
            try
            {
                list.BeginUpdate();
                list.Items.Clear();
                foreach (var display in displays) list.Items.Add(display);
                list.SelectedIndex = displays.ToList().FindIndex(d => d.Id == controller.Settings.ViewingId);
                list.ItemHeight = (int)(48 * CreateGraphicsDpi() / 96f);
                map.Displays = displays;
                map.SelectedId = controller.Settings.ViewingId;
                map.Invalidate();
                bool valid = list.SelectedIndex >= 0 && displays.Count > 1;
                start.Enabled = valid;
                selection.Text = "01  鑑賞する画面を選択  ·  " + displays.Count + " 台を検出";
                status.Text = displays.Count < 2 ? "拡張表示のディスプレイを 2 台以上接続してください。" :
                    valid ? "画面 " + displays[list.SelectedIndex].Number + " を残し、ほか " + (displays.Count - 1) + " 台を暗くします。" :
                    "映画を見る画面を選んでください。Windows メイン以外も選べます。";
            }
            finally { list.EndUpdate(); refreshing = false; }
        }
        private float CreateGraphicsDpi() { using (var graphics = CreateGraphics()) return graphics.DpiX; }
        private void DrawDisplay(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            var display = (DisplayInfo)list.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? Color.FromArgb(38, 67, 62) : Theme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
            int pad = (int)(12 * e.Graphics.DpiX / 96f);
            int half = e.Bounds.Height / 2;
            TextRenderer.DrawText(e.Graphics, (selected ? "●  " : "○  ") + display.Title, Font,
                new Rectangle(e.Bounds.X + pad, e.Bounds.Y + 2, e.Bounds.Width - pad * 2, half), selected ? Theme.Accent : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, "     " + display.Details, Font,
                new Rectangle(e.Bounds.X + pad, e.Bounds.Y + half - 1, e.Bounds.Width - pad * 2, half), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }
        public void ShowStatus(string message) { if (!String.IsNullOrEmpty(message)) status.Text = message; }
        public void RefreshUpdates()
        {
            var updates = controller.Updates;
            updateStatus.Text = updates.Message;
            updateStatus.ForeColor = updates.Available == null ? Theme.Muted : Theme.Accent;
        }
        private void ShowUpdateDialog()
        {
            if (UpdateDialogOpen) { updateDialog.Activate(); return; }
            using (var dialog = new UpdateForm(controller.Updates, controller.Settings, controller.SaveSettings))
            {
                updateDialog = dialog;
                try { dialog.ShowDialog(this); }
                finally { updateDialog = null; }
            }
        }
        public void ActivateSettings()
        {
            if (UpdateDialogOpen) updateDialog.Activate();
            else Activate();
        }
        public void ShowHotkeyStatus(string message, bool warning)
        {
            hotkeyStatus.Text = message;
            hotkeyStatus.ForeColor = warning ? Color.FromArgb(247, 192, 126) : Theme.Muted;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && HotkeyPressed != null) { HotkeyPressed(m.WParam.ToInt32()); return; }
            base.WndProc(ref m);
        }
    }

    internal sealed class HotkeyManager : IDisposable
    {
        public static readonly string[] KeyNames = { "F6", "F7", "F8", "F9", "F10", "F11", "B", "D", "V", "無効" };
        private readonly IntPtr handle;
        private readonly List<int> registered = new List<int>();
        public HotkeyManager(IntPtr handle) { this.handle = handle; }
        public static bool ValidName(string name) { return KeyNames.Contains(name); }
        public List<string> Apply(Settings settings)
        {
            Dispose();
            string[] values = { settings.ToggleKey, settings.RevealKey, settings.StopKey };
            string[] labels = { "開始 / 全解除", "画面の開閉", "全解除のみ" };
            var warnings = new List<string>();
            var used = new HashSet<string>();
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == "無効") continue;
                Keys key;
                if (!ValidName(values[i]) || !Enum.TryParse<Keys>(values[i], out key)) { warnings.Add(labels[i] + ": 設定が無効"); continue; }
                if (!used.Add(values[i])) { warnings.Add(labels[i] + ": キーが重複"); continue; }
                if (Native.RegisterHotKey(handle, i + 1, 1 | 2 | 4 | 0x4000, (uint)key)) registered.Add(i + 1);
                else warnings.Add(labels[i] + ": " + values[i] + " を登録できません（競合など）");
            }
            return warnings;
        }
        public void Dispose()
        {
            foreach (int id in registered) Native.UnregisterHotKey(handle, id);
            registered.Clear();
        }
    }
}
