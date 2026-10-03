using System;
using TraceLog;
using ADM.Core;
using ADM.Core.SocialAnalysis;

namespace ADM.Core.BrowserMonitoring
{
    public sealed class BrowserMonitor : IBrowserMonitoringService
    {
        private readonly IApplicationRuntimeContext runtimeContext;
        private BrowserProtocolPipeServer? protocolPipeServer;

        public BrowserMonitor(IApplicationRuntimeContext runtimeContext)
        {
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        }

        public void Run()
        {
            var browserSessionStore = new InMemoryBrowserSessionStore();
            var socialAnalysisService = new SocialAnalysisService(new YtDlpAnalyzer(() => runtimeContext.Application.RunOnUiThread(() => runtimeContext.Application.InstallLatestYtDlp())), browserSessionStore);
            var messageProcessor = new IpcHttpMessageProcessor(runtimeContext, socialAnalysisService);
            protocolPipeServer = new BrowserProtocolPipeServer(runtimeContext, browserSessionStore, socialAnalysisService, messageProcessor);
            protocolPipeServer.Run();
            try
            {
                messageProcessor.Run();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
            }
        }

    }
}
