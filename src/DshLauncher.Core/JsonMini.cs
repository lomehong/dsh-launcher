// DshLauncher.Core — 极简 JSON (RFC 8259 子集)
using System.Globalization;
using System.Text;

namespace DshLauncher
{
    public sealed class JsonObject
    {
        private readonly Dictionary<string, object> _map;
        public JsonObject(Dictionary<string, object> map) { _map = map; }
        internal Dictionary<string, object> Writable => _map;
        public IReadOnlyDictionary<string, object> Map => _map;
        public IEnumerable<KeyValuePair<string, object>> Entries => _map;

        public string GetString(string key) => _map.TryGetValue(key, out object v) ? v as string : null;

        public bool GetBool(string key, bool def)
        {
            if (!_map.TryGetValue(key, out object v)) return def;
            if (v is bool b) return b;
            if (v is string s && bool.TryParse(s, out bool parsed)) return parsed;
            return def;
        }

        public long GetLong(string key, long def) =>
            _map.TryGetValue(key, out object v) && v is long l ? l : def;

        public double GetDouble(string key, double def) =>
            _map.TryGetValue(key, out object v) && v is double d ? d : def;
    }

    public static class JsonMini
    {
        public static JsonObject Parse(string text)
        {
            int i = 0;
            SkipWs(text, ref i);
            object root = ReadValue(text, ref i);
            SkipWs(text, ref i);
            if (i < text.Length) throw new FormatException("JSON 末尾有未消费内容 at " + i);
            if (root is not Dictionary<string, object> map)
                throw new FormatException("根必须是对象");
            return new JsonObject(map);
        }

        private static object ReadValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("意外 EOF");
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i);
            if (c == '[') return ReadArray(s, ref i);
            if (c == '"') return ReadString(s, ref i);
            if (c == 't' || c == 'f') return ReadBool(s, ref i);
            if (c == 'n') return ReadNull(s, ref i);
            if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber(s, ref i);
            throw new FormatException("非法字符 '" + c + "' at " + i);
        }

        private static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var map = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                string key = ReadString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("缺少 ':' at " + i);
                i++;
                object val = ReadValue(s, ref i);
                map[key] = val;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return map; }
                throw new FormatException("对象缺少 '}' at " + i);
            }
            throw new FormatException("对象未闭合");
        }

        private static List<object> ReadArray(string s, ref int i)
        {
            var list = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (i < s.Length)
            {
                list.Add(ReadValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return list; }
                throw new FormatException("数组缺少 ']' at " + i);
            }
            throw new FormatException("数组未闭合");
        }

        private static string ReadString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("字符串需以 '\"' 开头 at " + i);
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (i >= s.Length) throw new FormatException("转义不完整");
                    char esc = s[i++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new FormatException("\\u 转义不完整");
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: throw new FormatException("非法转义 \\" + esc);
                    }
                }
                else sb.Append(c);
            }
            throw new FormatException("字符串未闭合");
        }

        private static bool ReadBool(string s, ref int i)
        {
            if (s.Substring(i, 4) == "true") { i += 4; return true; }
            if (s.Substring(i, 5) == "false") { i += 5; return false; }
            throw new FormatException("非法 bool at " + i);
        }

        private static object ReadNull(string s, ref int i)
        {
            if (s.Substring(i, 4) == "null") { i += 4; return null; }
            throw new FormatException("非法 null at " + i);
        }

        private static object ReadNumber(string s, ref int i)
        {
            int start = i;
            if (s[i] == '-') i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            if (i < s.Length && s[i] == '.')
            {
                i++;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            }
            string numStr = s.Substring(start, i - start);
            if (numStr.IndexOf('.') >= 0 || numStr.IndexOf('e') >= 0 || numStr.IndexOf('E') >= 0)
                return double.Parse(numStr, CultureInfo.InvariantCulture);
            return long.Parse(numStr, CultureInfo.InvariantCulture);
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        public static string Stringify(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (v is string s) { WriteString(sb, s); return; }
            if (v is long || v is int) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            if (v is double || v is float)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsInfinity(d) || double.IsNaN(d)) { sb.Append("null"); return; }
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (v is Dictionary<string, object> dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            if (v is List<object> list)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, v.ToString());
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
