namespace web_analizador_de_plagios.Data.Models;

/// <summary>Metadatos forenses y técnicos de una entrega.</summary>
public sealed class MetadatosArchivo
{
    public string NombreOriginal { get; set; } = string.Empty;
    public string NombreAlmacenado { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string? TipoContenido { get; set; }
    public long TamanoBytes { get; set; }
    public DateTime? CreadoWindows { get; set; }
    public DateTime? ModificadoWindows { get; set; }
    public DateTime? AccedidoWindows { get; set; }
    public DateTimeOffset? ModificadoNavegador { get; set; }
    public string? Propietario { get; set; }
    public string Codificacion { get; set; } = "No determinada";
    public bool TieneBom { get; set; }
    public string HashSha256 { get; set; } = string.Empty;
    public bool EsImagen { get; set; }
    public string Origen { get; set; } = "Carga web";
    public string? RutaRelativaSegura { get; set; }
    public double? ConfianzaOcr { get; set; }
    public string? Notas { get; set; }
    public List<string> Advertencias { get; set; } = new();
}

public sealed class ResultadoInspeccionRuta
{
    public bool Exito { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public MetadatosArchivo? Metadatos { get; set; }
}

public sealed class ResultadoOCR
{
    public bool Exito { get; set; }
    public string Texto { get; set; } = string.Empty;
    public double Confianza { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public string Motor { get; set; } = "Tesseract 5";
}

public sealed class MetricasCodigo
{
    public int LineasTotales { get; set; }
    public int LineasCodigo { get; set; }
    public int LineasComentario { get; set; }
    public int Metodos { get; set; }
    public int Bucles { get; set; }
    public int Condicionales { get; set; }
    public int Clases { get; set; }
    public int Caracteres { get; set; }
    public int VariablesDeclaradas { get; set; }
}

public sealed class IndicadorIA
{
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Evidencia { get; set; } = string.Empty;
    public double Aporte { get; set; }
    public string Severidad { get; set; } = "Baja";
}

public sealed class MetricasEstilo
{
    public int LineasTotales { get; set; }
    public int LineasComentario { get; set; }
    public double DensidadComentarios { get; set; }
    public double PromedioLongitudLinea { get; set; }
    public double DesviacionLongitudLinea { get; set; }
    public int IdentificadoresGenericos { get; set; }
    public int DeclaracionesDoc { get; set; }
    public double RatioLineasRepetidas { get; set; }
    public int MetodosSimples { get; set; }
    public double RatioVar { get; set; }
    public int MetodosExpresion { get; set; }
    public int ConsoleWrites { get; set; }
    public int CatchGenericos { get; set; }
}

public sealed class ResultadoDeteccionIA
{
    public double Probabilidad { get; set; }
    public string Nivel { get; set; } = "Bajo";
    public string Resumen { get; set; } = string.Empty;
    public List<IndicadorIA> Indicadores { get; set; } = new();
    public MetricasEstilo Metricas { get; set; } = new();
    public string VersionMotor { get; set; } = "1.0";
}

public enum EstadoLineaDiff
{
    Igual,
    Modificada,
    SoloIzquierda,
    SoloDerecha
}

public sealed class LineaDiff
{
    public int? NumeroIzquierda { get; set; }
    public int? NumeroDerecha { get; set; }
    public string TextoIzquierda { get; set; } = string.Empty;
    public string TextoDerecha { get; set; } = string.Empty;
    public EstadoLineaDiff Estado { get; set; }
}

public sealed class ResultadoSimilitud
{
    public double Porcentaje { get; set; }
    public int DistanciaTokens { get; set; }
    public double SimilitudTokens { get; set; }
    public double SimilitudLineas { get; set; }
    public double JaccardShingles { get; set; }
    public int LineasIguales { get; set; }
    public int LineasModificadas { get; set; }
    public int LineasSoloIzquierda { get; set; }
    public int LineasSoloDerecha { get; set; }
    public bool Truncado { get; set; }
    public string Metodologia { get; set; } = string.Empty;
    public List<LineaDiff> Filas { get; set; } = new();
}

public sealed class CoincidenciaSimilitud
{
    public int EvaluacionId { get; set; }
    public int EstudianteId { get; set; }
    public string EstudianteNombre { get; set; } = string.Empty;
    public string EstudianteCarnet { get; set; } = string.Empty;
    public string Archivo { get; set; } = string.Empty;
    public double Porcentaje { get; set; }
}

public sealed class ResumenSimilitud
{
    public double Porcentaje { get; set; }
    public int DistanciaTokens { get; set; }
    public double SimilitudTokens { get; set; }
    public double SimilitudLineas { get; set; }
    public double JaccardShingles { get; set; }
    public int LineasIguales { get; set; }
    public int LineasModificadas { get; set; }
    public int LineasSoloIzquierda { get; set; }
    public int LineasSoloDerecha { get; set; }
    public bool Truncado { get; set; }
    public int? ReferenciaEvaluacionId { get; set; }
    public string? ReferenciaEstudiante { get; set; }
    public string? ReferenciaArchivo { get; set; }
    public string Metodologia { get; set; } = string.Empty;
    public List<CoincidenciaSimilitud> MejoresCoincidencias { get; set; } = new();
}

public sealed class PreguntaDefensa
{
    public int Numero { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public string Pregunta { get; set; } = string.Empty;
    public string RespuestaEsperada { get; set; } = string.Empty;
    public string Evidencia { get; set; } = string.Empty;
    public int PuntajeSugerido { get; set; } = 20;
}

public sealed class FichaDefensa
{
    public List<PreguntaDefensa> Preguntas { get; set; } = new();
    public string TextoPlano { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public DateTime GeneradoEn { get; set; } = DateTime.Now;
    public string ResumenAnalisis { get; set; } = string.Empty;
}

public sealed class ArchivoEntrada
{
    public string Nombre { get; set; } = string.Empty;
    public string? TipoContenido { get; set; }
    public DateTimeOffset? UltimaModificacion { get; set; }
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public int? EstudianteId { get; set; }
}

public sealed class SolicitudAnalisis
{
    public int GrupoId { get; set; }
    public ArchivoEntrada ArchivoA { get; set; } = new();
    public ArchivoEntrada? ArchivoB { get; set; }
    public bool GuardarEnHistorial { get; set; } = true;
}

public sealed class ResultadoAnalisisGuardado
{
    public int EvaluacionId { get; set; }
    public int? SegundaEvaluacionId { get; set; }
    public double Similitud { get; set; }
    public double ProbabilidadIA { get; set; }
    public string NivelRiesgo { get; set; } = "Bajo";
    public bool ComparacionDirecta { get; set; }
    public string? Mensaje { get; set; }
    public List<string> Advertencias { get; set; } = new();
}

public sealed class ArchivoDescarga
{
    public string Nombre { get; set; } = "archivo.bin";
    public string TipoContenido { get; set; } = "application/octet-stream";
    public byte[] Contenido { get; set; } = Array.Empty<byte>();
}

public sealed class GrupoResumen
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public int Anio { get; set; }
    public string Ciclo { get; set; } = string.Empty;
    public int Computo { get; set; }
    public string? Descripcion { get; set; }
    public DateTime CreadoEn { get; set; }
    public int TotalEstudiantes { get; set; }
    public int TotalEvaluaciones { get; set; }
    public double PromedioSimilitud { get; set; }
    public double PromedioIA { get; set; }
}

public sealed class EvaluacionResumen
{
    public int Id { get; set; }
    public int EstudianteId { get; set; }
    public string EstudianteNombre { get; set; } = string.Empty;
    public string EstudianteCarnet { get; set; } = string.Empty;
    public string GrupoNombre { get; set; } = string.Empty;
    public string GrupoCodigo { get; set; } = string.Empty;
    public int Computo { get; set; }
    public int Anio { get; set; }
    public string Ciclo { get; set; } = string.Empty;
    public string NombreArchivo { get; set; } = string.Empty;
    public double Similitud { get; set; }
    public double ProbabilidadIA { get; set; }
    public string NivelRiesgo { get; set; } = string.Empty;
    public DateTime FechaAnalisis { get; set; }
}

public sealed class AvanceImportacion
{
    public int GrupoId { get; set; }
    public string GrupoNombre { get; set; } = string.Empty;
    public int EstudiantesImportados { get; set; }
    public int EstudiantesOmitidos { get; set; }
    public int EvaluacionesImportadas { get; set; }
    public int HistorialesImportados { get; set; }
    public List<string> Advertencias { get; set; } = new();
}
