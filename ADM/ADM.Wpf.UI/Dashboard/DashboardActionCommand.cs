using System;
using System.Windows.Input;

namespace ADM.Wpf.UI.Dashboard
{
    internal sealed class DashboardActionCommand : ICommand
    {
        private readonly Action<string> execute;

        internal DashboardActionCommand(Action<string> execute)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => parameter is string id && !string.IsNullOrWhiteSpace(id);

        public void Execute(object? parameter)
        {
            if (parameter is string id && !string.IsNullOrWhiteSpace(id)) execute(id);
        }
    }
}
