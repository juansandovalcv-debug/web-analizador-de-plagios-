using System.Net.Mail;
using System.Text;
using Microsoft.EntityFrameworkCore;
using web_analizador_de_plagios.Data;
using web_analizador_de_plagios.Data.Models;

namespace web_analizador_de_plagios.Services;

public sealed class FilaEstudianteImportacion
{
    public int Linea { get; set; }
    public string Carnet { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Correo { get; set; }
    public bool Valida { get; set; }
    public string? Error { get; set; }
}

public sealed class ResultadoImportacionEstudiantes
{
    public int Importados { get; set; }
    public int Omitidos { get; set; }
    public List<string> Mensajes { get; set; } = new();
}

/// <summary>
/// Importación masiva tolerante a CSV con coma, punto y coma o tabulador,
/// encabezados en español/inglés y campos entre comillas.
/// </summary>
public sealed class ImportadorEstudiantes
{
    private const int MaxFilas = 2000;
    private static readonly string[] EncabezadosCarnet = { "carnet", "cédula", "cedula", "identificación", "identificacion", "id", "studentid" };
    private static readonly string[] EncabezadosNombre = { "nombre", "estudiante", "name", "alumno", "student" };
    private static readonly string[] EncabezadosCorreo = { "correo", "email", "e-mail", "mail" };

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<ImportadorEstudiantes> _logger;

    public ImportadorEstudiantes(IDbContextFactory<AppDbContext> dbFactory, ILogger<ImportadorEstudiantes> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public IReadOnlyList<FilaEstudianteImportacion> Previsualizar(string contenido)
    {
        var filas = Parsear(contenido, out _);
        return filas;
    }

    /// <summary>
    /// Previsualiza el CSV y marca también los carnets que ya existen en el grupo,
    /// para que el docente vea el resultado real antes de confirmar la importación.
    /// </summary>
    public async Task<IReadOnlyList<FilaEstudianteImportacion>> PrevisualizarAsync(
        int grupoId, string contenido, CancellationToken cancellationToken = default)
    {
        var filas = Parsear(contenido, out _);
        if (grupoId <= 0 || filas.Count == 0)
        {
            return filas;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existentes = await db.Estudiantes
            .Where(e => e.GrupoId == grupoId)
            .Select(e => e.Carnet)
            .ToListAsync(cancellationToken);
        var carnets = new HashSet<string>(existentes, StringComparer.OrdinalIgnoreCase);

        foreach (var fila in filas)
        {
            if (fila.Valida && carnets.Contains(fila.Carnet))
            {
                fila.Valida = false;
                fila.Error = "Este carnet ya está registrado en el grupo.";
            }
        }

        return filas;
    }

    public async Task<ResultadoImportacionEstudiantes> ImportarAsync(int grupoId, string contenido, CancellationToken cancellationToken = default)
    {
        var resultado = new ResultadoImportacionEstudiantes();
        var filas = Parsear(contenido, out var erroresFormato);
        resultado.Mensajes.AddRange(erroresFormato);

        if (filas.Count == 0)
        {
            resultado.Mensajes.Add("No se encontraron filas válidas para importar.");
            return resultado;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var grupoExiste = await db.Grupos.AnyAsync(g => g.Id == grupoId, cancellationToken);
        if (!grupoExiste)
        {
            throw new InvalidOperationException("El grupo no existe.");
        }

        var existentes = await db.Estudiantes
            .Where(e => e.GrupoId == grupoId)
            .Select(e => e.Carnet)
            .ToListAsync(cancellationToken);
        var carnets = new HashSet<string>(existentes, StringComparer.OrdinalIgnoreCase);
        var vistosEnArchivo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fila in filas)
        {
            if (!fila.Valida)
            {
                resultado.Omitidos++;
                resultado.Mensajes.Add($"Línea {fila.Linea}: {fila.Error}");
                continue;
            }

            if (!vistosEnArchivo.Add(fila.Carnet))
            {
                resultado.Omitidos++;
                resultado.Mensajes.Add($"Línea {fila.Linea}: carnet duplicado en el archivo ({fila.Carnet}).");
                continue;
            }

            if (!carnets.Add(fila.Carnet))
            {
                resultado.Omitidos++;
                resultado.Mensajes.Add($"Línea {fila.Linea}: el carnet {fila.Carnet} ya está registrado en el grupo.");
                continue;
            }

            db.Estudiantes.Add(new Estudiante
            {
                GrupoId = grupoId,
                Carnet = fila.Carnet,
                Nombre = fila.Nombre,
                Correo = fila.Correo,
                CreadoEn = DateTime.UtcNow
            });
            resultado.Importados++;
        }

        if (resultado.Importados == 0)
        {
            resultado.Mensajes.Add("No se agregó ningún estudiante nuevo.");
            return resultado;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            resultado.Mensajes.Add($"Se importaron {resultado.Importados} estudiante(s).");
            _logger.LogInformation("Importación masiva en grupo {GrupoId}: {Importados} importados, {Omitidos} omitidos",
                grupoId, resultado.Importados, resultado.Omitidos);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Error guardando importación masiva en grupo {GrupoId}", grupoId);
            throw new InvalidOperationException("No se pudo guardar la importación. Verifique que los carnets no estén duplicados.", ex);
        }

        return resultado;
    }

    private static List<FilaEstudianteImportacion> Parsear(string contenido, out List<string> errores)
    {
        errores = new List<string>();
        var filas = new List<FilaEstudianteImportacion>();
        if (string.IsNullOrWhiteSpace(contenido))
        {
            return filas;
        }

        var lineas = contenido.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var noVacias = lineas.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (noVacias.Count == 0)
        {
            return filas;
        }

        var delimitador = DetectarDelimitador(noVacias[0]);
        var primera = ParsearLinea(noVacias[0], delimitador);
        var tieneEncabezado = primera.Any(c => EncabezadosCarnet.Contains(c.Trim().ToLowerInvariant())) ||
                              primera.Any(c => EncabezadosNombre.Contains(c.Trim().ToLowerInvariant()));

        var indiceCarnet = 0;
        var indiceNombre = 1;
        var indiceCorreo = 2;
        if (tieneEncabezado)
        {
            indiceCarnet = BuscarIndice(primera, EncabezadosCarnet, 0);
            indiceNombre = BuscarIndice(primera, EncabezadosNombre, 1);
            indiceCorreo = BuscarIndice(primera, EncabezadosCorreo, 2);
        }

        var inicio = tieneEncabezado ? 1 : 0;
        if (noVacias.Count - inicio > MaxFilas)
        {
            errores.Add($"El archivo supera el máximo de {MaxFilas} filas; se procesarán solo las primeras {MaxFilas}.");
        }

        var carnetsVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = inicio; i < noVacias.Count && filas.Count < MaxFilas; i++)
        {
            var numeroLinea = i + 1;
            var celdas = ParsearLinea(noVacias[i], delimitador);
            var carnet = ObtenerCelda(celdas, indiceCarnet);
            var nombre = ObtenerCelda(celdas, indiceNombre);
            var correo = ObtenerCelda(celdas, indiceCorreo);

            var fila = new FilaEstudianteImportacion
            {
                Linea = numeroLinea,
                Carnet = Limpiar(carnet, 40),
                Nombre = Limpiar(nombre, 160),
                Correo = Limpiar(correo, 180)
            };

            if (fila.Carnet.Length < 2)
            {
                fila.Valida = false;
                fila.Error = "El carnet debe tener al menos 2 caracteres.";
            }
            else if (fila.Nombre.Length < 2)
            {
                fila.Valida = false;
                fila.Error = "El nombre debe tener al menos 2 caracteres.";
            }
            else if (!carnetsVistos.Add(fila.Carnet))
            {
                fila.Valida = false;
                fila.Error = "Carnet duplicado en el archivo.";
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(fila.Correo) && !EsCorreoValido(fila.Correo))
                {
                    fila.Correo = null;
                }

                fila.Valida = true;
            }

            filas.Add(fila);
        }

        return filas;
    }

    private static char DetectarDelimitador(string linea)
    {
        var coma = linea.Count(c => c == ',');
        var puntoYComa = linea.Count(c => c == ';');
        var tab = linea.Count(c => c == '\t');
        if (puntoYComa > coma && puntoYComa >= tab)
        {
            return ';';
        }

        return tab > coma ? '\t' : ',';
    }

    private static List<string> ParsearLinea(string linea, char delimitador)
    {
        var celdas = new List<string>();
        var actual = new StringBuilder();
        var entreComillas = false;

        for (var i = 0; i < linea.Length; i++)
        {
            var c = linea[i];
            if (entreComillas)
            {
                if (c == '"' && i + 1 < linea.Length && linea[i + 1] == '"')
                {
                    actual.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    entreComillas = false;
                }
                else
                {
                    actual.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                entreComillas = true;
                continue;
            }

            if (c == delimitador)
            {
                celdas.Add(actual.ToString());
                actual.Clear();
                continue;
            }

            actual.Append(c);
        }

        celdas.Add(actual.ToString());
        return celdas;
    }

    private static int BuscarIndice(IReadOnlyList<string> encabezados, IEnumerable<string> candidatos, int predeterminado)
    {
        for (var i = 0; i < encabezados.Count; i++)
        {
            var valor = encabezados[i].Trim().ToLowerInvariant();
            if (candidatos.Contains(valor))
            {
                return i;
            }
        }

        return predeterminado;
    }

    private static string ObtenerCelda(IReadOnlyList<string> celdas, int indice)
        => indice >= 0 && indice < celdas.Count ? celdas[indice].Trim() : string.Empty;

    private static string Limpiar(string? valor, int max)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return string.Empty;
        }

        var limpio = new string(valor.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return limpio.Length <= max ? limpio : limpio[..max];
    }

    private static bool EsCorreoValido(string correo)
    {
        try
        {
            var direccion = new MailAddress(correo);
            return direccion.Address.Equals(correo, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
