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
            foreach (string scenario in new[] { "window", "minimized-tray", "viewing-tray", "pending-update" })
            {
                string current = scenario;
                runDesktop("Closing exits the process: " + current, delegate
                {
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
            if (!new[] { "window", "minimized-tray", "viewing-tray", "pending-update" }.Contains(scenario)) return 2;
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
                    if (scenario == "viewing-tray")
                    {
                        controller.Settings.ViewingId = controller.Displays[0].Id;
                        controller.Start();
                        if (controller.Displays.Count > 1 && !controller.Running) throw new Exception("Viewing did not start");
                        // Also exercise auxiliary window cleanup with a single attached display.
                        controller.Identify();
                    }
                    if (scenario == "pending-update")
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
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        if (scenario == "pending-update" && !controller.Updates.Busy) throw new Exception("Update was not pending");
                        if (scenario.EndsWith("-tray"))
                            ((ToolStripMenuItem)tray.ContextMenuStrip.Items[tray.ContextMenuStrip.Items.Count - 1]).PerformClick();
                        else controller.Window.Close();
                    };
                    timer.Start();
                    Application.Run(controller);
                    if (!exited || !controller.Window.IsDisposed || controller.Running || tray.Visible || Application.OpenForms.Count != 0)
                        throw new Exception("Windows, tray or message loop survived shutdown");
                }
                if (scenario == "pending-update" && !updateToken.IsCancellationRequested)
                    throw new Exception("Pending update was not cancelled");
                Console.WriteLine("SHUTDOWN COMPLETE");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
