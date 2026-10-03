using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ADM.Core.HttpServer
{
    public class RequestContextEventArgs : EventArgs
    {
        public RequestContext RequestContext { get; }
        public RequestContextEventArgs(RequestContext context)
        {
            this.RequestContext = context;
        }
    }
}
