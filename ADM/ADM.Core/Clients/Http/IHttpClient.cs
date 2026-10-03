using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ADM.Core;

namespace ADM.Core.Clients.Http
{
    public interface IHttpClient : IDisposable
    {
        public TimeSpan Timeout { get; set; }

        public HttpRequest CreateGetRequest(Uri uri,
            Dictionary<string, List<string>>? headers = null,
            string? cookies = null,
            AuthenticationInfo? authentication = null);

        public HttpRequest CreatePostRequest(Uri uri,
            Dictionary<string, List<string>>? headers = null,
            string? cookies = null,
            AuthenticationInfo? authentication = null,
            byte[]? body = null);

        public HttpResponse Send(HttpRequest request);
        public void Close();
    }
}
