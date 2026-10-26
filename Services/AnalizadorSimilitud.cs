using web_analizador_de_plagios.Data.Models;

namespace web_analizador_de_plagios.Services;

/// <summary>
/// Motor de comparación. Combina Levenshtein sobre tokens normalizados,
/// similitud de shingles (Jaccard) y Levenshtein a nivel de línea. El diff
/// lateral se reconstruye con la matriz de Levenshtein para señalar cambios.
/// </summary>
public sealed class AnalizadorSimilitud
{
    private const int MaxLineasDiff = 1800;
    private const int MaxTokens = 7000;
    private const int TamanoShingle = 4;

    private readonly NormalizadorCodigo _normalizador;

    public AnalizadorSimilitud(NormalizadorCodigo normalizador)
    {
        _normalizador = normalizador;
    }

    public ResultadoSimilitud Comparar(string? codigoIzquierdo, string? codigoDerecho, bool incluirFilas = true)
    {
        var izquierdo = codigoIzquierdo ?? string.Empty;
        var derecho = codigoDerecho ?? string.Empty;

        var normIzq = _normalizador.Normalizar(izquierdo);
        var normDer = _normalizador.Normalizar(derecho);

        var tokensIzq = normIzq.Tokens;
        var tokensDer = normDer.Tokens;
        var truncado = false;

        if (tokensIzq.Count > MaxTokens)
        {
            tokensIzq = tokensIzq.Take(MaxTokens).ToArray();
            truncado = true;
        }

        if (tokensDer.Count > MaxTokens)
        {
            tokensDer = tokensDer.Take(MaxTokens).ToArray();
            truncado = true;
        }

        var distanciaTokens = DistanciaLevenshtein(tokensIzq, tokensDer);
        var maxTokens = Math.Max(tokensIzq.Count, tokensDer.Count);
        var similitudTokens = maxTokens == 0 ? 0 : 1d - (double)distanciaTokens / maxTokens;

        var jaccard = JaccardShingles(tokensIzq, tokensDer);
        var similitudLineas = CalcularSimilitudLineas(izquierdo, derecho, out var lineasTruncadas);
        truncado |= lineasTruncadas;

        var porcentaje = Math.Clamp(
            (similitudTokens * 0.55 + jaccard * 0.25 + similitudLineas * 0.20) * 100d,
            0,
            100);

        if (maxTokens == 0 && NormalizarLineas(izquierdo).All(string.IsNullOrWhiteSpace))
        {
            porcentaje = 0;
        }

        var resultado = new ResultadoSimilitud
        {
            Porcentaje = Math.Round(porcentaje, 2),
            DistanciaTokens = distanciaTokens,
            SimilitudTokens = Math.Round(similitudTokens * 100, 2),
            SimilitudLineas = Math.Round(similitudLineas * 100, 2),
            JaccardShingles = Math.Round(jaccard * 100, 2),
            Truncado = truncado,
            Metodologia = "Levenshtein de tokens 55% + Jaccard de shingles 25% + Levenshtein de líneas 20%"
        };

        if (incluirFilas)
        {
            resultado.Filas = ConstruirDiff(izquierdo, derecho, out var resumen);
            resultado.LineasIguales = resumen.Iguales;
            resultado.LineasModificadas = resumen.Modificadas;
            resultado.LineasSoloIzquierda = resumen.SoloIzquierda;
            resultado.LineasSoloDerecha = resumen.SoloDerecha;
        }

        return resultado;
    }

    public ResumenSimilitud Resumir(ResultadoSimilitud resultado, int? referenciaId = null,
        string? referenciaEstudiante = null, string? referenciaArchivo = null)
    {
        return new ResumenSimilitud
        {
            Porcentaje = resultado.Porcentaje,
            DistanciaTokens = resultado.DistanciaTokens,
            SimilitudTokens = resultado.SimilitudTokens,
            SimilitudLineas = resultado.SimilitudLineas,
            JaccardShingles = resultado.JaccardShingles,
            LineasIguales = resultado.LineasIguales,
            LineasModificadas = resultado.LineasModificadas,
            LineasSoloIzquierda = resultado.LineasSoloIzquierda,
            LineasSoloDerecha = resultado.LineasSoloDerecha,
            Truncado = resultado.Truncado,
            ReferenciaEvaluacionId = referenciaId,
            ReferenciaEstudiante = referenciaEstudiante,
            ReferenciaArchivo = referenciaArchivo,
            Metodologia = resultado.Metodologia
        };
    }

    public static string ClasificarRiesgo(double porcentaje) => porcentaje switch
    {
        >= 80 => "Crítico",
        >= 60 => "Alto",
        >= 35 => "Medio",
        _ => "Bajo"
    };

    public static IReadOnlyList<string> NormalizarLineas(string texto)
    {
        var limpio = (texto ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('\uFEFF');
        var lineas = limpio.Split('\n');
        if (lineas.Length > 1 && lineas[^1].Length == 0)
        {
            return lineas[..^1];
        }

        return lineas;
    }

    private static double CalcularSimilitudLineas(string izquierdo, string derecho, out bool truncado)
    {
        var lineasIzq = NormalizarLineas(izquierdo);
        var lineasDer = NormalizarLineas(derecho);

        truncado = lineasIzq.Count > MaxLineasDiff || lineasDer.Count > MaxLineasDiff;
        var normIzq = lineasIzq.Take(MaxLineasDiff).Select(l => l.Trim()).ToArray();
        var normDer = lineasDer.Take(MaxLineasDiff).Select(l => l.Trim()).ToArray();

        if (normIzq.Length == 0 && normDer.Length == 0)
        {
            return 1;
        }

        var distancia = DistanciaLevenshtein(normIzq, normDer);
        return 1d - (double)distancia / Math.Max(normIzq.Length, normDer.Length);
    }

    private static int DistanciaLevenshtein(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0)
        {
            return b.Count;
        }

        if (b.Count == 0)
        {
            return a.Count;
        }

        // Dos filas: O(min(n,m)) de memoria.
        if (b.Count > a.Count)
        {
            (a, b) = (b, a);
        }

        var anterior = new int[b.Count + 1];
        var actual = new int[b.Count + 1];
        for (var j = 0; j <= b.Count; j++)
        {
            anterior[j] = j;
        }

        for (var i = 1; i <= a.Count; i++)
        {
            actual[0] = i;
            var filaA = a[i - 1];
            for (var j = 1; j <= b.Count; j++)
            {
                var costo = string.Equals(filaA, b[j - 1], StringComparison.Ordinal) ? 0 : 1;
                actual[j] = Math.Min(Math.Min(actual[j - 1] + 1, anterior[j] + 1), anterior[j - 1] + costo);
            }

            (anterior, actual) = (actual, anterior);
        }

        return anterior[b.Count];
    }

    private static double JaccardShingles(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
        {
            return 0;
        }

        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var setA = CrearShingles(a);
        var setB = CrearShingles(b);
        if (setA.Count == 0 || setB.Count == 0)
        {
            return 0;
        }

        var interseccion = 0;
        foreach (var item in setA)
        {
            if (setB.Contains(item))
            {
                interseccion++;
            }
        }

        var union = setA.Count + setB.Count - interseccion;
        return union == 0 ? 0 : (double)interseccion / union;
    }

    private static HashSet<string> CrearShingles(IReadOnlyList<string> tokens)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (tokens.Count < TamanoShingle)
        {
            foreach (var token in tokens)
            {
                set.Add(token);
            }

            return set;
        }

        for (var i = 0; i <= tokens.Count - TamanoShingle; i++)
        {
            set.Add(string.Join('\u001F', tokens.Skip(i).Take(TamanoShingle)));
        }

        return set;
    }

    private static List<LineaDiff> ConstruirDiff(
        string izquierdo, string derecho, out (int Iguales, int Modificadas, int SoloIzquierda, int SoloDerecha) resumen)
    {
        var lineasIzq = NormalizarLineas(izquierdo).Take(MaxLineasDiff).ToArray();
        var lineasDer = NormalizarLineas(derecho).Take(MaxLineasDiff).ToArray();
        var normIzq = lineasIzq.Select(l => l.Trim()).ToArray();
        var normDer = lineasDer.Select(l => l.Trim()).ToArray();

        var filas = new List<LineaDiff>(Math.Max(lineasIzq.Length, lineasDer.Length));
        var n = normIzq.Length;
        var m = normDer.Length;
        var dp = new int[n + 1, m + 1];

        for (var i = 0; i <= n; i++)
        {
            dp[i, 0] = i;
        }

        for (var j = 0; j <= m; j++)
        {
            dp[0, j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var costo = string.Equals(normIzq[i - 1], normDer[j - 1], StringComparison.Ordinal) ? 0 : 1;
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + costo);
            }
        }

        var iActual = n;
        var jActual = m;
        var inversas = new List<LineaDiff>(Math.Max(n, m));

        while (iActual > 0 || jActual > 0)
        {
            if (iActual > 0 && jActual > 0)
            {
                var igual = string.Equals(normIzq[iActual - 1], normDer[jActual - 1], StringComparison.Ordinal);
                var costo = igual ? 0 : 1;
                if (dp[iActual, jActual] == dp[iActual - 1, jActual - 1] + costo)
                {
                    inversas.Add(new LineaDiff
                    {
                        NumeroIzquierda = iActual,
                        NumeroDerecha = jActual,
                        TextoIzquierda = lineasIzq[iActual - 1],
                        TextoDerecha = lineasDer[jActual - 1],
                        Estado = igual ? EstadoLineaDiff.Igual : EstadoLineaDiff.Modificada
                    });
                    iActual--;
                    jActual--;
                    continue;
                }
            }

            if (iActual > 0 && (jActual == 0 || dp[iActual, jActual] == dp[iActual - 1, jActual] + 1))
            {
                inversas.Add(new LineaDiff
                {
                    NumeroIzquierda = iActual,
                    TextoIzquierda = lineasIzq[iActual - 1],
                    Estado = EstadoLineaDiff.SoloIzquierda
                });
                iActual--;
                continue;
            }

            inversas.Add(new LineaDiff
            {
                NumeroDerecha = jActual,
                TextoDerecha = lineasDer[jActual - 1],
                Estado = EstadoLineaDiff.SoloDerecha
            });
            jActual--;
        }

        inversas.Reverse();
        filas.AddRange(inversas);

        var iguales = filas.Count(f => f.Estado == EstadoLineaDiff.Igual);
        var modificadas = filas.Count(f => f.Estado == EstadoLineaDiff.Modificada);
        var soloIzquierda = filas.Count(f => f.Estado == EstadoLineaDiff.SoloIzquierda);
        var soloDerecha = filas.Count(f => f.Estado == EstadoLineaDiff.SoloDerecha);

        resumen = (iguales, modificadas, soloIzquierda, soloDerecha);
        return filas;
    }

    // Sobrecarga de conveniencia para obtener únicamente las filas del diff.
    public IReadOnlyList<LineaDiff> ObtenerDiff(string izquierdo, string derecho)
    {
        var resultado = Comparar(izquierdo, derecho, incluirFilas: true);
        return resultado.Filas;
    }
}
