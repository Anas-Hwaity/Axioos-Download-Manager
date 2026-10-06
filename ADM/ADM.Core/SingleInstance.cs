using Newtonsoft.Json;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using TraceLog;
using ADM.Core.BrowserMonitoring;

namespace ADM.Core
{
    public static class SingleInstance
    {
        private const string SessionMutexName = @"Local\ADM_Active_Instance";

        private enum Presence
        {
            Absent,
            Present,
            Foreign
        }

        public static Mutex? GlobalMutex;

        public static void Ensure()
        {
            var machine = Probe(ProductIdentity.GlobalMutexName);
            var session = machine == Presence.Present ? Presence.Absent : Probe(SessionMutexName);
            if (machine == Presence.Present || session != Presence.Absent)
            {
                var forwarded = machine != Presence.Foreign && SendArgsToRunningInstance();
                Environment.Exit(forwarded ? 0 : 1);
            }
            var sessionMarker = Claim(SessionMutexName);
            if (sessionMarker != null) GCHandle.Alloc(sessionMarker);
            GlobalMutex = machine == Presence.Foreign ? sessionMarker : Claim(ProductIdentity.GlobalMutexName);
        }

        public static bool AnotherAccountIsRunning()
        {
            return Probe(ProductIdentity.GlobalMutexName) == Presence.Foreign;
        }

        private static Presence Probe(string name)
        {
            try
            {
                using var mutex = Mutex.OpenExisting(name);
                return Presence.Present;
            }
            catch (WaitHandleCannotBeOpenedException ex)
            {
                Log.Debug("No other running instance holds " + name + " (" + ex.GetType().Name + ")");
                return Presence.Absent;
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Debug(ex, "Another Windows account is running the app");
                return Presence.Foreign;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The running instance check failed");
                return Presence.Absent;
            }
        }

        private static Mutex? Claim(string name)
        {
            try
            {
                return new Mutex(true, name);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The single instance marker could not be created");
                return null;
            }
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
