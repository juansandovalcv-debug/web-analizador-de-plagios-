using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using web_analizador_de_plagios.Data.Models;

namespace web_analizador_de_plagios.Services;

/// <summary>
/// Inspecciona archivos locales y entregas almacenadas: fechas de Windows,
/// propietario NTFS, tamaño, codificación y hash SHA-256. El navegador no expone
/// la ruta ni la fecha de creación original, por lo que se distingue el origen
/// de cada dato en los metadatos devueltos.
/// </summary>
public sealed class InspectorMetadatos
{
    private static readonly HashSet<string> ExtensionesInspeccionables = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".txt", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp"
    };

    private readonly ILogger<InspectorMetadatos> _logger;
    private readonly string _contentRoot;
    private readonly string[] _raicesPermitidas;

    public InspectorMetadatos(IWebHostEnvironment entorno, IConfiguration configuracion, ILogger<InspectorMetadatos> logger)
    {
        _logger = logger;
        _contentRoot = Path.GetFullPath(entorno.ContentRootPath);

        var configuradas = configuracion.GetSection("Inspector:RutasPermitidas").Get<string[]>() ?? Array.Empty<string>();
        var perfiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } perfil
                ? Path.Combine(perfil, "Downloads")
                : string.Empty
        };

        _raicesPermitidas = configuradas
            .Concat(perfiles)
            .Append(_contentRoot)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> RaicesPermitidas => _raicesPermitidas;

    public async Task<MetadatosArchivo> InspeccionarArchivoAsync(
        string rutaCompleta,
        string nombreOriginal,
        string? tipoContenido = null,
        DateTimeOffset? modificadoNavegador = null,
        string origen = "Carga web",
        double? confianzaOcr = null,
        string? rutaRelativaSegura = null,
        CancellationToken cancellationToken = default)
    {
        var metadatos = await LeerMetadatosAsync(rutaCompleta, cancellationToken);
        metadatos.NombreOriginal = AlmacenArchivos.SanitizarNombreArchivo(nombreOriginal);
        metadatos.TipoContenido = tipoContenido;
        metadatos.ModificadoNavegador = modificadoNavegador;
        metadatos.Origen = origen;
        metadatos.ConfianzaOcr = confianzaOcr;
        metadatos.RutaRelativaSegura = rutaRelativaSegura;

        if (modificadoNavegador.HasValue)
        {
            metadatos.Notas = "La fecha de modificación reportada por el navegador se conserva como referencia; " +
                              "las fechas de creación/escritura mostradas corresponden al archivo almacenado en este equipo.";
        }
        else
        {
            metadatos.Notas = "Fechas obtenidas directamente del sistema de archivos local.";
        }

        if (operatingSystemEsWindows)
        {
            metadatos.Advertencias.Add("El propietario NTFS corresponde a la cuenta que ejecuta la aplicación local, no al autor original.");
        }

        return metadatos;
    }

    private static readonly bool operatingSystemEsWindows = OperatingSystem.IsWindows();

    public async Task<ResultadoInspeccionRuta> InspeccionarRutaLocalAsync(string ruta, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ruta))
        {
            return new ResultadoInspeccionRuta { Exito = false, Mensaje = "Escriba una ruta de archivo." };
        }

        try
        {
            var candidata = ruta.Trim().Trim('"');
            if (candidata.Contains('\0'))
            {
                return new ResultadoInspeccionRuta { Exito = false, Mensaje = "La ruta contiene caracteres no válidos." };
            }

            candidata = Environment.ExpandEnvironmentVariables(candidata);
            var segmentos = candidata.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segmentos.Any(s => s == ".."))
            {
                return new ResultadoInspeccionRuta { Exito = false, Mensaje = "No se permiten segmentos '..' en la ruta (protección contra path traversal)." };
            }

            if (!Path.IsPathRooted(candidata))
            {
                candidata = Path.Combine(_contentRoot, candidata);
            }

            var rutaCompleta = Path.GetFullPath(candidata);
            var raizValida = _raicesPermitidas.FirstOrDefault(raiz => EstaDentroDe(rutaCompleta, raiz));
            if (raizValida is null)
            {
                return new ResultadoInspeccionRuta
                {
                    Exito = false,
                    Mensaje = "La ruta está fuera de las carpetas autorizadas. Ajuste 'Inspector:RutasPermitidas' en appsettings.json si necesita otra ubicación."
                };
            }

            if (!File.Exists(rutaCompleta))
            {
                return new ResultadoInspeccionRuta { Exito = false, Mensaje = "El archivo no existe o no es accesible." };
            }

            var extension = Path.GetExtension(rutaCompleta);
            if (!ExtensionesInspeccionables.Contains(extension))
            {
                return new ResultadoInspeccionRuta { Exito = false, Mensaje = "Solo se inspeccionan archivos .cs, .txt o imágenes compatibles." };
            }

            var metadatos = await LeerMetadatosAsync(rutaCompleta, cancellationToken);
            metadatos.NombreOriginal = Path.GetFileName(rutaCompleta);
            metadatos.Origen = "Ruta local (Windows)";
            metadatos.RutaRelativaSegura = rutaCompleta;
            metadatos.Notas = $"Inspección directa del archivo original dentro de: {raizValida}";

            return new ResultadoInspeccionRuta
            {
                Exito = true,
                Mensaje = "Metadatos leídos correctamente.",
                Metadatos = metadatos
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new ResultadoInspeccionRuta { Exito = false, Mensaje = "Windows denegó el acceso a ese archivo." };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error inspeccionando ruta local");
            return new ResultadoInspeccionRuta { Exito = false, Mensaje = $"No se pudo inspeccionar el archivo: {ex.Message}" };
        }
    }

    private async Task<MetadatosArchivo> LeerMetadatosAsync(string rutaCompleta, CancellationToken cancellationToken)
    {
        var info = new FileInfo(rutaCompleta);
        if (!info.Exists)
        {
            throw new FileNotFoundException("El archivo no existe.", rutaCompleta);
        }

        var extension = info.Extension.ToLowerInvariant();
        var esImagen = AlmacenArchivos.EsImagen(info.Name);
        var bytes = await File.ReadAllBytesAsync(rutaCompleta, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        var metadatos = new MetadatosArchivo
        {
            NombreAlmacenado = info.Name,
            Extension = extension,
            TamanoBytes = info.Length,
            CreadoWindows = info.CreationTime,
            ModificadoWindows = info.LastWriteTime,
            AccedidoWindows = info.LastAccessTime,
            Propietario = ObtenerPropietario(rutaCompleta),
            HashSha256 = hash,
            EsImagen = esImagen,
            Codificacion = esImagen ? "Binario (imagen)" : "No determinada"
        };

        if (!esImagen)
        {
            var decodificacion = DecodificadorTexto.Decodificar(bytes);
            metadatos.Codificacion = decodificacion.Codificacion;
            metadatos.TieneBom = decodificacion.TieneBom;
            if (!string.IsNullOrWhiteSpace(decodificacion.Advertencia))
            {
                metadatos.Advertencias.Add(decodificacion.Advertencia);
            }
        }

        if (info.Length == 0)
        {
            metadatos.Advertencias.Add("El archivo está vacío.");
        }

        return metadatos;
    }

    private string? ObtenerPropietario(string ruta)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "No disponible (sistema no Windows)";
        }

        try
        {
            var info = new FileInfo(ruta);
            var seguridad = info.GetAccessControl();
            var propietario = seguridad.GetOwner(typeof(NTAccount));
            return propietario?.Value ?? WindowsIdentity.GetCurrent().Name;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "No se pudo leer el propietario NTFS de {Ruta}", ruta);
            return WindowsIdentity.GetCurrent().Name;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo leer el propietario de {Ruta}", ruta);
            return "No disponible";
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
