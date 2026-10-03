using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ADM.Core;

namespace ADM.Core.Downloader.Adaptive
{
    public class SimpleStreamMap : IChunkStreamMap
    {
        public Dictionary<string, string> StreamMap { get; set; }
        public string GetStream(string prefix)
        {
            return StreamMap[prefix];
        }
    }
}
