using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BIM765T.Revit.Copilot.Core.Brain;

internal enum LightweightJsonKind
{
    Object,
    Array,
    String,
    Number,
    Boolean,
    Null
}

internal sealed class LightweightJsonValue
{
    private readonly Dictionary<string, LightweightJsonValue>? _properties;
    private readonly List<LightweightJsonValue>? _items;
    private readonly string _text;
    private readonly bool _boolean;

    private LightweightJsonValue(
        LightweightJsonKind kind,
        Dictionary<string, LightweightJsonValue>? properties = null,
        List<LightweightJsonValue>? items = null,
        string? text = null,
        bool boolean = false)
    {
        Kind = kind;
        _properties = properties;
        _items = items;
        _text = text ?? string.Empty;
        _boolean = boolean;
    }

    public LightweightJsonKind Kind { get; }

    public IReadOnlyDictionary<string, LightweightJsonValue> Properties => _properties ?? EmptyProperties;

    public IReadOnlyList<LightweightJsonValue> Items => _items ?? EmptyItems;

    public string StringValue => _text;

    public bool BooleanValue => _boolean;

    public bool IsObject => Kind == LightweightJsonKind.Object;

    public bool IsArray => Kind == LightweightJsonKind.Array;

    public bool TryGetProperty(string name, out LightweightJsonValue value)
    {
        value = Null();
        if (_properties == null || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return _properties.TryGetValue(name, out value!);
    }

    public bool TryGetDouble(out double value)
    {
        value = 0d;
        return Kind == LightweightJsonKind.Number
            && double.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static LightweightJsonValue Object(Dictionary<string, LightweightJsonValue> properties)
    {
        return new LightweightJsonValue(LightweightJsonKind.Object, properties: properties);
    }

    public static LightweightJsonValue Array(List<LightweightJsonValue> items)
    {
        return new LightweightJsonValue(LightweightJsonKind.Array, items: items);
    }

    public static LightweightJsonValue String(string value)
    {
        return new LightweightJsonValue(LightweightJsonKind.String, text: value);
    }

    public static LightweightJsonValue Number(string value)
    {
        return new LightweightJsonValue(LightweightJsonKind.Number, text: value);
    }

    public static LightweightJsonValue Boolean(bool value)
    {
        return new LightweightJsonValue(LightweightJsonKind.Boolean, boolean: value);
    }

    public static LightweightJsonValue Null()
    {
        return new LightweightJsonValue(LightweightJsonKind.Null);
    }

    private static readonly IReadOnlyDictionary<string, LightweightJsonValue> EmptyProperties =
        new Dictionary<string, LightweightJsonValue>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<LightweightJsonValue> EmptyItems =
        System.Array.Empty<LightweightJsonValue>();
}

internal static class LightweightJson
{
    public static bool TryParse(string json, out LightweightJsonValue value)
    {
        value = LightweightJsonValue.Null();
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        var index = 0;
        if (!TryParseValue(json, ref index, out value))
        {
            value = LightweightJsonValue.Null();
            return false;
        }

        SkipWhitespace(json, ref index);
        return index == json.Length;
    }

    private static bool TryParseValue(string json, ref int index, out LightweightJsonValue value)
    {
        value = LightweightJsonValue.Null();
        SkipWhitespace(json, ref index);
        if (index >= json.Length)
        {
            return false;
        }

        switch (json[index])
        {
            case '{':
                return TryParseObject(json, ref index, out value);
            case '[':
                return TryParseArray(json, ref index, out value);
            case '"':
                if (TryParseString(json, ref index, out var stringValue))
                {
                    value = LightweightJsonValue.String(stringValue);
                    return true;
                }

                return false;
            case 't':
                if (TryParseLiteral(json, ref index, "true"))
                {
                    value = LightweightJsonValue.Boolean(true);
                    return true;
                }

                return false;
            case 'f':
                if (TryParseLiteral(json, ref index, "false"))
                {
                    value = LightweightJsonValue.Boolean(false);
                    return true;
                }

                return false;
            case 'n':
                if (TryParseLiteral(json, ref index, "null"))
                {
                    value = LightweightJsonValue.Null();
                    return true;
                }

                return false;
            default:
                return TryParseNumber(json, ref index, out value);
        }
    }

    private static bool TryParseObject(string json, ref int index, out LightweightJsonValue value)
    {
        value = LightweightJsonValue.Null();
        if (index >= json.Length || json[index] != '{')
        {
            return false;
        }

        index++;
        SkipWhitespace(json, ref index);

        var properties = new Dictionary<string, LightweightJsonValue>(StringComparer.OrdinalIgnoreCase);
        if (index < json.Length && json[index] == '}')
        {
            index++;
            value = LightweightJsonValue.Object(properties);
            return true;
        }

        while (index < json.Length)
        {
            if (!TryParseString(json, ref index, out var propertyName))
            {
                return false;
            }

            SkipWhitespace(json, ref index);
            if (index >= json.Length || json[index] != ':')
            {
                return false;
            }

            index++;
            if (!TryParseValue(json, ref index, out var propertyValue))
            {
                return false;
            }

            properties[propertyName] = propertyValue;
            SkipWhitespace(json, ref index);
            if (index >= json.Length)
            {
                return false;
            }

            if (json[index] == '}')
            {
                index++;
                value = LightweightJsonValue.Object(properties);
                return true;
            }

            if (json[index] != ',')
            {
                return false;
            }

            index++;
            SkipWhitespace(json, ref index);
        }

        return false;
    }

    private static bool TryParseArray(string json, ref int index, out LightweightJsonValue value)
    {
        value = LightweightJsonValue.Null();
        if (index >= json.Length || json[index] != '[')
        {
            return false;
        }

        index++;
        SkipWhitespace(json, ref index);

        var items = new List<LightweightJsonValue>();
        if (index < json.Length && json[index] == ']')
        {
            index++;
            value = LightweightJsonValue.Array(items);
            return true;
        }

        while (index < json.Length)
        {
            if (!TryParseValue(json, ref index, out var item))
            {
                return false;
            }

            items.Add(item);
            SkipWhitespace(json, ref index);
            if (index >= json.Length)
            {
                return false;
            }

            if (json[index] == ']')
            {
                index++;
                value = LightweightJsonValue.Array(items);
                return true;
            }

            if (json[index] != ',')
            {
                return false;
            }

            index++;
            SkipWhitespace(json, ref index);
        }

        return false;
    }

    private static bool TryParseString(string json, ref int index, out string value)
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
            var ch = json[index++];
            if (ch == '"')
            {
                value = builder.ToString();
                return true;
            }

            if (ch != '\\')
            {
                builder.Append(ch);
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

                    if (!ushort.TryParse(json.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
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

    private static bool TryParseNumber(string json, ref int index, out LightweightJsonValue value)
    {
        value = LightweightJsonValue.Null();
        var start = index;

        if (index < json.Length && json[index] == '-')
        {
            index++;
        }

        if (index >= json.Length)
        {
            return false;
        }

        if (json[index] == '0')
        {
            index++;
        }
        else
        {
            if (!char.IsDigit(json[index]))
            {
                return false;
            }

            while (index < json.Length && char.IsDigit(json[index]))
            {
                index++;
            }
        }

        if (index < json.Length && json[index] == '.')
        {
            index++;
            if (index >= json.Length || !char.IsDigit(json[index]))
            {
                return false;
            }

            while (index < json.Length && char.IsDigit(json[index]))
            {
                index++;
            }
        }

        if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
        {
            index++;
            if (index < json.Length && (json[index] == '+' || json[index] == '-'))
            {
                index++;
            }

            if (index >= json.Length || !char.IsDigit(json[index]))
            {
                return false;
            }

            while (index < json.Length && char.IsDigit(json[index]))
            {
                index++;
            }
        }

        value = LightweightJsonValue.Number(json.Substring(start, index - start));
        return true;
    }

    private static bool TryParseLiteral(string json, ref int index, string literal)
    {
        if (index + literal.Length > json.Length
            || !string.Equals(json.Substring(index, literal.Length), literal, StringComparison.Ordinal))
        {
            return false;
        }

        index += literal.Length;
        return true;
    }

    private static void SkipWhitespace(string json, ref int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index]))
        {
            index++;
        }
    }
}
