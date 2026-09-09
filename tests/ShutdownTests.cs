using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DisplayVeil.Tests
{
    internal static class ShutdownTests
    {
        public static void RunAll(Action<string, Action> runDesktop)
        {
            foreach (string scenario in new[] { "window", "minimized-tray", "viewing-tray", "pending-update", "curtain-stop", "bar-stop", "update-dialog", "pending-update-dialog" })
            {
                string current = scenario;
                runDesktop("Closing exits the process: " + current, delegate
                {
                    if ((current == "curtain-stop" || current == "bar-stop") && Native.GetDisplays().Count < 2)
                        throw new TestRunner.SkipTestException("requires multiple physical displays");
                    var start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                        "--shutdown-child " + current)
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    using (var process = Process.Start(start))
                    {
                        if (!process.WaitForExit(10000))
                        {
                            process.Kill();
                            process.WaitForExit();
                            throw new Exception("Process remained alive after close: " + current);
                        }
                        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                        if (process.ExitCode != 0 || !output.Contains("SHUTDOWN COMPLETE"))
                            throw new Exception("Shutdown did not complete: " + output);
                    }
                });
            }
        }

        public static int RunChild(string scenario)
        {
            if (!new[] { "window", "minimized-tray", "viewing-tray", "pending-update", "curtain-stop", "bar-stop", "update-dialog", "pending-update-dialog" }.Contains(scenario)) return 2;
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shutdown-settings", Guid.NewGuid().ToString("N"));
            try
            {
                Native.EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                // Keep these process tests offline and independent of personal settings/hotkeys.
                string error;
                if (!new SettingsStore(directory).Save(new Settings
                {
                    AutoCheckUpdates = false, ToggleKey = "無効", RevealKey = "無効", StopKey = "無効"
                }, out error)) throw new Exception(error);
                bool exited = false;
                CancellationToken updateToken = default(CancellationToken);
                using (var controller = new AppController(directory))
                using (var timer = new System.Windows.Forms.Timer { Interval = 250 })
                {
                    controller.ThreadExit += delegate { exited = true; };
                    var tray = (NotifyIcon)typeof(AppController).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                    if (scenario == "minimized-tray") controller.Window.WindowState = FormWindowState.Minimized;
                    bool panelStop = scenario == "curtain-stop" || scenario == "bar-stop";
                    if (scenario == "viewing-tray" || panelStop)
                    {
                        controller.Settings.ViewingId = controller.Displays[0].Id;
                        controller.Start();
                        if (controller.Displays.Count > 1 && !controller.Running) throw new Exception("Viewing did not start");
                        if (panelStop && !controller.Running) throw new Exception("Panel stop tests require multiple displays");
                        // Also exercise auxiliary window cleanup with a single attached display.
                        controller.Identify();
                    }
                    bool updateDialog = scenario.EndsWith("update-dialog");
                    bool pendingUpdate = scenario.StartsWith("pending-update");
                    if (pendingUpdate)
                    {
                        var pending = new TaskCompletionSource<UpdateRelease>();
                        var fetch = new Func<CancellationToken, Task<UpdateRelease>>(delegate(CancellationToken token)
                        {
                            updateToken = token;
                            token.Register(delegate { pending.TrySetCanceled(); });
                            return pending.Task;
                        });
                        typeof(UpdateMonitor).GetField("fetch", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller.Updates, fetch);
                        controller.Window.BeginInvoke(new Action(async delegate { await controller.Updates.CheckAsync(true, DateTime.UtcNow); }));
                    }
                    if (updateDialog)
                    {
                        if (!pendingUpdate)
                        {
                            var fetch = new Func<CancellationToken, Task<UpdateRelease>>(delegate
                            { return Task.FromResult(UpdateRelease.FromTag("v9.0.0", "変更内容")); });
                            typeof(UpdateMonitor).GetField("fetch", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller.Updates, fetch);
                        }
                        controller.Window.BeginInvoke(new Action(delegate
                        {
                            ((Button)typeof(MainForm).GetField("openUpdates", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller.Window)).PerformClick();
                        }));
                    }
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        if (panelStop)
                        {
                            string viewingId = controller.Settings.ViewingId;
                            var curtains = Application.OpenForms.OfType<CurtainForm>().ToList();
                            var bars = Application.OpenForms.OfType<RevealBar>().ToList();
                            var curtain = curtains.First();
                            curtain.SetHint(true);
                            Button stop;
                            if (scenario == "curtain-stop")
                            {
                                stop = (Button)typeof(CurtainForm).GetField("stop", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(curtain);
                            }
                            else
                            {
                                // Invoke the existing reveal action, then click the bar's real button.
                                ((Button)typeof(CurtainForm).GetField("reveal", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(curtain)).PerformClick();
                                var bar = Application.OpenForms.OfType<RevealBar>().First(b => b.Visible);
                                stop = bar.Controls.OfType<Button>().Single(b => b.Text == "全解除");
                            }
                            stop.PerformClick();
                            if (controller.Running || !controller.Window.Visible || controller.Window.WindowState != FormWindowState.Normal ||
                                controller.Window.IsDisposed || controller.Settings.ViewingId != viewingId ||
                                curtains.Any(c => !c.IsDisposed) || bars.Any(b => !b.IsDisposed))
                                throw new Exception("Panel stop did not clear overlays and restore settings");
                            controller.Start();
                            if (!controller.Running || controller.Window.Visible) throw new Exception("Cannot restart viewing from restored settings");
                            controller.ShowUi(null);
                        }
                        if (pendingUpdate && !controller.Updates.Busy) throw new Exception("Update was not pending");
                        if (updateDialog)
                        {
                            var dialog = Application.OpenForms.OfType<UpdateForm>().Single();
                            if (!dialog.Modal || dialog.Owner != controller.Window)
                                throw new Exception("Update details are not owned modal UI: modal=" + dialog.Modal + ", owner=" + (dialog.Owner == controller.Window));
                            controller.Settings.ViewingId = controller.Displays[0].Id;
                            controller.Start();
                            if (controller.Running) throw new Exception("Viewing started over update details");
                            if (!pendingUpdate)
                            {
                                dialog.Close();
                                controller.Window.BeginInvoke(new Action(delegate
                                {
                                    if (controller.Window.UpdateDialogOpen || !controller.Window.Enabled) throw new Exception("Settings did not recover after closing details");
                                    controller.Window.Close();
                                }));
                                return;
                            }
                        }
                        if (scenario.EndsWith("-tray"))
                            ((ToolStripMenuItem)tray.ContextMenuStrip.Items[tray.ContextMenuStrip.Items.Count - 1]).PerformClick();
                        else controller.Window.Close();
                    };
                    timer.Start();
                    Application.Run(controller);
                    if (!exited || !controller.Window.IsDisposed || controller.Running || tray.Visible || Application.OpenForms.Count != 0)
                        throw new Exception("Windows, tray or message loop survived shutdown");
                }
                if (scenario.StartsWith("pending-update") && !updateToken.IsCancellationRequested)
                    throw new Exception("Pending update was not cancelled");
                Console.WriteLine("SHUTDOWN COMPLETE");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
