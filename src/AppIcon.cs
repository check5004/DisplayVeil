using System;
using System.Drawing;

namespace DisplayVeil
{
    internal static class AppIcon
    {
        // Each caller owns its icon; no external ICO is needed at runtime.
        public static Icon Load()
        {
            using (var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("DisplayVeil.App.ico"))
            {
                if (stream == null) throw new InvalidOperationException("The application icon resource is missing.");
                using (var icon = new Icon(stream)) return (Icon)icon.Clone();
            }
        }
    }
}
