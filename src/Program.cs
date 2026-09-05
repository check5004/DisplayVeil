using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DisplayVeil
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Native.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool created;
            using (var mutex = new Mutex(true, "Local\\DisplayVeil.Application.v1", out created))
            using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DisplayVeil.ShowSettings.v1"))
            {
                if (!created) { wake.Set(); return; }
                try
                {
                    string settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayVeil");
                    for (int i = 0; i < args.Length - 1; i++)
                        if (args[i] == "--settings-dir") settingsDirectory = Path.GetFullPath(args[i + 1]);
                    using (var controller = new AppController(settingsDirectory))
                    {
                        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                        {
                            controller.Stop(null);
                            MessageBox.Show("エラーのため黒幕を全解除しました。\n" + e.Exception.Message, "Display Veil", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        };
                        var registration = ThreadPool.RegisterWaitForSingleObject(wake, delegate { controller.ShowUi(null); }, null, -1, false);
                        try { Application.Run(controller); }
                        finally { registration.Unregister(null); }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Display Veil を起動できませんでした。\n" + ex.Message, "Display Veil", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
