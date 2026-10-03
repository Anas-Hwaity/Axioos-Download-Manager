using System;
using System.ComponentModel;
using ADM.Core.UI;
using ADM.Core;
using ADM.Core.Util;

namespace ADM.Wpf.UI
{
    internal class InProgressDownloadEntryWrapper : INotifyPropertyChanged, IInProgressDownloadRow
    {
        private readonly InProgressDownloadItem entry;

        public event PropertyChangedEventHandler PropertyChanged;

        public InProgressDownloadEntryWrapper(InProgressDownloadItem entry)
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

        public int Progress
        {
            get { return entry.Progress; }
            set
            {
                entry.Progress = value;
                OnPropertyChanged("Progress");
                OnPropertyChanged("Status");
                OnPropertyChanged("StatusText");
            }
        }

        public string StatusText => Helpers.GenerateStatusText(this.entry);

        public InProgressDownloadItem DownloadEntry => this.entry;

        public string FileIconText => IconMap.GetVectorNameForFileType(entry.Name);

        public DownloadStatus Status
        {
            get => entry.Status;
            set
            {
                entry.Status = value;
                OnPropertyChanged("Status");
                OnPropertyChanged("StatusText");
            }
        }

        public string DownloadSpeed
        {
            get => entry.DownloadSpeed ?? string.Empty;
            set
            {
                entry.DownloadSpeed = value;
                OnPropertyChanged("Status");
                OnPropertyChanged("StatusText");
            }
        }

        public string ETA
        {
            get => entry.ETA ?? string.Empty;
            set
            {
                entry.ETA = value;
                OnPropertyChanged("Status");
                OnPropertyChanged("StatusText");
            }
        }

        public void UpdateStatusText()
        {
            OnPropertyChanged("Status");
            OnPropertyChanged("StatusText");
        }

        private void OnPropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
