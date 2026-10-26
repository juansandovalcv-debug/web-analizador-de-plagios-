using System.ComponentModel.DataAnnotations;

namespace web_analizador_de_plagios.Data.Models;

/// <summary>
/// Representa una entrega analizada (código o captura procesada por OCR).
/// </summary>
public class Evaluacion
{
    public int Id { get; set; }

    public int GrupoId { get; set; }
    public Grupo? Grupo { get; set; }

    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    [Required]
    [StringLength(260)]
    public string NombreArchivo { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string NombreAlmacenado { get; set; } = string.Empty;

    [StringLength(20)]
    public string Extension { get; set; } = ".cs";

    [StringLength(120)]
    public string? TipoContenido { get; set; }

    public long TamanoBytes { get; set; }

    [StringLength(64)]
    public string HashSha256 { get; set; } = string.Empty;

    /// <summary>Código fuente normalizado para conservar el texto exacto de la entrega.</summary>
    public string CodigoFuente { get; set; } = string.Empty;

    [StringLength(40)]
    public string OrigenArchivo { get; set; } = "Código";

    public double ConfianzaOcr { get; set; }

    public int LineasCodigo { get; set; }
    public int Metodos { get; set; }
    public int Bucles { get; set; }
    public int Condicionales { get; set; }

    [Range(0, 100)]
    public double Similitud { get; set; }

    [Range(0, 100)]
    public double ProbabilidadIA { get; set; }

    [StringLength(30)]
    public string NivelRiesgo { get; set; } = "Bajo";

    public DateTime FechaAnalisis { get; set; } = DateTime.UtcNow;

    public int? ReferenciaEvaluacionId { get; set; }
    public Evaluacion? ReferenciaEvaluacion { get; set; }

    [Range(0, 100)]
    public double SimilitudReferencia { get; set; }

    /// <summary>JSON con los metadatos forenses del archivo (fechas, propietario, codificación).</summary>
    public string? MetadatosJson { get; set; }

    /// <summary>JSON serializado de <c>ResultadoDeteccionIA</c>.</summary>
    public string? AnalisisIaJson { get; set; }

    /// <summary>JSON serializado de <c>ResumenSimilitud</c> y las mejores coincidencias.</summary>
    public string? AnalisisSimilitudJson { get; set; }

    [StringLength(1200)]
    public string? Observaciones { get; set; }

    public ICollection<HistorialAnalisis> Historiales { get; set; } = new List<HistorialAnalisis>();
}
