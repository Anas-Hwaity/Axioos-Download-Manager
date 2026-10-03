using System;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ADM.Core.BrowserMonitoring;

namespace ADM.Wpf.UI.Dialogs.ChromeIntegrator
{
    public partial class Page1 : UserControl
    {
        public Page1()
        {
            InitializeComponent();
        }

        public Browser Browser
        {
            set
            {
                this.Img.Source = new BitmapImage(
                    new Uri(
                    System.IO.Path.Combine(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images"),
                    $"{value}.jpg")));
            }
        }


    }
}
