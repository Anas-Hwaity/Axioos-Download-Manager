using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using ADM.Core.BrowserMonitoring;

namespace ADM.App.Host
{
    internal sealed class BrowserProtocolPipeClient : IDisposable
    {
        private NamedPipeClientStream? pipe;

        internal void Connect(string origin, TimeSpan? timeout = null)
        {
            var client = new NamedPipeClientStream(
                ".",
                BrowserProtocolV1.DesktopPipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            client.Connect((int)(timeout ?? BrowserProtocolV1.NativeHostConnectionTimeout).TotalMilliseconds);

            var helloId = Guid.NewGuid().ToString("D");
            var hello = new JObject
            {
                ["protocolVersion"] = BrowserProtocolV1.ProtocolVersion,
                ["messageId"] = helloId,
                ["sessionId"] = JValue.CreateNull(),
                ["type"] = "BrowserHostHello",
                ["sentAtUtc"] = DateTime.UtcNow.ToString("o"),
                ["replyTo"] = JValue.CreateNull(),
                ["payload"] = new JObject
                {
                    ["productIdentity"] = BrowserProtocolV1.BrowserHostProductIdentity,
                    ["origin"] = origin ?? string.Empty
                }
            };
            NativeMessageSerializer.WriteMessage(client, hello.ToString(Formatting.None));
            var responseBytes = NativeMessageSerializer.ReadMessageBytes(client);
            var response = BrowserProtocolJson.ParseBounded(responseBytes);
            if (!string.Equals(response.Value<string>("type"), "BrowserHostHelloAck", StringComparison.Ordinal) ||
                !string.Equals(response.Value<string>("replyTo"), helloId, StringComparison.OrdinalIgnoreCase))
            {
                client.Dispose();
                throw new IOException("Desktop browser-protocol pipe handshake was rejected.");
            }
            pipe = client;
        }

        internal byte[] SendCommand(byte[] request)
        {
            if (pipe == null || !pipe.IsConnected)
            {
                throw new IOException("Desktop browser-protocol pipe is not connected.");
            }
            NativeMessageSerializer.WriteMessage(pipe, request);
            return NativeMessageSerializer.ReadMessageBytes(pipe);
        }

        public void Dispose()
        {
            try { pipe?.Dispose(); } catch { }
            pipe = null;
        }
    }
}
