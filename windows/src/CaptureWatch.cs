using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace Snapline
{
    internal sealed class CaptureWatch : IDisposable
    {
        private readonly ImageStore store;
        private readonly Dispatcher dispatcher;
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly Dictionary<string, int> pending = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer timer;
        private string watchingFolder;
        internal event Action<Shot> Captured;
        internal event Action<string> Error;

        internal CaptureWatch(ImageStore store, Dispatcher dispatcher)
        {
            this.store = store;
            this.dispatcher = dispatcher;
            timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, Tick, dispatcher);
            Restart();
        }

        internal void Restart()
        {
            foreach (var watcher in watchers) watcher.Dispose();
            watchers.Clear();
            pending.Clear();
            watchingFolder = null;
            Watch(store.Inbox);
            TryWatchScreenshots();
        }

        private void TryWatchScreenshots()
        {
            string folder = store.Settings.WatchFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder) || watchingFolder != null) return;
            if (!string.Equals(Path.GetFullPath(folder), store.Inbox, StringComparison.OrdinalIgnoreCase)) Watch(folder);
            watchingFolder = folder;
        }

        private void Watch(string folder)
        {
            var watcher = new FileSystemWatcher(folder) {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false
            };
            watcher.Created += delegate(object sender, FileSystemEventArgs e) { Queue(e.FullPath); };
            watcher.Changed += delegate(object sender, FileSystemEventArgs e) { Queue(e.FullPath); };
            watcher.Renamed += delegate(object sender, RenamedEventArgs e) { Queue(e.FullPath); };
            watcher.Deleted += delegate { dispatcher.BeginInvoke(new Action(store.Prune)); };
            watcher.Error += delegate { dispatcher.BeginInvoke(new Action(delegate {
                if (Error != null) Error("截图文件夹监听已中断，请从托盘重新选择文件夹。");
            })); };
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }

        private void Queue(string path)
        {
            if (!ImageStore.IsImage(path) || store.Settings.CollectionPaused) return;
            dispatcher.BeginInvoke(new Action(delegate { if (!store.Settings.CollectionPaused && !pending.ContainsKey(path)) pending[path] = 0; }));
        }

        internal void DiscardPending() { pending.Clear(); }

        private int ticks;
        private void Tick(object sender, EventArgs args)
        {
            if (++ticks % 15 == 0) { TryWatchScreenshots(); store.Prune(); }
            if (store.Settings.CollectionPaused) { pending.Clear(); return; }
            foreach (var path in pending.Keys.ToArray())
            {
                try
                {
                    var shot = store.AddFile(path, true);
                    pending.Remove(path);
                    if (shot != null && Captured != null) Captured(shot);
                }
                catch (Exception ex)
                {
                    if (!ImageStore.IsImageError(ex)) throw;
                    if (++pending[path] >= 20)
                    {
                        pending.Remove(path);
                        if (Error != null) Error("无法读取图片：" + Path.GetFileName(path));
                    }
                }
            }
        }

        public void Dispose()
        {
            timer.Stop();
            foreach (var watcher in watchers) watcher.Dispose();
            watchers.Clear();
        }
    }
}
