using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using web_analizador_de_plagios.Data;
using web_analizador_de_plagios.Data.Models;

namespace web_analizador_de_plagios.Services;

/// <summary>
/// Orquesta una ejecución completa: almacenamiento seguro, OCR, metadatos,
/// normalización, comparación, detección de IA y persistencia transaccional.
/// </summary>
public sealed class ServicioAnalisis
{
    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AlmacenArchivos _almacen;
    private readonly InspectorMetadatos _inspector;
    private readonly NormalizadorCodigo _normalizador;
    private readonly AnalizadorEstructura _estructura;
    private readonly AnalizadorSimilitud _similitud;
    private readonly DetectorIAPatrones _detectorIa;
    private readonly ILectorOCR _ocr;
    private readonly ILogger<ServicioAnalisis> _logger;

    public ServicioAnalisis(
        IDbContextFactory<AppDbContext> dbFactory,
        AlmacenArchivos almacen,
        InspectorMetadatos inspector,
        NormalizadorCodigo normalizador,
        AnalizadorEstructura estructura,
        AnalizadorSimilitud similitud,
        DetectorIAPatrones detectorIa,
        ILectorOCR ocr,
        ILogger<ServicioAnalisis> logger)
    {
        _dbFactory = dbFactory;
        _almacen = almacen;
        _inspector = inspector;
        _normalizador = normalizador;
        _estructura = estructura;
        _similitud = similitud;
        _detectorIa = detectorIa;
        _ocr = ocr;
        _logger = logger;
    }

    public async Task<ResultadoAnalisisGuardado> AnalizarAsync(SolicitudAnalisis solicitud, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        if (solicitud.ArchivoA is null || solicitud.ArchivoA.Bytes.Length == 0)
        {
            throw new InvalidOperationException("Debe cargar al menos un archivo para analizar.");
        }

        if (solicitud.ArchivoA.EstudianteId is not int estudianteAId)
        {
            throw new InvalidOperationException("Seleccione el estudiante de la entrega A.");
        }

        var comparacionDirecta = solicitud.ArchivoB is not null && solicitud.ArchivoB.Bytes.Length > 0;
        if (comparacionDirecta && solicitud.ArchivoB!.EstudianteId is not int estudianteBId)
        {
            throw new InvalidOperationException("Seleccione el estudiante de la entrega B.");
        }

        if (comparacionDirecta && estudianteAId == solicitud.ArchivoB!.EstudianteId)
        {
            throw new InvalidOperationException("Las dos entregas deben pertenecer a estudiantes distintos.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var grupo = await db.Grupos.AsNoTracking().FirstOrDefaultAsync(g => g.Id == solicitud.GrupoId, cancellationToken)
                    ?? throw new InvalidOperationException("El grupo seleccionado no existe.");

        var estudiantes = await db.Estudiantes
            .AsNoTracking()
            .Where(e => e.GrupoId == grupo.Id)
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        if (!estudiantes.TryGetValue(estudianteAId, out var estudianteA))
        {
            throw new InvalidOperationException("El estudiante de la entrega A no pertenece al grupo seleccionado.");
        }

        Estudiante? estudianteB = null;
        if (comparacionDirecta && !estudiantes.TryGetValue(solicitud.ArchivoB!.EstudianteId!.Value, out estudianteB))
        {
            throw new InvalidOperationException("El estudiante de la entrega B no pertenece al grupo seleccionado.");
        }

        var archivosGuardados = new List<ArchivoGuardado>();
        var resultado = new ResultadoAnalisisGuardado();
        var advertencias = new List<string>();
        var transaccionConfirmada = false;

        try
        {
            var guardadoA = await _almacen.GuardarAsync(solicitud.ArchivoA, cancellationToken);
            archivosGuardados.Add(guardadoA);
            var procesadoA = await ProcesarArchivoAsync(solicitud.ArchivoA, guardadoA, cancellationToken, advertencias);

            ProcesadoArchivo? procesadoB = null;
            ArchivoGuardado? guardadoB = null;
            if (comparacionDirecta)
            {
                guardadoB = await _almacen.GuardarAsync(solicitud.ArchivoB!, cancellationToken);
                archivosGuardados.Add(guardadoB);
                procesadoB = await ProcesarArchivoAsync(solicitud.ArchivoB!, guardadoB, cancellationToken, advertencias);
            }

            if (procesadoA.Codigo.Trim().Length == 0)
            {
                throw new InvalidOperationException("La entrega A no contiene código reconocible.");
            }

            if (procesadoB is not null && procesadoB.Codigo.Trim().Length == 0)
            {
                throw new InvalidOperationException("La entrega B no contiene código reconocible.");
            }

            ResumenSimilitud resumenA;
            ResumenSimilitud? resumenB = null;
            ResultadoSimilitud? diffCompleto = null;
            int? referenciaId = null;
            string? referenciaEstudiante = null;
            string? referenciaArchivo = null;

            if (procesadoB is not null)
            {
                diffCompleto = _similitud.Comparar(procesadoA.Codigo, procesadoB.Codigo, incluirFilas: true);
                resumenA = _similitud.Resumir(diffCompleto, null, estudianteB?.Nombre, procesadoB.NombreArchivo);
                resumenB = _similitud.Resumir(diffCompleto, null, estudianteA.Nombre, procesadoA.NombreArchivo);

                resumenA.MejoresCoincidencias.Add(CrearCoincidencia(0, estudianteB!, procesadoB.NombreArchivo, diffCompleto.Porcentaje));
                resumenB.MejoresCoincidencias.Add(CrearCoincidencia(0, estudianteA, procesadoA.NombreArchivo, diffCompleto.Porcentaje));
                referenciaEstudiante = estudianteB?.Nombre;
                referenciaArchivo = procesadoB.NombreArchivo;
            }
            else
            {
                var referencia = await BuscarMejorCoincidenciaAsync(db, grupo.Id, estudianteAId, procesadoA, cancellationToken);
                diffCompleto = referencia.Diff;
                resumenA = referencia.Resumen;

                if (referencia.Mejor is not null)
                {
                    referenciaId = referencia.Mejor.EvaluacionId;
                    referenciaEstudiante = referencia.Mejor.EstudianteNombre;
                    referenciaArchivo = referencia.Mejor.Archivo;
                    resumenA.ReferenciaEvaluacionId = referenciaId;
                    resumenA.ReferenciaEstudiante = referenciaEstudiante;
                    resumenA.ReferenciaArchivo = referenciaArchivo;
                }
            }

            var porcentaje = diffCompleto?.Porcentaje ?? 0;
            var nivel = AnalizadorSimilitud.ClasificarRiesgo(porcentaje);

            var duplicadoA = await db.Evaluaciones.AsNoTracking().AnyAsync(
                e => e.GrupoId == grupo.Id && e.EstudianteId == estudianteAId && e.HashSha256 == procesadoA.Metadatos.HashSha256,
                cancellationToken);
            if (duplicadoA)
            {
                advertencias.Add("Ya existe una entrega idéntica registrada para este estudiante en el grupo. Se conserva como nueva ejecución del análisis.");
            }

            var evalA = CrearEvaluacion(grupo, estudianteA, procesadoA, porcentaje, nivel);
            evalA.ReferenciaEvaluacionId = referenciaId;
            evalA.SimilitudReferencia = porcentaje;
            evalA.AnalisisSimilitudJson = JsonSerializer.Serialize(resumenA, JsonOpciones);
            db.Evaluaciones.Add(evalA);

            Evaluacion? evalB = null;
            if (procesadoB is not null && estudianteB is not null)
            {
                evalB = CrearEvaluacion(grupo, estudianteB, procesadoB, porcentaje, nivel);
                evalB.SimilitudReferencia = porcentaje;
                db.Evaluaciones.Add(evalB);
            }

            await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            if (evalB is not null)
            {
                evalA.ReferenciaEvaluacionId = evalB.Id;
                resumenA.ReferenciaEvaluacionId = evalB.Id;
                resumenA.ReferenciaEstudiante = estudianteB?.Nombre;
                resumenA.ReferenciaArchivo = procesadoB?.NombreArchivo;
                AjustarCoincidencia(resumenA, evalB.Id, estudianteB!, procesadoB!.NombreArchivo);
                evalA.AnalisisSimilitudJson = JsonSerializer.Serialize(resumenA, JsonOpciones);

                resumenB!.ReferenciaEvaluacionId = evalA.Id;
                resumenB.ReferenciaEstudiante = estudianteA.Nombre;
                resumenB.ReferenciaArchivo = procesadoA.NombreArchivo;
                AjustarCoincidencia(resumenB, evalA.Id, estudianteA, procesadoA.NombreArchivo);
                evalB.AnalisisSimilitudJson = JsonSerializer.Serialize(resumenB, JsonOpciones);
                evalB.SimilitudReferencia = porcentaje;

                await db.SaveChangesAsync(cancellationToken);
            }

            if (solicitud.GuardarEnHistorial)
            {
                db.Historiales.Add(CrearHistorial(grupo, estudianteA, evalA, porcentaje, procesadoA.AnalisisIa.Probabilidad,
                    procesadoB is null ? "Análisis individual" : "Comparación directa",
                    ConstruirResumenHistorial(procesadoA.AnalisisIa, porcentaje, referenciaEstudiante, referenciaArchivo)));

                if (evalB is not null && estudianteB is not null && procesadoB is not null)
                {
                    db.Historiales.Add(CrearHistorial(grupo, estudianteB, evalB, porcentaje, procesadoB.AnalisisIa.Probabilidad,
                        "Comparación directa (par)",
                        $"Par comparado con {estudianteA.Nombre} · similitud {porcentaje:F1}% · IA {procesadoB.AnalisisIa.Probabilidad:F1}%"));
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            await transaccion.CommitAsync(cancellationToken);
            transaccionConfirmada = true;

            resultado.EvaluacionId = evalA.Id;
            resultado.SegundaEvaluacionId = evalB?.Id;
            resultado.Similitud = porcentaje;
            resultado.ProbabilidadIA = procesadoA.AnalisisIa.Probabilidad;
            resultado.NivelRiesgo = nivel;
            resultado.ComparacionDirecta = evalB is not null;
            resultado.Mensaje = evalB is not null
                ? "Comparación directa completada y guardada."
                : referenciaEstudiante is null
                    ? "Análisis guardado. No había otras entregas comparables en el grupo."
                    : $"Análisis guardado. Mejor coincidencia: {referenciaEstudiante} ({porcentaje:F1}%).";

            if (diffCompleto?.Truncado == true)
            {
                advertencias.Add("El archivo es muy grande; el diff y la similitud se calcularon sobre las primeras 1.800 líneas / 7.000 tokens.");
            }

            if (!string.IsNullOrWhiteSpace(procesadoA.Estructura.ErrorSintaxis))
            {
                advertencias.Add("El archivo presenta errores de sintaxis; la estructura se analizó de forma parcial.");
            }

            resultado.Advertencias = advertencias;
            return resultado;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en el análisis del grupo {GrupoId}", solicitud.GrupoId);
            if (!transaccionConfirmada)
            {
                foreach (var archivo in archivosGuardados)
                {
                    await _almacen.EliminarAsync(archivo.RutaRelativa);
                }
            }

            throw;
        }
    }

    private static void AjustarCoincidencia(ResumenSimilitud resumen, int evaluacionId, Estudiante estudiante, string archivo)
    {
        if (resumen.MejoresCoincidencias.Count == 0)
        {
            resumen.MejoresCoincidencias.Add(CrearCoincidencia(evaluacionId, estudiante, archivo, resumen.Porcentaje));
        }
        else
        {
            resumen.MejoresCoincidencias[0].EvaluacionId = evaluacionId;
            resumen.MejoresCoincidencias[0].EstudianteId = estudiante.Id;
            resumen.MejoresCoincidencias[0].EstudianteNombre = estudiante.Nombre;
            resumen.MejoresCoincidencias[0].EstudianteCarnet = estudiante.Carnet;
            resumen.MejoresCoincidencias[0].Archivo = archivo;
            resumen.MejoresCoincidencias[0].Porcentaje = resumen.Porcentaje;
        }
    }

    private async Task<ProcesadoArchivo> ProcesarArchivoAsync(
        ArchivoEntrada archivo,
        ArchivoGuardado guardado,
        CancellationToken cancellationToken,
        List<string> advertencias)
    {
        string codigo;
        string origen;
        string codificacion;
        double confianzaOcr = 0;

        if (AlmacenArchivos.EsImagen(guardado.NombreOriginal))
        {
            var ocr = await _ocr.LeerAsync(archivo.Bytes, guardado.NombreOriginal, cancellationToken);
            if (!ocr.Exito)
            {
                throw new InvalidOperationException($"No se pudo extraer texto de la imagen «{guardado.NombreOriginal}»: {ocr.Mensaje}");
            }

            codigo = ocr.Texto.Replace("\f", "\n").Replace("\0", string.Empty).Trim();
            origen = "Imagen OCR";
            codificacion = $"OCR Tesseract ({ocr.Confianza:F1}%)";
            confianzaOcr = ocr.Confianza;
            advertencias.Add($"«{guardado.NombreOriginal}» se procesó por OCR ({ocr.Confianza:F1}% de confianza). Verifique el texto reconocido.");
        }
        else
        {
            var decodificacion = DecodificadorTexto.Decodificar(archivo.Bytes);
            var esUtf16 = decodificacion.Codificacion.StartsWith("UTF-16", StringComparison.OrdinalIgnoreCase) ||
                          decodificacion.Codificacion.StartsWith("UTF-32", StringComparison.OrdinalIgnoreCase);
            if (!esUtf16 && DecodificadorTexto.PareceBinario(archivo.Bytes))
            {
                throw new InvalidOperationException($"El archivo «{guardado.NombreOriginal}» parece binario y no es una imagen compatible.");
            }

            codigo = decodificacion.Texto.Replace("\0", string.Empty).Trim();
            origen = guardado.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ? "Código C#" : "Texto";
            codificacion = decodificacion.Codificacion;
            if (!string.IsNullOrWhiteSpace(decodificacion.Advertencia))
            {
                advertencias.Add($"{guardado.NombreOriginal}: {decodificacion.Advertencia}");
            }
        }

        if (string.IsNullOrWhiteSpace(codigo))
        {
            throw new InvalidOperationException($"El archivo «{guardado.NombreOriginal}» no contiene texto analizable.");
        }

        var normalizacion = _normalizador.Normalizar(codigo);
        var estructura = _estructura.Analizar(codigo, normalizacion);
        var analisisIa = _detectorIa.Detectar(codigo, estructura);

        var metadatos = await _inspector.InspeccionarArchivoAsync(
            guardado.RutaCompleta,
            guardado.NombreOriginal,
            guardado.TipoContenido,
            archivo.UltimaModificacion,
            origen,
            confianzaOcr > 0 ? confianzaOcr : null,
            guardado.RutaRelativa,
            cancellationToken);

        metadatos.Codificacion = codificacion;

        if (codigo.Length > 0 && metadatos.HashSha256.Length == 0)
        {
            metadatos.HashSha256 = NormalizadorCodigo.CalcularHash(codigo);
        }

        return new ProcesadoArchivo
        {
            NombreArchivo = guardado.NombreOriginal,
            RutaRelativa = guardado.RutaRelativa,
            Metadatos = metadatos,
            Codigo = codigo,
            Origen = origen,
            ConfianzaOcr = confianzaOcr,
            Normalizacion = normalizacion,
            Estructura = estructura,
            AnalisisIa = analisisIa
        };
    }

    private async Task<ReferenciaEncontrada> BuscarMejorCoincidenciaAsync(
        AppDbContext db,
        int grupoId,
        int estudianteId,
        ProcesadoArchivo procesado,
        CancellationToken cancellationToken)
    {
        var candidatos = await db.Evaluaciones
            .AsNoTracking()
            .Where(e => e.GrupoId == grupoId && e.EstudianteId != estudianteId && e.CodigoFuente != "")
            .OrderByDescending(e => e.FechaAnalisis)
            .Take(60)
            .Select(e => new
            {
                e.Id,
                e.EstudianteId,
                e.NombreArchivo,
                e.CodigoFuente,
                e.HashSha256,
                e.Similitud,
                EstudianteNombre = e.Estudiante != null ? e.Estudiante.Nombre : "Estudiante",
                EstudianteCarnet = e.Estudiante != null ? e.Estudiante.Carnet : string.Empty
            })
            .ToListAsync(cancellationToken);

        if (candidatos.Count == 0)
        {
            return new ReferenciaEncontrada
            {
                Diff = _similitud.Comparar(procesado.Codigo, string.Empty, incluirFilas: true),
                Resumen = new ResumenSimilitud
                {
                    Porcentaje = 0,
                    Metodologia = "Sin entregas comparables en el grupo."
                }
            };
        }

        var coincidencias = new List<CoincidenciaSimilitud>();
        foreach (var candidato in candidatos)
        {
            double porcentaje;
            if (!string.IsNullOrWhiteSpace(candidato.HashSha256) &&
                string.Equals(candidato.HashSha256, procesado.Metadatos.HashSha256, StringComparison.OrdinalIgnoreCase))
            {
                porcentaje = 100;
            }
            else
            {
                var parcial = _similitud.Comparar(procesado.Codigo, candidato.CodigoFuente, incluirFilas: false);
                porcentaje = parcial.Porcentaje;
            }

            coincidencias.Add(new CoincidenciaSimilitud
            {
                EvaluacionId = candidato.Id,
                EstudianteId = candidato.EstudianteId,
                EstudianteNombre = candidato.EstudianteNombre,
                EstudianteCarnet = candidato.EstudianteCarnet,
                Archivo = candidato.NombreArchivo,
                Porcentaje = Math.Round(porcentaje, 2)
            });
        }

        var ordenadas = coincidencias.OrderByDescending(c => c.Porcentaje).ToList();
        var mejor = ordenadas.FirstOrDefault();
        var resumen = new ResumenSimilitud
        {
            Porcentaje = mejor?.Porcentaje ?? 0,
            MejoresCoincidencias = ordenadas.Take(5).ToList(),
            Metodologia = "Levenshtein de tokens 55% + Jaccard de shingles 25% + Levenshtein de líneas 20%"
        };

        ResultadoSimilitud diff;
        if (mejor is null)
        {
            diff = _similitud.Comparar(procesado.Codigo, string.Empty, incluirFilas: true);
        }
        else
        {
            var referencia = candidatos.First(c => c.Id == mejor.EvaluacionId);
            diff = _similitud.Comparar(procesado.Codigo, referencia.CodigoFuente, incluirFilas: true);
            resumen.DistanciaTokens = diff.DistanciaTokens;
            resumen.SimilitudTokens = diff.SimilitudTokens;
            resumen.SimilitudLineas = diff.SimilitudLineas;
            resumen.JaccardShingles = diff.JaccardShingles;
            resumen.LineasIguales = diff.LineasIguales;
            resumen.LineasModificadas = diff.LineasModificadas;
            resumen.LineasSoloIzquierda = diff.LineasSoloIzquierda;
            resumen.LineasSoloDerecha = diff.LineasSoloDerecha;
            resumen.Truncado = diff.Truncado;
            resumen.ReferenciaEvaluacionId = mejor.EvaluacionId;
            resumen.ReferenciaEstudiante = mejor.EstudianteNombre;
            resumen.ReferenciaArchivo = mejor.Archivo;
            // La comparación completa puede diferir ligeramente de la estimación parcial.
            if (Math.Abs(diff.Porcentaje - mejor.Porcentaje) > 0.01)
            {
                mejor.Porcentaje = diff.Porcentaje;
                resumen.Porcentaje = diff.Porcentaje;
            }
        }

        return new ReferenciaEncontrada { Diff = diff, Resumen = resumen, Mejor = mejor };
    }

    private static Evaluacion CrearEvaluacion(Grupo grupo, Estudiante estudiante, ProcesadoArchivo procesado,
        double similitud, string nivel)
    {
        return new Evaluacion
        {
            GrupoId = grupo.Id,
            EstudianteId = estudiante.Id,
            NombreArchivo = procesado.NombreArchivo,
            NombreAlmacenado = procesado.RutaRelativa,
            Extension = Path.GetExtension(procesado.NombreArchivo),
            TipoContenido = procesado.Metadatos.TipoContenido,
            TamanoBytes = procesado.Metadatos.TamanoBytes,
            HashSha256 = procesado.Metadatos.HashSha256,
            CodigoFuente = procesado.Codigo,
            OrigenArchivo = procesado.Origen,
            ConfianzaOcr = procesado.ConfianzaOcr,
            LineasCodigo = procesado.Estructura.Metricas.LineasCodigo,
            Metodos = procesado.Estructura.Metricas.Metodos,
            Bucles = procesado.Estructura.Metricas.Bucles,
            Condicionales = procesado.Estructura.Metricas.Condicionales,
            Similitud = similitud,
            ProbabilidadIA = procesado.AnalisisIa.Probabilidad,
            NivelRiesgo = nivel,
            FechaAnalisis = DateTime.UtcNow,
            MetadatosJson = JsonSerializer.Serialize(procesado.Metadatos, JsonOpciones),
            AnalisisIaJson = JsonSerializer.Serialize(procesado.AnalisisIa, JsonOpciones)
        };
    }

    private static HistorialAnalisis CrearHistorial(Grupo grupo, Estudiante estudiante, Evaluacion evaluacion,
        double similitud, double probabilidadIa, string origen, string resumen)
    {
        return new HistorialAnalisis
        {
            EvaluacionId = evaluacion.Id,
            EstudianteId = estudiante.Id,
            GrupoId = grupo.Id,
            Computo = grupo.Computo,
            Anio = grupo.Anio,
            Ciclo = grupo.Ciclo,
            Similitud = similitud,
            ProbabilidadIA = probabilidadIa,
            Origen = origen,
            Resumen = resumen.Length > 600 ? resumen[..600] : resumen,
            ReferenciaEvaluacionId = evaluacion.ReferenciaEvaluacionId,
            Fecha = DateTime.UtcNow
        };
    }

    private static CoincidenciaSimilitud CrearCoincidencia(int evaluacionId, Estudiante estudiante, string archivo, double porcentaje)
        => new()
        {
            EvaluacionId = evaluacionId,
            EstudianteId = estudiante.Id,
            EstudianteNombre = estudiante.Nombre,
            EstudianteCarnet = estudiante.Carnet,
            Archivo = archivo,
            Porcentaje = Math.Round(porcentaje, 2)
        };

    private static string ConstruirResumenHistorial(ResultadoDeteccionIA ia, double similitud, string? referenciaEstudiante, string? referenciaArchivo)
    {
        var referencia = referenciaEstudiante is null
            ? "sin coincidencia relevante"
            : $"con {referenciaEstudiante}{(referenciaArchivo is null ? string.Empty : $" ({referenciaArchivo})")}";
        return $"Similitud {similitud:F1}% {referencia} · IA {ia.Probabilidad:F1}% ({ia.Nivel})";
    }

    private sealed class ProcesadoArchivo
    {
        public string NombreArchivo { get; set; } = string.Empty;
        public string RutaRelativa { get; set; } = string.Empty;
        public MetadatosArchivo Metadatos { get; set; } = new();
        public string Codigo { get; set; } = string.Empty;
        public string Origen { get; set; } = "Código C#";
        public double ConfianzaOcr { get; set; }
        public ResultadoNormalizacion Normalizacion { get; set; } = null!;
        public EstructuraCodigo Estructura { get; set; } = new();
        public ResultadoDeteccionIA AnalisisIa { get; set; } = new();
    }

    private sealed class ReferenciaEncontrada
    {
        public ResultadoSimilitud Diff { get; set; } = new();
        public ResumenSimilitud Resumen { get; set; } = new();
        public CoincidenciaSimilitud? Mejor { get; set; }
    }
}
