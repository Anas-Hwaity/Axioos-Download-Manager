using System;
using System.Collections.Generic;
using System.Linq;
using ADM.Core;
using ADM.Core.UI;

namespace ADM.Core
{
    internal static class QueueWindowManager
    {
        private static IQueuesWindow? queueWindow;

        internal static void RefreshView()
        {
            if (queueWindow != null)
            {
                queueWindow.RefreshView();
            }
        }

        internal static void ShowWindow(object window, IQueuesWindow qwin, IApplicationCore coreService)
        {
            if (queueWindow != null)
            {
                return;
            }

            var queuesCopy = QueueManager.Queues.Select(q => new DownloadQueue(q.ID, q.Name)
            {
                DownloadIds = q.DownloadIds.Select(d => d).ToList(),
                Schedule = q.Schedule
            });

            queueWindow = qwin;
            queueWindow.SetData(queuesCopy);
            queueWindow.QueuesModified += QueueWindow_QueuesModified;
            EventHandler<DownloadListEventArgs> queueStartRequested = (_, e) => coreService.ResumeNonInteractiveDownloads(e.Downloads);
            EventHandler<DownloadListEventArgs> queueStopRequested = (_, e) => coreService.StopDownloads(e.Downloads, true);
            EventHandler? windowClosing = null;
            windowClosing = (_, _) =>
            {
                queueWindow!.QueuesModified -= QueueWindow_QueuesModified;
                queueWindow.QueueStartRequested -= queueStartRequested;
                queueWindow.QueueStopRequested -= queueStopRequested;
                queueWindow.WindowClosing -= windowClosing;
                queueWindow = null;
            };
            queueWindow.QueueStartRequested += queueStartRequested;
            queueWindow.QueueStopRequested += queueStopRequested;
            queueWindow.WindowClosing += windowClosing;
            queueWindow.ShowWindow(window);
        }

        private static void QueueWindow_QueuesModified(object? sender, QueueListEventArgs e)
        {
            OnQueueModified(e.Queues);
        }

        private static void OnQueueModified(IEnumerable<DownloadQueue> queues)
        {
            lock (QueueManager.Queues)
            {
                var dict1 = new Dictionary<string, DownloadQueue>();
                var dict2 = new Dictionary<string, DownloadQueue>();

                foreach (var q in queues)
                {
                    dict1.Add(q.ID, q);
                }

                foreach (var q in QueueManager.Queues)
                {
                    dict2.Add(q.ID, q);
                }

                foreach (var queue in queues)
                {
                    if (dict2.TryGetValue(queue.ID, out var q) && q != null)
                    {
                        q.Name = queue.Name;
                        q.DownloadIds = queue.DownloadIds.ToList();
                        q.Schedule = queue.Schedule;

                        dict1.Remove(queue.ID);
                        dict2.Remove(q.ID);
                    }
                }

                if (dict1.Count > 0)
                {
                    foreach (var key in dict1.Keys)
                    {
                        var queue = dict1[key];
                        QueueManager.Queues.Add(new DownloadQueue(queue.ID, queue.Name)
                        {
                            DownloadIds = queue.DownloadIds.Select(x => x).ToList(),
                            Schedule = queue.Schedule
                        });
                    }
                }

                if (dict2.Count > 0)
                {
                    foreach (var key in dict2.Keys)
                    {
                        var queue = dict2[key];
                        QueueManager.Queues.Remove(queue);
                    }
                }

                QueueManager.Save();
            }
        }
    }
}
