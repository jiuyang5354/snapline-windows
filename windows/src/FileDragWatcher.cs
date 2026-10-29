// Adapted from jiuyang5354/Dropline- v0.1.0 (MIT), commit 51f2d7c. See NOTICE.md.
using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace Snapline.Dropline
{
    // Detects a threshold-crossing drag from Snapline's existing mouse polling timer.
    // Source-window checks are queued once per drag, outside the input sampling path.
    internal sealed class DragWatcher : IDisposable
    {
        private const int Threshold = 10;
        private bool pressed;
        private bool dragging;
        private bool activeDrag;
        private bool disposed;
        private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        internal Func<Native.POINT, bool> SourceFilter { get; set; }
        private Native.POINT downPoint;

        public event Action DragStarted;
        public event Action DragEnded;

        public bool AnyApp { get; set; }
        public Func<IntPtr, bool> IsOwnWindow { get; set; }

        internal void ProcessMouse(int message, Native.POINT point)
        {
            if (disposed) return;
            if (message == Native.WM_LBUTTONDOWN) {
                pressed = true; dragging = false; downPoint = point;
            }
            else if (message == Native.WM_MOUSEMOVE && pressed && !dragging &&
                (Math.Abs(point.X - downPoint.X) > Threshold || Math.Abs(point.Y - downPoint.Y) > Threshold)) {
                dragging = true;
                Native.POINT origin = downPoint;
                // Keep source-window queries outside the input sampling path.
                dispatcher.BeginInvoke(new Action(delegate {
                    if (disposed || !(SourceFilter == null ? IsEligibleSource(origin) : SourceFilter(origin))) return;
                    activeDrag = true;
                    if (DragStarted != null) DragStarted();
                }));
            }
            else if (message == Native.WM_LBUTTONUP) {
                pressed = false;
                if (!dragging) return;
                dragging = false;
                dispatcher.BeginInvoke(new Action(delegate {
                    if (disposed || !activeDrag) return;
                    activeDrag = false;
                    if (DragEnded != null) DragEnded();
                }));
            }
        }

        private bool IsEligibleSource(Native.POINT pt)
        {
            try
            {
                IntPtr hwnd = Native.WindowFromPoint(pt);
                if (hwnd == IntPtr.Zero) return false;
                IntPtr root = Native.GetAncestor(hwnd, Native.GA_ROOT);
                if (IsOwnWindow != null && IsOwnWindow(root)) return false;

                // Skip presses on a title bar or a resize border: those move or size a window.
                IntPtr hit;
                int lp = (pt.Y << 16) | (pt.X & 0xFFFF);
                if (Native.SendMessageTimeout(root, Native.WM_NCHITTEST, IntPtr.Zero, new IntPtr(lp), 0x2, 50, out hit) != IntPtr.Zero)
                {
                    long h = hit.ToInt64();
                    if (h == 2 || (h >= 10 && h <= 17) || h == 20 || h == 8 || h == 9) return false;
                }

                if (AnyApp) return true;

                string cls = Native.ClassOf(root);
                if (cls == "Progman" || cls == "WorkerW" || cls == "CabinetWClass" || cls == "ExploreWClass") return true;

                uint pid;
                Native.GetWindowThreadProcessId(root, out pid);
                using (var p = Process.GetProcessById((int)pid))
                {
                    return string.Equals(p.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is InvalidOperationException) && !(ex is System.ComponentModel.Win32Exception)) throw;
                return false;
            }
        }

        public void Dispose()
        {
            disposed = true; pressed = false; dragging = false; activeDrag = false;
        }
    }
}
