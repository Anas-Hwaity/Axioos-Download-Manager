using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ADM.Core.BrowserMonitoring
{
    public static class BrowserProtocolJson
    {
        public static JObject ParseBounded(byte[] utf8)
        {
            if (utf8 == null) throw new ArgumentNullException(nameof(utf8));
            if (utf8.Length > BrowserProtocolV1.MaxApplicationMessageBytes)
                throw new JsonReaderException("MessageTooLarge");
            var text = Encoding.UTF8.GetString(utf8);
            Preflight(text);
            var value = JObject.Parse(text);
            ValidateKnownFieldBounds(value);
            return value;
        }

        private static void Preflight(string text)
        {
            using var sr = new StringReader(text);
            using var reader = new JsonTextReader(sr)
            {
                MaxDepth = BrowserProtocolV1.MaxJsonDepth,
                DateParseHandling = DateParseHandling.None
            };
            var arrays = new Stack<int>();
            while (reader.Read())
            {
                if ((reader.TokenType == JsonToken.String || reader.TokenType == JsonToken.PropertyName) &&
                    reader.Value is string value && value.Length > BrowserProtocolV1.MaxStringChars)
                {
                    throw new JsonReaderException("StringTooLong");
                }
                if (reader.TokenType == JsonToken.StartArray)
                {
                    arrays.Push(0);
                    continue;
                }
                if (reader.TokenType == JsonToken.EndArray)
                {
                    arrays.Pop();
                    continue;
                }
                if (arrays.Count > 0 && reader.Depth == arrays.Count)
                {
                    var count = arrays.Pop() + 1;
                    if (count > BrowserProtocolV1.MaxArrayItems) throw new JsonReaderException("ArrayTooLarge");
                    arrays.Push(count);
                }
            }
        }

        private static void ValidateKnownFieldBounds(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    var name = property.Name;
                    if (property.Value.Type == JTokenType.String && IsUrlField(name))
                    {
                        var url = property.Value.Value<string>() ?? string.Empty;
                        if (url.Length > BrowserProtocolV1.MaxUrlChars) throw new JsonReaderException("UrlTooLong");
                    }
                    if (string.Equals(name, "headers", StringComparison.OrdinalIgnoreCase))
                    {
                        ValidateHeaders(property.Value);
                    }
                    ValidateKnownFieldBounds(property.Value);
                }
            }
            else if (token is JArray array)
            {
                if (array.Count > BrowserProtocolV1.MaxArrayItems) throw new JsonReaderException("ArrayTooLarge");
                foreach (var child in array) ValidateKnownFieldBounds(child);
            }
        }

        private static bool IsUrlField(string name)
        {
            return string.Equals(name, "url", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "finalUrl", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "referrer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "tabUrl", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateHeaders(JToken token)
        {
            if (token is JArray array)
            {
                if (array.Count > BrowserProtocolV1.MaxHeaderCount) throw new JsonReaderException("TooManyHeaders");
                foreach (var item in array)
                {
                    if (item.Type == JTokenType.String && (item.Value<string>() ?? string.Empty).Length > BrowserProtocolV1.MaxHeaderChars)
                        throw new JsonReaderException("HeaderTooLong");
                }
                return;
            }
            if (token is JObject obj)
            {
                var properties = new List<JProperty>(obj.Properties());
                if (properties.Count > BrowserProtocolV1.MaxHeaderCount) throw new JsonReaderException("TooManyHeaders");
                foreach (var property in properties)
                {
                    if (property.Name.Length > BrowserProtocolV1.MaxHeaderChars) throw new JsonReaderException("HeaderTooLong");
                    if (property.Value.Type == JTokenType.String && (property.Value.Value<string>() ?? string.Empty).Length > BrowserProtocolV1.MaxHeaderChars)
                        throw new JsonReaderException("HeaderTooLong");
                }
            }
        }
    }
}
