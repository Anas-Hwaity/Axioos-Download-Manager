using System;
using System.IO;

namespace ADM.Core.Clients.Http
{
    internal static class HttpHeaderPolicy
    {
        internal static void ValidateName(string? name)
        {
            if (string.IsNullOrEmpty(name)) throw new InvalidDataException("HTTP header name is empty.");
            foreach (var c in name)
            {
                if (c <= 0x20 || c >= 0x7f || "()<>@,;:\\\"/[]?={}".IndexOf(c) >= 0)
                    throw new InvalidDataException("HTTP header name contains an invalid character.");
            }
        }

        internal static void ValidateValue(string? value)
        {
            if (value == null) return;
            if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\0') >= 0)
                throw new InvalidDataException("HTTP header value contains a forbidden control character.");
        }
    }
}
