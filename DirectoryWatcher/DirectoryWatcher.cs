using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Teamnet.DirectoryWatcher
{
    public class DirectoryWatcher : IDisposable
    {
        private FileSystemWatcher watcher;
        private Timer timer;
        private HashSet<string> files;

        public DirectoryWatcher()
        {
            files = new HashSet<string>();
            watcher = new FileSystemWatcher { NotifyFilter = NotifyFilters.LastWrite };
            watcher.Changed += OnDirectoryChanged;
            watcher.Error += OnError;
        }

        public string Path { get { return watcher.Path; } set { watcher.Path = value; } }
        public string SearchPattern { get { return watcher.Filter; } set { watcher.Filter = value; } }
        public bool IncludeSubdirectories { get { return watcher.IncludeSubdirectories; } set { watcher.IncludeSubdirectories = value; } }

        public event ErrorEventHandler Error;
        public event EventHandler<FileEventArgs> HandleFile;

        protected void OnError(Exception ex)
        {
            OnError(this, new ErrorEventArgs(ex));
        }

        protected virtual void OnError(object sender, ErrorEventArgs args)
        {
            Error(sender, args);
        }

        protected virtual void Dispose(bool disposing)
        {
            if(disposing)
            {
                if(timer != null)
                {
                    timer.Dispose();
                }
                if(watcher != null)
                {
                    watcher.Dispose();
                }
            }
            // Use SupressFinalize in case a subclass of this type implements a finalizer.
            GC.SuppressFinalize(this);
        }

        public void Start()
        {
            timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            ThreadPool.QueueUserWorkItem(HandleExistingFiles);
            watcher.EnableRaisingEvents = true;
        }

        private void OnDirectoryChanged(object sender, FileSystemEventArgs args)
        {
            const int Delay = 1000;
            lock (files)
            {
                files.Add(args.FullPath);
                timer.Change(Delay, Timeout.Infinite);
            }
        }

        private void OnTimer(object unused)
        {
            string[] newFiles;
            lock (files)
            {
                timer.Change(Timeout.Infinite, Timeout.Infinite);
                newFiles = new string[files.Count];
                files.CopyTo(newFiles);
                files.Clear();
            }
            HandleFiles(newFiles);
        }

        private void HandleExistingFiles(object unused)
        {
            var searchOption = IncludeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var existingFiles = Directory.EnumerateFiles(Path, SearchPattern, searchOption);
            HandleFiles(existingFiles);
        }

        private void HandleFiles(IEnumerable<string> existingFiles)
        {
            foreach(var filePath in existingFiles)
            {
                try
                {
                    HandleFile(this, new FileEventArgs { Path = filePath });
                }
                catch(Exception ex)
                {
                    OnError(ex);
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }

    public class FileEventArgs : EventArgs
    {
        public string Path { get; internal set; }
    }
}