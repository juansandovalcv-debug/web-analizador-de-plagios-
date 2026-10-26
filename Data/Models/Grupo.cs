using System.ComponentModel.DataAnnotations;

namespace web_analizador_de_plagios.Data.Models;

/// <summary>
/// Agrupa a los estudiantes de una asignatura para un ciclo, año y cómputo concretos.
/// </summary>
public class Grupo
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El nombre del grupo es obligatorio.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 120 caracteres.")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "El código del grupo es obligatorio.")]
    [StringLength(40, MinimumLength = 2, ErrorMessage = "El código debe tener entre 2 y 40 caracteres.")]
    public string Codigo { get; set; } = string.Empty;

    [Range(2000, 2100, ErrorMessage = "El año debe estar entre 2000 y 2100.")]
    public int Anio { get; set; } = DateTime.Today.Year;

    [Required(ErrorMessage = "El ciclo es obligatorio.")]
    [StringLength(30, ErrorMessage = "El ciclo no puede superar 30 caracteres.")]
    public string Ciclo { get; set; } = $"Ciclo {DateTime.Today.Year}";

    [Range(1, 3, ErrorMessage = "El cómputo debe ser 1, 2 o 3.")]
    public int Computo { get; set; } = 1;

    [StringLength(400, ErrorMessage = "La descripción no puede superar 400 caracteres.")]
    public string? Descripcion { get; set; }

    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;

    public ICollection<Estudiante> Estudiantes { get; set; } = new List<Estudiante>();
    public ICollection<Evaluacion> Evaluaciones { get; set; } = new List<Evaluacion>();
    public ICollection<HistorialAnalisis> Historiales { get; set; } = new List<HistorialAnalisis>();

    public string EtiquetaPeriodo => $"{Ciclo} {Anio} · Cómputo {Computo}";
}
