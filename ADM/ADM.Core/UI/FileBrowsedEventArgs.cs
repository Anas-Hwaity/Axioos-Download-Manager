using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace ADM.Core.UI
{
    public class FileBrowsedEventArgs : EventArgs
    {
        public string SelectedFile { get; private set; }
        public FileBrowsedEventArgs(string file)
        {
            this.SelectedFile = file;
        }
    }
}
