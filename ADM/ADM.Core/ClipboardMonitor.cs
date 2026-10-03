using System;
using System.Collections.Generic;
using System.Text;
using TraceLog;
using ADM.Core.Util;

namespace ADM.Core
{
    public interface IClipboardMonitor
    {
        void Start();
        void Stop();
    }

    public class ClipboardMonitor : IClipboardMonitor
    {
        private bool isClipboardMonitorActive = false;
        private string lastClipboardText;
        private readonly IApplicationRuntimeContext runtimeContext;

        public ClipboardMonitor(IApplicationRuntimeContext runtimeContext)
        {
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
            this.runtimeContext.SubscribeApplicationEvent(ApplicationContext_ApplicationEvent);
        }

        private void ApplicationContext_ApplicationEvent(object sender, ApplicationEvent e)
        {
            if (e.EventType == "ConfigChanged")
            {
                if (Config.Instance.MonitorClipboard)
                {
                    Start();
                }
                else
                {
                    Stop();
                }
            }
        }

        public void Start()
        {
            Log.Debug("StartClipboardMonitor");
            if (isClipboardMonitorActive) return;
            var cm = runtimeContext.Application.GetPlatformClipboardMonitor();
            if (Config.Instance.MonitorClipboard)
            {
                cm.StartClipboardMonitoring();
                isClipboardMonitorActive = true;
                cm.ClipboardChanged += Cm_ClipboardChanged;
            }
        }

        public void Stop()
        {
            if (!isClipboardMonitorActive) return;
            var cm = runtimeContext.Application.GetPlatformClipboardMonitor();
            cm.StopClipboardMonitoring();
            isClipboardMonitorActive = false;
            cm.ClipboardChanged -= Cm_ClipboardChanged;
        }

        private void Cm_ClipboardChanged(object? sender, EventArgs e)
        {
            var text = runtimeContext.Application.GetPlatformClipboardMonitor().GetClipboardText();
            if (text is string clipboardText && clipboardText.Length > 0 && Helpers.IsUriValid(clipboardText) && clipboardText != lastClipboardText)
            {
                lastClipboardText = clipboardText;
                runtimeContext.CoreService.AddDownload(new Message { Url = clipboardText });
            }
        }
    }
}
