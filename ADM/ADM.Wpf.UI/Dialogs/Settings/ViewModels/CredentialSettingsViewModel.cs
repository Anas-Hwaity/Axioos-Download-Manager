using System;
using System.Collections.ObjectModel;
using System.Linq;
using ADM.Core;
using ADM.Core.Settings;

namespace ADM.Wpf.UI.Dialogs.Settings.ViewModels
{
    public sealed class CredentialSettingsViewModel
    {
        private readonly ICredentialSettingsService settingsService;

        public CredentialSettingsViewModel(ICredentialSettingsService settingsService)
        {
            this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public ObservableCollection<PasswordEntry> Passwords { get; } = new ObservableCollection<PasswordEntry>();

        public void Reload()
        {
            Passwords.Clear();
            foreach (var password in settingsService.Load())
            {
                Passwords.Add(password);
            }
        }

        public void Save()
        {
            settingsService.Save(Passwords.ToArray());
        }

        public PasswordEntry GetAt(int index) => Passwords[index];

        public void Add(string host, string user, string password)
        {
            Passwords.Add(CreateEntry(host, user, password));
        }

        public void Replace(int index, string host, string user, string password)
        {
            Passwords[index] = CreateEntry(host, user, password);
        }

        public void RemoveAt(int index)
        {
            Passwords.RemoveAt(index);
        }

        private static PasswordEntry CreateEntry(string host, string user, string password)
        {
            return new PasswordEntry
            {
                Host = host,
                User = user,
                Password = password
            };
        }
    }
}
