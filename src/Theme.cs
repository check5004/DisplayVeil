using System.Drawing;
using System.Windows.Forms;

namespace DisplayVeil
{
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(16, 19, 25);
        public static readonly Color Surface = Color.FromArgb(25, 30, 39);
        public static readonly Color Border = Color.FromArgb(48, 57, 71);
        public static readonly Color Text = Color.FromArgb(235, 240, 246);
        public static readonly Color Muted = Color.FromArgb(155, 167, 185);
        public static readonly Color Accent = Color.FromArgb(130, 222, 195);
        public static Font Font(float size, FontStyle style) { return new Font("Yu Gothic UI", size, style, GraphicsUnit.Point); }
        // Passive windows use physical-pixel bounds, so their fonts must use pixels too.
        public static Font PhysicalFont(float pointsAt96Dpi, int dpi, FontStyle style)
        {
            return new Font("Yu Gothic UI", pointsAt96Dpi * dpi / 72f, style, GraphicsUnit.Pixel);
        }
        public static Button Button(string text, bool primary)
        {
            var button = new Button { Text = text, AccessibleName = text, AutoSize = false,
                FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Surface,
                ForeColor = primary ? Background : Text, Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
                Font = Font(10, FontStyle.Bold), Margin = new Padding(0, 0, 8, 0) };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(157, 236, 214) : Color.FromArgb(42, 51, 65);
            return button;
        }
        public static Label Label(string text, float size, Color color, FontStyle style)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = color, Font = Font(size, style),
                BackColor = Color.Transparent, Margin = new Padding(0) };
        }
    }
}
