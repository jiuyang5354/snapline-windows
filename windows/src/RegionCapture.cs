using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal static class ScreenCapture
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo { internal int Size, Flags; internal IntPtr Cursor; internal Native.POINT Position; }
        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo { internal int Icon; internal uint HotX, HotY; internal IntPtr Mask, Color; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorInfo(ref CursorInfo cursor);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr value);

        internal static Bitmap Read(Rectangle bounds)
        {
            var image = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try {
                using (var graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                return image;
            }
            catch { image.Dispose(); throw; }
        }

        internal static BitmapSource Crop(Bitmap desktop, Rectangle desktopBounds, Rectangle selection, bool includeCursor)
        {
            selection.Intersect(new Rectangle(Point.Empty, desktop.Size));
            if (selection.Width < 2 || selection.Height < 2) throw new ArgumentException("请拖动选择截图区域。");
            using (var image = desktop.Clone(selection, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                if (includeCursor) DrawCursor(image, new Point(desktopBounds.Left + selection.Left, desktopBounds.Top + selection.Top));
                using (var stream = new MemoryStream())
                {
                    image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); stream.Position = 0;
                    var result = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                    result.Freeze(); return result;
                }
            }
        }

        private static void DrawCursor(Bitmap image, Point origin)
        {
            var cursor = new CursorInfo { Size = Marshal.SizeOf(typeof(CursorInfo)) };
            if (!GetCursorInfo(ref cursor)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if ((cursor.Flags & 1) == 0 || cursor.Cursor == IntPtr.Zero) return;
            IconInfo icon;
            if (!GetIconInfo(cursor.Cursor, out icon)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                using (var graphics = Graphics.FromImage(image))
                {
                    IntPtr dc = graphics.GetHdc();
                    try {
                        if (!DrawIconEx(dc, cursor.Position.X - origin.X - (int)icon.HotX,
                            cursor.Position.Y - origin.Y - (int)icon.HotY, cursor.Cursor, 0, 0, 0, IntPtr.Zero, 3))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    finally { graphics.ReleaseHdc(dc); }
                }
            }
            finally { if (icon.Mask != IntPtr.Zero) DeleteObject(icon.Mask); if (icon.Color != IntPtr.Zero) DeleteObject(icon.Color); }
        }
    }

    internal sealed class RegionCapture : Forms.Form
    {
        private readonly Bitmap desktop;
        private readonly Rectangle desktopBounds;
        private readonly bool includeCursor;
        private Point start;
        private bool dragging;
        internal Rectangle Selection { get; private set; }
        internal BitmapSource Result { get; private set; }
        private Exception failure;

        internal RegionCapture(Bitmap desktop, Rectangle bounds, bool includeCursor)
        {
            this.desktop = desktop; desktopBounds = bounds; this.includeCursor = includeCursor;
            Text = "Snapline · 区域截图";
            FormBorderStyle = Forms.FormBorderStyle.None; StartPosition = Forms.FormStartPosition.Manual;
            AutoScaleMode = Forms.AutoScaleMode.None; Bounds = bounds;
            TopMost = true; ShowInTaskbar = false; DoubleBuffered = true; KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 10F); Cursor = Forms.Cursors.Cross;
        }

        internal static BitmapSource Pick(bool includeCursor)
        {
            Rectangle bounds = Forms.SystemInformation.VirtualScreen;
            using (var desktop = ScreenCapture.Read(bounds))
            using (var picker = new RegionCapture(desktop, bounds, includeCursor))
            {
                var status = picker.ShowDialog();
                if (picker.failure != null) throw picker.failure;
                return status == Forms.DialogResult.OK ? picker.Result : null;
            }
        }

        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(desktop, 0, 0);
            using (var shade = new SolidBrush(Color.FromArgb(115, 0, 0, 0))) e.Graphics.FillRectangle(shade, ClientRectangle);
            if (!Selection.IsEmpty)
            {
                e.Graphics.DrawImage(desktop, Selection, Selection, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.White, 2)) e.Graphics.DrawRectangle(pen, Selection);
            }
            string hint = !dragging && !Selection.IsEmpty && includeCursor
                ? "移动鼠标到要展示的位置，按 Enter 截图 · Esc 取消 · 拖动可重新选择"
                : "拖动选择截图区域 · Esc 取消" + (includeCursor ? " · 框选后可摆放鼠标指针" : "");
            Size size = Forms.TextRenderer.MeasureText(hint, Font);
            Rectangle label = new Rectangle(Math.Max(12, (ClientSize.Width - size.Width) / 2 - 16), 18, size.Width + 32, size.Height + 20);
            using (var shade = new SolidBrush(Color.FromArgb(225, 27, 29, 33))) e.Graphics.FillRectangle(shade, label);
            Forms.TextRenderer.DrawText(e.Graphics, hint, Font, label, Color.White, Forms.TextFormatFlags.HorizontalCenter | Forms.TextFormatFlags.VerticalCenter);
            base.OnPaint(e);
        }

        protected override void OnMouseDown(Forms.MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != Forms.MouseButtons.Left) return;
            start = e.Location; dragging = true; Selection = Rectangle.Empty; Capture = true; Cursor = Forms.Cursors.Cross; Invalidate();
        }

        protected override void OnMouseMove(Forms.MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            Selection = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y));
            Selection = Rectangle.Intersect(Selection, ClientRectangle); Invalidate();
        }

        protected override void OnMouseUp(Forms.MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != Forms.MouseButtons.Left || !dragging) return;
            OnMouseMove(e); dragging = false; Capture = false;
            if (Selection.Width < 2 || Selection.Height < 2) { Selection = Rectangle.Empty; Invalidate(); return; }
            Cursor = Forms.Cursors.Arrow;
            if (!includeCursor) Finish(); else Invalidate();
        }

        protected override void OnKeyDown(Forms.KeyEventArgs e)
        {
            if (e.KeyCode == Forms.Keys.Escape) { e.Handled = true; DialogResult = Forms.DialogResult.Cancel; Close(); }
            else if (e.KeyCode == Forms.Keys.Enter && !dragging && !Selection.IsEmpty) { e.Handled = true; Finish(); }
            else base.OnKeyDown(e);
        }

        private void Finish()
        {
            try { Result = ScreenCapture.Crop(desktop, desktopBounds, Selection, includeCursor); }
            catch (Exception ex) {
                if (!ImageStore.IsImageError(ex) && !(ex is Win32Exception)) throw;
                failure = ex; DialogResult = Forms.DialogResult.Cancel; Close(); return;
            }
            DialogResult = Forms.DialogResult.OK; Close();
        }
    }
}
