using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NativeMessaging
{
    public class IpcClient
    {
        private TcpClient? _socket;
        private Stream? _socketIn;
        private Stream? _socketOut;

        public void Connect(int port, int timeoutMilliseconds = 5000, int ioTimeoutMilliseconds = 10000)
        {
            if (port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort) throw new ArgumentOutOfRangeException(nameof(port));
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            if (ioTimeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(ioTimeoutMilliseconds));

            _socket = new TcpClient(AddressFamily.InterNetwork)
            {
                SendTimeout = ioTimeoutMilliseconds,
                ReceiveTimeout = ioTimeoutMilliseconds
            };
            var connect = _socket.BeginConnect(IPAddress.Loopback, port, null, null);
            try
            {
                if (!connect.AsyncWaitHandle.WaitOne(timeoutMilliseconds))
                {
                    _socket.Close();
                    throw new TimeoutException("Timed out connecting to the ADM legacy loopback transport.");
                }
                _socket.EndConnect(connect);
            }
            finally
            {
                connect.AsyncWaitHandle.Close();
            }
            _socketIn = _socket.GetStream();
            _socketOut = _socket.GetStream();
        }

        public void Send(IEnumerable<string> args)
        {
            IpcUtil.Send(_socketOut!, args);
        }

        public IEnumerable<string> Receive()
        {
            return IpcUtil.Receive(_socketIn!);
        }

        public void Close()
        {
            try { _socket?.Close(); } catch { }
        }
    }
}
