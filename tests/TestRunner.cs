using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace DisplayVeil.Tests
{
    internal static class TestRunner
    {
        private static int passed, failed, skipped;
        private static bool headless;
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Any(arg => arg != "--headless")) { Console.Error.WriteLine("Usage: DisplayVeil.Tests.exe [--headless]"); return 2; }
            headless = args.Contains("--headless");
            if (!headless)
            {
                Native.EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
            }
            Run("A viewing display can be non-primary", delegate
            {
                var displays = FourDisplays();
                var targets = DisplayLayout.Targets(displays, "movie");
                Assert(targets.Count == 3 && targets.Any(d => d.IsPrimary) && targets.All(d => d.Id != "movie"));
            });
            Run("No selection cannot black out all displays", delegate { Assert(DisplayLayout.Targets(FourDisplays(), "").Count == 0); });
            Run("Disconnected selection does not fall back to primary", delegate { Assert(DisplayLayout.Targets(FourDisplays(), "missing").Count == 0); });
            Run("Ambiguous identity is rejected", delegate
            {
                var displays = FourDisplays(); displays[0].Id = "movie";
                Assert(DisplayLayout.Targets(displays, "movie").Count == 0);
            });
            Run("One screen yields no curtains", delegate { Assert(DisplayLayout.Targets(FourDisplays().Take(1).ToList(), "primary").Count == 0); });
            Run("No screens is safe", delegate { Assert(DisplayLayout.Targets(new List<DisplayInfo>(), "movie").Count == 0); });
            Run("129 displays with every possible viewing choice", delegate
            {
                var displays = Enumerable.Range(0, 129).Select(i => new DisplayInfo { Id = "d" + i,
                    Bounds = new Rectangle((i % 16 - 8) * 2560, (i / 16 - 4) * 1440, 2560, 1440), IsPrimary = i == 2 }).ToList();
                foreach (var display in displays)
                {
                    var targets = DisplayLayout.Targets(displays, display.Id);
                    Assert(targets.Count == 128 && !targets.Contains(display));
                }
            });
            Run("Mixed resolutions and negative coordinates fit without clipping", delegate
            {
                var displays = FourDisplays(); var area = new RectangleF(20, 20, 800, 200);
                var result = DisplayLayout.Fit(displays, area);
                Assert(result.Count == 4);
                for (int i = 0; i < result.Count; i++)
                {
                    var r = result[i];
                    Assert(r.Left >= area.Left - .01 && r.Right <= area.Right + .01 && r.Top >= area.Top - .01 && r.Bottom <= area.Bottom + .01);
                    Assert(Math.Abs(r.Width / r.Height - (float)displays[i].Bounds.Width / displays[i].Bounds.Height) < .001);
                }
            });
            Run("Portrait + ultrawide map preserves aspect ratio", delegate
            {
                var displays = FourDisplays(); displays[0].Bounds = new Rectangle(-1440, -2560, 1440, 2560);
                var result = DisplayLayout.Fit(displays, new RectangleF(0, 0, 500, 200));
                Assert(Math.Abs(result[0].Width / result[0].Height - 1440f / 2560) < .001);
            });
            Run("Empty and tiny map areas are safe", delegate
            {
                Assert(DisplayLayout.Fit(new List<DisplayInfo>(), new RectangleF(0, 0, 100, 100)).Count == 0);
                Assert(DisplayLayout.Fit(FourDisplays(), RectangleF.Empty).Count == 0);
            });
            Run("Enumeration order alone is not a topology change", delegate
            {
                var displays = FourDisplays(); string signature = DisplayLayout.Signature(displays);
                displays.Reverse(); Assert(signature == DisplayLayout.Signature(displays));
            });
            Run("DPI, work area and resolution changes invalidate topology", delegate
            {
                var displays = FourDisplays(); string signature = DisplayLayout.Signature(displays);
                displays[1].Dpi = 144; Assert(signature != DisplayLayout.Signature(displays));
                displays[1].Dpi = 96; displays[1].Bounds = new Rectangle(0, 0, 1920, 1080);
                Assert(signature != DisplayLayout.Signature(displays));
            });
            Run("Revealed screen remains open while pointer stays", delegate
            {
                var session = new VeilSession(); session.Reveal();
                Assert(!session.Tick(true, false, true, 500000) && session.State == VeilState.Revealed);
            });
            Run("Leaving screen covers after exactly 1500 ms", delegate
            {
                var session = new VeilSession(); session.Reveal();
                Assert(!session.Tick(false, false, true, 100));
                Assert(!session.Tick(false, false, true, 1599));
                Assert(session.Tick(false, false, true, 1600) && session.State == VeilState.Covered);
            });
            Run("Re-entering cancels the exit countdown", delegate
            {
                var session = new VeilSession(); session.Reveal();
                session.Tick(false, false, true, 0); session.Tick(true, false, true, 1400);
                Assert(!session.Tick(false, false, true, 1500));
                Assert(!session.Tick(false, false, true, 2999));
                Assert(session.Tick(false, false, true, 3000));
            });
            Run("Dragging never covers and resets the countdown", delegate
            {
                var session = new VeilSession(); session.Reveal();
                session.Tick(false, false, true, 0);
                Assert(!session.Tick(false, true, true, 2000));
                Assert(!session.Tick(false, false, true, 9000));
                Assert(!session.Tick(false, false, true, 10499));
                Assert(session.Tick(false, false, true, 10500));
            });
            Run("Pinned screen remains open outside", delegate
            {
                var session = new VeilSession(); session.Reveal(); session.TogglePin();
                Assert(!session.Tick(false, false, true, 0));
                Assert(!session.Tick(false, false, true, 99999999) && session.State == VeilState.Pinned);
            });
            Run("Unpin begins a fresh countdown", delegate
            {
                var session = new VeilSession(); session.Reveal(); session.TogglePin();
                session.Tick(false, false, true, 0); session.TogglePin();
                Assert(!session.Tick(false, false, true, 9999));
                Assert(session.Tick(false, false, true, 11499));
            });
            Run("Auto-return can be disabled and re-enabled", delegate
            {
                var session = new VeilSession(); session.Reveal();
                session.Tick(false, false, true, 0);
                Assert(!session.Tick(false, false, false, 9000));
                Assert(!session.Tick(false, false, true, 10000));
                Assert(session.Tick(false, false, true, 11500));
            });
            Run("Manual cover clears pin and countdown", delegate
            {
                var session = new VeilSession(); session.Reveal(); session.TogglePin(); session.Cover();
                Assert(session.State == VeilState.Covered);
                session.Reveal(); Assert(!session.Tick(false, false, true, 9999));
            });
            Run("Covered screen cannot be pinned accidentally", delegate
            {
                var session = new VeilSession(); session.TogglePin(); Assert(session.State == VeilState.Covered);
            });
            Run("Independent sessions do not uncover other displays", delegate
            {
                var a = new VeilSession(); var b = new VeilSession(); a.Reveal(); a.TogglePin();
                Assert(a.State == VeilState.Pinned && b.State == VeilState.Covered);
            });
            string settingsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-" + Guid.NewGuid().ToString("N"));
            Run("Settings defaults and atomic overwrite round trip", delegate
            {
                var store = new SettingsStore(settingsDir); string warning;
                var settings = store.Load(out warning);
                Assert(settings.AutoCover && settings.ShowMouseHints && settings.ToggleKey == "F9");
                settings.ViewingId = "\\\\?\\DISPLAY#日本語"; settings.AutoCover = false; settings.RevealKey = "B";
                Assert(store.Save(settings, out warning)); settings.ShowMouseHints = false;
                Assert(store.Save(settings, out warning));
                var loaded = store.Load(out warning);
                Assert(warning == "" && loaded.ViewingId == settings.ViewingId && !loaded.AutoCover && !loaded.ShowMouseHints && loaded.RevealKey == "B");
            });
            Run("Older settings retain defaults for missing fields", delegate
            {
                File.WriteAllText(Path.Combine(settingsDir, "settings.json"), "{\"ViewingId\":\"movie\"}");
                string warning; var settings = new SettingsStore(settingsDir).Load(out warning);
                Assert(settings.ViewingId == "movie" && settings.AutoCover && settings.ShowMouseHints && settings.StopKey == "F11");
            });
            Run("Corrupt settings recover without crashing", delegate
            {
                File.WriteAllText(Path.Combine(settingsDir, "settings.json"), "{broken");
                string warning; var settings = new SettingsStore(settingsDir).Load(out warning);
                Assert(warning.Length > 0 && settings.AutoCover);
            });
            Run("Save failure is reported without losing runtime settings", delegate
            {
                string blocked = Path.Combine(settingsDir, "not-a-directory"); File.WriteAllText(blocked, "test");
                string warning; var settings = new Settings { ViewingId = "movie" };
                Assert(!new SettingsStore(blocked).Save(settings, out warning) && warning.Length > 0 && settings.ViewingId == "movie");
            });
            RunDesktop("Real display enumeration gives unique identities and physical bounds", delegate
            {
                var displays = Native.GetDisplays();
                Assert(displays.Count > 0 && displays.Select(d => d.Id).Distinct().Count() == displays.Count);
                foreach (var display in displays)
                {
                    Assert(display.Bounds.Width > 0 && display.Bounds.Height > 0 && display.Dpi >= 96);
                    Console.WriteLine("  Display " + display.Number + ": " + display.Bounds + ", DPI=" + display.Dpi + ", primary=" + display.IsPrimary);
                }
            });
            RunDesktop("Native full-monitor curtains preserve focus and exact bounds", delegate
            {
                foreach (var display in Native.GetDisplays())
                {
                    IntPtr foreground = Native.GetForegroundWindow();
                    using (var curtain = new CurtainForm(display))
                    {
                        curtain.ShowCurtain(); Application.DoEvents();
                        Native.RECT bounds;
                        Assert(Native.GetWindowRect(curtain.Handle, out bounds) && bounds.ToRectangle() == display.Bounds);
                        Assert(curtain.BackColor == Color.Black && !curtain.ShowInTaskbar && !curtain.HintVisible);
                        int style = Native.GetWindowLong(curtain.Handle, -20);
                        Assert((style & Native.WS_EX_NOACTIVATE) != 0 && (style & Native.WS_EX_TOOLWINDOW) != 0 && (style & 8) != 0);
                        Assert(foreground == Native.GetForegroundWindow());
                    }
                    Application.DoEvents();
                }
            });
            RunDesktop("Reveal and cover update native visibility without activation", delegate
            {
                var display = Native.GetDisplays()[0];
                IntPtr foreground = Native.GetForegroundWindow();
                using (var veil = new VeilWindow(display, delegate { }))
                {
                    veil.Cover(); Application.DoEvents();
                    Assert(veil.Curtain.Visible && !veil.Bar.Visible);
                    veil.Reveal(); Application.DoEvents();
                    Assert(!veil.Curtain.Visible && veil.Bar.Visible && veil.Session.State == VeilState.Revealed);
                    Assert(display.WorkArea.Contains(veil.Bar.Bounds));
                    veil.Cover(); Application.DoEvents();
                    Assert(veil.Curtain.Visible && !veil.Bar.Visible && foreground == Native.GetForegroundWindow());
                }
            });
            RunDesktop("Global hotkey conflicts, duplicates, disable and cleanup", delegate
            {
                using (var first = new Form())
                using (var second = new Form())
                using (var manager = new HotkeyManager(second.Handle))
                {
                    Assert(Native.RegisterHotKey(first.Handle, 999, 1 | 2 | 4 | 0x4000, (uint)Keys.F8));
                    try
                    {
                        var warnings = manager.Apply(new Settings { ToggleKey = "F8", RevealKey = "無効", StopKey = "無効" });
                        Assert(warnings.Count == 1 && warnings[0].Contains("登録できません"));
                    }
                    finally { Native.UnregisterHotKey(first.Handle, 999); }
                    Assert(manager.Apply(new Settings { ToggleKey = "F8", RevealKey = "F8", StopKey = "無効" }).Count == 1);
                    Assert(manager.Apply(new Settings { ToggleKey = "無効", RevealKey = "無効", StopKey = "無効" }).Count == 0);
                    Assert(Native.RegisterHotKey(first.Handle, 999, 1 | 2 | 4 | 0x4000, (uint)Keys.F8));
                    Native.UnregisterHotKey(first.Handle, 999);
                }
            });
            RunDesktop("Double click reveals only after the final mouse release", delegate
            {
                var display = Native.GetDisplays()[0]; bool revealed = false;
                using (var curtain = new CurtainForm(display))
                {
                    curtain.RevealRequested += delegate { revealed = true; };
                    var handle = curtain.Handle;
                    typeof(CurtainForm).GetMethod("OnMouseDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(curtain, new object[] { new MouseEventArgs(MouseButtons.Left, 2, 200, 200, 0) });
                    Assert(!revealed);
                    typeof(CurtainForm).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(curtain, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 200, 200, 0) });
                    Assert(!revealed);
                    Application.DoEvents(); Assert(revealed);
                }
            });
            RunDesktop("Right click explicitly renews escape controls", delegate
            {
                var display = Native.GetDisplays()[0]; int hints = 0;
                using (var curtain = new CurtainForm(display))
                {
                    curtain.HintRequested += delegate { hints++; };
                    curtain.ShowCurtain();
                    typeof(CurtainForm).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(curtain, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 200, 200, 0) });
                    Assert(hints == 1 && curtain.HintVisible);
                    using (var bitmap = new Bitmap(curtain.Width, curtain.Height))
                    {
                        curtain.DrawToBitmap(bitmap, curtain.ClientRectangle);
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "curtain-hint.png"));
                    }
                }
            });
            RunDesktop("Physical overlay controls fit at 100, 125, 150 and 200 percent", delegate
            {
                foreach (int dpi in new[] { 96, 120, 144, 192 })
                {
                    var display = Native.GetDisplays()[0]; display.Dpi = dpi;
                    using (var bar = new RevealBar(display))
                    {
                        var handle = bar.Handle;
                        bar.UpdateStatus(VeilState.Pinned, true);
                        bar.ShowBar();
                        Application.DoEvents();
                        foreach (Control control in bar.Controls)
                        {
                            Assert(bar.ClientRectangle.Contains(control.Bounds));
                            Assert(control.Font.Unit == GraphicsUnit.Pixel);
                            Assert(control.Font.Size < control.Height);
                        }
                        using (var bitmap = new Bitmap(bar.Width, bar.Height))
                        {
                            bar.DrawToBitmap(bitmap, bar.ClientRectangle);
                            bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reveal-bar-" + dpi + ".png"));
                        }
                    }
                }
            });
            RunDesktop("Controller covers all real non-viewing screens and stops on disconnect", delegate
            {
                var snapshot = Native.GetDisplays();
                if (snapshot.Count < 2) throw new SkipTestException("requires multiple physical displays");
                var viewing = snapshot.First(d => !d.IsPrimary);
                using (var controller = new AppController(Path.Combine(settingsDir, "controller"), delegate { return snapshot; }))
                {
                    controller.Settings.ViewingId = viewing.Id;
                    controller.Start(); Application.DoEvents();
                    Assert(controller.Running && !controller.Window.Visible);
                    var curtains = Application.OpenForms.OfType<CurtainForm>().ToList();
                    Assert(curtains.Count == snapshot.Count - 1 && curtains.All(c => c.Bounds != viewing.Bounds));
                    Assert(curtains.Any(c => c.Bounds == snapshot.First(d => d.IsPrimary).Bounds));
                    snapshot = snapshot.Where(d => d.Id != viewing.Id).ToList();
                    typeof(AppController).GetMethod("RefreshTopology", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(controller, new object[] { true });
                    Assert(!controller.Running && controller.Window.Visible && !Application.OpenForms.OfType<CurtainForm>().Any());
                    controller.Start();
                    Assert(!controller.Running);
                    controller.Window.Close();
                }
            });
            RunDesktop("Failed display refresh cannot start with stale bounds", delegate
            {
                bool fail = false;
                using (var controller = new AppController(Path.Combine(settingsDir, "failed-enumeration"), delegate
                {
                    if (fail) throw new InvalidOperationException("Simulated enumeration failure");
                    return Native.GetDisplays();
                }))
                {
                    controller.Settings.ViewingId = controller.Displays[0].Id;
                    fail = true;
                    controller.Start();
                    Assert(!controller.Running && !Application.OpenForms.OfType<CurtainForm>().Any());
                    controller.Window.Close();
                }
            });
            Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed, " + skipped + " skipped");
            return failed == 0 ? 0 : 1;
        }
        private static List<DisplayInfo> FourDisplays()
        {
            return new List<DisplayInfo>
            {
                new DisplayInfo { Id = "primary", DeviceName = "DISPLAY1", Bounds = new Rectangle(0, 0, 2560, 1440), IsPrimary = true },
                new DisplayInfo { Id = "secondary", DeviceName = "DISPLAY2", Bounds = new Rectangle(2560, 0, 2560, 1440) },
                new DisplayInfo { Id = "movie", DeviceName = "DISPLAY3", Bounds = new Rectangle(0, -1440, 3440, 1440), Dpi = 120 },
                new DisplayInfo { Id = "ultrawide", DeviceName = "DISPLAY4", Bounds = new Rectangle(-3440, -1440, 3440, 1440), Dpi = 144 }
            };
        }
        private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
        private sealed class SkipTestException : Exception
        {
            public SkipTestException(string reason) : base(reason) { }
        }
        private static void RunDesktop(string name, Action test)
        {
            if (headless) { skipped++; Console.WriteLine("SKIP " + name + " (requires an interactive desktop)"); return; }
            Run(name, test);
        }
        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (SkipTestException ex) { skipped++; Console.WriteLine("SKIP " + name + " (" + ex.Message + ")"); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
        }
    }
}
