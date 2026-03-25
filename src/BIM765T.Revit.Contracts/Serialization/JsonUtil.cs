using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BIM765T.Revit.Contracts.Serialization;

public static class JsonUtil
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> CachedPublicProperties = new ConcurrentDictionary<Type, PropertyInfo[]>();

    private static DataContractJsonSerializer CreateSerializer(Type type)
    {
        return new DataContractJsonSerializer(type, new DataContractJsonSerializerSettings
        {
            UseSimpleDictionaryFormat = true
        });
    }

    public static string Serialize<T>(T value)
    {
        var type = value != null ? value.GetType() : typeof(T);
        var serializer = CreateSerializer(type);
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static T Deserialize<T>(string? json)
    {
        return DeserializeOrDefault<T>(json);
    }

    public static T DeserializeOrDefault<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Activator.CreateInstance<T>();
        }

        return DeserializeRequired<T>(json);
    }

    public static T DeserializePayloadOrDefault<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Activator.CreateInstance<T>();
        }

        return DeserializeRequired<T>(json);
    }

    public static T DeserializeRequired<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException($"JSON payload for {typeof(T).Name} is empty.");
        }

        if (!TryDeserialize(json, out T value, out var error))
        {
            throw new InvalidDataException(error ?? $"JSON payload is not a valid {typeof(T).Name}.");
        }

        return value;
    }

    public static bool TryDeserialize<T>(string? json, out T value, out string? error)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            value = Activator.CreateInstance<T>();
            error = "JSON payload is empty.";
            return false;
        }

        try
        {
            var serializer = CreateSerializer(typeof(T));
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var obj = serializer.ReadObject(stream);
            if (obj is T typed)
            {
                ApplyMissingDefaults(typed, json!);
                value = typed;
                error = null;
                return true;
            }

            value = Activator.CreateInstance<T>();
            error = $"JSON payload is not a valid {typeof(T).Name}.";
            return false;
        }
        catch (Exception ex)
        {
            value = Activator.CreateInstance<T>();
            error = ex.Message;
            return false;
        }
    }

    private static void ApplyMissingDefaults<T>(T deserialized, string json)
    {
        if (deserialized == null)
        {
            return;
        }

        T defaults;
        try
        {
            defaults = Activator.CreateInstance<T>();
        }
        catch
        {
            return;
        }

        if (defaults == null)
        {
            return;
        }

        var properties = CachedPublicProperties.GetOrAdd(typeof(T), static type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        foreach (var prop in properties)
        {
            if (!prop.CanRead || !prop.CanWrite)
            {
                continue;
            }

            if (prop.PropertyType == typeof(int))
            {
                var current = (int)prop.GetValue(deserialized);
                var def = (int)prop.GetValue(defaults);
                if (current == 0 && def > 0 && !JsonContainsTopLevelField(json, prop.Name))
                {
                    prop.SetValue(deserialized, def);
                }
            }
            else if (prop.PropertyType == typeof(long))
            {
                var current = (long)prop.GetValue(deserialized);
                var def = (long)prop.GetValue(defaults);
                if (current == 0L && def > 0L && !JsonContainsTopLevelField(json, prop.Name))
                {
                    prop.SetValue(deserialized, def);
                }
            }
            else if (prop.PropertyType == typeof(string))
            {
                var current = (string)prop.GetValue(deserialized);
                var def = (string)prop.GetValue(defaults);
                if (current == null && def != null && !JsonContainsTopLevelField(json, prop.Name))
                {
                    prop.SetValue(deserialized, def);
                }
            }
            else if (prop.PropertyType == typeof(bool))
            {
                var current = (bool)prop.GetValue(deserialized);
                var def = (bool)prop.GetValue(defaults);
                if (!current && def && !JsonContainsTopLevelField(json, prop.Name))
                {
                    prop.SetValue(deserialized, def);
                }
            }
            else if (prop.PropertyType == typeof(double))
            {
                var current = (double)prop.GetValue(deserialized);
                var def = (double)prop.GetValue(defaults);
                if (Math.Abs(current) < double.Epsilon && Math.Abs(def) > double.Epsilon && !JsonContainsTopLevelField(json, prop.Name))
                {
                    prop.SetValue(deserialized, def);
                }
            }
            else if (prop.PropertyType == typeof(float))
            {
                var current = (float)prop.GetValue(deserialized);
                var def = (float)prop.GetValue(defaults);
                if (Math.Abs(current) < float.Epsilon && Math.Abs(def) > float.Epsilon && !JsonContainsTopLevelField(json, prop.Name))
                {
                    prop.SetValue(deserialized, def);
                }
            }
            else if (prop.PropertyType == typeof(decimal))
            {
                var current = (decimal)prop.GetValue(deserialized);
                var def = (decimal)prop.GetValue(defaults);
                if (current == decimal.Zero && def != decimal.Zero && !JsonContainsTopLevelField(json, prop.Name))
                {
                    prop.SetValue(deserialized, def);
                }
            }
            else if (prop.PropertyType == typeof(DateTime))
            {
                var current = (DateTime)prop.GetValue(deserialized);
                var def = (DateTime)prop.GetValue(defaults);
                if (current == default(DateTime) && def != default(DateTime) && !JsonContainsTopLevelField(json, prop.Name))
                {
                    prop.SetValue(deserialized, def);
                }
            }
            else if (prop.PropertyType == typeof(Guid))
            {
                var current = (Guid)prop.GetValue(deserialized);
                var def = (Guid)prop.GetValue(defaults);
                if (current == Guid.Empty && def != Guid.Empty && !JsonContainsTopLevelField(json, prop.Name))
                {
                    prop.SetValue(deserialized, def);
                }
            }
        }
    }

    private static bool JsonContainsTopLevelField(string json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        if (!TryReadTopLevelPropertyNames(json, out var propertyNames))
        {
            return false;
        }

        return propertyNames.Contains(fieldName) || propertyNames.Contains(ToCamelCase(fieldName));
    }

    private static bool TryReadTopLevelPropertyNames(string json, out HashSet<string> propertyNames)
    {
        propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        SkipWhitespace(json, ref index);
        if (index >= json.Length || json[index] != '{')
        {
            return false;
        }

        index++;
        while (index < json.Length)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length)
            {
                return false;
            }

            if (json[index] == '}')
            {
                return true;
            }

            if (!TryReadJsonString(json, ref index, out var propertyName))
            {
                return false;
            }

            propertyNames.Add(propertyName);
            SkipWhitespace(json, ref index);
            if (index >= json.Length || json[index] != ':')
            {
                return false;
            }

            index++;
            if (!TrySkipJsonValue(json, ref index))
            {
                return false;
            }

            SkipWhitespace(json, ref index);
            if (index >= json.Length)
            {
                return false;
            }

            if (json[index] == ',')
            {
                index++;
                continue;
            }

            if (json[index] == '}')
            {
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool TrySkipJsonValue(string json, ref int index)
    {
        SkipWhitespace(json, ref index);
        if (index >= json.Length)
        {
            return false;
        }

        switch (json[index])
        {
            case '"':
                return TryReadJsonString(json, ref index, out _);
            case '{':
                return TrySkipNested(json, ref index, '{', '}');
            case '[':
                return TrySkipNested(json, ref index, '[', ']');
            case 't':
                return TryConsumeLiteral(json, ref index, "true");
            case 'f':
                return TryConsumeLiteral(json, ref index, "false");
            case 'n':
                return TryConsumeLiteral(json, ref index, "null");
            default:
                return TrySkipNumber(json, ref index);
        }
    }

    private static bool TrySkipNested(string json, ref int index, char openChar, char closeChar)
    {
        if (index >= json.Length || json[index] != openChar)
        {
            return false;
        }

        var depth = 0;
        while (index < json.Length)
        {
            var current = json[index];
            if (current == '"')
            {
                if (!TryReadJsonString(json, ref index, out _))
                {
                    return false;
                }

                continue;
            }

            if (current == openChar)
            {
                depth++;
            }
            else if (current == closeChar)
            {
                depth--;
                index++;
                if (depth == 0)
                {
                    return true;
                }

                continue;
            }

            index++;
        }

        return false;
    }

    private static bool TryReadJsonString(string json, ref int index, out string value)
    {
        value = string.Empty;
        if (index >= json.Length || json[index] != '"')
        {
            return false;
        }

        index++;
        var builder = new StringBuilder();
        while (index < json.Length)
        {
            var current = json[index++];
            if (current == '"')
            {
                value = builder.ToString();
                return true;
            }

            if (current != '\\')
            {
                builder.Append(current);
                continue;
            }

            if (index >= json.Length)
            {
                return false;
            }

            var escaped = json[index++];
            switch (escaped)
            {
                case '"':
                case '\\':
                case '/':
                    builder.Append(escaped);
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'u':
                    if (index + 4 > json.Length)
                    {
                        return false;
                    }

                    if (!ushort.TryParse(json.Substring(index, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var codePoint))
                    {
                        return false;
                    }

                    builder.Append((char)codePoint);
                    index += 4;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool TryConsumeLiteral(string json, ref int index, string literal)
    {
        if (index + literal.Length > json.Length || !string.Equals(json.Substring(index, literal.Length), literal, StringComparison.Ordinal))
        {
            return false;
        }

        index += literal.Length;
        return true;
    }

    private static bool TrySkipNumber(string json, ref int index)
    {
        var start = index;
        if (json[index] == '-')
        {
            index++;
        }

        index = SkipDigits(json, index);
        if (index < json.Length && json[index] == '.')
        {
            index++;
            index = SkipDigits(json, index);
        }

        if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
        {
            index++;
            if (index < json.Length && (json[index] == '+' || json[index] == '-'))
            {
                index++;
            }

            index = SkipDigits(json, index);
        }

        return index > start;
    }

    private static int SkipDigits(string json, int index)
    {
        while (index < json.Length && char.IsDigit(json[index]))
        {
            index++;
        }

        return index;
    }

    private static void SkipWhitespace(string json, ref int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index]))
        {
            index++;
        }
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
        {
            return value;
        }

        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }
}
