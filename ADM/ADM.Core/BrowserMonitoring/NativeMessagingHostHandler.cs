using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.Pipes;
using System.IO;
using ADM.Core;
using System.Threading;
#if NET35
using ADM.Compatibility;
#else
using System.Collections.Concurrent;
#endif
using TraceLog;

namespace ADM.Core.BrowserMonitoring
{
    public class NativeMessagingHostHandler : IDisposable
    {
        private int MaxPipeInstance = 254;
        private static readonly string PipeName = ProductIdentity.LegacyBrowserMonitoringPipeName;
        private List<NativeMessagingHostChannel> connectedChannels = new();
        private static Mutex globalMutex;
        private Thread listenerThread;
        private readonly IApplicationRuntimeContext runtimeContext;

        public static void EnsureSingleInstance()
        {
            try
            {
                using var mutex = Mutex.OpenExisting(ProductIdentity.GlobalMutexName);
                throw new InstanceAlreadyRunningException("ADM instance already running, Mutex exists '" + ProductIdentity.GlobalMutexName + "'");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Exception in NativeMessagingHostHandler ctor");
                if (ex is InstanceAlreadyRunningException)
                {
                    var args = Environment.GetCommandLineArgs().Skip(1);
                    if (args.Count() > 0)
                    {
                        Log.Debug(ex, "Sending args to running instance");
                        SendArgsToRunningInstance(args);
                        Environment.Exit(0);
                    }
                    throw;
                }
            }
            globalMutex = new Mutex(true, ProductIdentity.GlobalMutexName);
        }

        public NativeMessagingHostHandler(IApplicationRuntimeContext runtimeContext)
        {
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
            EnsureSingleInstance();
        }

        public void BroadcastConfig()
        {
            var bytes = GetSyncBytes();
            lock (this)
            {
                foreach (var channel in connectedChannels)
                {
                    try
                    {
                        channel.Publish(bytes);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, ex.Message);
                    }
                }
            }
        }

        public void StartPipedChannel()
        {
            listenerThread = new Thread(() =>
              {
                  while (true)
                  {
                      var pipe =
                            new NamedPipeServerStream(PipeName,
                            PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                      Log.Debug("Waiting for native host pipe...");
                      pipe.WaitForConnection();
                      Log.Debug("Pipe request received");
                      lock (connectedChannels)
                      {
                          var channel = CreateChannel(pipe);
                          connectedChannels.Add(channel);
                          channel.Start(GetSyncBytes());
                      }
                  }
              });
            listenerThread.Start();
            runtimeContext.SubscribeApplicationEvent(ApplicationContext_ApplicationEvent);
        }

        private void ApplicationContext_ApplicationEvent(object? sender, ApplicationEvent e)
        {
            if (e.EventType == "ConfigChanged")
            {
                BroadcastConfig();
            }
        }

        private NativeMessagingHostChannel CreateChannel(NamedPipeServerStream pipe)
        {
            var channel = new NativeMessagingHostChannel(pipe);
            channel.MessageReceived += (sender, args) =>
            {
                try
                {
                    using var br = new BinaryReader(new MemoryStream(args.Data));
                    var envelop = RawBrowserMessageEnvelop.Deserialize(br);
                    BrowserMessageHandler.Handle(envelop);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, ex.ToString());
                }
            };
            channel.Disconnected += (sender, bytes) =>
            {
                lock (connectedChannels)
                {
                    connectedChannels.Remove((NativeMessagingHostChannel)sender);
                }
            };
            return channel;
        }











        public void Dispose()
        {
            runtimeContext.UnsubscribeApplicationEvent(ApplicationContext_ApplicationEvent);
            lock (connectedChannels)
            {
                foreach (var channel in connectedChannels)
                {
                    channel.Disconnect();
                }
            }
        }

        private byte[] GetSyncBytes()
        {
            var msg = new SyncMessage()
            {
                Enabled = Config.Instance.IsBrowserMonitoringEnabled,
                BlockedHosts = Config.Instance.BlockedHosts,
                VideoUrls = new string[0],
                FileExts = Config.Instance.FileExtensions,
                VidExts = Config.Instance.VideoExtensions,
                VidList = runtimeContext.VideoTracker.GetVideoList(false).Select(a => new VideoItem
                {
                    Id = a.ID,
                    Text = a.File,
                    Info = a.DisplayName
                }).ToList(),
                MimeList = new string[] { "video", "audio", "mpegurl", "f4m", "m3u8", "dash" },
                BlockedMimeList = new string[] { "text/javascript", "application/javascript", "text/css", "text/html" },
                VideoUrlsWithPostReq = new string[] { "ubei/v1/player?key=", "ubei/v1/next?key=" }
            };
            return msg.Serialize();
        }

        private static void SendArgsToRunningInstance(IEnumerable<string> args)
        {
            if (args == null || args.Count() < 1) return;
            try
            {
                var values = args.ToArray();
                if (values.Length > BrowserProtocolV1.MaxArrayItems ||
                    values.Any(a => a == null || a.Length > BrowserProtocolV1.MaxStringChars))
                    throw new InvalidDataException("Legacy single-instance forwarding arguments exceed protocol bounds.");
                var joined = string.Join("\r", values);
                if (Encoding.UTF8.GetByteCount(joined) > BrowserProtocolV1.MaxApplicationMessageBytes)
                    throw new InvalidDataException("Legacy single-instance forwarding payload is too large.");

                using var npc = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                npc.Connect(ProductIdentity.LegacyIpcConnectTimeoutMilliseconds);
                using var b = new MemoryStream();
                using (var wb = new BinaryWriter(b, Encoding.UTF8, true))
                {
                    wb.Write(Int32.MaxValue);
                    wb.Write(joined);
                }
                NativeMessageSerializer.WriteMessage(npc, b.ToArray());
                npc.Flush();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, ex.Message);
            }
        }
    }

    public class InstanceAlreadyRunningException : Exception
    {
        public InstanceAlreadyRunningException(string message) : base(message)
        {
        }
    }
}
