using System;
using Windows.Data.Json;

namespace App1.Helpers
{
    internal static class JsonUtil
    {
        public static bool Contains(JsonObject obj, string key)
        {
            return obj != null && obj.ContainsKey(key) && obj[key].ValueType != JsonValueType.Null;
        }

        public static string GetString(JsonObject obj, string key, string fallback = "")
        {
            if (!Contains(obj, key))
            {
                return fallback;
            }

            if (obj[key].ValueType == JsonValueType.String)
            {
                return obj[key].GetString();
            }

            if (obj[key].ValueType == JsonValueType.Number)
            {
                return obj[key].GetNumber().ToString();
            }

            return fallback;
        }

        public static bool GetBool(JsonObject obj, string key)
        {
            if (!Contains(obj, key))
            {
                return false;
            }

            if (obj[key].ValueType == JsonValueType.Boolean)
            {
                return obj[key].GetBoolean();
            }

            if (obj[key].ValueType == JsonValueType.String)
            {
                return string.Equals(obj[key].GetString(), "true", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public static long GetInt64(JsonObject obj, string key)
        {
            if (!Contains(obj, key))
            {
                return 0;
            }

            if (obj[key].ValueType == JsonValueType.Number)
            {
                return (long)obj[key].GetNumber();
            }

            if (obj[key].ValueType == JsonValueType.String)
            {
                long value;
                if (long.TryParse(obj[key].GetString(), out value))
                {
                    return value;
                }
            }

            return 0;
        }

        public static int GetInt32(JsonObject obj, string key)
        {
            return (int)GetInt64(obj, key);
        }

        public static double GetDouble(JsonObject obj, string key)
        {
            if (!Contains(obj, key))
            {
                return 0;
            }

            if (obj[key].ValueType == JsonValueType.Number)
            {
                return obj[key].GetNumber();
            }

            return 0;
        }

        public static JsonObject GetObject(JsonObject obj, string key)
        {
            if (!Contains(obj, key) || obj[key].ValueType != JsonValueType.Object)
            {
                return null;
            }

            return obj[key].GetObject();
        }

        public static JsonArray GetArray(JsonObject obj, string key)
        {
            if (!Contains(obj, key) || obj[key].ValueType != JsonValueType.Array)
            {
                return null;
            }

            return obj[key].GetArray();
        }

        public static JsonArray ParseArray(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JsonArray();
            }

            var trimmed = json.TrimStart();
            if (trimmed.StartsWith("["))
            {
                return JsonArray.Parse(json);
            }

            var obj = JsonObject.Parse(json);
            if (Contains(obj, "videos"))
            {
                return GetArray(obj, "videos") ?? new JsonArray();
            }

            if (Contains(obj, "comments"))
            {
                return GetArray(obj, "comments") ?? new JsonArray();
            }

            return new JsonArray();
        }

        public static JsonObject ParseObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JsonObject();
            }

            return JsonObject.Parse(json);
        }

        public static JsonObject GetPath(JsonObject obj, params string[] keys)
        {
            var current = obj;
            for (int i = 0; i < keys.Length; i++)
            {
                current = GetObject(current, keys[i]);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        public static string GetText(JsonObject obj)
        {
            if (obj == null)
            {
                return "";
            }

            var simple = GetString(obj, "simpleText");
            if (!string.IsNullOrEmpty(simple))
            {
                return simple;
            }

            var content = GetString(obj, "content");
            if (!string.IsNullOrEmpty(content))
            {
                return content;
            }

            var label = GetString(obj, "label");
            if (!string.IsNullOrEmpty(label))
            {
                return label;
            }

            var runs = GetArray(obj, "runs");
            if (runs != null)
            {
                var text = "";
                foreach (var run in runs)
                {
                    if (run.ValueType == JsonValueType.Object)
                    {
                        text += GetString(run.GetObject(), "text");
                    }
                }

                return text;
            }

            var accessibility = GetObject(obj, "accessibility");
            if (accessibility != null)
            {
                var nested = GetText(GetObject(accessibility, "accessibilityData"));
                if (!string.IsNullOrEmpty(nested))
                {
                    return nested;
                }

                return GetText(accessibility);
            }

            return "";
        }

        public static string GetText(JsonObject parent, string key)
        {
            if (!Contains(parent, key))
            {
                return "";
            }

            if (parent[key].ValueType == JsonValueType.String)
            {
                return parent[key].GetString();
            }

            if (parent[key].ValueType == JsonValueType.Object)
            {
                return GetText(parent[key].GetObject());
            }

            return "";
        }

        public static void WalkObjects(IJsonValue node, Action<JsonObject> action)
        {
            if (node == null || action == null)
            {
                return;
            }

            if (node.ValueType == JsonValueType.Object)
            {
                var obj = node.GetObject();
                action(obj);
                foreach (var pair in obj)
                {
                    WalkObjects(pair.Value, action);
                }
            }
            else if (node.ValueType == JsonValueType.Array)
            {
                foreach (var item in node.GetArray())
                {
                    WalkObjects(item, action);
                }
            }
        }
    }
}
