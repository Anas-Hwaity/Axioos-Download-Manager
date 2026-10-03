using System;
using System.ComponentModel;
using ADM.Core.UI;
using ADM.Core;

namespace ADM.Wpf.UI
{
    internal class FinishedDownloadEntryWrapper : INotifyPropertyChanged, IFinishedDownloadRow
    {
        private FinishedDownloadItem entry;

        public event PropertyChangedEventHandler PropertyChanged;

        public FinishedDownloadEntryWrapper(FinishedDownloadItem entry)
        {
            this.entry = entry;
        }

        public string Name
        {
            get { return entry.Name; }
            set
            {
                entry.Name = value;
                OnPropertyChanged("Name");
            }
        }

        public long Size
        {
            get { return entry.Size; }
            set
            {
                entry.Size = value;
                OnPropertyChanged("Size");
            }
        }

        public DateTime DateAdded
        {
            get { return entry.DateAdded; }
            set
            {
                entry.DateAdded = value;
                OnPropertyChanged("DateAdded");
            }
        }

        public void UpdateStatusText()
        {
            OnPropertyChanged("Status");
        }

        public FinishedDownloadItem DownloadEntry => this.entry;

        public string FileIconText => IconMap.GetVectorNameForFileType(entry.Name);

        private void OnPropertyChanged(string propName)
        {
            PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
    }
}
