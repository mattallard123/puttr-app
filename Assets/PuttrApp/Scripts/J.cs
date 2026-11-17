// Reading and writing JSON without a schema. Reading leans on the SDK's PuttrJson, which gives
// Dictionary<string, object>, List<object>, string, double, bool and null. The API's rows are
// loosely typed (numbers arrive as strings from some queries), so every getter is forgiving.
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Puttr;

namespace PuttrApp
{
    public static class J
    {
        public static object Parse(string text) => PuttrJson.TryParse(text, out object value) ? value : null;

        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static List<object> List(object o) => o as List<object> ?? new List<object>();

        /// <summary>The API answers some single-row reads with a one-row array: take the row either way.</summary>
        public static object First(object o) => o is List<object> list ? (list.Count > 0 ? list[0] : null) : o;

        public static object Get(object o, string key)
        {
            var map = Obj(o);
            return map != null && map.TryGetValue(key, out object value) ? value : null;
        }

        public static string Str(object o, string key, string fallback = "")
        {
            object v = Get(o, key);
            if (v is string s) return s;
            if (v is double n) return n.ToString(CultureInfo.InvariantCulture);
            if (v is bool b) return b ? "true" : "false";
            return fallback;
        }

        public static double Num(object o, string key, double fallback = 0)
        {
            object v = Get(o, key);
            if (v is double n) return n;
            if (v is bool b) return b ? 1 : 0;
            if (v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) return parsed;
            return fallback;
        }

        public static int Int(object o, string key, int fallback = 0) => (int)System.Math.Round(Num(o, key, fallback));
        public static bool Flag(object o, string key) => Num(o, key) != 0 || Str(o, key) == "true";

        /// <summary>Writes maps, lists, strings, numbers, bools and null as JSON.</summary>
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case string s: Quote(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case IDictionary map:
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry pair in map)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Quote(sb, pair.Key.ToString());
                        sb.Append(':');
                        Write(sb, pair.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    bool start = true;
                    foreach (object item in list)
                    {
                        if (!start) sb.Append(',');
                        start = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    break;
                case System.IFormattable number: sb.Append(number.ToString(null, CultureInfo.InvariantCulture)); break;
                default: Quote(sb, value.ToString()); break;
            }
        }

        static void Quote(StringBuilder sb, string s)
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
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
