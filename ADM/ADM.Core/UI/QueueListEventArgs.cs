using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ADM.Core;

namespace ADM.Core.UI
{
    public class QueueListEventArgs:EventArgs
    {
        public List<DownloadQueue> Queues { get; }
        public QueueListEventArgs(List<DownloadQueue> queues)
        {
            this.Queues = queues;
        }
    }
}
