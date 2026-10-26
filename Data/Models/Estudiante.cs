using System.ComponentModel.DataAnnotations;

namespace web_analizador_de_plagios.Data.Models;

public class Estudiante
{
    public int Id { get; set; }

    public int GrupoId { get; set; }
    public Grupo? Grupo { get; set; }

    [Required(ErrorMessage = "El carnet es obligatorio.")]
    [StringLength(40, MinimumLength = 2, ErrorMessage = "El carnet debe tener entre 2 y 40 caracteres.")]
    public string Carnet { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(160, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 160 caracteres.")]
    public string Nombre { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    [StringLength(180, ErrorMessage = "El correo no puede superar 180 caracteres.")]
    public string? Correo { get; set; }

    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;

    public ICollection<Evaluacion> Evaluaciones { get; set; } = new List<Evaluacion>();
    public ICollection<HistorialAnalisis> Historiales { get; set; } = new List<HistorialAnalisis>();
}
