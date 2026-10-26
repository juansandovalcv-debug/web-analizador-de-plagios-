using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using web_analizador_de_plagios.Data;
using web_analizador_de_plagios.Data.Models;

namespace web_analizador_de_plagios.Services;

public sealed class RespaldoGrupoDocumento
{
    public string Formato { get; set; } = "plagio-grupo";
    public int Version { get; set; } = 1;
    public string Aplicacion { get; set; } = "Analizador de Plagios";
    public DateTime ExportadoEn { get; set; } = DateTime.UtcNow;
    public RespaldoGrupoDto? Grupo { get; set; }
}

public sealed class RespaldoGrupoDto
{
    public int IdOriginal { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public int Anio { get; set; }
    public string Ciclo { get; set; } = string.Empty;
    public int Computo { get; set; }
    public string? Descripcion { get; set; }
    public DateTime CreadoEn { get; set; }
    public List<RespaldoEstudianteDto> Estudiantes { get; set; } = new();
    public List<RespaldoEvaluacionDto> Evaluaciones { get; set; } = new();
    public List<RespaldoHistorialDto> Historiales { get; set; } = new();
}

public sealed class RespaldoEstudianteDto
{
    public int IdOriginal { get; set; }
    public string Carnet { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Correo { get; set; }
    public DateTime CreadoEn { get; set; }
}

public sealed class RespaldoEvaluacionDto
{
    public int IdOriginal { get; set; }
    public int EstudianteIdOriginal { get; set; }
    public int? ReferenciaEvaluacionIdOriginal { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string NombreAlmacenado { get; set; } = string.Empty;
    public string Extension { get; set; } = ".cs";
    public string? TipoContenido { get; set; }
    public long TamanoBytes { get; set; }
    public string HashSha256 { get; set; } = string.Empty;
    public string CodigoFuente { get; set; } = string.Empty;
    public string OrigenArchivo { get; set; } = "Código";
    public double ConfianzaOcr { get; set; }
    public int LineasCodigo { get; set; }
    public int Metodos { get; set; }
    public int Bucles { get; set; }
    public int Condicionales { get; set; }
    public double Similitud { get; set; }
    public double ProbabilidadIA { get; set; }
    public string NivelRiesgo { get; set; } = "Bajo";
    public DateTime FechaAnalisis { get; set; }
    public double SimilitudReferencia { get; set; }
    public string? MetadatosJson { get; set; }
    public string? AnalisisIaJson { get; set; }
    public string? AnalisisSimilitudJson { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class RespaldoHistorialDto
{
    public int IdOriginal { get; set; }
    public int EvaluacionIdOriginal { get; set; }
    public int EstudianteIdOriginal { get; set; }
    public int Computo { get; set; }
    public int Anio { get; set; }
    public string Ciclo { get; set; } = string.Empty;
    public double Similitud { get; set; }
    public double ProbabilidadIA { get; set; }
    public string Origen { get; set; } = "Análisis";
    public string? Resumen { get; set; }
    public int? ReferenciaEvaluacionIdOriginal { get; set; }
    public DateTime Fecha { get; set; }
}

/// <summary>
/// Exporta e importa un grupo completo con estudiantes, evaluaciones, código y
/// historiales en un ZIP con un único JSON interno. No se extraen entradas a disco:
/// se lee exclusivamente el manifiesto, lo que evita ataques zip-slip y zip-bomb.
/// </summary>
public sealed class GestorRespaldos
{
    private const string EntradaJson = "grupo.json";
    private const long MaxJsonBytes = 64L * 1024 * 1024;
    private const int MaxEstudiantes = 5000;
    private const int MaxEvaluaciones = 20000;
    private const int MaxHistoriales = 100000;

    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        MaxDepth = 64,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<GestorRespaldos> _logger;

    public GestorRespaldos(IDbContextFactory<AppDbContext> dbFactory, ILogger<GestorRespaldos> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<ArchivoDescarga> ExportarGrupoAsync(int grupoId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var grupo = await db.Grupos
            .AsNoTracking()
            .Include(g => g.Estudiantes)
            .Include(g => g.Evaluaciones)
            .Include(g => g.Historiales)
            .FirstOrDefaultAsync(g => g.Id == grupoId, cancellationToken);

        if (grupo is null)
        {
            throw new InvalidOperationException("El grupo no existe o fue eliminado.");
        }

        var documento = new RespaldoGrupoDocumento
        {
            ExportadoEn = DateTime.UtcNow,
            Grupo = new RespaldoGrupoDto
            {
                IdOriginal = grupo.Id,
                Nombre = grupo.Nombre,
                Codigo = grupo.Codigo,
                Anio = grupo.Anio,
                Ciclo = grupo.Ciclo,
                Computo = grupo.Computo,
                Descripcion = grupo.Descripcion,
                CreadoEn = grupo.CreadoEn,
                Estudiantes = grupo.Estudiantes.OrderBy(e => e.Nombre).Select(e => new RespaldoEstudianteDto
                {
                    IdOriginal = e.Id,
                    Carnet = e.Carnet,
                    Nombre = e.Nombre,
                    Correo = e.Correo,
                    CreadoEn = e.CreadoEn
                }).ToList(),
                Evaluaciones = grupo.Evaluaciones.OrderBy(e => e.FechaAnalisis).Select(e => new RespaldoEvaluacionDto
                {
                    IdOriginal = e.Id,
                    EstudianteIdOriginal = e.EstudianteId,
                    ReferenciaEvaluacionIdOriginal = e.ReferenciaEvaluacionId,
                    NombreArchivo = e.NombreArchivo,
                    NombreAlmacenado = e.NombreAlmacenado,
                    Extension = e.Extension,
                    TipoContenido = e.TipoContenido,
                    TamanoBytes = e.TamanoBytes,
                    HashSha256 = e.HashSha256,
                    CodigoFuente = e.CodigoFuente,
                    OrigenArchivo = e.OrigenArchivo,
                    ConfianzaOcr = e.ConfianzaOcr,
                    LineasCodigo = e.LineasCodigo,
                    Metodos = e.Metodos,
                    Bucles = e.Bucles,
                    Condicionales = e.Condicionales,
                    Similitud = e.Similitud,
                    ProbabilidadIA = e.ProbabilidadIA,
                    NivelRiesgo = e.NivelRiesgo,
                    FechaAnalisis = e.FechaAnalisis,
                    SimilitudReferencia = e.SimilitudReferencia,
                    MetadatosJson = e.MetadatosJson,
                    AnalisisIaJson = e.AnalisisIaJson,
                    AnalisisSimilitudJson = e.AnalisisSimilitudJson,
                    Observaciones = e.Observaciones
                }).ToList(),
                Historiales = grupo.Historiales.OrderBy(h => h.Fecha).Select(h => new RespaldoHistorialDto
                {
                    IdOriginal = h.Id,
                    EvaluacionIdOriginal = h.EvaluacionId,
                    EstudianteIdOriginal = h.EstudianteId,
                    Computo = h.Computo,
                    Anio = h.Anio,
                    Ciclo = h.Ciclo,
                    Similitud = h.Similitud,
                    ProbabilidadIA = h.ProbabilidadIA,
                    Origen = h.Origen,
                    Resumen = h.Resumen,
                    ReferenciaEvaluacionIdOriginal = h.ReferenciaEvaluacionId,
                    Fecha = h.Fecha
                }).ToList()
            }
        };

        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entrada = zip.CreateEntry(EntradaJson, CompressionLevel.Optimal);
            await using (var salida = entrada.Open())
            {
                await JsonSerializer.SerializeAsync(salida, documento, OpcionesJson, cancellationToken);
            }

            var leeme = zip.CreateEntry("LEEME.txt", CompressionLevel.Optimal);
            await using var writer = new StreamWriter(leeme.Open(), Encoding.UTF8);
            await writer.WriteAsync(
                $"Respaldo del grupo {grupo.Nombre} ({grupo.Codigo})\r\n" +
                $"Periodo: {grupo.Ciclo} {grupo.Anio} · Cómputo {grupo.Computo}\r\n" +
                $"Estudiantes: {grupo.Estudiantes.Count}\r\n" +
                $"Evaluaciones: {grupo.Evaluaciones.Count}\r\n" +
                $"Historiales: {grupo.Historiales.Count}\r\n" +
                $"Exportado: {documento.ExportadoEn:u}\r\n\r\n" +
                "Para restaurar, use la opción «Importar respaldo» en la sección Grupos.\r\n");
        }

        var nombre = $"respaldo_{LimpiarNombre(grupo.Codigo)}_{grupo.Anio}_C{grupo.Computo}_{DateTime.Now:yyyyMMdd_HHmm}.zip";
        _logger.LogInformation("Respaldo exportado para grupo {GrupoId} ({Bytes} bytes)", grupoId, memoria.Length);

        return new ArchivoDescarga
        {
            Nombre = nombre,
            TipoContenido = "application/zip",
            Contenido = memoria.ToArray()
        };
    }

    public async Task<AvanceImportacion> ImportarAsync(Stream entrada, CancellationToken cancellationToken = default)
    {
        if (entrada is null || !entrada.CanRead)
        {
            throw new ArgumentException("No se recibió un archivo de respaldo válido.", nameof(entrada));
        }

        // El stream de un InputFile del navegador no admite lecturas sincrónicas,
        // que es lo que hace ZipArchive al abrir el archivo. Se copia primero a
        // memoria de forma asíncrona y con límite de tamaño.
        using var memoriaZip = new MemoryStream();
        var bufferCopia = new byte[81920];
        long totalCopiado = 0;
        int leidosCopia;
        while ((leidosCopia = await entrada.ReadAsync(bufferCopia, cancellationToken)) > 0)
        {
            totalCopiado += leidosCopia;
            if (totalCopiado > MaxJsonBytes)
            {
                throw new InvalidDataException("El respaldo supera el tamaño máximo permitido.");
            }

            await memoriaZip.WriteAsync(bufferCopia.AsMemory(0, leidosCopia), cancellationToken);
        }

        if (totalCopiado == 0)
        {
            throw new InvalidDataException("El archivo de respaldo está vacío.");
        }

        memoriaZip.Position = 0;

        RespaldoGrupoDocumento? documento;
        using (var zip = new ZipArchive(memoriaZip, ZipArchiveMode.Read, leaveOpen: false))
        {
            if (zip.Entries.Count == 0)
            {
                throw new InvalidDataException("El ZIP está vacío.");
            }

            var entradaJson = zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, EntradaJson, StringComparison.OrdinalIgnoreCase));

            if (entradaJson is null)
            {
                throw new InvalidDataException("El ZIP no contiene el manifiesto grupo.json esperado.");
            }

            if (entradaJson.Length > MaxJsonBytes)
            {
                throw new InvalidDataException("El manifiesto del respaldo es demasiado grande.");
            }

            var bytes = await LeerLimitadoAsync(entradaJson.Open(), MaxJsonBytes, cancellationToken);
            documento = JsonSerializer.Deserialize<RespaldoGrupoDocumento>(bytes, OpcionesJson);
        }

        if (documento is null || documento.Grupo is null)
        {
            throw new InvalidDataException("El manifiesto no contiene datos de grupo.");
        }

        if (!string.Equals(documento.Formato, "plagio-grupo", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("El archivo no es un respaldo del Analizador de Plagios.");
        }

        if (documento.Version > 1)
        {
            throw new InvalidDataException($"La versión del respaldo ({documento.Version}) no es compatible con esta aplicación.");
        }

        var grupoDto = documento.Grupo;
        if (grupoDto.Estudiantes.Count > MaxEstudiantes || grupoDto.Evaluaciones.Count > MaxEvaluaciones ||
            grupoDto.Historiales.Count > MaxHistoriales)
        {
            throw new InvalidDataException("El respaldo excede los límites de seguridad de la aplicación.");
        }

        var avance = new AvanceImportacion
        {
            GrupoNombre = Limpiar(grupoDto.Nombre, 120)
        };

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var anio = Math.Clamp(grupoDto.Anio, 2000, 2100);
            var ciclo = Limpiar(grupoDto.Ciclo, 30);
            var computo = Math.Clamp(grupoDto.Computo, 1, 3);
            var codigoBase = Limpiar(grupoDto.Codigo, 32);
            if (string.IsNullOrWhiteSpace(codigoBase))
            {
                codigoBase = "GRP";
            }

            var codigo = codigoBase;
            var sufijo = 1;
            while (await db.Grupos.AnyAsync(g => g.Codigo == codigo && g.Anio == anio && g.Ciclo == ciclo && g.Computo == computo, cancellationToken))
            {
                var sufijoTexto = $"-R{sufijo++}";
                codigo = codigoBase[..Math.Min(codigoBase.Length, 40 - sufijoTexto.Length)] + sufijoTexto;
                if (sufijo > 999)
                {
                    throw new InvalidOperationException("No se pudo generar un código único para el grupo importado.");
                }
            }

            var grupo = new Grupo
            {
                Nombre = Limpiar(grupoDto.Nombre, 120) is { Length: > 0 } nombre ? nombre : "Grupo importado",
                Codigo = codigo,
                Anio = anio,
                Ciclo = string.IsNullOrWhiteSpace(ciclo) ? $"Ciclo {anio}" : ciclo,
                Computo = computo,
                Descripcion = LimpiarOpcional(grupoDto.Descripcion, 400),
                CreadoEn = DateTime.UtcNow
            };

            db.Grupos.Add(grupo);
            await db.SaveChangesAsync(cancellationToken);

            var mapaEstudiantes = new Dictionary<int, Estudiante>();
            var carnets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var estudianteDto in grupoDto.Estudiantes)
            {
                var carnet = Limpiar(estudianteDto.Carnet, 40);
                var nombreEstudiante = Limpiar(estudianteDto.Nombre, 160);
                if (string.IsNullOrWhiteSpace(carnet) || string.IsNullOrWhiteSpace(nombreEstudiante))
                {
                    avance.Advertencias.Add($"Estudiante omitido por datos incompletos (carnet: '{estudianteDto.Carnet}').");
                    continue;
                }

                if (!carnets.Add(carnet))
                {
                    avance.EstudiantesOmitidos++;
                    avance.Advertencias.Add($"Carnet duplicado en el respaldo: {carnet}.");
                    continue;
                }

                var estudiante = new Estudiante
                {
                    Grupo = grupo,
                    Carnet = carnet,
                    Nombre = nombreEstudiante,
                    Correo = LimpiarOpcional(estudianteDto.Correo, 180),
                    CreadoEn = DateTime.UtcNow
                };

                db.Estudiantes.Add(estudiante);
                await db.SaveChangesAsync(cancellationToken);
                mapaEstudiantes[estudianteDto.IdOriginal] = estudiante;
                avance.EstudiantesImportados++;
            }

            var mapaEvaluaciones = new Dictionary<int, Evaluacion>();
            foreach (var evaluacionDto in grupoDto.Evaluaciones)
            {
                if (!mapaEstudiantes.TryGetValue(evaluacionDto.EstudianteIdOriginal, out var estudiante))
                {
                    avance.Advertencias.Add($"Evaluación omitida: no se encontró el estudiante original {evaluacionDto.EstudianteIdOriginal}.");
                    continue;
                }

                var codigoFuente = evaluacionDto.CodigoFuente ?? string.Empty;
                if (codigoFuente.Length > 20 * 1024 * 1024)
                {
                    avance.Advertencias.Add($"Evaluación omitida por código demasiado grande: {evaluacionDto.NombreArchivo}.");
                    continue;
                }

                var hash = string.IsNullOrWhiteSpace(evaluacionDto.HashSha256)
                    ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(codigoFuente)))
                    : Limpiar(evaluacionDto.HashSha256, 64);

                var evaluacion = new Evaluacion
                {
                    Grupo = grupo,
                    Estudiante = estudiante,
                    NombreArchivo = Limpiar(evaluacionDto.NombreArchivo, 260) is { Length: > 0 } archivo ? archivo : "entrega.cs",
                    NombreAlmacenado = Limpiar(evaluacionDto.NombreAlmacenado, 120) is { Length: > 0 } almacenado ? almacenado : "importado.cs",
                    Extension = Limpiar(evaluacionDto.Extension, 20) is { Length: > 0 } extension ? extension : ".cs",
                    TipoContenido = LimpiarOpcional(evaluacionDto.TipoContenido, 120),
                    TamanoBytes = evaluacionDto.TamanoBytes,
                    HashSha256 = hash,
                    CodigoFuente = codigoFuente,
                    OrigenArchivo = Limpiar(evaluacionDto.OrigenArchivo, 40) is { Length: > 0 } origen ? origen : "Código",
                    ConfianzaOcr = Math.Clamp(evaluacionDto.ConfianzaOcr, 0, 100),
                    LineasCodigo = Math.Max(0, evaluacionDto.LineasCodigo),
                    Metodos = Math.Max(0, evaluacionDto.Metodos),
                    Bucles = Math.Max(0, evaluacionDto.Bucles),
                    Condicionales = Math.Max(0, evaluacionDto.Condicionales),
                    Similitud = Math.Clamp(evaluacionDto.Similitud, 0, 100),
                    ProbabilidadIA = Math.Clamp(evaluacionDto.ProbabilidadIA, 0, 100),
                    NivelRiesgo = Limpiar(evaluacionDto.NivelRiesgo, 30) is { Length: > 0 } nivel ? nivel : "Bajo",
                    FechaAnalisis = NormalizarFecha(evaluacionDto.FechaAnalisis),
                    SimilitudReferencia = Math.Clamp(evaluacionDto.SimilitudReferencia, 0, 100),
                    MetadatosJson = LimitarOpcional(evaluacionDto.MetadatosJson, 2 * 1024 * 1024),
                    AnalisisIaJson = LimitarOpcional(evaluacionDto.AnalisisIaJson, 2 * 1024 * 1024),
                    AnalisisSimilitudJson = LimitarOpcional(evaluacionDto.AnalisisSimilitudJson, 4 * 1024 * 1024),
                    Observaciones = LimpiarOpcional(evaluacionDto.Observaciones, 1200)
                };

                db.Evaluaciones.Add(evaluacion);
                await db.SaveChangesAsync(cancellationToken);
                mapaEvaluaciones[evaluacionDto.IdOriginal] = evaluacion;
                avance.EvaluacionesImportadas++;
            }

            // Segunda pasada: referencias entre evaluaciones ya con identificadores nuevos.
            foreach (var evaluacionDto in grupoDto.Evaluaciones)
            {
                if (evaluacionDto.ReferenciaEvaluacionIdOriginal is not int referenciaOriginal ||
                    !mapaEvaluaciones.TryGetValue(evaluacionDto.IdOriginal, out var evaluacion) ||
                    !mapaEvaluaciones.TryGetValue(referenciaOriginal, out var referencia))
                {
                    continue;
                }

                evaluacion.ReferenciaEvaluacionId = referencia.Id;
            }

            foreach (var historialDto in grupoDto.Historiales)
            {
                if (!mapaEvaluaciones.TryGetValue(historialDto.EvaluacionIdOriginal, out var evaluacion) ||
                    !mapaEstudiantes.TryGetValue(historialDto.EstudianteIdOriginal, out var estudiante))
                {
                    avance.Advertencias.Add("Se omitió un historial sin evaluación/estudiante asociado.");
                    continue;
                }

                var historial = new HistorialAnalisis
                {
                    Evaluacion = evaluacion,
                    Estudiante = estudiante,
                    Grupo = grupo,
                    Computo = Math.Clamp(historialDto.Computo, 1, 3),
                    Anio = Math.Clamp(historialDto.Anio, 2000, 2100),
                    Ciclo = Limpiar(historialDto.Ciclo, 30),
                    Similitud = Math.Clamp(historialDto.Similitud, 0, 100),
                    ProbabilidadIA = Math.Clamp(historialDto.ProbabilidadIA, 0, 100),
                    Origen = Limpiar(historialDto.Origen, 40) is { Length: > 0 } origen ? origen : "Importado",
                    Resumen = LimpiarOpcional(historialDto.Resumen, 600),
                    ReferenciaEvaluacionId = historialDto.ReferenciaEvaluacionIdOriginal is int refHistorial &&
                                              mapaEvaluaciones.ContainsKey(refHistorial)
                        ? mapaEvaluaciones[refHistorial].Id
                        : null,
                    Fecha = NormalizarFecha(historialDto.Fecha)
                };

                db.Historiales.Add(historial);
                avance.HistorialesImportados++;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaccion.CommitAsync(cancellationToken);

            avance.GrupoId = grupo.Id;
            avance.GrupoNombre = grupo.Nombre + " · " + grupo.Codigo;
            _logger.LogInformation(
                "Respaldo importado: grupo {GrupoId}, {Estudiantes} estudiantes, {Evaluaciones} evaluaciones, {Historiales} historiales",
                grupo.Id, avance.EstudiantesImportados, avance.EvaluacionesImportadas, avance.HistorialesImportados);

            return avance;
        }
        catch
        {
            await transaccion.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<byte[]> LeerLimitadoAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        using var memoria = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int leidos;
        while ((leidos = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += leidos;
            if (total > maxBytes)
            {
                throw new InvalidDataException("El manifiesto excede el tamaño máximo permitido.");
            }

            memoria.Write(buffer, 0, leidos);
        }

        return memoria.ToArray();
    }

    private static string Limpiar(string? valor, int max)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return string.Empty;
        }

        var limpio = new string(valor.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return limpio.Length <= max ? limpio : limpio[..max];
    }

    private static string? LimpiarOpcional(string? valor, int max)
    {
        var limpio = Limpiar(valor, max);
        return string.IsNullOrWhiteSpace(limpio) ? null : limpio;
    }

    private static string? LimitarOpcional(string? valor, int max)
        => string.IsNullOrEmpty(valor) || valor.Length <= max ? valor : null;

    private static DateTime NormalizarFecha(DateTime fecha)
    {
        if (fecha == default)
        {
            return DateTime.UtcNow;
        }

        return fecha.Kind == DateTimeKind.Utc ? fecha : fecha.ToUniversalTime();
    }

    private static string LimpiarNombre(string valor)
    {
        var limpio = new string((valor ?? "grupo").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(limpio) ? "grupo" : limpio[..Math.Min(limpio.Length, 30)];
    }
}
