using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ADM.Core;

namespace ADM.Wpf.UI.Dialogs.ChromeIntegrator
{
    public partial class Page3 : UserControl
    {
        public Page3()
        {
            InitializeComponent();
            TxtFolder.Text = System.IO.Path.Combine(Config.AppDir, "chrome-extension");
            this.Img.Source = new BitmapImage(
                    new Uri(
                    System.IO.Path.Combine(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images"),
                    "extension-folder.jpg")));
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(TxtFolder.Text);
        }


    }
}
