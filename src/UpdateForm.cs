using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DisplayVeil
{
    internal sealed class UpdateForm : Form
    {
        private readonly UpdateMonitor updates;
        private readonly Action<string> openUrl;
        private readonly Label latestVersion, status, notesTitle, guide;
        private readonly Button check, releasePage;
        private readonly RichTextBox notes;
        private readonly Font headingFont;
        private UpdateRelease displayedRelease;

        public UpdateForm(UpdateMonitor updates, Settings settings, Action save, Action<string> openUrl = null)
        {
            this.updates = updates;
            this.openUrl = openUrl ?? OpenBrowser;
            Text = "更新とリリースノート — Display Veil";
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Font(10, FontStyle.Regular);
            headingFont = Theme.Font(11, FontStyle.Bold);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 720);
            MinimumSize = new Size(640, 640);
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9,
                Padding = new Padding(24), Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(root);

            var title = Theme.Label("Display Veil のアップデート", 19, Theme.Text, FontStyle.Bold);
            title.Margin = new Padding(0, 0, 0, 16);
            root.Controls.Add(title, 0, 0);
            var versions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            versions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            versions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            Label current;
            versions.Controls.Add(VersionCard("現在使用中", out current), 0, 0);
            current.Name = "CurrentVersion";
            current.Text = "v" + updates.CurrentVersion.ToString(3);
            versions.Controls.Add(VersionCard("公開中の最新バージョン", out latestVersion), 1, 0);
            root.Controls.Add(versions, 0, 1);

            status = Theme.Label("", 10, Theme.Muted, FontStyle.Regular);
            status.Name = "UpdateStatus";
            status.Dock = DockStyle.Fill;
            status.Margin = new Padding(0, 8, 0, 12);
            root.Controls.Add(status, 0, 2);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            check = Theme.Button("今すぐ更新を確認", false);
            check.Name = "CheckUpdates";
            check.Dock = DockStyle.Fill;
            check.Margin = new Padding(0, 0, 6, 6);
            check.Click += async delegate { await updates.CheckAsync(true, DateTime.UtcNow); };
            releasePage = Theme.Button("GitHubでダウンロード", true);
            releasePage.Name = "ReleasePage";
            releasePage.Dock = DockStyle.Fill;
            releasePage.Margin = new Padding(6, 0, 0, 6);
            releasePage.Click += delegate { OpenReleasePage(); };
            actions.Controls.Add(check, 0, 0);
            actions.Controls.Add(releasePage, 1, 0);
            root.Controls.Add(actions, 0, 3);

            notesTitle = Theme.Label("リリースノート", 11, Theme.Text, FontStyle.Bold);
            notesTitle.Margin = new Padding(0, 12, 0, 8);
            root.Controls.Add(notesTitle, 0, 4);
            notes = new RichTextBox { Name = "ReleaseNotes", Dock = DockStyle.Fill, ReadOnly = true,
                DetectUrls = false, BorderStyle = BorderStyle.None, BackColor = Theme.Surface,
                ForeColor = Theme.Text, Font = Font, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical,
                AccessibleName = "公開版のリリースノート", Margin = new Padding(0, 0, 0, 12) };
            root.Controls.Add(notes, 0, 5);
            guide = Theme.Label("", 9, Theme.Muted, FontStyle.Regular);
            guide.Name = "UpdateGuide";
            guide.Dock = DockStyle.Fill;
            guide.Margin = new Padding(0, 0, 0, 10);
            root.Controls.Add(guide, 0, 6);
            var autoUpdates = new CheckBox { Name = "AutoCheckUpdates", AutoSize = true, Dock = DockStyle.Fill,
                Text = "起動時にGitHubで更新を確認する（1日1回）", Checked = settings.AutoCheckUpdates,
                ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 12) };
            autoUpdates.CheckedChanged += delegate { settings.AutoCheckUpdates = autoUpdates.Checked; save(); };
            root.Controls.Add(autoUpdates, 0, 7);
            var close = Theme.Button("閉じる", false);
            close.Name = "CloseUpdates";
            close.DialogResult = DialogResult.Cancel;
            close.Dock = DockStyle.Right;
            close.Width = 116;
            close.Margin = Padding.Empty;
            CancelButton = close;
            root.Controls.Add(close, 0, 8);
            updates.Changed += RefreshUpdates;
            RefreshUpdates();
            Shown += async delegate
            {
                var work = Screen.FromControl(this).WorkingArea;
                if (Height > work.Height || Width > work.Width)
                {
                    MinimumSize = new Size(Math.Min(MinimumSize.Width, work.Width), Math.Min(MinimumSize.Height, work.Height));
                    Bounds = new Rectangle(work.X, work.Y, Math.Min(Width, work.Width), Math.Min(Height, work.Height));
                }
                // A saved tag has no notes. Fetch details on the explicit opening of this dialog.
                if (updates.Latest == null || updates.Latest.Notes == null)
                    await updates.CheckAsync(true, DateTime.UtcNow);
            };
        }

        private static Control VersionCard(string caption, out Label value)
        {
            var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                BackColor = Theme.Surface, Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 0, 8, 0) };
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.Controls.Add(Theme.Label(caption, 9, Theme.Muted, FontStyle.Regular), 0, 0);
            value = Theme.Label("", 20, Theme.Text, FontStyle.Bold);
            value.AutoSize = false;
            value.Dock = DockStyle.Fill;
            value.TextAlign = ContentAlignment.MiddleLeft;
            card.Controls.Add(value, 0, 1);
            return card;
        }

        private void RefreshUpdates()
        {
            if (IsDisposed) return;
            var release = updates.Latest;
            latestVersion.Text = release == null ? "未確認" : release.Tag;
            latestVersion.ForeColor = updates.Available == null ? Theme.Text : Theme.Accent;
            status.Text = updates.Message;
            status.ForeColor = updates.Available == null ? Theme.Muted : Theme.Accent;
            check.Enabled = !updates.Busy;
            check.Text = updates.Busy ? "確認しています…" : "今すぐ更新を確認";
            releasePage.Text = updates.Available == null ? "GitHubの公開ページを開く" : "GitHubでダウンロード";
            string zip = release == null ? "DisplayVeil-<バージョン>-win.zip" : "DisplayVeil-" + release.Version.ToString(3) + "-win.zip";
            guide.Text = "更新の手順（手動）\n1. GitHubの「Assets」にある " + zip + " を保存します。\n" +
                "2. アプリを終了し、ZIPを別フォルダーに「すべて展開」します。\n" +
                "3. 展開した DisplayVeil.exe を起動します。設定は引き継がれます。\n" +
                "「Source code」は実行用ではありません。";
            if (release != displayedRelease || notes.TextLength == 0)
            {
                displayedRelease = release;
                notesTitle.Text = release == null ? "リリースノート" : release.Tag + " の変更内容（このリリース分）";
                string body = release == null || release.Notes == null ?
                    "変更内容は更新確認後に表示されます。「今すぐ更新を確認」で取得できます。" :
                    String.IsNullOrWhiteSpace(release.Notes) ? "このリリースには変更内容の記載がありません。" : release.Notes;
                RenderNotes(body);
            }
        }

        private void RenderNotes(string body)
        {
            // Render text only: release Markdown never becomes HTML, executable RTF or navigation.
            var formatted = new StringBuilder();
            var headings = new List<KeyValuePair<int, int>>();
            foreach (string line in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                bool heading = Regex.IsMatch(line, @"^#{1,6}\s+");
                string text = Regex.Replace(line, @"^#{1,6}\s+", "");
                text = Regex.Replace(text, @"^(\s*)[-*+]\s+", "$1• ");
                text = Regex.Replace(text, @"!?\[([^\]]+)\]\([^\r\n]*?\)", "$1");
                text = Regex.Replace(text, @"\*\*(.+?)\*\*|__(.+?)__", "$1$2");
                text = Regex.Replace(text, @"`([^`]+)`", "$1");
                if (heading) headings.Add(new KeyValuePair<int, int>(formatted.Length, text.Length));
                formatted.Append(text).Append('\n');
            }
            notes.Text = formatted.ToString();
            notes.SelectAll();
            notes.SelectionFont = Font;
            notes.SelectionColor = Theme.Text;
            foreach (var heading in headings)
            {
                notes.Select(heading.Key, heading.Value);
                notes.SelectionFont = headingFont;
                notes.SelectionColor = Theme.Accent;
            }
            notes.SelectionStart = 0;
            notes.SelectionLength = 0;
            notes.ScrollToCaret();
        }

        private static void OpenBrowser(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        private void OpenReleasePage()
        {
            try { openUrl(updates.Latest == null ? UpdateRelease.ReleasesUrl : updates.Latest.PageUrl); }
            catch (Win32Exception) { status.Text = "ブラウザーを開けませんでした。GitHubで DisplayVeil を検索してください。"; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) updates.Changed -= RefreshUpdates;
            base.Dispose(disposing);
            if (disposing) headingFont.Dispose();
        }
    }
}
