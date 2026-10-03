using System;
using System.Text;

namespace YDLWrapper
{
    internal static class ProcessArgumentEncoder
    {
        public static void AppendArgument(StringBuilder builder, string argument)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(QuoteArgument(argument ?? string.Empty));
        }

        public static string QuoteArgument(string argument)
        {
            argument = argument ?? string.Empty;
            if (argument.Length == 0) return "\"\"";

            var requiresQuotes = false;
            foreach (var c in argument)
            {
                if (char.IsWhiteSpace(c) || c == '"')
                {
                    requiresQuotes = true;
                    break;
                }
            }
            if (!requiresQuotes) return argument;

            var builder = new StringBuilder(argument.Length + 2);
            builder.Append('"');
            var backslashes = 0;
            foreach (var c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }
                if (backslashes > 0)
                {
                    builder.Append('\\', backslashes);
                    backslashes = 0;
                }
                builder.Append(c);
            }
            if (backslashes > 0) builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }
}
