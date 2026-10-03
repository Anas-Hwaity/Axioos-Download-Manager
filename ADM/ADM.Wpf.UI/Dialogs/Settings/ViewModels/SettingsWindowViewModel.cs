using System;
using System.Windows.Input;
using ADM.Core.Settings;
using ADM.Wpf.UI.Rules;

namespace ADM.Wpf.UI.Dialogs.Settings.ViewModels
{
    public sealed class SettingsWindowViewModel
    {
        private readonly ISettingsCommitService settingsCommitService;

        public SettingsWindowViewModel(
            NetworkSettingsViewModel network,
            AdvancedSettingsViewModel advanced,
            CredentialSettingsViewModel credentials,
            BrowserMonitoringSettingsViewModel browserMonitoring,
            GeneralSettingsViewModel general,
            RuleEditorViewModel rules,
            ISettingsCommitService settingsCommitService)
        {
            Network = network ?? throw new ArgumentNullException(nameof(network));
            Advanced = advanced ?? throw new ArgumentNullException(nameof(advanced));
            Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            BrowserMonitoring = browserMonitoring ?? throw new ArgumentNullException(nameof(browserMonitoring));
            General = general ?? throw new ArgumentNullException(nameof(general));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.settingsCommitService = settingsCommitService ?? throw new ArgumentNullException(nameof(settingsCommitService));
            Reload();
            SaveCommand = new ActionCommand(Save);
        }

        public event EventHandler? Saved;
        public NetworkSettingsViewModel Network { get; }
        public AdvancedSettingsViewModel Advanced { get; }
        public CredentialSettingsViewModel Credentials { get; }
        public BrowserMonitoringSettingsViewModel BrowserMonitoring { get; }
        public GeneralSettingsViewModel General { get; }
        public RuleEditorViewModel Rules { get; }
        public ICommand SaveCommand { get; }

        private void Reload()
        {
            BrowserMonitoring.Reload();
            General.Reload();
            Network.Reload();
            Credentials.Reload();
            Advanced.Reload();
        }

        private void Save()
        {
            BrowserMonitoring.Save();
            General.Save();
            Network.Save();
            Credentials.Save();
            Advanced.Save();
            Rules.Save();
            settingsCommitService.Commit();
            Saved?.Invoke(this, EventArgs.Empty);
        }
    }
}
