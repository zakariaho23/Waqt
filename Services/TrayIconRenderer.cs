using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Waqt.Services
{
    public static class TrayIconRenderer
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private static Icon? _activeIcon;
        private static IntPtr _activeHIcon = IntPtr.Zero;

        public static Icon RenderMinuteIcon(int minutes)
        {
            const int size = 32;
            using var bitmap = new Bitmap(size, size);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                // Background fill — dark rounded rectangle
                var bgColor = ColorTranslator.FromHtml("#0B0F17");
                var borderColor = ColorTranslator.FromHtml("#00D2B4");

                var rect = new Rectangle(0, 0, size - 1, size - 1);
                int cornerRadius = 6;

                using (var bgBrush = new SolidBrush(bgColor))
                using (var borderPen = new Pen(borderColor, 1f))
                using (var path = CreateRoundedRectPath(rect, cornerRadius))
                {
                    g.FillPath(bgBrush, path);
                    g.DrawPath(borderPen, path);
                }

                // Format the text
                string text;
                if (minutes < 0)
                {
                    text = "--";
                }
                else if (minutes <= 99)
                {
                    text = $"{minutes}m";
                }
                else
                {
                    int hours = minutes / 60;
                    text = $"{hours}h";
                }

                // Choose font size to fit within the icon
                float fontSize = text.Length <= 2 ? 13f : (text.Length == 3 ? 10f : 8.5f);

                using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var textBrush = new SolidBrush(Color.White))
                {
                    var textSize = g.MeasureString(text, font);
                    float x = (size - textSize.Width) / 2f;
                    float y = (size - textSize.Height) / 2f;
                    g.DrawString(text, font, textBrush, x, y);
                }
            }

            IntPtr newHIcon = bitmap.GetHicon();
            var newIcon = Icon.FromHandle(newHIcon);

            // Clean up previous native icon handle once new icon is safely created
            if (_activeHIcon != IntPtr.Zero)
            {
                DestroyIcon(_activeHIcon);
            }
            _activeIcon?.Dispose();

            _activeHIcon = newHIcon;
            _activeIcon = newIcon;

            return newIcon;
        }

        public static void DisposeActiveIcon()
        {
            if (_activeHIcon != IntPtr.Zero)
            {
                DestroyIcon(_activeHIcon);
                _activeHIcon = IntPtr.Zero;
            }
            _activeIcon?.Dispose();
            _activeIcon = null;
        }

        public static void CleanupIcon(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                DestroyIcon(handle);
            }
        }

        private static GraphicsPath CreateRoundedRectPath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();

            // Top-left arc
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            // Top-right arc
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            // Bottom-right arc
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            // Bottom-left arc
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);

            path.CloseFigure();
            return path;
        }
    }
}
