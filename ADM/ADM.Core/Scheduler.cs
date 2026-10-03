using System;
using System.Collections.Generic;
using System.Threading;
using TraceLog;
using ADM.Core;
using ADM.Core.DataAccess;

namespace ADM.Core
{
    public static class ScheduleWindow
    {
        public const int MaxCatchUpDays = 7;

        public static bool RunsOn(WeekDays days, DayOfWeek day)
        {
            var flag = (WeekDays)(1 << (int)day);
            return ((byte)days & (byte)flag) == (byte)flag;
        }

        private static TimeSpan TimeOfDay(TimeSpan value)
        {
            return new TimeSpan(value.Hours, value.Minutes, 0);
        }

        public static DateTime StartOf(DownloadSchedule schedule, DateTime day)
        {
            return day.Date + TimeOfDay(schedule.StartTime);
        }

        public static DateTime EndOf(DownloadSchedule schedule, DateTime day)
        {
            var start = TimeOfDay(schedule.StartTime);
            var end = TimeOfDay(schedule.EndTime);
            return end > start ? day.Date + end : day.Date.AddDays(1) + end;
        }

        public static DateTime? ActiveStart(DownloadSchedule schedule, DateTime now)
        {
            for (var offset = 0; offset <= 1; offset++)
            {
                var day = now.Date.AddDays(-offset);
                if (!RunsOn(schedule.Days, day.DayOfWeek)) continue;
                var start = StartOf(schedule, day);
                if (now >= start && now < EndOf(schedule, day)) return start;
            }
            return null;
        }

        public static bool EndedBetween(DownloadSchedule schedule, DateTime previous, DateTime now)
        {
            if (previous >= now) return false;
            if (now - previous > TimeSpan.FromDays(MaxCatchUpDays)) previous = now.AddDays(-MaxCatchUpDays);
            for (var day = previous.Date.AddDays(-1); day <= now.Date; day = day.AddDays(1))
            {
                if (!RunsOn(schedule.Days, day.DayOfWeek)) continue;
                var end = EndOf(schedule, day);
                if (end > previous && end <= now) return true;
            }
            return false;
        }
    }

    public class Scheduler : IDisposable
    {
        private const int FirstCheckMilliseconds = 15000;
        private const int CheckMilliseconds = 20000;
        private Timer? timer;
        private readonly Dictionary<string, DateTime> startedOccurrences = new Dictionary<string, DateTime>();
        private readonly object evaluation = new object();
        private DateTime? lastEvaluation;
        private readonly IApplicationRuntimeContext runtimeContext;

        public Scheduler(IApplicationRuntimeContext runtimeContext)
        {
            this.runtimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
        }

        public void Start()
        {
            timer = new Timer(OnTimer, null, FirstCheckMilliseconds, CheckMilliseconds);
        }

        public void Dispose()
        {
            this.timer?.Dispose();
        }

        public void Stop()
        {
            this.timer?.Dispose();
        }

        private void Evaluate(DownloadQueue queue, DateTime previous, DateTime now)
        {
            if (queue.Schedule == null)
            {
                startedOccurrences.Remove(queue.ID);
                return;
            }
            var schedule = queue.Schedule.Value;
            var active = ScheduleWindow.ActiveStart(schedule, now);
            if (active == null)
            {
                if (ScheduleWindow.EndedBetween(schedule, previous, now))
                {
                    runtimeContext.CoreService.StopDownloads(new List<string>(queue.DownloadIds), true);
                    startedOccurrences.Remove(queue.ID);
                }
                return;
            }
            if (startedOccurrences.TryGetValue(queue.ID, out var started) && started == active.Value) return;
            var dict = new Dictionary<string, DownloadItemBase>();
            foreach (var id in queue.DownloadIds)
            {
                var ent = AppDB.Instance.Downloads.GetDownloadById(id);
                if (ent != null)
                {
                    dict[id] = ent;
                }
            }
            runtimeContext.CoreService.ResumeDownload(dict, nonInteractive: true);
            startedOccurrences[queue.ID] = active.Value;
        }

        private void OnTimer(object? state)
        {
            if (!Monitor.TryEnter(evaluation)) return;
            try
            {
                var now = DateTime.Now;
                var previous = lastEvaluation ?? now;
                if (previous > now) previous = now;
                foreach (var queue in QueueManager.Snapshot())
                {
                    try
                    {
                        Evaluate(queue, previous, now);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Scheduled queue could not be evaluated");
                    }
                }
                lastEvaluation = now;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Scheduler check failed");
            }
            finally
            {
                Monitor.Exit(evaluation);
            }
        }
    }
}
