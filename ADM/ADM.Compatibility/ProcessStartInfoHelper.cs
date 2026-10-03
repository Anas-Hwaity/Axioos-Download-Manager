using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ADM.Compatibility
{
    public static class ProcessStartInfoHelper
    {
        public static string ArgumentListToArgsString(IList<string> arguments)
        {
            var stringBuilder = new StringBuilder();
            foreach (var argument in arguments)
            {
                AppendArgument(stringBuilder, argument);
            }
            return stringBuilder.ToString();
        }

        public static void AppendArgument(StringBuilder stringBuilder, string argument)
        {
            if (stringBuilder.Length != 0)
            {
                stringBuilder.Append(' ');
            }

            if (argument.Length != 0 && ContainsNoWhitespaceOrQuotes(argument))
            {
                stringBuilder.Append(argument);
            }
            else
            {
                stringBuilder.Append(Quote);
                int idx = 0;
                while (idx < argument.Length)
                {
                    char c = argument[idx++];
                    if (c == Backslash)
                    {
                        int numBackSlash = 1;
                        while (idx < argument.Length && argument[idx] == Backslash)
                        {
                            idx++;
                            numBackSlash++;
                        }

                        if (idx == argument.Length)
                        {
                            stringBuilder.Append(Backslash, numBackSlash * 2);
                        }
                        else if (argument[idx] == Quote)
                        {
                            stringBuilder.Append(Backslash, numBackSlash * 2 + 1);
                            stringBuilder.Append(Quote);
                            idx++;
                        }
                        else
                        {
                            stringBuilder.Append(Backslash, numBackSlash);
                        }

                        continue;
                    }

                    if (c == Quote)
                    {
                        stringBuilder.Append(Backslash);
                        stringBuilder.Append(Quote);
                        continue;
                    }

                    stringBuilder.Append(c);
                }

                stringBuilder.Append(Quote);
            }
        }

        private static bool ContainsNoWhitespaceOrQuotes(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == Quote)
                {
                    return false;
                }
            }

            return true;
        }

        private const char Quote = '\"';
        private const char Backslash = '\\';
    }
}
