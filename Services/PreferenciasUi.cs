using web_analizador_de_plagios.Data.Models;

namespace web_analizador_de_plagios.Services;

/// <summary>
/// Estado de interfaz por circuito. El modo a ciegas sustituye nombre y carnet
/// para reducir sesgos durante la revisión.
/// </summary>
public sealed class PreferenciasUi
{
    public bool TemaOscuro { get; private set; } = true;
    public bool ModoCiego { get; private set; }

    public event Action? Cambio;

    public void EstablecerTema(bool oscuro)
    {
        if (TemaOscuro == oscuro)
        {
            return;
        }

        TemaOscuro = oscuro;
        Cambio?.Invoke();
    }

    public void EstablecerModoCiego(bool activo)
    {
        if (ModoCiego == activo)
        {
            return;
        }

        ModoCiego = activo;
        Cambio?.Invoke();
    }

    public string NombreVisible(int estudianteId, string nombre)
        => ModoCiego ? $"Estudiante #{estudianteId:000}" : nombre;

    public string CarnetVisible(int estudianteId, string carnet)
        => ModoCiego ? $"•••• {estudianteId:000}" : carnet;

    public string EtiquetaVisible(int estudianteId, string nombre, string carnet)
        => ModoCiego ? $"Estudiante #{estudianteId:000} · identidad oculta" : $"{nombre} · {carnet}";

    public string NombreVisible(Estudiante? estudiante)
        => estudiante is null ? "Sin estudiante" : NombreVisible(estudiante.Id, estudiante.Nombre);

    public string CarnetVisible(Estudiante? estudiante)
        => estudiante is null ? "—" : CarnetVisible(estudiante.Id, estudiante.Carnet);

    public string EtiquetaVisible(Estudiante? estudiante)
        => estudiante is null ? "Sin estudiante" : EtiquetaVisible(estudiante.Id, estudiante.Nombre, estudiante.Carnet);
}
