using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DisplayVeil
{
    internal sealed class DisplayInfo
    {
        public string Id;
        public string DeviceName;
        public string Name;
        public Rectangle Bounds;
        public Rectangle WorkArea;
        public bool IsPrimary;
        public int Dpi = 96;
        public int Number;
        public string Title { get { return "画面 " + Number + "  ·  " + Name; } }
        public string Details { get { return Bounds.Width + " × " + Bounds.Height + "  /  " + Math.Round(Dpi * 100.0 / 96) + "%" + (IsPrimary ? "  /  Windows メイン" : ""); } }
        public override string ToString() { return Title + "  " + Details; }
    }

    internal enum VeilState { Covered, Revealed, Pinned }

    // All timing uses a monotonic Stopwatch clock, never the wall clock.
    internal sealed class VeilSession
    {
        public const long LeaveDelayMs = 1500;
        public VeilState State { get; private set; }
        private long? outsideSince;
        public VeilSession() { State = VeilState.Covered; }
        public void Reveal() { State = VeilState.Revealed; outsideSince = null; }
        public void Cover() { State = VeilState.Covered; outsideSince = null; }
        public void TogglePin()
        {
            if (State == VeilState.Covered) return;
            State = State == VeilState.Pinned ? VeilState.Revealed : VeilState.Pinned;
            outsideSince = null;
        }
        public bool Tick(bool pointerInside, bool mouseButtonDown, bool autoCover, long now)
        {
            if (State != VeilState.Revealed || !autoCover || pointerInside || mouseButtonDown)
            {
                outsideSince = null;
                return false;
            }
            if (!outsideSince.HasValue) outsideSince = now;
            if (now - outsideSince.Value < LeaveDelayMs) return false;
            Cover();
            return true;
        }
    }

    internal static class DisplayLayout
    {
        public static List<DisplayInfo> Targets(IList<DisplayInfo> displays, string viewingId)
        {
            if (String.IsNullOrEmpty(viewingId) || displays.Count(d => d.Id == viewingId) != 1)
                return new List<DisplayInfo>();
            return displays.Where(d => d.Id != viewingId).ToList();
        }

        public static string Signature(IEnumerable<DisplayInfo> displays)
        {
            return String.Join("|", displays.OrderBy(d => d.Id, StringComparer.Ordinal).Select(d =>
                d.Id + ":" + d.DeviceName + ":" + d.Bounds + ":" + d.WorkArea + ":" + d.Dpi + ":" + d.IsPrimary));
        }

        public static List<RectangleF> Fit(IList<DisplayInfo> displays, RectangleF area)
        {
            var result = new List<RectangleF>();
            if (displays.Count == 0 || area.Width <= 0 || area.Height <= 0) return result;
            double left = displays.Min(d => (double)d.Bounds.Left);
            double top = displays.Min(d => (double)d.Bounds.Top);
            double width = displays.Max(d => (double)d.Bounds.Right) - left;
            double height = displays.Max(d => (double)d.Bounds.Bottom) - top;
            if (width <= 0 || height <= 0) return result;
            double scale = Math.Min(area.Width / width, area.Height / height);
            double x = area.X + (area.Width - width * scale) / 2;
            double y = area.Y + (area.Height - height * scale) / 2;
            foreach (var display in displays)
                result.Add(new RectangleF((float)(x + (display.Bounds.X - left) * scale),
                    (float)(y + (display.Bounds.Y - top) * scale),
                    (float)(display.Bounds.Width * scale), (float)(display.Bounds.Height * scale)));
            return result;
        }
    }
}
