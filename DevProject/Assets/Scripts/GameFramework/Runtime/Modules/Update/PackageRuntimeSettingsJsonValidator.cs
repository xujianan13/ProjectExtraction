using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameFramework
{
    /// <summary>以单一严格语法校验 Package Settings JSON 的字段集合和值类型。</summary>
    internal static class PackageRuntimeSettingsJsonValidator
    {
        private const int MaximumJsonLength = 64 * 1024;
        private const int MaximumArrayItems = 4096;

        private static readonly IReadOnlyDictionary<string, JsonFieldKind>
            FieldKinds = new Dictionary<string, JsonFieldKind>(
                StringComparer.Ordinal)
            {
                ["_schemaVersion"] = JsonFieldKind.Integer,
                ["_assemblyName"] = JsonFieldKind.String,
                ["_entryTypeName"] = JsonFieldKind.String,
                ["_entryMethodName"] = JsonFieldKind.String,
                ["_aotMetadataLocations"] = JsonFieldKind.StringArray,
                ["_requiredLocations"] = JsonFieldKind.StringArray,
                ["_startupSceneLocation"] = JsonFieldKind.String,
                ["_minimumHostVersion"] = JsonFieldKind.String,
                ["_maximumHostVersion"] = JsonFieldKind.String,
                ["_defaultHostServer"] = JsonFieldKind.String,
                ["_fallbackHostServer"] = JsonFieldKind.String,
                ["_downloadMaxConcurrency"] = JsonFieldKind.Integer,
                ["_retryCount"] = JsonFieldKind.Integer,
                ["_timeoutSeconds"] = JsonFieldKind.Integer,
                ["_allowOfflineGameFallback"] = JsonFieldKind.Boolean
            };

        internal static void Validate(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException(
                    "Package Settings JSON 不能为空。",
                    nameof(json));
            }
            if (json.Length > MaximumJsonLength)
            {
                throw new FormatException(
                    $"Package Settings JSON 超出长度上限：" +
                    $"actual={json.Length}, maximum={MaximumJsonLength}");
            }

            var parser = new StrictJsonParser(json);
            parser.ParseSettingsObject();
        }

        private enum JsonFieldKind
        {
            Integer,
            String,
            StringArray,
            Boolean
        }

        private sealed class StrictJsonParser
        {
            private readonly string _json;
            private int _index;

            internal StrictJsonParser(string json)
            {
                _json = json;
            }

            internal void ParseSettingsObject()
            {
                SkipWhitespace();
                Expect('{');
                var fields = new HashSet<string>(StringComparer.Ordinal);
                SkipWhitespace();
                if (!TryConsume('}'))
                {
                    while (true)
                    {
                        SkipWhitespace();
                        string fieldName = ParseString(
                            out bool fieldNameUsesEscape);
                        if (fieldNameUsesEscape)
                        {
                            ThrowFormat(
                                $"Package Settings 字段名不得使用转义：{fieldName}");
                        }
                        if (!FieldKinds.TryGetValue(
                                fieldName,
                                out JsonFieldKind fieldKind))
                        {
                            ThrowFormat(
                                $"Package Settings 包含未知字段：{fieldName}");
                        }
                        if (!fields.Add(fieldName))
                        {
                            ThrowFormat(
                                $"Package Settings 字段重复：{fieldName}");
                        }

                        SkipWhitespace();
                        Expect(':');
                        SkipWhitespace();
                        ParseFieldValue(fieldName, fieldKind);
                        SkipWhitespace();
                        if (TryConsume('}')) break;
                        Expect(',');
                    }
                }

                foreach (string requiredField in FieldKinds.Keys)
                {
                    if (fields.Contains(requiredField)) continue;
                    ThrowFormat(
                        $"Package Settings 缺少必需字段：{requiredField}");
                }

                SkipWhitespace();
                if (_index != _json.Length)
                {
                    ThrowFormat(
                        "Package Settings JSON 根对象后存在尾随内容。");
                }
            }

            private void ParseFieldValue(
                string fieldName,
                JsonFieldKind fieldKind)
            {
                switch (fieldKind)
                {
                    case JsonFieldKind.Integer:
                        ParseInteger(fieldName);
                        return;
                    case JsonFieldKind.String:
                        ParseString(out _);
                        return;
                    case JsonFieldKind.StringArray:
                        ParseStringArray(fieldName);
                        return;
                    case JsonFieldKind.Boolean:
                        ParseBoolean(fieldName);
                        return;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(fieldKind),
                            fieldKind,
                            null);
                }
            }

            private void ParseInteger(string fieldName)
            {
                int start = _index;
                if (TryConsume('-') && IsAtEnd)
                {
                    ThrowFormat(
                        $"Package Settings 整数字段缺少数字：{fieldName}");
                }

                if (TryConsume('0'))
                {
                    if (!IsAtEnd && IsDigit(Peek()))
                    {
                        ThrowFormat(
                            $"Package Settings 整数字段包含前导零：{fieldName}");
                    }
                }
                else
                {
                    if (IsAtEnd || !IsDigitOneToNine(Peek()))
                    {
                        ThrowFormat(
                            $"Package Settings 字段必须是整数：{fieldName}");
                    }
                    _index++;
                    while (!IsAtEnd && IsDigit(Peek()))
                        _index++;
                }

                if (!IsAtEnd &&
                    (Peek() == '.' || Peek() == 'e' || Peek() == 'E'))
                {
                    ThrowFormat(
                        $"Package Settings 字段必须是整数，不能使用小数或指数：" +
                        $"{fieldName}");
                }
                if (_index == start)
                {
                    ThrowFormat(
                        $"Package Settings 字段必须是整数：{fieldName}");
                }

                string token = _json.Substring(start, _index - start);
                if (string.Equals(token, "-0", StringComparison.Ordinal))
                {
                    ThrowFormat(
                        $"Package Settings 整数字段必须使用规范零值：{fieldName}");
                }
                if (!int.TryParse(
                        token,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out _))
                {
                    ThrowFormat(
                        $"Package Settings 整数字段超出 Int32 范围：{fieldName}");
                }
            }

            private void ParseStringArray(string fieldName)
            {
                Expect('[');
                SkipWhitespace();
                int count = 0;
                if (TryConsume(']')) return;

                while (true)
                {
                    SkipWhitespace();
                    if (IsAtEnd || Peek() != '"')
                    {
                        ThrowFormat(
                            $"Package Settings 字段必须是字符串数组：{fieldName}");
                    }
                    ParseString(out _);
                    count++;
                    if (count > MaximumArrayItems)
                    {
                        ThrowFormat(
                            $"Package Settings 字符串数组超出项数上限：" +
                            $"field={fieldName}, maximum={MaximumArrayItems}");
                    }
                    SkipWhitespace();
                    if (TryConsume(']')) return;
                    Expect(',');
                }
            }

            private void ParseBoolean(string fieldName)
            {
                if (TryConsumeLiteral("true") ||
                    TryConsumeLiteral("false"))
                {
                    return;
                }
                ThrowFormat(
                    $"Package Settings 字段必须是布尔值：{fieldName}");
            }

            private string ParseString(out bool usesEscape)
            {
                Expect('"');
                usesEscape = false;
                var builder = new StringBuilder();
                while (!IsAtEnd)
                {
                    char current = _json[_index++];
                    if (current == '"')
                        return builder.ToString();
                    if (current < 0x20)
                        ThrowFormat("Package Settings JSON 字符串包含控制字符。");
                    if (current == '\\')
                    {
                        usesEscape = true;
                        if (IsAtEnd)
                            ThrowFormat("Package Settings JSON 字符串转义不完整。");
                        char escaped = _json[_index++];
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
                                AppendUnicodeEscape(builder);
                                break;
                            default:
                                ThrowFormat(
                                    $"Package Settings JSON 包含非法转义：\\{escaped}");
                                break;
                        }
                        continue;
                    }

                    if (char.IsHighSurrogate(current))
                    {
                        if (IsAtEnd || !char.IsLowSurrogate(_json[_index]))
                        {
                            ThrowFormat(
                                "Package Settings JSON 字符串包含不完整代理对。");
                        }
                        builder.Append(current);
                        builder.Append(_json[_index++]);
                        continue;
                    }
                    if (char.IsLowSurrogate(current))
                    {
                        ThrowFormat(
                            "Package Settings JSON 字符串包含孤立低位代理项。");
                    }
                    builder.Append(current);
                }

                ThrowFormat("Package Settings JSON 字符串未闭合。");
                return string.Empty;
            }

            private void AppendUnicodeEscape(StringBuilder builder)
            {
                char first = ParseUnicodeCodeUnit();
                if (char.IsHighSurrogate(first))
                {
                    if (_index + 1 >= _json.Length ||
                        _json[_index] != '\\' ||
                        _json[_index + 1] != 'u')
                    {
                        ThrowFormat(
                            "Package Settings JSON Unicode 转义包含不完整代理对。");
                    }
                    _index += 2;
                    char second = ParseUnicodeCodeUnit();
                    if (!char.IsLowSurrogate(second))
                    {
                        ThrowFormat(
                            "Package Settings JSON Unicode 转义代理对无效。");
                    }
                    builder.Append(first);
                    builder.Append(second);
                    return;
                }
                if (char.IsLowSurrogate(first))
                {
                    ThrowFormat(
                        "Package Settings JSON Unicode 转义包含孤立低位代理项。");
                }
                builder.Append(first);
            }

            private char ParseUnicodeCodeUnit()
            {
                if (_index + 4 > _json.Length)
                {
                    ThrowFormat(
                        "Package Settings JSON Unicode 转义长度不足。");
                }

                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    int hex = HexValue(_json[_index++]);
                    if (hex < 0)
                    {
                        ThrowFormat(
                            "Package Settings JSON Unicode 转义包含非十六进制字符。");
                    }
                    value = (value << 4) | hex;
                }
                return (char)value;
            }

            private void SkipWhitespace()
            {
                while (!IsAtEnd)
                {
                    char current = Peek();
                    if (current != ' ' &&
                        current != '\t' &&
                        current != '\r' &&
                        current != '\n')
                    {
                        return;
                    }
                    _index++;
                }
            }

            private void Expect(char expected)
            {
                if (IsAtEnd || _json[_index] != expected)
                {
                    ThrowFormat(
                        $"Package Settings JSON 语法错误：expected='{expected}'");
                }
                _index++;
            }

            private bool TryConsume(char expected)
            {
                if (IsAtEnd || _json[_index] != expected)
                    return false;
                _index++;
                return true;
            }

            private bool TryConsumeLiteral(string literal)
            {
                if (_index + literal.Length > _json.Length)
                    return false;
                for (int i = 0; i < literal.Length; i++)
                {
                    if (_json[_index + i] != literal[i])
                        return false;
                }
                _index += literal.Length;
                return true;
            }

            private char Peek()
            {
                return _json[_index];
            }

            private bool IsAtEnd => _index >= _json.Length;

            private static bool IsDigit(char value)
            {
                return value >= '0' && value <= '9';
            }

            private static bool IsDigitOneToNine(char value)
            {
                return value >= '1' && value <= '9';
            }

            private static int HexValue(char value)
            {
                if (value >= '0' && value <= '9') return value - '0';
                if (value >= 'a' && value <= 'f') return value - 'a' + 10;
                if (value >= 'A' && value <= 'F') return value - 'A' + 10;
                return -1;
            }

            private void ThrowFormat(string message)
            {
                throw new FormatException($"{message} index={_index}");
            }
        }
    }
}
