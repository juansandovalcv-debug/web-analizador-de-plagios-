using web_analizador_de_plagios.Data.Models;

namespace web_analizador_de_plagios.Services;

public sealed record ArchivoGuardado(
    string NombreOriginal,
    string NombreAlmacenado,
    string RutaRelativa,
    string RutaCompleta,
    string Extension,
    string? TipoContenido,
    long TamanoBytes);

/// <summary>
/// Guarda los archivos con nombres generados por el servidor. Ningún nombre
/// recibido del navegador se usa como ruta, lo que elimina el riesgo de path traversal.
/// </summary>
public sealed class AlmacenArchivos
{
    private static readonly HashSet<string> ExtensionesPermitidas = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".txt", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp"
    };

    private static readonly HashSet<string> ExtensionesImagen = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp"
    };

    private static readonly Dictionary<string, string> TiposContenido = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "text/plain",
        [".txt"] = "text/plain",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".bmp"] = "image/bmp",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".webp"] = "image/webp"
    };

    private readonly string _raiz;
    private readonly long _maxBytes;
    private readonly ILogger<AlmacenArchivos> _logger;

    public AlmacenArchivos(IWebHostEnvironment entorno, IConfiguration configuracion, ILogger<AlmacenArchivos> logger)
    {
        _raiz = Path.GetFullPath(Path.Combine(entorno.ContentRootPath, "App_Data", "uploads"));
        _maxBytes = configuracion.GetValue<long?>("Archivos:TamanoMaximoBytes") ?? 10 * 1024 * 1024;
        _logger = logger;
    }

    public string RaizAlmacenamiento => _raiz;
    public long TamanoMaximoBytes => _maxBytes;

    public static bool ExtensionPermitida(string nombreArchivo)
        => ExtensionesPermitidas.Contains(Path.GetExtension(Path.GetFileName(nombreArchivo)));

    public static bool EsImagen(string nombreArchivo)
        => ExtensionesImagen.Contains(Path.GetExtension(Path.GetFileName(nombreArchivo)));

    public static bool EsCodigoOTexto(string nombreArchivo)
    {
        var extension = Path.GetExtension(Path.GetFileName(nombreArchivo));
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    public static string SanitizarNombreArchivo(string? nombre)
    {
        var baseNombre = Path.GetFileName((nombre ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(baseNombre))
        {
            return "archivo.cs";
        }

        var caracteres = baseNombre.ToCharArray();
        for (var i = 0; i < caracteres.Length; i++)
        {
            if (char.IsControl(caracteres[i]) || Path.GetInvalidFileNameChars().Contains(caracteres[i]))
            {
                caracteres[i] = '_';
            }
        }

        var limpio = new string(caracteres).Trim();
        if (limpio.Length > 180)
        {
            var extension = Path.GetExtension(limpio);
            var sinExtension = Path.GetFileNameWithoutExtension(limpio);
            limpio = sinExtension[..Math.Min(sinExtension.Length, 170)] + extension;
        }

        return string.IsNullOrWhiteSpace(limpio) ? "archivo.cs" : limpio;
    }

    public async Task<ArchivoGuardado> GuardarAsync(ArchivoEntrada archivo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archivo);

        var nombreOriginal = SanitizarNombreArchivo(archivo.Nombre);
        var extension = Path.GetExtension(nombreOriginal).ToLowerInvariant();

        if (!ExtensionesPermitidas.Contains(extension))
        {
            throw new InvalidOperationException("Tipo de archivo no permitido. Use .cs, .txt o una imagen compatible.");
        }

        if (archivo.Bytes.Length == 0)
        {
            throw new InvalidOperationException("El archivo está vacío.");
        }

        if (archivo.Bytes.Length > _maxBytes)
        {
            throw new InvalidOperationException($"El archivo supera el límite de {_maxBytes / (1024 * 1024)} MB.");
        }

        var subcarpeta = DateTime.UtcNow.ToString("yyyy-MM");
        var directorio = Path.GetFullPath(Path.Combine(_raiz, subcarpeta));
        if (!EstaDentroDe(directorio, _raiz))
        {
            throw new InvalidOperationException("Ruta de almacenamiento inválida.");
        }

        Directory.CreateDirectory(directorio);

        var nombreAlmacenado = $"{Guid.NewGuid():N}{extension}";
        var rutaCompleta = Path.GetFullPath(Path.Combine(directorio, nombreAlmacenado));
        if (!EstaDentroDe(rutaCompleta, _raiz))
        {
            throw new InvalidOperationException("Ruta de almacenamiento inválida.");
        }

        await File.WriteAllBytesAsync(rutaCompleta, archivo.Bytes, cancellationToken);
        _logger.LogInformation("Archivo almacenado: {Archivo} ({Bytes} bytes)", rutaCompleta, archivo.Bytes.Length);

        return new ArchivoGuardado(
            nombreOriginal,
            nombreAlmacenado,
            Path.GetRelativePath(_raiz, rutaCompleta).Replace('\\', '/'),
            rutaCompleta,
            extension,
            string.IsNullOrWhiteSpace(archivo.TipoContenido) ? TiposContenido.GetValueOrDefault(extension, "application/octet-stream") : archivo.TipoContenido,
            archivo.Bytes.Length);
    }

    public string ResolverRutaAlmacenada(string rutaRelativa)
    {
        if (string.IsNullOrWhiteSpace(rutaRelativa))
        {
            throw new ArgumentException("La ruta almacenada está vacía.", nameof(rutaRelativa));
        }

        var normalizada = rutaRelativa.Replace('\\', '/').TrimStart('/');
        var segmentos = normalizada.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segmentos.Any(s => s == ".." || s == "." || s.Contains(':')))
        {
            throw new InvalidOperationException("La ruta almacenada contiene segmentos no permitidos.");
        }

        var rutaCompleta = Path.GetFullPath(Path.Combine(_raiz, normalizada));
        if (!EstaDentroDe(rutaCompleta, _raiz))
        {
            throw new InvalidOperationException("La ruta almacenada queda fuera del área de archivos.");
        }

        return rutaCompleta;
    }

    public async Task EliminarAsync(string rutaRelativa)
    {
        try
        {
            var ruta = ResolverRutaAlmacenada(rutaRelativa);
            if (File.Exists(ruta))
            {
                await Task.Run(() => File.Delete(ruta));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo eliminar el archivo {Ruta}", rutaRelativa);
        }
    }

    private static bool EstaDentroDe(string ruta, string raiz)
    {
        var raizNormalizada = Path.GetFullPath(raiz).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rutaNormalizada = Path.GetFullPath(ruta);
        return rutaNormalizada.Equals(raizNormalizada, StringComparison.OrdinalIgnoreCase) ||
               rutaNormalizada.StartsWith(raizNormalizada + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
