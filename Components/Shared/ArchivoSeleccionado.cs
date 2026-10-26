namespace web_analizador_de_plagios.Components.Shared;

public sealed class ArchivoSeleccionado
{
    public string Nombre { get; set; } = string.Empty;
    public string? TipoContenido { get; set; }
    public DateTimeOffset? UltimaModificacion { get; set; }
    public long TamanoBytes { get; set; }
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public bool EsImagen { get; set; }
}
