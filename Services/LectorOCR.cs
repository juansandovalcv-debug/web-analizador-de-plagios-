using Tesseract;
using web_analizador_de_plagios.Data.Models;

namespace web_analizador_de_plagios.Services;

public interface ILectorOCR
{
    bool EsImagen(string nombreArchivo);
    Task<ResultadoOCR> LeerAsync(byte[] contenido, string nombreArchivo, CancellationToken cancellationToken = default);
}

/// <summary>
/// OCR local con Tesseract 5. Se inicializa de forma perezosa y se serializa el
/// acceso al motor (TesseractEngine no es thread-safe). Si faltan los datos de
/// idioma o los binarios nativos, se devuelve un error claro en la interfaz.
/// </summary>
public sealed class LectorOCR : ILectorOCR, IDisposable
{
    private readonly SemaphoreSlim _semaforo = new(1, 1);
    private readonly string _rutaTessdata;
    private readonly ILogger<LectorOCR> _logger;
    private TesseractEngine? _motor;
    private string? _errorInicializacion;

    public LectorOCR(IWebHostEnvironment entorno, IConfiguration configuracion, ILogger<LectorOCR> logger)
    {
        _logger = logger;
        var rutaConfigurada = configuracion.GetValue<string>("Ocr:TessDataPath") ?? "tessdata";
        _rutaTessdata = Path.IsPathRooted(rutaConfigurada)
            ? rutaConfigurada
            : Path.Combine(entorno.ContentRootPath, rutaConfigurada);
    }

    public bool EsImagen(string nombreArchivo) => AlmacenArchivos.EsImagen(nombreArchivo);

    public async Task<ResultadoOCR> LeerAsync(byte[] contenido, string nombreArchivo, CancellationToken cancellationToken = default)
    {
        if (!EsImagen(nombreArchivo))
        {
            return new ResultadoOCR
            {
                Exito = false,
                Mensaje = "El OCR solo se aplica a imágenes (.png, .jpg, .jpeg, .bmp, .tif, .tiff, .webp)."
            };
        }

        if (contenido.Length == 0)
        {
            return new ResultadoOCR { Exito = false, Mensaje = "La imagen está vacía." };
        }

        if (contenido.Length > 20 * 1024 * 1024)
        {
            return new ResultadoOCR { Exito = false, Mensaje = "La imagen supera el límite de 20 MB para OCR." };
        }

        await _semaforo.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var motor = ObtenerMotor();
            using var pix = Pix.LoadFromMemory(contenido);
            using var pagina = motor.Process(pix, PageSegMode.SingleBlock);
            var texto = pagina.GetText()?.Trim() ?? string.Empty;
            var confianza = Math.Round(pagina.GetMeanConfidence() * 100, 1);

            if (string.IsNullOrWhiteSpace(texto))
            {
                // Segundo intento con segmentación automática: útil en capturas con varios bloques.
                using var pixAlterno = Pix.LoadFromMemory(contenido);
                using var paginaAlterna = motor.Process(pixAlterno, PageSegMode.Auto);
                texto = paginaAlterna.GetText()?.Trim() ?? string.Empty;
                confianza = Math.Round(paginaAlterna.GetMeanConfidence() * 100, 1);
            }

            if (string.IsNullOrWhiteSpace(texto))
            {
                return new ResultadoOCR
                {
                    Exito = false,
                    Confianza = confianza,
                    Mensaje = "No se reconoció texto en la imagen. Use una captura nítida, con letra legible y sin fondo oscuro."
                };
            }

            var mensaje = confianza < 55
                ? $"OCR completado con confianza baja ({confianza:F1}%). Revise el texto reconocido antes de confiar en el resultado."
                : $"Texto reconocido con {confianza:F1}% de confianza media.";

            _logger.LogInformation("OCR completado para {Archivo}: {Caracteres} caracteres, confianza {Confianza}%",
                nombreArchivo, texto.Length, confianza);

            return new ResultadoOCR
            {
                Exito = true,
                Texto = texto,
                Confianza = confianza,
                Mensaje = mensaje
            };
        }
        catch (OperationCanceledException)
        {
            return new ResultadoOCR { Exito = false, Mensaje = "El OCR fue cancelado." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ejecutando OCR sobre {Archivo}", nombreArchivo);
            return new ResultadoOCR
            {
                Exito = false,
                Mensaje = $"No se pudo ejecutar el OCR: {_errorInicializacion ?? ex.Message}. " +
                          "Verifique que la carpeta tessdata contenga eng.traineddata y que la aplicación se ejecute en x64."
            };
        }
        finally
        {
            _semaforo.Release();
        }
    }

    private TesseractEngine ObtenerMotor()
    {
        if (_motor is not null)
        {
            return _motor;
        }

        if (!string.IsNullOrWhiteSpace(_errorInicializacion))
        {
            throw new InvalidOperationException(_errorInicializacion);
        }

        if (!Directory.Exists(_rutaTessdata))
        {
            _errorInicializacion = $"No existe la carpeta de idioma OCR: {_rutaTessdata}";
            throw new InvalidOperationException(_errorInicializacion);
        }

        if (!File.Exists(Path.Combine(_rutaTessdata, "eng.traineddata")))
        {
            _errorInicializacion = $"Falta el archivo eng.traineddata en {_rutaTessdata}";
            throw new InvalidOperationException(_errorInicializacion);
        }

        try
        {
            _motor = new TesseractEngine(_rutaTessdata, "eng", EngineMode.Default);
            _motor.SetVariable("preserve_interword_spaces", "1");
            _logger.LogInformation("Tesseract inicializado con datos en {Ruta}", _rutaTessdata);
            return _motor;
        }
        catch (Exception ex)
        {
            _errorInicializacion = $"Tesseract no pudo inicializarse: {ex.Message}";
            _logger.LogError(ex, "Fallo inicializando Tesseract");
            throw new InvalidOperationException(_errorInicializacion, ex);
        }
    }

    public void Dispose()
    {
        _motor?.Dispose();
        _semaforo.Dispose();
    }
}
