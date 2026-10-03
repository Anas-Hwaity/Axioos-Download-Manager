using System;
using ADM.Core.DataAccess;

namespace ADM.Core.BrowserMonitoring
{
    public sealed class BrowserProtocolRuntimeSnapshot
    {
        public string ListenerState { get; set; } = "NotStarted";
        public int ActiveConnections { get; set; }
        public long AcceptedConnections { get; set; }
        public long SuccessfulHandshakes { get; set; }
        public long RejectedHandshakes { get; set; }
        public string LastFaultType { get; set; } = string.Empty;
        public DateTime? LastStateChangeUtc { get; set; }
    }

    public sealed class BrowserProtocolRuntimeDiagnosticsState
    {
        private readonly object sync = new object();
        private string listenerState = "NotStarted";
        private int activeConnections;
        private long acceptedConnections;
        private long successfulHandshakes;
        private long rejectedHandshakes;
        private string lastFaultType = string.Empty;
        private DateTime? lastStateChangeUtc;

        internal void MarkListening()
        {
            lock (sync)
            {
                listenerState = "Listening";
                lastFaultType = string.Empty;
                lastStateChangeUtc = DateTime.UtcNow;
            }
        }

        internal void MarkListenerFault(Exception ex)
        {
            lock (sync)
            {
                listenerState = "Faulted";
                lastFaultType = ex?.GetType().Name ?? "Unknown";
                lastStateChangeUtc = DateTime.UtcNow;
            }
        }

        internal void MarkConnectionOpened()
        {
            lock (sync)
            {
                activeConnections++;
                acceptedConnections++;
                lastStateChangeUtc = DateTime.UtcNow;
            }
        }

        internal void MarkConnectionClosed()
        {
            lock (sync)
            {
                if (activeConnections > 0) activeConnections--;
                lastStateChangeUtc = DateTime.UtcNow;
            }
        }

        internal void MarkHandshakeAccepted()
        {
            lock (sync)
            {
                successfulHandshakes++;
                lastStateChangeUtc = DateTime.UtcNow;
            }
        }

        internal void MarkHandshakeRejected()
        {
            lock (sync)
            {
                rejectedHandshakes++;
                lastStateChangeUtc = DateTime.UtcNow;
            }
        }

        internal void MarkDisposed()
        {
            lock (sync)
            {
                listenerState = "Disposed";
                activeConnections = 0;
                lastStateChangeUtc = DateTime.UtcNow;
            }
        }

        public BrowserProtocolRuntimeSnapshot Snapshot()
        {
            lock (sync)
            {
                return new BrowserProtocolRuntimeSnapshot
                {
                    ListenerState = listenerState,
                    ActiveConnections = activeConnections,
                    AcceptedConnections = acceptedConnections,
                    SuccessfulHandshakes = successfulHandshakes,
                    RejectedHandshakes = rejectedHandshakes,
                    LastFaultType = lastFaultType,
                    LastStateChangeUtc = lastStateChangeUtc
                };
            }
        }
    }

    public static class BrowserProtocolRuntimeDiagnostics
    {
        private static BrowserProtocolRuntimeDiagnosticsState State => AppDB.Instance.BrowserProtocolRuntimeDiagnostics;

        internal static void MarkListening() => State.MarkListening();
        internal static void MarkListenerFault(Exception ex) => State.MarkListenerFault(ex);
        internal static void MarkConnectionOpened() => State.MarkConnectionOpened();
        internal static void MarkConnectionClosed() => State.MarkConnectionClosed();
        internal static void MarkHandshakeAccepted() => State.MarkHandshakeAccepted();
        internal static void MarkHandshakeRejected() => State.MarkHandshakeRejected();
        internal static void MarkDisposed() => State.MarkDisposed();
        public static BrowserProtocolRuntimeSnapshot Snapshot() => State.Snapshot();
    }
}
