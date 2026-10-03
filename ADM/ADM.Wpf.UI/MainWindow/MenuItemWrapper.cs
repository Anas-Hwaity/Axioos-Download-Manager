using System;
using System.Windows;
using System.Windows.Controls;
using ADM.Core.UI;

namespace ADM.Wpf.UI
{
    internal class MenuItemWrapper : IMenuItem
    {
        private MenuItem menu;

        public MenuItemWrapper(string name, string text) : this(name, text, true)
        { }

        public MenuItemWrapper(string name, string text, bool visible)
        {
            this.menu = new MenuItem
            {
                Style = Application.Current?.TryFindResource("GlassLeafMenuItem") as Style,
                Name = name,
                Header = text,
                IsEnabled = false,
                Visibility = visible ? Visibility.Visible : Visibility.Collapsed
            };
            this.menu.Click += Mi_Click;
        }

        private void Mi_Click(object sender, RoutedEventArgs e)
        {
            this.Clicked?.Invoke(this, e);
        }

        public string Name => menu.Name;

        public bool Enabled
        {
            get => menu.IsEnabled;
            set => menu.IsEnabled = value;
        }

        public event EventHandler? Clicked;

        public MenuItem Menu => menu;
    }
}
