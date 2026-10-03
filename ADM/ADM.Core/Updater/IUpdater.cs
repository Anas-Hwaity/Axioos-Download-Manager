using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ADM.Core.Updater;

namespace ADM.Core.Updater
{
    public interface IUpdater
    {
        public void StartUpdate(IList<UpdateInfo> updates);
        public void CancelUpdate();
    }
}
