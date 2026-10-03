using System;
using System.Windows.Threading;

namespace ADM.Wpf.UI.Dashboard
{
    internal sealed class WpfDashboardUiDispatcher : IDashboardUiDispatcher
    {
        private readonly Dispatcher dispatcher;

        internal WpfDashboardUiDispatcher(Dispatcher dispatcher)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public void Post(Action action)
        {
            if (action == null) return;
            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }
    }
}
