using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using web_analizador_de_plagios.Data;
using web_analizador_de_plagios.Data.Models;
using web_analizador_de_plagios.Services;

namespace web_analizador_de_plagios.Endpoints;

public static class DescargasEndpoints
{
    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);

    public static void MapDescargasEndpoints(this WebApplication app)
    {
        app.MapGet("/api/respaldos/grupo/{id:int}", async (
            int id,
            GestorRespaldos respaldos,
            ILoggerFactory loggerFactory) =>
        {
            try
            {
                var archivo = await respaldos.ExportarGrupoAsync(id);
                return Results.File(archivo.Contenido, archivo.TipoContenido, archivo.Nombre);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("Descargas").LogError(ex, "Error exportando respaldo del grupo {GrupoId}", id);
                return Results.Problem("No se pudo generar el respaldo del grupo.");
            }
        }).WithName("DescargarRespaldoGrupo");

        app.MapGet("/api/evaluaciones/{id:int}/quiz", async (
            int id,
            string? formato,
            IDbContextFactory<AppDbContext> dbFactory,
            GeneradorQuizDefensa generador,
            ILoggerFactory loggerFactory) =>
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var evaluacion = await db.Evaluaciones
                    .AsNoTracking()
                    .Include(e => e.Estudiante)
                    .Include(e => e.Grupo)
                    .Include(e => e.ReferenciaEvaluacion)
                        .ThenInclude(r => r!.Estudiante)
                    .FirstOrDefaultAsync(e => e.Id == id);

                if (evaluacion is null)
                {
                    return Results.NotFound(new { error = "La evaluación no existe." });
                }

                ResultadoDeteccionIA? analisisIa = null;
                if (!string.IsNullOrWhiteSpace(evaluacion.AnalisisIaJson))
                {
                    try
                    {
                        analisisIa = JsonSerializer.Deserialize<ResultadoDeteccionIA>(evaluacion.AnalisisIaJson, JsonOpciones);
                    }
                    catch (JsonException)
                    {
                        analisisIa = null;
                    }
                }

                var ficha = generador.Generar(
                    evaluacion.CodigoFuente,
                    evaluacion.NombreArchivo,
                    evaluacion.Estudiante?.Nombre,
                    evaluacion.Grupo is null ? null : $"{evaluacion.Grupo.Nombre} ({evaluacion.Grupo.Codigo}) · {evaluacion.Grupo.Ciclo} {evaluacion.Grupo.Anio} · C{evaluacion.Grupo.Computo}",
                    evaluacion.Similitud,
                    evaluacion.ProbabilidadIA,
                    analisisIa,
                    evaluacion.ReferenciaEvaluacion?.Estudiante?.Nombre,
                    evaluacion.ReferenciaEvaluacion?.NombreArchivo);

                var baseNombre = $"defensa_{(evaluacion.Estudiante?.Carnet ?? "estudiante")}_{evaluacion.Id}";
                baseNombre = string.Concat(baseNombre.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

                if (string.Equals(formato, "html", StringComparison.OrdinalIgnoreCase))
                {
                    var bytesHtml = Encoding.UTF8.GetBytes(ficha.Html);
                    return Results.File(bytesHtml, "text/html; charset=utf-8", baseNombre + ".html");
                }

                var bytes = Encoding.UTF8.GetBytes(ficha.TextoPlano);
                return Results.File(bytes, "text/plain; charset=utf-8", baseNombre + ".txt");
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("Descargas").LogError(ex, "Error generando quiz de evaluación {EvaluacionId}", id);
                return Results.Problem("No se pudo generar la ficha de defensa.");
            }
        }).WithName("DescargarQuizDefensa");

        app.MapGet("/api/plantillas/estudiantes.csv", () =>
        {
            const string contenido = "carnet,nombre,correo\r\n20240001,Ana Pérez López,ana.perez@ejemplo.com\r\n20240002,Carlos Ramírez,carlos.ramirez@ejemplo.com\r\n";
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(contenido)).ToArray();
            return Results.File(bytes, "text/csv; charset=utf-8", "plantilla_estudiantes.csv");
        }).WithName("DescargarPlantillaEstudiantes");

        app.MapGet("/api/resultados/exportar.csv", async (
            int? grupoId,
            int? computo,
            IDbContextFactory<AppDbContext> dbFactory,
            ILoggerFactory loggerFactory) =>
        {
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var consulta = db.Evaluaciones
                    .AsNoTracking()
                    .AsQueryable();

                if (grupoId is int gid)
                {
                    consulta = consulta.Where(e => e.GrupoId == gid);
                }

                if (computo is int c && c is >= 1 and <= 3)
                {
                    consulta = consulta.Where(e => e.Grupo != null && e.Grupo.Computo == c);
                }

                var filas = await consulta
                    .OrderByDescending(e => e.FechaAnalisis)
                    .Select(e => new
                    {
                        Estudiante = e.Estudiante != null ? e.Estudiante.Nombre : "Sin estudiante",
                        Carnet = e.Estudiante != null ? e.Estudiante.Carnet : string.Empty,
                        Grupo = e.Grupo != null ? e.Grupo.Nombre : string.Empty,
                        CodigoGrupo = e.Grupo != null ? e.Grupo.Codigo : string.Empty,
                        Ciclo = e.Grupo != null ? e.Grupo.Ciclo : string.Empty,
                        Anio = e.Grupo != null ? e.Grupo.Anio : 0,
                        Computo = e.Grupo != null ? e.Grupo.Computo : 0,
                        e.NombreArchivo,
                        e.OrigenArchivo,
                        e.Similitud,
                        e.ProbabilidadIA,
                        e.NivelRiesgo,
                        e.FechaAnalisis
                    })
                    .ToListAsync();

                var sb = new StringBuilder();
                sb.AppendLine("Estudiante,Carnet,Grupo,CodigoGrupo,Ciclo,Anio,Computo,Archivo,Origen,Similitud,ProbabilidadIA,NivelRiesgo,FechaAnalisis");
                foreach (var fila in filas)
                {
                    sb.AppendLine(string.Join(',',
                        Csv(fila.Estudiante),
                        Csv(fila.Carnet),
                        Csv(fila.Grupo),
                        Csv(fila.CodigoGrupo),
                        Csv(fila.Ciclo),
                        fila.Anio.ToString(CultureInfo.InvariantCulture),
                        fila.Computo.ToString(CultureInfo.InvariantCulture),
                        Csv(fila.NombreArchivo),
                        Csv(fila.OrigenArchivo),
                        fila.Similitud.ToString("F2", CultureInfo.InvariantCulture),
                        fila.ProbabilidadIA.ToString("F2", CultureInfo.InvariantCulture),
                        Csv(fila.NivelRiesgo),
                        Csv(fila.FechaAnalisis.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))));
                }

                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
                return Results.File(bytes, "text/csv; charset=utf-8", $"resultados_{DateTime.Now:yyyyMMdd_HHmm}.csv");
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("Descargas").LogError(ex, "Error exportando resultados CSV");
                return Results.Problem("No se pudo exportar el CSV de resultados.");
            }
        }).WithName("ExportarResultadosCsv");
    }

    private static string Csv(string? valor)
    {
        var limpio = (valor ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        if (limpio.Contains(',') || limpio.Contains('"'))
        {
            return "\"" + limpio.Replace("\"", "\"\"") + "\"";
        }

        return limpio;
    }
}
