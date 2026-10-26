using System.Text;

namespace web_analizador_de_plagios.Services;

public sealed record ResultadoDecodificacion(
    string Texto,
    string Codificacion,
    bool TieneBom,
    bool Valido,
    string? Advertencia);

/// <summary>
/// Detección de codificación sin dependencias externas. Prioriza UTF-8 estricto y
/// aplica una heurística conservadora para archivos ANSI / Latin-1.
/// </summary>
public static class DecodificadorTexto
{
    private static readonly UTF8Encoding Utf8Estricto = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static ResultadoDecodificacion Decodificar(byte[] contenido)
    {
        if (contenido.Length == 0)
        {
            return new ResultadoDecodificacion(string.Empty, "Vacío", false, true, null);
        }

        try
        {
            if (contenido.Length >= 4 && contenido[0] == 0xFF && contenido[1] == 0xFE && contenido[2] == 0x00 && contenido[3] == 0x00)
            {
                return new ResultadoDecodificacion(Encoding.UTF32.GetString(contenido, 4, contenido.Length - 4), "UTF-32 LE", true, true, null);
            }

            if (contenido.Length >= 4 && contenido[0] == 0x00 && contenido[1] == 0x00 && contenido[2] == 0xFE && contenido[3] == 0xFF)
            {
                return new ResultadoDecodificacion(Encoding.UTF32.GetString(contenido, 4, contenido.Length - 4), "UTF-32 BE", true, true, null);
            }

            if (contenido.Length >= 3 && contenido[0] == 0xEF && contenido[1] == 0xBB && contenido[2] == 0xBF)
            {
                return new ResultadoDecodificacion(Utf8Estricto.GetString(contenido, 3, contenido.Length - 3), "UTF-8", true, true, null);
            }

            if (contenido.Length >= 2 && contenido[0] == 0xFF && contenido[1] == 0xFE)
            {
                return new ResultadoDecodificacion(Encoding.Unicode.GetString(contenido, 2, contenido.Length - 2), "UTF-16 LE", true, true, null);
            }

            if (contenido.Length >= 2 && contenido[0] == 0xFE && contenido[1] == 0xFF)
            {
                return new ResultadoDecodificacion(Encoding.BigEndianUnicode.GetString(contenido, 2, contenido.Length - 2), "UTF-16 BE", true, true, null);
            }

            // UTF-16 sin BOM: los ceros alternos son una firma fiable en texto latino.
            if (contenido.Length >= 8)
            {
                var cerosPares = 0;
                var cerosImpares = 0;
                var muestras = Math.Min(contenido.Length, 2048);
                for (var i = 0; i < muestras; i++)
                {
                    if (contenido[i] != 0)
                    {
                        continue;
                    }

                    if (i % 2 == 0)
                    {
                        cerosPares++;
                    }
                    else
                    {
                        cerosImpares++;
                    }
                }

                var mitad = muestras / 2;
                if (mitad > 0 && cerosImpares > mitad * 0.6)
                {
                    return new ResultadoDecodificacion(Encoding.Unicode.GetString(contenido), "UTF-16 LE (sin BOM)", false, true, "Codificación detectada por heurística.");
                }

                if (mitad > 0 && cerosPares > mitad * 0.6)
                {
                    return new ResultadoDecodificacion(Encoding.BigEndianUnicode.GetString(contenido), "UTF-16 BE (sin BOM)", false, true, "Codificación detectada por heurística.");
                }
            }

            var utf8 = Utf8Estricto.GetString(contenido);
            return new ResultadoDecodificacion(utf8, "UTF-8", false, true, null);
        }
        catch (DecoderFallbackException)
        {
            var latin1 = Encoding.Latin1.GetString(contenido);
            return new ResultadoDecodificacion(
                latin1,
                "ANSI / Latin-1",
                false,
                true,
                "El archivo no es UTF-8 válido; se interpretó como ANSI/Latin-1. Revise caracteres especiales.");
        }
        catch (Exception ex)
        {
            return new ResultadoDecodificacion(
                Encoding.Latin1.GetString(contenido),
                "Latin-1 (respaldo)",
                false,
                false,
                $"No se pudo confirmar la codificación: {ex.Message}");
        }
    }

    public static bool PareceBinario(byte[] contenido)
    {
        if (contenido.Length == 0)
        {
            return false;
        }

        var muestra = Math.Min(contenido.Length, 4096);
        var sospechosos = 0;
        for (var i = 0; i < muestra; i++)
        {
            var b = contenido[i];
            if (b == 0)
            {
                return true;
            }

            if (b < 7 || (b > 13 && b < 32))
            {
                sospechosos++;
            }
        }

        return sospechosos > muestra * 0.12;
    }
}
