using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ADM.Compatibility;

namespace ADM.Core.Util
{
    internal static class ProcessInvocation
    {
        internal static ProcessStartInfo Create(string executable, IEnumerable<string>? arguments = null,
            bool useShellExecute = false, bool createNoWindow = true)
        {
            if (string.IsNullOrWhiteSpace(executable))
                throw new ArgumentException("Executable path must not be empty.", nameof(executable));

            var psi = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = useShellExecute,
                CreateNoWindow = createNoWindow
            };

            if (arguments != null)
            {
                var list = new List<string>();
                foreach (var argument in arguments)
                    list.Add(argument ?? string.Empty);
                if (list.Count > 0)
                    psi.Arguments = ProcessStartInfoHelper.ArgumentListToArgsString(list);
            }
            return psi;
        }

        internal static IReadOnlyList<string> ParseLegacyCommandLine(string commandLine)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(commandLine)) return result;

            var current = new StringBuilder();
            var inQuotes = false;
            var backslashes = 0;
            for (var i = 0; i < commandLine.Length; i++)
            {
                var c = commandLine[i];
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    if (backslashes > 0)
                    {
                        current.Append('\\', backslashes / 2);
                        if ((backslashes & 1) == 1)
                        {
                            current.Append('"');
                            backslashes = 0;
                            continue;
                        }
                    }
                    backslashes = 0;
                    inQuotes = !inQuotes;
                    continue;
                }

                if (backslashes > 0)
                {
                    current.Append('\\', backslashes);
                    backslashes = 0;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }
                current.Append(c);
            }

            if (backslashes > 0) current.Append('\\', backslashes);
            if (inQuotes) throw new FormatException("Unterminated quote in command line.");
            if (current.Length > 0) result.Add(current.ToString());
            return result;
        }
    }
}
