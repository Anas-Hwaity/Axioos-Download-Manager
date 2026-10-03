using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Text;
using ADM.Core.BrowserMonitoring;

namespace ADM.App.Host
{
    internal sealed class BrowserProtocolV1HostSession
    {
        private readonly Action<byte[]> send;
        private readonly Func<byte[], byte[]>? forwardToDesktop;
        private readonly Func<bool>? ensureDesktop;
        private string? sessionId;

        public BrowserProtocolV1HostSession(
            Action<byte[]> send,
            Func<byte[], byte[]>? forwardToDesktop = null,
            Func<bool>? ensureDesktop = null)
        {
            this.send = send ?? throw new ArgumentNullException(nameof(send));
            this.forwardToDesktop = forwardToDesktop;
            this.ensureDesktop = ensureDesktop;
        }

        public bool IsNegotiated => sessionId != null;

        public bool TryHandle(byte[] input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            JObject envelope;
            try
            {
                envelope = BrowserProtocolJson.ParseBounded(input);
            }
            catch (JsonException)
            {
                return false;
            }

            var protocolToken = envelope["protocolVersion"];
            if (protocolToken == null)
            {
                return false;
            }

            var replyTo = envelope.Value<string>("messageId");
            if (protocolToken.Type != JTokenType.Integer || protocolToken.Value<int>() != BrowserProtocolV1.ProtocolVersion)
            {
                SendCommandResult(replyTo, "unsupported", "UnsupportedProtocolVersion", "No compatible browser protocol version is available.");
                return true;
            }

            if (!Guid.TryParse(replyTo, out _))
            {
                SendCommandResult(null, "rejected", "InvalidMessageId", "messageId must be a UUID.");
                return true;
            }

            var type = envelope.Value<string>("type") ?? string.Empty;
            if (type == "Hello")
            {
                HandleHello(envelope, replyTo!);
                return true;
            }

            if (!IsNegotiated)
            {
                SendCommandResult(replyTo, "rejected", "HandshakeRequired", "Hello/HelloAck negotiation must complete first.");
                return true;
            }

            if (!string.Equals(envelope.Value<string>("sessionId"), sessionId, StringComparison.OrdinalIgnoreCase))
            {
                SendCommandResult(replyTo, "rejected", "InvalidSession", "The protocol session identifier does not match this native session.");
                return true;
            }

            if (type == "EnsureDesktop")
            {
                HandleEnsureDesktop(replyTo!);
                return true;
            }

            if (forwardToDesktop == null)
            {
                SendCommandResult(replyTo, "busy", "DesktopUnavailable", "The desktop browser-protocol endpoint is unavailable.");
                return true;
            }
            try
            {
                ForwardDesktopResponse(input, replyTo!);
            }
            catch (Exception)
            {
                SendCommandResult(replyTo, "busy", "DesktopUnavailable", "The desktop browser-protocol endpoint could not process the request.");
            }
            return true;
        }


        private void HandleEnsureDesktop(string replyTo)
        {
            if (ensureDesktop == null)
            {
                SendCommandResult(replyTo, "busy", "DesktopUnavailable", "Desktop launch is unavailable in this native host.");
                return;
            }
            try
            {
                if (ensureDesktop())
                {
                    SendCommandResult(replyTo, "accepted", "DesktopReady", "The desktop browser-protocol endpoint is ready.");
                    return;
                }
            }
            catch
            {
            }
            SendCommandResult(replyTo, "busy", "DesktopUnavailable", "The desktop browser-protocol endpoint did not become ready.");
        }

        private void ForwardDesktopResponse(byte[] input, string replyTo)
        {
            var responseBytes = forwardToDesktop!(input);
            var response = BrowserProtocolJson.ParseBounded(responseBytes);
            if (response.Value<int?>("protocolVersion") != BrowserProtocolV1.ProtocolVersion ||
                !string.Equals(response.Value<string>("replyTo"), replyTo, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Desktop browser-protocol response does not match the forwarded request.");
            }
            response["sessionId"] = sessionId == null ? JValue.CreateNull() : new JValue(sessionId);
            SendEnvelope(response);
        }

        private void HandleHello(JObject envelope, string replyTo)
        {
            var payload = envelope["payload"] as JObject;
            var versions = payload?["supportedProtocolVersions"] as JArray;
            var supportsV1 = versions != null && versions.Any(token => token.Type == JTokenType.Integer && token.Value<int>() == BrowserProtocolV1.ProtocolVersion);
            if (!supportsV1)
            {
                SendCommandResult(replyTo, "unsupported", "UnsupportedProtocolVersion", "No compatible browser protocol version is available.");
                return;
            }

            sessionId = Guid.NewGuid().ToString("D");
            var response = NewEnvelope("HelloAck", replyTo, new JObject
            {
                ["selectedProtocolVersion"] = BrowserProtocolV1.ProtocolVersion,
                ["appVersion"] = typeof(BrowserProtocolV1HostSession).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
                ["capabilities"] = new JArray()
            });
            SendEnvelope(response);
        }

        private void SendCommandResult(string? replyTo, string status, string code, string detail)
        {
            var response = NewEnvelope("CommandResult", replyTo, new JObject
            {
                ["status"] = status,
                ["code"] = code,
                ["detail"] = detail
            });
            SendEnvelope(response);
        }

        private JObject NewEnvelope(string type, string? replyTo, JObject payload)
        {
            return new JObject
            {
                ["protocolVersion"] = BrowserProtocolV1.ProtocolVersion,
                ["messageId"] = Guid.NewGuid().ToString("D"),
                ["sessionId"] = sessionId == null ? JValue.CreateNull() : new JValue(sessionId),
                ["type"] = type,
                ["sentAtUtc"] = DateTime.UtcNow.ToString("o"),
                ["replyTo"] = replyTo == null ? JValue.CreateNull() : new JValue(replyTo),
                ["payload"] = payload
            };
        }

        private void SendEnvelope(JObject envelope)
        {
            var bytes = Encoding.UTF8.GetBytes(envelope.ToString(Formatting.None));
            if (bytes.Length > BrowserProtocolV1.MaxApplicationMessageBytes)
            {
                throw new InvalidOperationException("Protocol response exceeds the application message limit.");
            }
            send(bytes);
        }
    }
}
