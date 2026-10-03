using Newtonsoft.Json;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using TraceLog;
using ADM.Core.BrowserMonitoring;

namespace ADM.Core
{
    public static class SingleInstance
    {
        public static Mutex GlobalMutex;
        public static void Ensure()
        {
            try
            {
                using var mutex = Mutex.OpenExisting(ProductIdentity.GlobalMutexName);
                throw new InstanceAlreadyRunningException($"ADM instance already running, Mutex exists '{ProductIdentity.GlobalMutexName}'");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Exception in NativeMessagingHostHandler ctor");
                if (ex is InstanceAlreadyRunningException)
                {
                    var forwarded = SendArgsToRunningInstance();
                    Environment.Exit(forwarded ? 0 : 1);
                }
            }
            GlobalMutex = new Mutex(true, ProductIdentity.GlobalMutexName);
        }

        private static bool SendArgsToRunningInstance()
        {
            try
            {
                Log.Debug("Sending to running instance...");
                var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
                if (args.Length > BrowserProtocolV1.MaxArrayItems)
                    throw new InvalidOperationException("Too many forwarded command-line arguments.");
                if (args.Any(a => a == null || a.Length > BrowserProtocolV1.MaxStringChars))
                    throw new InvalidOperationException("A forwarded command-line argument is too large.");

                var forwardedArgs = args.Length == 0 ? new string[] { "--restore-window" } : args;
                var postData = JsonConvert.SerializeObject(forwardedArgs);
                var data = Encoding.UTF8.GetBytes(postData);
                if (data.Length > BrowserProtocolV1.MaxApplicationMessageBytes)
                    throw new InvalidOperationException("Forwarded command-line payload is too large.");

                var request = (HttpWebRequest)WebRequest.Create($"http://127.0.0.1:{ProductIdentity.LegacyControlPort}/args");
                request.Method = "POST";
                request.ContentType = "application/json";
                request.ContentLength = data.Length;
                request.Timeout = ProductIdentity.LegacyIpcIoTimeoutMilliseconds;
                request.ReadWriteTimeout = ProductIdentity.LegacyIpcIoTimeoutMilliseconds;
                request.KeepAlive = false;
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                        throw new IOException("Running ADM instance rejected forwarded arguments: HTTP " + (int)response.StatusCode);
                }
                Log.Debug("Sent arguments to running instance.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed sending args to running instance");
                return false;
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
