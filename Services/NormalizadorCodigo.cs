using System.Security.Cryptography;
using System.Text;

namespace web_analizador_de_plagios.Services;

public sealed record ResultadoNormalizacion(
    string CodigoNormalizado,
    IReadOnlyList<string> Tokens,
    int LineasTotales,
    int LineasCodigo,
    int LineasComentario,
    int Caracteres,
    string Hash,
    int IdentificadoresDistintos);

/// <summary>
/// Normaliza código C# para comparación estructural: elimina comentarios,
/// neutraliza literales y unifica identificadores (V1, V2...) conservando el orden
/// de aparición. El tokenizador entiende cadenas normales, verbatim, raw strings,
/// caracteres y comentarios de bloque/anidados de línea.
/// </summary>
public sealed class NormalizadorCodigo
{
    private static readonly HashSet<string> PalabrasClave = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
        // Contextuales de uso frecuente en código moderno.
        "add", "and", "async", "await", "by", "descending", "dynamic", "equals", "file", "from",
        "get", "global", "group", "init", "into", "join", "let", "managed", "nameof", "nint",
        "not", "notnull", "nuint", "on", "or", "orderby", "partial", "record", "remove",
        "required", "scoped", "select", "set", "unmanaged", "value", "var", "when", "where",
        "with", "yield"
    };

    private static readonly string[] Operadores =
    {
        ">>>=", "??=", ">>>", "<<=", ">>=", "=>", "==", "!=", ">=", "<=", "&&", "||", "++", "--",
        "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "??", "?.", "::", "..", "->", "<<", ">>",
        "??", "!!"
    };

    public ResultadoNormalizacion Normalizar(string codigo, bool normalizarIdentificadores = true)
    {
        codigo ??= string.Empty;
        var texto = codigo.Replace("\r\n", "\n").Replace('\r', '\n');
        var lineasTotales = texto.Length == 0 ? 0 : texto.Count(c => c == '\n') + 1;
        var flagsCodigo = new bool[Math.Max(1, lineasTotales)];
        var flagsComentario = new bool[Math.Max(1, lineasTotales)];

        var limpio = new StringBuilder(texto.Length);
        var linea = 0;
        var i = 0;

        void SaltarSaltoDeLinea()
        {
            if (linea + 1 < flagsCodigo.Length)
            {
                linea++;
            }
        }

        while (i < texto.Length)
        {
            var c = texto[i];
            var siguiente = i + 1 < texto.Length ? texto[i + 1] : '\0';

            if (c == '/' && siguiente == '/')
            {
                flagsComentario[linea] = true;
                i += 2;
                while (i < texto.Length && texto[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && siguiente == '*')
            {
                flagsComentario[linea] = true;
                i += 2;
                while (i < texto.Length)
                {
                    if (texto[i] == '*' && i + 1 < texto.Length && texto[i + 1] == '/')
                    {
                        i += 2;
                        break;
                    }

                    if (texto[i] == '\n')
                    {
                        limpio.Append('\n');
                        SaltarSaltoDeLinea();
                        flagsComentario[linea] = true;
                    }

                    i++;
                }

                continue;
            }

            if (c == '"' || ((c == '@' || c == '$') && EsInicioDeCadena(texto, i)))
            {
                flagsCodigo[linea] = true;
                limpio.Append(" __STR__ ");
                i = SaltarCadena(texto, i, ref linea, flagsCodigo, limpio);
                continue;
            }

            if (c == '\'')
            {
                flagsCodigo[linea] = true;
                limpio.Append(" __CHR__ ");
                i = SaltarCaracter(texto, i, ref linea, limpio);
                continue;
            }

            if (c == '\n')
            {
                limpio.Append('\n');
                SaltarSaltoDeLinea();
                i++;
                continue;
            }

            if (!char.IsWhiteSpace(c))
            {
                flagsCodigo[linea] = true;
            }

            limpio.Append(c);
            i++;
        }

        var tokens = Tokenizar(limpio.ToString(), normalizarIdentificadores, out var distintos);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(" ", tokens))));

        var lineasCodigo = 0;
        var lineasComentario = 0;
        for (var l = 0; l < flagsCodigo.Length; l++)
        {
            if (flagsCodigo[l])
            {
                lineasCodigo++;
            }
            else if (flagsComentario[l])
            {
                lineasComentario++;
            }
        }

        return new ResultadoNormalizacion(
            limpio.ToString(),
            tokens,
            lineasTotales,
            lineasCodigo,
            lineasComentario,
            codigo.Length,
            hash,
            distintos);
    }

    public IReadOnlyList<string> Tokenizar(string codigo, bool normalizarIdentificadores = true)
        => Tokenizar(codigo, normalizarIdentificadores, out _);

    public static string CalcularHash(string contenido)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contenido ?? string.Empty)));

    public static bool EsPalabraClave(string token) => PalabrasClave.Contains(token);

    private static IReadOnlyList<string> Tokenizar(string codigo, bool normalizarIdentificadores, out int identificadoresDistintos)
    {
        var tokens = new List<string>(Math.Max(16, codigo.Length / 4));
        var identificadores = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;

        while (i < codigo.Length)
        {
            var c = codigo[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '_' && i + 7 <= codigo.Length && string.CompareOrdinal(codigo, i, "__STR__", 0, 7) == 0)
            {
                tokens.Add("\"S\"");
                i += 7;
                continue;
            }

            if (c == '_' && i + 7 <= codigo.Length && string.CompareOrdinal(codigo, i, "__CHR__", 0, 7) == 0)
            {
                tokens.Add("'C'");
                i += 7;
                continue;
            }

            if (char.IsLetter(c) || c == '_' || c == '@')
            {
                var inicio = i;
                i++;
                while (i < codigo.Length && (char.IsLetterOrDigit(codigo[i]) || codigo[i] == '_'))
                {
                    i++;
                }

                var identificador = codigo[inicio..i];
                if (identificador.StartsWith('@') && identificador.Length > 1)
                {
                    identificador = identificador[1..];
                }

                if (PalabrasClave.Contains(identificador))
                {
                    tokens.Add(identificador);
                }
                else if (!normalizarIdentificadores)
                {
                    tokens.Add(identificador);
                }
                else
                {
                    if (!identificadores.TryGetValue(identificador, out var alias))
                    {
                        alias = $"V{identificadores.Count + 1}";
                        identificadores[identificador] = alias;
                    }

                    tokens.Add(alias);
                }

                continue;
            }

            if (char.IsDigit(c))
            {
                i++;
                while (i < codigo.Length)
                {
                    var n = codigo[i];
                    if (char.IsLetterOrDigit(n) || n == '_' || n == '.' || n == 'x' || n == 'X')
                    {
                        i++;
                        continue;
                    }

                    break;
                }

                tokens.Add("N");
                continue;
            }

            if (c == '"' || c == '\'')
            {
                // Los literales ya vienen neutralizados, pero se cubre el caso de texto externo.
                var cierre = c;
                i++;
                while (i < codigo.Length)
                {
                    if (codigo[i] == '\\' && i + 1 < codigo.Length)
                    {
                        i += 2;
                        continue;
                    }

                    if (codigo[i] == cierre)
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                tokens.Add(c == '"' ? "\"S\"" : "'C'");
                continue;
            }

            var operadorEncontrado = false;
            foreach (var operador in Operadores)
            {
                if (i + operador.Length <= codigo.Length &&
                    string.CompareOrdinal(codigo, i, operador, 0, operador.Length) == 0)
                {
                    tokens.Add(operador);
                    i += operador.Length;
                    operadorEncontrado = true;
                    break;
                }
            }

            if (operadorEncontrado)
            {
                continue;
            }

            if (c == ';' || c == '{' || c == '}' || c == '(' || c == ')' || c == '[' || c == ']' ||
                c == ',' || c == '.' || c == ':' || c == '+' || c == '-' || c == '*' || c == '/' ||
                c == '%' || c == '=' || c == '<' || c == '>' || c == '!' || c == '&' || c == '|' ||
                c == '^' || c == '~' || c == '?' || c == '#')
            {
                tokens.Add(c.ToString());
            }

            i++;
        }

        identificadoresDistintos = identificadores.Count;
        return tokens;
    }

    private static bool EsInicioDeCadena(string texto, int indice)
    {
        var i = indice;
        while (i < texto.Length && (texto[i] == '@' || texto[i] == '$'))
        {
            i++;
        }

        return i < texto.Length && texto[i] == '"';
    }

    private static int SaltarCadena(string texto, int indice, ref int linea, bool[] flagsCodigo, StringBuilder limpio)
    {
        var inicio = indice;
        var esVerbatim = false;
        while (inicio < texto.Length && (texto[inicio] == '@' || texto[inicio] == '$'))
        {
            if (texto[inicio] == '@')
            {
                esVerbatim = true;
            }

            inicio++;
        }

        // Raw string: tres o más comillas.
        var comillas = 0;
        var j = inicio;
        while (j < texto.Length && texto[j] == '"')
        {
            comillas++;
            j++;
        }

        if (comillas >= 3)
        {
            var cierre = new string('"', comillas);
            var fin = texto.IndexOf(cierre, j, StringComparison.Ordinal);
            fin = fin < 0 ? texto.Length : fin + comillas;
            for (var k = inicio; k < fin; k++)
            {
                if (texto[k] == '\n')
                {
                    limpio.Append('\n');
                    if (linea + 1 < flagsCodigo.Length)
                    {
                        linea++;
                        flagsCodigo[linea] = true;
                    }
                }
            }

            return fin;
        }

        var pos = inicio + 1;
        while (pos < texto.Length)
        {
            var c = texto[pos];
            if (c == '\n')
            {
                limpio.Append('\n');
                if (linea + 1 < flagsCodigo.Length)
                {
                    linea++;
                    flagsCodigo[linea] = true;
                }

                pos++;
                continue;
            }

            if (esVerbatim)
            {
                if (c == '"')
                {
                    if (pos + 1 < texto.Length && texto[pos + 1] == '"')
                    {
                        pos += 2;
                        continue;
                    }

                    return pos + 1;
                }

                pos++;
                continue;
            }

            if (c == '\\' && pos + 1 < texto.Length)
            {
                pos += 2;
                continue;
            }

            if (c == '"')
            {
                return pos + 1;
            }

            pos++;
        }

        return pos;
    }

    private static int SaltarCaracter(string texto, int indice, ref int linea, StringBuilder limpio)
    {
        var i = indice + 1;
        while (i < texto.Length)
        {
            if (texto[i] == '\n')
            {
                limpio.Append('\n');
                linea++;
                i++;
                continue;
            }

            if (texto[i] == '\\' && i + 1 < texto.Length)
            {
                i += 2;
                continue;
            }

            if (texto[i] == '\'')
            {
                return i + 1;
            }

            i++;
        }

        return i;
    }
}
