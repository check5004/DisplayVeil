using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace DisplayVeil.Tests
{
    internal static class RevealBarTests
    {
        public static void RunAll(Action<string, Action> run)
        {
            run("Reveal bar drags without focus changes and keeps its position when reopened", delegate
            {
                foreach (var display in Native.GetDisplays())
                foreach (int dpi in new[] { 96, 120, 144, 192 })
                using (var bar = CreateBar(display, dpi))
                {
                    IntPtr foreground = Native.GetForegroundWindow();
                    bar.ShowBar();
                    var status = bar.Controls.OfType<Label>().Single();
                    Point start = bar.Location;
                    Mouse(status, "OnMouseDown", MouseButtons.Left, new Point(10, 10));
                    Assert(status.Capture);
                    Mouse(status, "OnMouseMove", MouseButtons.Left, new Point(30, 90));
                    Assert(bar.Location == new Point(start.X + 20, start.Y + 80));
                    Mouse(status, "OnMouseUp", MouseButtons.Left, new Point(30, 90));
                    Assert(!status.Capture);
                    Point moved = bar.Location;
                    Mouse(status, "OnMouseMove", MouseButtons.None, new Point(80, 120));
                    bar.Hide(); bar.ShowBar();
                    Assert(bar.Location == moved);
                    Assert(Native.GetForegroundWindow() == foreground);
                }
            });
            run("Reveal bar stays in its work area and cancels dragging on capture loss or hide", delegate
            {
                foreach (var display in Native.GetDisplays())
                using (var bar = new RevealBar(display))
                {
                    bar.ShowBar();
                    Mouse(bar, "OnMouseDown", MouseButtons.Left, new Point(2, 2));
                    Mouse(bar, "OnMouseMove", MouseButtons.Left, new Point(-30000, -30000));
                    Assert(bar.Location == display.WorkArea.Location);
                    Mouse(bar, "OnMouseMove", MouseButtons.Left, new Point(30000, 30000));
                    Assert(bar.Right == display.WorkArea.Right && bar.Bottom == display.WorkArea.Bottom);
                    bar.Capture = false;
                    Point moved = bar.Location;
                    Mouse(bar, "OnMouseMove", MouseButtons.Left, new Point(100, 100));
                    Assert(bar.Location == moved);
                    var status = bar.Controls.OfType<Label>().Single();
                    Mouse(status, "OnMouseDown", MouseButtons.Left, new Point(10, 10));
                    bar.Hide(); bar.ShowBar();
                    Assert(!status.Capture);
                    Mouse(status, "OnMouseMove", MouseButtons.Left, new Point(100, 100));
                    Assert(bar.Location == moved);
                }
            });
            run("Reveal bar right clicks and action buttons do not start a drag", delegate
            {
                using (var bar = new RevealBar(Native.GetDisplays()[0]))
                {
                    bar.ShowBar();
                    Point start = bar.Location;
                    var status = bar.Controls.OfType<Label>().Single();
                    Mouse(status, "OnMouseDown", MouseButtons.Right, new Point(10, 10));
                    Mouse(status, "OnMouseMove", MouseButtons.Right, new Point(100, 100));
                    Assert(bar.Location == start && !status.Capture);
                    int pins = 0, covers = 0, stops = 0;
                    bar.PinRequested += delegate { pins++; };
                    bar.CoverRequested += delegate { covers++; };
                    bar.StopRequested += delegate { stops++; };
                    foreach (var button in bar.Controls.OfType<Button>())
                    {
                        Mouse(button, "OnMouseDown", MouseButtons.Left, new Point(5, 5));
                        Mouse(button, "OnMouseMove", MouseButtons.Left, new Point(100, 100));
                        Mouse(button, "OnMouseUp", MouseButtons.Left, new Point(100, 100));
                        Assert(bar.Location == start);
                        button.PerformClick();
                    }
                    Assert(pins == 1 && covers == 1 && stops == 1);
                }
            });
        }
        private static RevealBar CreateBar(DisplayInfo display, int dpi)
        {
            return new RevealBar(new DisplayInfo { Number = display.Number, Bounds = display.Bounds,
                WorkArea = display.WorkArea, Dpi = dpi });
        }
        private static void Mouse(Control control, string method, MouseButtons button, Point point)
        {
            typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(control, new object[] { new MouseEventArgs(button, 1, point.X, point.Y, 0) });
        }
        private static void Assert(bool condition)
        {
            if (!condition) throw new Exception("Reveal bar assertion failed.");
        }
    }
}
