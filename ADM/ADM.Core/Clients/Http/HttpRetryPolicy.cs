using System.Net;

namespace ADM.Core.Clients.Http
{
    public static class HttpRetryPolicy
    {
        public static bool IsTransient(HttpStatusCode status)
        {
            var code = (int)status;
            return code == 408 || code == 425 || code == 429 || code == 500 || code == 502 || code == 503 || code == 504;
        }
    }
}
