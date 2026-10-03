using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using TraceLog;

namespace ADM.Core.HttpServer
{
    public class NanoServer
    {
        internal const int MaxConcurrentRequests = 32;
        internal const int RequestIoTimeoutMs = 10000;
        private readonly TcpListener listener;
        private readonly SemaphoreSlim requestSlots = new SemaphoreSlim(MaxConcurrentRequests, MaxConcurrentRequests);
        public event EventHandler<RequestContextEventArgs>? RequestReceived;

        public NanoServer(int port) : this(IPAddress.Any, port) { }

        public NanoServer(IPAddress host, int port)
        {
            this.listener = new TcpListener(host, port);
        }

        public void Start()
        {
            listener.Start();
            while (true)
            {
                var tcp = listener.AcceptTcpClient();
                ProcessRequest(tcp);
            }
        }

        public void Stop()
        {
            try
            {
                this.listener.Stop();
            }
            catch { }
        }

        private void ProcessRequest(TcpClient tcp)
        {
            if (!requestSlots.Wait(0))
            {
                try { tcp.Close(); } catch { }
                return;
            }
            tcp.ReceiveTimeout = RequestIoTimeoutMs;
            tcp.SendTimeout = RequestIoTimeoutMs;
            var worker = new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        var ctx = HttpParser.ParseContext(tcp);
                        this.RequestReceived?.Invoke(this, new RequestContextEventArgs(ctx));
                        if (!ctx.KeepAlive)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, ex.Message);
                }
                finally
                {
                    try { tcp.Close(); } catch { }
                    requestSlots.Release();
                }
            })
            {
                IsBackground = true,
                Name = "ADM local browser control"
            };
            worker.Start();
        }
    }
}
