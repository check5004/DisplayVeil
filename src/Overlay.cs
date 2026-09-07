using System;
using System.Drawing;
using System.Windows.Forms;

namespace DisplayVeil
{
    internal class PassiveForm : Form
    {
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW; return cp; }
        }
        public PassiveForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            DoubleBuffered = true;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }
        public void Place(Rectangle bounds)
        {
            Bounds = bounds;
            if (!Visible) Show();
            if (!Native.SetWindowPos(Handle, Native.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW))
                throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
        public void MaintainTopmost()
        {
            if (Visible) Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOACTIVATE | 1 | 2);
        }
    }

    internal sealed class CurtainForm : PassiveForm
    {
        private readonly DisplayInfo display;
        private readonly Panel hint;
        private readonly Label explanation;
        private readonly Button reveal, stop;
        private bool revealOnRelease;
        public event Action RevealRequested;
        public event Action StopRequested;
        public event Action HintRequested;
        public bool HintVisible { get { return hint.Visible; } }
        public CurtainForm(DisplayInfo display)
        {
            this.display = display;
            Text = "Display Veil — 黒幕 " + display.Number;
            AccessibleName = Text;
            BackColor = Color.Black;
            Bounds = display.Bounds;
            hint = new Panel { BackColor = Theme.Surface, Visible = false };
            explanation = Theme.Label("ダブルクリックでも、この画面を操作できます", 9, Theme.Muted, FontStyle.Regular);
            reveal = Theme.Button("この画面を操作", true);
            stop = Theme.Button("すべて解除", false);
            reveal.Click += delegate { if (RevealRequested != null) RevealRequested(); };
            stop.Click += delegate { if (StopRequested != null) StopRequested(); };
            hint.Controls.AddRange(new Control[] { explanation, reveal, stop });
            Controls.Add(hint);
            LayoutHint();
        }
        private int Px(int value) { return (int)Math.Round(value * display.Dpi / 96.0); }
        private void LayoutHint()
        {
            int width = Math.Min(Px(430), Math.Max(1, ClientSize.Width - Px(24)));
            hint.SetBounds((ClientSize.Width - width) / 2, Math.Max(0, ClientSize.Height - Px(120)), width, Px(100));
            explanation.SetBounds(Px(16), Px(12), width - Px(32), Px(24));
            reveal.SetBounds(Px(16), Px(45), Math.Max(1, (width - Px(42)) / 2), Px(38));
            stop.SetBounds(reveal.Right + Px(10), Px(45), reveal.Width, Px(38));
            explanation.Font = Theme.PhysicalFont(9, display.Dpi, FontStyle.Regular);
            reveal.Font = Theme.PhysicalFont(10, display.Dpi, FontStyle.Bold);
            stop.Font = Theme.PhysicalFont(10, display.Dpi, FontStyle.Bold);
        }
        public void ShowCurtain() { hint.Visible = false; Place(display.Bounds); }
        public void SetHint(bool visible)
        {
            if (hint.Visible != visible) hint.Visible = visible;
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) revealOnRelease = true;
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                SetHint(true);
                if (HintRequested != null) HintRequested();
            }
            if (e.Button == MouseButtons.Left && revealOnRelease)
            {
                revealOnRelease = false;
                // Wait until BOTH presses and releases have been consumed by this window.
                BeginInvoke(new Action(delegate { if (!IsDisposed && RevealRequested != null) RevealRequested(); }));
            }
        }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == Native.WM_DPICHANGED && IsHandleCreated)
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, display.Bounds.X, display.Bounds.Y,
                    display.Bounds.Width, display.Bounds.Height, Native.SWP_NOACTIVATE);
        }
    }

    internal sealed class RevealBar : PassiveForm
    {
        private readonly DisplayInfo display;
        private readonly Label status;
        private readonly Button pin;
        private readonly ToolTip dragHint = new ToolTip();
        private Control dragSource;
        private Point dragPointerStart, dragBarStart;
        public event Action CoverRequested;
        public event Action PinRequested;
        public event Action StopRequested;
        public RevealBar(DisplayInfo display)
        {
            this.display = display;
            Text = "Display Veil — 操作中 " + display.Number;
            BackColor = Theme.Surface;
            status = Theme.Label("画面 " + display.Number + "  操作中", 9, Theme.Text, FontStyle.Bold);
            pin = Theme.Button("固定する", false);
            var cover = Theme.Button("暗く戻す", true);
            var stop = Theme.Button("全解除", false);
            pin.Click += delegate { if (PinRequested != null) PinRequested(); };
            cover.Click += delegate { if (CoverRequested != null) CoverRequested(); };
            stop.Click += delegate { if (StopRequested != null) StopRequested(); };
            Controls.AddRange(new Control[] { status, pin, cover, stop });
            Cursor = Cursors.SizeAll;
            foreach (Control surface in new Control[] { this, status })
            {
                dragHint.SetToolTip(surface, "ドラッグしてメニューを移動");
                surface.MouseDown += BeginDrag;
                surface.MouseMove += MoveDrag;
                surface.MouseUp += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) EndDrag(); };
                surface.MouseCaptureChanged += delegate(object sender, EventArgs e)
                {
                    if (dragSource == sender && !dragSource.Capture) EndDrag();
                };
            }
            float scale = display.Dpi / 96f;
            int width = Math.Min((int)(470 * scale), display.WorkArea.Width);
            int height = (int)(56 * scale);
            int gap = (int)(8 * scale), bw = Math.Max(1, (int)(90 * scale));
            status.AutoSize = false;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.SetBounds(gap * 2, 0, Math.Max(1, width - 3 * bw - 6 * gap), height);
            pin.SetBounds(status.Right + gap, gap, bw, height - gap * 2);
            cover.SetBounds(pin.Right + gap, gap, bw, height - gap * 2);
            stop.SetBounds(cover.Right + gap, gap, bw, height - gap * 2);
            foreach (Control control in Controls) control.Font = Theme.PhysicalFont(9, display.Dpi, FontStyle.Bold);
            Bounds = new Rectangle(display.WorkArea.X + (display.WorkArea.Width - width) / 2, display.WorkArea.Y + (int)(12 * scale), width, height);
        }
        private void BeginDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            EndDrag();
            dragSource = (Control)sender;
            dragPointerStart = dragSource.PointToScreen(e.Location);
            dragBarStart = Location;
            dragSource.Capture = true;
        }
        private void MoveDrag(object sender, MouseEventArgs e)
        {
            if (dragSource != sender) return;
            if ((e.Button & MouseButtons.Left) == 0) { EndDrag(); return; }
            Point pointer = dragSource.PointToScreen(e.Location);
            Rectangle area = display.WorkArea;
            int x = dragBarStart.X + pointer.X - dragPointerStart.X;
            int y = dragBarStart.Y + pointer.Y - dragPointerStart.Y;
            // Keep the controls on their own display and out of the taskbar area.
            x = Math.Max(area.Left, Math.Min(x, area.Right - Width));
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - Height));
            Place(new Rectangle(x, y, Width, Height));
        }
        private void EndDrag()
        {
            Control source = dragSource;
            dragSource = null;
            if (source != null && source.Capture) source.Capture = false;
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            if (!Visible) EndDrag();
            base.OnVisibleChanged(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { EndDrag(); dragHint.Dispose(); }
            base.Dispose(disposing);
        }
        public void UpdateStatus(VeilState state, bool autoCover)
        {
            status.Text = "画面 " + display.Number + (state == VeilState.Pinned ? "  固定中" : autoCover ? "  一時操作中" : "  操作中");
            pin.Text = state == VeilState.Pinned ? "固定を解除" : "固定する";
            pin.AccessibleName = pin.Text;
            pin.BackColor = state == VeilState.Pinned ? Color.FromArgb(53, 82, 77) : Theme.Surface;
        }
        public void ShowBar() { Place(Bounds); }
    }

    internal sealed class DisplayBadge : PassiveForm
    {
        public DisplayBadge(DisplayInfo display)
        {
            Text = "Display Veil — 画面番号 " + display.Number;
            BackColor = Theme.Surface;
            int side = (int)(180 * display.Dpi / 96.0);
            Bounds = new Rectangle(display.Bounds.X + (display.Bounds.Width - side) / 2,
                display.Bounds.Y + (display.Bounds.Height - side) / 2, side, side);
            var number = Theme.Label(display.Number.ToString(), 56 * display.Dpi / 96f, Theme.Accent, FontStyle.Bold);
            number.Font = Theme.PhysicalFont(56, display.Dpi, FontStyle.Bold);
            number.AutoSize = false;
            number.Dock = DockStyle.Fill;
            number.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(number);
        }
    }
}
