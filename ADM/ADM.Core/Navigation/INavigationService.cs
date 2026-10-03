namespace ADM.Core.Navigation
{
    public enum SettingsRoute
    {
        BrowserMonitoring = 0,
        General = 1,
        Network = 2,
        Credentials = 3,
        Advanced = 4
    }

    public interface INavigationService
    {
        void ShowSettings(SettingsRoute route);
        void ShowLanguageSettings();
    }
}
