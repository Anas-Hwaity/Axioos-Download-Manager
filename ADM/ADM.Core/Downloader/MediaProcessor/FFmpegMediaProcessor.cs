using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using ADM.Core;
using System.Diagnostics;
using System.Threading;
using ADM.Core.Util;
using TraceLog;

namespace ADM.Core.MediaProcessor
{
    public class FFmpegMediaProcessor : BaseMediaProcessor
    {
        public override MediaProcessingResult MergeAudioVideStream(string file1, string file2,
            string outfile, CancelFlag cancellationToken, out long outFileSize)
        {
            var args = CreateMergeArgs(file1, file2, outfile);
            var ret = this.ProcessMedia(args, cancellationToken);
            try
            {
                outFileSize = new FileInfo(outfile).Length;
            }
            catch { outFileSize = -1; }
            return ret;
        }

        public override MediaProcessingResult MergeHLSAudioVideStream(string fileList, string outfile,
            CancelFlag cancellationToken, out long outFileSize)
        {
            var args = CreateHLSMergeArgs(fileList, outfile);
            var ret = this.ProcessMedia(args, cancellationToken);
            try
            {
                outFileSize = new FileInfo(outfile).Length;
            }
            catch { outFileSize = -1; }
            return ret;
        }

        public override MediaProcessingResult ConvertToMp3Audio(string infile, string outfile,
            CancelFlag cancellationToken, out long outFileSize)
        {
            var args = CreateMP3MergeArgs(infile, outfile);
            var ret = this.ProcessMedia(args, cancellationToken);
            try
            {
                outFileSize = new FileInfo(outfile).Length;
            }
            catch { outFileSize = -1; }
            return ret;
        }

        private static string[] CreateMergeArgs(string file1, string file2, string outfile)
        {
            var args = new string[] { "-i", file1, "-i", file2, "-acodec", "copy", "-vcodec", "copy",
                "-map", "0", "-map", "1", outfile, "-y" };
            return args;
        }

        private string[] CreateHLSMergeArgs(string file, string outfile)
        {
            var args = new string[] { "-f", "concat", "-safe", "0", "-i", file, "-auto_convert", "1", "-acodec", "copy", "-vcodec", "copy", outfile, "-y" };
            return args;
        }

        private string[] CreateMP3MergeArgs(string file, string outfile)
        {
            var args = new string[] { "-i", file, "-acodec", "libmp3lame", outfile, "-y" };
            return args;
        }

        private MediaProcessingResult ProcessMedia(string[] args, CancelFlag cancellationToken)
        {
            try
            {

                var duration = 0L;
                var time = 0L;
                var file = FindFFmpegBinary();

                Log.Debug("Starting media processor");

                var lastTick = Helpers.TickCount();
                var pb = new ProcessStartInfo
                {
                    FileName = file,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

#if NET5_0_OR_GREATER
                foreach (var arg in args)
                {
                    pb.ArgumentList.Add(arg);
                }
#else
                pb.Arguments = ADM.Compatibility.ProcessStartInfoHelper.ArgumentListToArgsString(args);
#endif
                pb.RedirectStandardOutput = true;
                pb.RedirectStandardError = true;

                using var proc = Process.Start(pb);
                if (proc == null)
                {
                    throw new Exception("FFmpeg process could not be started - Process.Start");
                }

                DataReceivedEventHandler readProgress = (a, b) =>
                {
                    try
                    {
                        var line = b.Data;
                        if (line != null)
                        {
                            Log.Debug(line);
                            if (duration == 0.0)
                            {
                                var md = ParsingHelper.RxDuration.Match(line);
                                var ret = ParsingHelper.ParseTime(md);
                                if (ret > 0) duration = ret;
                            }
                            var mt = ParsingHelper.RxTime.Match(line);
                            var ret2 = ParsingHelper.ParseTime(mt);
                            if (ret2 > 0)
                            {
                                time = ret2;

                                var tick = Helpers.TickCount();

                                if (duration > 0 && tick - lastTick > 1000)
                                {
                                    lastTick = tick;
                                    UpdateProgress((int)Math.Min(100, (time * 100) / duration));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, ex.Message);
                    }
                };

                proc.OutputDataReceived += readProgress;
                proc.ErrorDataReceived += readProgress;

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                while (true)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try { proc.Kill(); } catch (Exception ex) { Log.Debug(ex, "FFmpeg could not be stopped"); }
                        proc.WaitForExit(5000);
                        return MediaProcessingResult.Failed;
                    }
                    if (proc.WaitForExit(100))
                    {
                        proc.WaitForExit();
                        break;
                    }
                }

                if (proc.ExitCode == 0)
                {
                    return MediaProcessingResult.Success;
                }
                Log.Debug("FFmpeg exitcode: " + proc.ExitCode);
                return MediaProcessingResult.Failed;
            }
            catch (OperationCanceledException ex)
            {
                Log.Debug(ex, "FFmpeg processing failed");
                return MediaProcessingResult.Success;
            }
            catch (FileNotFoundException ex)
            {
                Log.Debug(ex, "FFmpeg processing failed");
                return MediaProcessingResult.AppNotFound;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "FFmpeg processing failed");
                return MediaProcessingResult.Failed;
            }
        }

        public static string FindFFmpegBinary()
        {
            var executableNames =
                Environment.OSVersion.Platform == PlatformID.Win32NT ?
                new string[] { "ffmpeg-x86.exe", "ffmpeg.exe" } : new string[] { "ffmpeg" };
            foreach (var executableName in executableNames)
            {
                var path = Path.Combine(Config.AppDir, executableName);
                if (File.Exists(path))
                {
                    return path;
                }
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, executableName);
                if (File.Exists(path))
                {
                    return path;
                }
                var ffmpegPathEnvVar = Environment.GetEnvironmentVariable("FFMPEG_HOME");
                if (ffmpegPathEnvVar != null)
                {
                    path = Path.Combine(ffmpegPathEnvVar, executableName);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
                path = PlatformHelper.FindExecutableFromSystemPath(executableName);
                if (path != null)
                {
                    return path;
                }
            }
            throw new FileNotFoundException("FFmpeg executable not found");
        }

        public static bool IsFFmpegInstalled()
        {
            try
            {
                FindFFmpegBinary();
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            return true;
        }
    }
}
