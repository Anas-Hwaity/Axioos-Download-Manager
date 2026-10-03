using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ADM.Core;

namespace ADM.Core.Clients.Http
{
    public class HttpRequest
    {



        internal IHttpSession? Session { get; set; }

        public void AddRange(long range)
        {
            this.Session!.AddRange(range);
        }

        public void AddRange(long start, long end)
        {
            this.Session!.AddRange(start, end);
        }

        public void Abort()
        {
            this.Session?.Abort();
        }
    }
}
