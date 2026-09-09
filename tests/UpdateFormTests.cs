using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DisplayVeil.Tests
{
    internal static class UpdateFormTests
    {
        private static void Assert(bool condition) { if (!condition) throw new Exception("Update dialog assertion failed"); }
        private static T Find<T>(Control parent, string name) where T : Control { return (T)parent.Controls.Find(name, true)[0]; }
        private static void Finish(Task task)
        {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted && watch.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(10); }
            Assert(task.IsCompleted);
            task.GetAwaiter().GetResult();
            Application.DoEvents();
        }
        public static void RunAll(Action<string, Action> runDesktop)
        {
            runDesktop("Update dialog keeps current version and notes through checks and failures", delegate
            {
                var pending = new TaskCompletionSource<UpdateRelease>();
                var settings = new Settings { AutoCheckUpdates = false, LatestReleaseTag = "v1.2.0" };
                int requests = 0, saves = 0;
                string opened = null;
                using (var monitor = new UpdateMonitor(settings, new Version(1, 1, 2, 0), delegate { requests++; return pending.Task; }, delegate { }))
                using (var dialog = new UpdateForm(monitor, settings, delegate { saves++; }, delegate(string url) { opened = url; }))
                {
                    dialog.Show(); Application.DoEvents();
                    Assert(monitor.Busy && requests == 1);
                    Assert(Find<Label>(dialog, "CurrentVersion").Text == "v1.1.2");
                    Assert(!Find<Button>(dialog, "CheckUpdates").Enabled && Find<Button>(dialog, "CloseUpdates").Enabled);
                    Find<Button>(dialog, "ReleasePage").PerformClick();
                    Assert(opened == UpdateRelease.ReleasesUrl + "/tag/v1.2.0");
                    pending.SetResult(UpdateRelease.FromTag("v1.2.0", "## 変更内容\n- **日本語**の表示を改善\n- [詳細](https://example.invalid)\n<script>alert(1)</script>"));
                    Finish(monitor.CheckAsync(true, DateTime.UtcNow));
                    var notes = Find<RichTextBox>(dialog, "ReleaseNotes");
                    Assert(!monitor.Busy && notes.Text.Contains("• 日本語の表示を改善") && !notes.Text.Contains("##") && !notes.DetectUrls);
                    Assert(notes.Text.Contains("<script>alert(1)</script>"));
                    pending = new TaskCompletionSource<UpdateRelease>();
                    var retry = monitor.CheckAsync(true, DateTime.UtcNow);
                    Assert(!Find<Button>(dialog, "CheckUpdates").Enabled && notes.Text.Contains("日本語"));
                    pending.SetException(new HttpRequestException()); Finish(retry);
                    Assert(Find<Label>(dialog, "UpdateStatus").Text.Contains("確認できません") && notes.Text.Contains("日本語"));
                    Assert(Find<Label>(dialog, "CurrentVersion").Text == "v1.1.2");
                    Assert(Find<Button>(dialog, "CheckUpdates").Enabled);
                    Find<CheckBox>(dialog, "AutoCheckUpdates").Checked = true;
                    Assert(settings.AutoCheckUpdates && saves == 1);
                    dialog.Close();
                }
            });
            runDesktop("Update dialog handles empty notes, missing releases and browser launch failures", delegate
            {
                foreach (bool missing in new[] { false, true })
                using (var monitor = new UpdateMonitor(new Settings(), new Version(1, 1, 2, 0),
                    delegate { return Task.FromResult(missing ? null : UpdateRelease.FromTag("v1.1.2", "")); }, delegate { }))
                using (var dialog = new UpdateForm(monitor, new Settings(), delegate { }, delegate { throw new Win32Exception(); }))
                {
                    dialog.Show(); Application.DoEvents();
                    Assert(Find<RichTextBox>(dialog, "ReleaseNotes").Text.Contains(missing ? "更新確認後" : "記載がありません"));
                    Find<Button>(dialog, "ReleasePage").PerformClick();
                    Assert(Find<Label>(dialog, "UpdateStatus").Text.Contains("ブラウザーを開けません"));
                    Assert(Find<Label>(dialog, "CurrentVersion").Text == "v1.1.2");
                    Find<Button>(dialog, "CheckUpdates").PerformClick();
                    Assert(!Find<Label>(dialog, "UpdateStatus").Text.Contains("ブラウザー"));
                    dialog.Close();
                }
            });
            runDesktop("Closing and reopening update details during a request updates only the active dialog", delegate
            {
                int requests = 0;
                var pending = new TaskCompletionSource<UpdateRelease>();
                using (var monitor = new UpdateMonitor(new Settings(), new Version(1, 1, 2, 0), delegate { requests++; return pending.Task; }, delegate { }))
                {
                    using (var first = new UpdateForm(monitor, new Settings(), delegate { }))
                    { first.Show(); Application.DoEvents(); first.Close(); }
                    using (var second = new UpdateForm(monitor, new Settings(), delegate { }))
                    {
                        second.Show(); Application.DoEvents(); Assert(requests == 1);
                        pending.SetResult(UpdateRelease.FromTag("v1.2.0", "再表示した画面の変更内容"));
                        Finish(monitor.CheckAsync(true, DateTime.UtcNow));
                        Assert(Find<RichTextBox>(second, "ReleaseNotes").Text.Contains("再表示"));
                        second.Close();
                    }
                }
            });
            runDesktop("Update dialog scales and scrolls long notes at 100, 125, 150 and 200 percent", delegate
            {
                string body = "## 新しくできること\n- **操作バー**をドラッグして移動できます。\n\n## 修正\n- 現在のバージョンが常に表示されます。\n\n";
                for (int i = 0; i < 40; i++) body += "- 長いリリースノートもスクロールして読むことができます。\n";
                using (var monitor = new UpdateMonitor(new Settings(), new Version(1, 1, 2, 0), delegate { return Task.FromResult(UpdateRelease.FromTag("v1.2.0", body)); }, delegate { }))
                {
                    Finish(monitor.CheckAsync(true, DateTime.UtcNow));
                    foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
                    using (var dialog = new UpdateForm(monitor, new Settings(), delegate { }))
                    {
                        // Simulates layout scaling; physical mixed-DPI moves remain a manual check.
                        dialog.Scale(new SizeF(scale, scale));
                        dialog.Show(); Application.DoEvents();
                        var notes = Find<RichTextBox>(dialog, "ReleaseNotes");
                        var close = Find<Button>(dialog, "CloseUpdates");
                        if (notes.Height < 80 || notes.ScrollBars != RichTextBoxScrollBars.Vertical)
                            throw new Exception("Notes viewport at scale " + scale + ": " + notes.Bounds);
                        foreach (string name in new[] { "CurrentVersion", "ReleaseNotes", "UpdateGuide", "CheckUpdates", "ReleasePage", "CloseUpdates" })
                        {
                            var control = dialog.Controls.Find(name, true)[0];
                            var bounds = dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                            if (!dialog.ClientRectangle.Contains(bounds))
                                throw new Exception(name + " at scale " + scale + ": " + bounds + " outside " + dialog.ClientRectangle);
                        }
                        Assert(notes.RectangleToScreen(notes.ClientRectangle).Bottom < close.RectangleToScreen(close.ClientRectangle).Top);
                        if (scale == 1f)
                        {
                            using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                            {
                                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update-dialog.png"));
                            }
                            dialog.Size = dialog.MinimumSize; Application.DoEvents();
                            if (notes.Height < 60 || close.Bottom > close.Parent.ClientSize.Height)
                                throw new Exception("Minimum dialog viewport: " + notes.Bounds + "; close: " + close.Bounds);
                        }
                        dialog.Close();
                    }
                }
            });
        }
    }
}
