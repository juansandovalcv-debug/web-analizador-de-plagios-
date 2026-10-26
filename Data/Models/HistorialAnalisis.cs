using System.ComponentModel.DataAnnotations;

namespace web_analizador_de_plagios.Data.Models;

/// <summary>
/// Instantánea de cada ejecución del motor. Permite reconstruir tendencias por cómputo.
/// </summary>
public class HistorialAnalisis
{
    public int Id { get; set; }

    public int EvaluacionId { get; set; }
    public Evaluacion? Evaluacion { get; set; }

    public int EstudianteId { get; set; }
    public Estudiante? Estudiante { get; set; }

    public int GrupoId { get; set; }
    public Grupo? Grupo { get; set; }

    public int Computo { get; set; }
    public int Anio { get; set; }

    [StringLength(30)]
    public string Ciclo { get; set; } = string.Empty;

    [Range(0, 100)]
    public double Similitud { get; set; }

    [Range(0, 100)]
    public double ProbabilidadIA { get; set; }

    [StringLength(40)]
    public string Origen { get; set; } = "Análisis";

    [StringLength(600)]
    public string? Resumen { get; set; }

    public int? ReferenciaEvaluacionId { get; set; }

    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
