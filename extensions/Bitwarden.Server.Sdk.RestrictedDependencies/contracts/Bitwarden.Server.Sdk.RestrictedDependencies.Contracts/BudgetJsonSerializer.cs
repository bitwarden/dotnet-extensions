using System.Globalization;
using System.Text;

namespace Bitwarden.Server.Sdk.RestrictedDependencies;

/// <summary>
/// Reads JSON text and escapes JSON strings. Analyzers run inside the compiler and must not carry
/// System.Text.Json. The baseline format needs only objects, arrays, strings, integers and booleans.
/// </summary>
internal static class BudgetJsonSerializer
{
    /// <summary>
    /// Parses a complete JSON document into dictionaries, lists, strings, longs, bools and nulls.
    /// Throws <see cref="FormatException"/> on malformed input.
    /// </summary>
    public static object? Parse(string text)
    {
        var reader = new Reader(text);
        reader.SkipWhitespace();
        var value = reader.ReadValue();
        reader.SkipWhitespace();
        return !reader.AtEnd ? throw reader.Error("unexpected trailing content") : value;
    }

    /// <summary>
    /// Appends <paramref name="value"/> as a quoted, escaped JSON string.
    /// </summary>
    public static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }
                    break;
            }
        }

        builder.Append('"');
    }

    private sealed class Reader
    {
        /// <summary>
        /// The baseline format nests three deep (document, sites array, site object). The limit is
        /// well clear of that and exists because a StackOverflowException cannot be caught: without
        /// it, a hand-edited baseline could take down the compiler that hosts the gate rather than
        /// being reported as malformed.
        /// </summary>
        private const int _maxDepth = 32;

        private readonly string _text;

        private int _position;

        private int _depth;

        public Reader(string text)
        {
            _text = text;
        }

        public bool AtEnd => _position >= _text.Length;

        public FormatException Error(string message) =>
            new($"Invalid JSON at offset {_position}: {message}.");

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }

        public object? ReadValue()
        {
            if (AtEnd)
            {
                throw Error("unexpected end of input");
            }

            var c = _text[_position];
            switch (c)
            {
                case '{': return ReadNested(ReadObject);
                case '[': return ReadNested(ReadArray);
                case '"': return ReadString();
                case 't': ReadLiteral("true"); return true;
                case 'f': ReadLiteral("false"); return false;
                case 'n': ReadLiteral("null"); return null;
                default:
                    if (c == '-' || char.IsDigit(c))
                    {
                        return ReadNumber();
                    }

                    throw Error($"unexpected character '{c}'");
            }
        }

        /// <summary>
        /// Runs a container reader one level deeper, refusing to go past <see cref="_maxDepth"/>.
        /// </summary>
        private object ReadNested<T>(Func<T> read)
            where T : notnull
        {
            if (++_depth > _maxDepth)
            {
                throw Error($"nested more than {_maxDepth} levels deep");
            }

            var value = read();
            _depth--;
            return value;
        }

        private Dictionary<string, object?> ReadObject()
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            _position++;
            SkipWhitespace();
            if (Peek('}'))
            {
                _position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (!Peek('"'))
                {
                    throw Error("expected property name");
                }

                var name = ReadString();
                SkipWhitespace();
                Expect(':');
                SkipWhitespace();
                result[name] = ReadValue();
                SkipWhitespace();
                if (Peek(','))
                {
                    _position++;
                    continue;
                }

                Expect('}');
                return result;
            }
        }

        private List<object?> ReadArray()
        {
            var result = new List<object?>();
            _position++;
            SkipWhitespace();
            if (Peek(']'))
            {
                _position++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                result.Add(ReadValue());
                SkipWhitespace();
                if (Peek(','))
                {
                    _position++;
                    continue;
                }

                Expect(']');
                return result;
            }
        }

        private string ReadString()
        {
            _position++;
            var builder = new StringBuilder();
            while (true)
            {
                if (AtEnd)
                {
                    throw Error("unterminated string");
                }

                var c = _text[_position++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (AtEnd)
                {
                    throw Error("unterminated escape");
                }

                var escape = _text[_position++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (_position + 4 > _text.Length)
                        {
                            throw Error("truncated unicode escape");
                        }

                        builder.Append((char)int.Parse(_text.Substring(_position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _position += 4;
                        break;
                    default:
                        throw Error($"unknown escape '\\{escape}'");
                }
            }
        }

        /// <summary>
        /// Reads an integer. The baseline schema has no decimals, so a fractional or exponent
        /// token is malformed input and is reported as such rather than silently widening.
        /// </summary>
        private long ReadNumber()
        {
            var start = _position;
            if (Peek('-'))
            {
                _position++;
            }

            // Consume the whole numeric-looking run, including '.' and 'e', so the error names the
            // entire offending token instead of stopping at the first character it cannot use.
            while (!AtEnd && (char.IsDigit(_text[_position]) || _text[_position] is '.' or 'e' or 'E' or '+' or '-'))
            {
                _position++;
            }

            var token = _text.Substring(start, _position - start);
            return long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
                ? integer
                : throw Error($"invalid number '{token}'");
        }

        private void ReadLiteral(string literal)
        {
            if (string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0)
            {
                throw Error($"expected '{literal}'");
            }

            _position += literal.Length;
        }

        private bool Peek(char c) => !AtEnd && _text[_position] == c;

        private void Expect(char c)
        {
            if (!Peek(c))
            {
                throw Error($"expected '{c}'");
            }

            _position++;
        }
    }
}
