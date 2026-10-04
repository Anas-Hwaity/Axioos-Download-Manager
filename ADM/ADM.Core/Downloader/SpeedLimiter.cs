using System;
using System.Threading;
using TraceLog;
using ADM.Core.Util;

namespace ADM.Core.Downloader
{
    public class SpeedLimiter
    {
        public const int FollowGlobal = 0;
        public const int Unlimited = -1;
        private const int PauseSliceMilliseconds = 100;

        private const int MaxCreditMilliseconds = 100;
        private long lastBytes;
        private double paidUntil;
        private long lastChecked = Helpers.TickCount();
        private int cachedSpeedLimit = -2;
        private int explicitSetting;
        private int wakeGeneration;

        public int Setting => explicitSetting;

        public int SpeedLimit => explicitSetting > 0 ? explicitSetting : explicitSetting < 0 ? 0 : cachedSpeedLimit;

        public static int SettingFromDialog(bool enabled, int valueKiB, bool globalEnabled, int globalValueKiB)
        {
            var globalActive = globalEnabled && globalValueKiB > 0;
            if (!enabled) return globalActive ? Unlimited : FollowGlobal;
            if (valueKiB <= 0) return FollowGlobal;
            return globalActive && globalValueKiB == valueKiB ? FollowGlobal : valueKiB;
        }

        public static int EffectiveLimit(int setting, bool globalEnabled, int globalValueKiB)
        {
            if (setting > 0) return setting;
            if (setting < 0) return 0;
            return globalEnabled && globalValueKiB > 0 ? globalValueKiB : 0;
        }

        public void SetExplicitLimit(int? speedLimitKiB)
        {
            var value = speedLimitKiB ?? FollowGlobal;
            explicitSetting = value > 0 ? value : value < 0 ? Unlimited : FollowGlobal;
            WakeIfSleeping();
        }

        public void WakeIfSleeping()
        {
            Interlocked.Increment(ref wakeGeneration);
        }

        private int CurrentLimit()
        {
            var setting = explicitSetting;
            if (setting > 0) return setting;
            if (setting < 0) return 0;
            var now = Helpers.TickCount();
            if (now - lastChecked > 3000 || cachedSpeedLimit == -2)
            {
                lastChecked = now;
                cachedSpeedLimit = GetGlobalSpeedLimit();
            }
            return cachedSpeedLimit;
        }

        private int GetGlobalSpeedLimit()
        {
            int speedLimit = 0;
            lock (Config.Instance)
            {
                if (Config.Instance.EnableSpeedLimit && Config.Instance.DefaltDownloadSpeed > 0)
                {
                    speedLimit = Config.Instance.DefaltDownloadSpeed;
                }
            }
            return speedLimit;
        }

        public void ThrottleIfNeeded(IBaseDownloader downloader)
        {
            if (CurrentLimit() < 1)
            {
                paidUntil = 0;
                return;
            }
            downloader.Lock.EnterWriteLock();
            try
            {
                int speedLimit = CurrentLimit();
                if (speedLimit < 1)
                {
                    paidUntil = 0;
                    return;
                }
                var now = Helpers.TickCount();
                var bytes = downloader.GetDownloaded();
                if (paidUntil <= 0 || bytes < lastBytes)
                {
                    lastBytes = bytes;
                    paidUntil = now;
                    return;
                }
                var due = Math.Max(paidUntil, now - MaxCreditMilliseconds) + (bytes - lastBytes) * 1000.0 / (speedLimit * 1024.0);
                lastBytes = bytes;
                paidUntil = due;
                var wait = (int)Math.Min(int.MaxValue, Math.Ceiling(due - now));
                if (wait > 0)
                {
                    Pause(wait, downloader);
                    var after = Helpers.TickCount();
                    if (paidUntil > after) paidUntil = after;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Exception while throttling");
            }
            finally
            {
                downloader.Lock.ExitWriteLock();
            }
        }

        private void Pause(int interval, IBaseDownloader downloader)
        {
            var generation = Volatile.Read(ref wakeGeneration);
            var deadline = Helpers.TickCount() + interval;
            while (generation == Volatile.Read(ref wakeGeneration) && !downloader.IsCancelled)
            {
                var remaining = deadline - Helpers.TickCount();
                if (remaining <= 0) return;
                Thread.Sleep((int)Math.Min(remaining, PauseSliceMilliseconds));
            }
        }
    }
}
