using System.Net;
using System.Text;
using web_analizador_de_plagios.Data.Models;

namespace web_analizador_de_plagios.Services;

/// <summary>
/// Genera entre 3 y 5 preguntas de defensa oral basadas en la estructura real del
/// código (métodos, bucles, variables y condicionales) y en las señales del análisis.
/// </summary>
public sealed class GeneradorQuizDefensa
{
    private readonly AnalizadorEstructura _estructura;

    public GeneradorQuizDefensa(AnalizadorEstructura estructura)
    {
        _estructura = estructura;
    }

    public FichaDefensa Generar(
        string codigo,
        string nombreArchivo,
        string? estudiante,
        string? grupo,
        double similitud,
        double probabilidadIa,
        ResultadoDeteccionIA? analisisIa = null,
        string? referenciaEstudiante = null,
        string? referenciaArchivo = null)
    {
        var estructura = _estructura.Analizar(codigo);
        var preguntas = new List<PreguntaDefensa>();
        var categorias = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Agregar(string categoria, string pregunta, string respuestaEsperada, string evidencia)
        {
            if (preguntas.Count >= 5 || !categorias.Add(categoria))
            {
                return;
            }

            preguntas.Add(new PreguntaDefensa
            {
                Numero = preguntas.Count + 1,
                Categoria = categoria,
                Pregunta = pregunta,
                RespuestaEsperada = respuestaEsperada,
                Evidencia = evidencia,
                PuntajeSugerido = 20
            });
        }

        var metodoPrincipal = estructura.Metodos
            .Where(m => !m.EsConstructor)
            .OrderByDescending(m => m.Nombre.Equals("Main", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.LineaFin - m.LineaInicio)
            .FirstOrDefault() ?? estructura.Metodos.FirstOrDefault();

        if (metodoPrincipal is not null)
        {
            var detalle = metodoPrincipal.EsAsync ? " y explique dónde se espera una operación asíncrona" : string.Empty;
            Agregar("Flujo del programa",
                $"Explique paso a paso qué hace el método «{metodoPrincipal.Nombre}» entre las líneas {metodoPrincipal.LineaInicio} y {metodoPrincipal.LineaFin}{detalle}. Mencione al menos tres instrucciones concretas y una decisión tomada.",
                "El estudiante debe describir la secuencia real del código, no solo leer el nombre del método. Se valoran las referencias a variables, llamadas y decisiones internas.",
                metodoPrincipal.Ejemplo ?? $"Método {metodoPrincipal.Nombre}, líneas {metodoPrincipal.LineaInicio}-{metodoPrincipal.LineaFin}.");
        }
        else
        {
            Agregar("Flujo del programa",
                $"El archivo «{nombreArchivo}» no declara métodos claros. Explique cómo se ejecuta el código desde el inicio y cuál es el resultado esperado.",
                "Debe identificar el punto de entrada o el bloque principal y justificar el orden de ejecución.",
                "Sin métodos declarados; revise la estructura superior del archivo.");
        }

        var bucle = estructura.Bucles
            .OrderBy(b => b.Tipo == "foreach" ? 0 : b.Tipo == "for" ? 1 : 2)
            .ThenBy(b => b.Linea)
            .FirstOrDefault();

        if (bucle is not null)
        {
            var descripcion = bucle.Tipo switch
            {
                "foreach" => $"recorre «{bucle.Coleccion}» con la variable «{bucle.Variable}»",
                "for" => $"usa la variable «{bucle.Variable}» y la condición «{bucle.Condicion}»",
                _ => $"evalúa la condición «{bucle.Condicion}»"
            };

            Agregar("Bucle específico",
                $"En la línea {bucle.Linea}, el bucle {bucle.Tipo} {descripcion}. Explique qué valores toma la variable de control, cuándo termina el bucle y qué ocurriría si la colección o condición estuviera vacía.",
                "Debe explicar la inicialización/iteración, la condición de salida y el caso límite. Si es foreach, debe identificar el tipo de colección y el papel de cada elemento.",
                bucle.Ejemplo ?? $"Bucle {bucle.Tipo} en línea {bucle.Linea}.");
        }
        else
        {
            Agregar("Control de flujo",
                "No se detectaron bucles. Explique cómo resolvería con un bucle la parte del programa que procesa varios elementos y qué condición de salida usaría.",
                "Se espera que proponga un for/foreach coherente con el problema y justifique la condición de parada.",
                "Sin bucles detectados por Roslyn.");
        }

        var variableClave = estructura.Variables
            .Where(v => !new[] { "i", "j", "k", "x", "y", "n" }.Contains(v.Nombre, StringComparer.OrdinalIgnoreCase))
            .Where(v => v.Nombre.Length >= 4)
            .OrderByDescending(v => v.ValorInicial is not null)
            .ThenBy(v => v.Linea)
            .FirstOrDefault() ?? estructura.Variables.FirstOrDefault();

        if (variableClave is not null)
        {
            var inicial = string.IsNullOrWhiteSpace(variableClave.ValorInicial)
                ? "sin valor inicial explícito"
                : $"inicializada como «{variableClave.ValorInicial}»";
            Agregar("Variable clave",
                $"La variable «{variableClave.Nombre}» de tipo «{variableClave.Tipo}» aparece en la línea {variableClave.Linea} y está {inicial}. ¿Qué representa en el problema, quién la modifica y qué efecto tendría cambiar su valor o su tipo?",
                "Debe vincular la variable con el dominio del problema, indicar sus escrituras/lecturas y razonar sobre el impacto de un cambio. No basta con decir que guarda un valor.",
                variableClave.ValorInicial is null
                    ? $"Declaración: {variableClave.Tipo} {variableClave.Nombre};"
                    : $"Declaración: {variableClave.Tipo} {variableClave.Nombre} = {variableClave.ValorInicial};");
        }

        var condicional = estructura.Condicionales.FirstOrDefault();
        if (condicional is not null)
        {
            Agregar("Caso límite",
                $"En la línea {condicional.Linea} se evalúa «{condicional.Condicion}» dentro de un {condicional.Tipo}. Explique qué ocurre cuando la condición es verdadera, cuándo es falsa y qué entrada límite podría romper esa lógica.",
                "Debe describir ambas ramas, proponer al menos un valor límite y justificar si el código lo maneja o no.",
                condicional.Ejemplo ?? $"Condición en línea {condicional.Linea}.");
        }

        if (similitud >= 55)
        {
            var referencia = string.IsNullOrWhiteSpace(referenciaEstudiante)
                ? "otra entrega del grupo"
                : $"la entrega de {referenciaEstudiante}";
            var archivoReferencia = string.IsNullOrWhiteSpace(referenciaArchivo)
                ? string.Empty
                : $" («{referenciaArchivo}»)";
            Agregar("Similitud alta",
                $"La comparación con {referencia}{archivoReferencia} alcanzó {similitud:F1}% de similitud estructural. Señale dos bloques equivalentes, explique las diferencias y describa cómo se organizó el trabajo con esa persona.",
                "Debe localizar coincidencias reales y explicar su origen. Una explicación vaga o la incapacidad de modificar el código sugiere copia; la colaboración legítima debe poder justificarse.",
                $"Similitud medida: {similitud:F1}% (Levenshtein + shingles + líneas).");
        }

        if (probabilidadIa >= 40)
        {
            var senal = analisisIa?.Indicadores.FirstOrDefault(i => i.Aporte > 0)?.Nombre ?? "estilo uniforme y comentarios";
            Agregar("Autoría y estilo",
                $"El análisis heurístico estimó {probabilidadIa:F1}% de probabilidad de generación asistida, con la señal «{senal}». Explique un método sin leer los comentarios, justifique por qué eligió esos nombres y reescriba una parte con sus propias palabras.",
                "Se evalúa la comprensión real: debe poder explicar la lógica, justificar decisiones y modificar el código en vivo. La estimación es una señal, no una prueba.",
                analisisIa?.Resumen ?? $"Probabilidad estimada: {probabilidadIa:F1}%.");
        }

        if (estructura.UsaColecciones)
        {
            var coleccion = estructura.Variables.FirstOrDefault(v =>
                v.Tipo.Contains("List<", StringComparison.Ordinal) ||
                v.Tipo.Contains("Dictionary<", StringComparison.Ordinal) ||
                v.Tipo.Contains("HashSet<", StringComparison.Ordinal) ||
                v.Tipo.EndsWith("[]", StringComparison.Ordinal));

            Agregar("Estructura de datos",
                coleccion is null
                    ? "El código utiliza colecciones. Explique por qué eligió esa estructura y cómo cambiaría el rendimiento si la colección tuviera 100 000 elementos."
                    : $"La variable «{coleccion.Nombre}» usa el tipo «{coleccion.Tipo}» en la línea {coleccion.Linea}. ¿Por qué esa estructura es adecuada y cómo cambiaría el rendimiento con 100 000 elementos?",
                "Debe comparar la estructura elegida con alternativas (lista, arreglo, diccionario) y razonar sobre búsquedas, inserciones y memoria.",
                coleccion is null ? "Uso de colecciones detectado." : $"Tipo detectado: {coleccion.Tipo}.");
        }

        if (estructura.UsaAsync)
        {
            Agregar("Concurrencia",
                "El código usa async/await. Explique qué operación se espera, qué ocurre con el hilo mientras se espera y qué excepción podría producirse si la tarea falla.",
                "Debe distinguir async/await de paralelismo, identificar la operación asíncrona y describir el manejo de errores.",
                "Se detectaron modificadores async o expresiones await.");
        }

        if (estructura.TieneRecursion)
        {
            var recursivo = estructura.Metodos.FirstOrDefault(m => m.EsRecursivo);
            Agregar("Recursividad",
                recursivo is null
                    ? "Se detectó recursividad. Explique el caso base y qué ocurre si nunca se alcanza."
                    : $"El método «{recursivo.Nombre}» se llama a sí mismo. Explique el caso base, cómo cambia el argumento y qué ocurre si nunca se alcanza.",
                "Debe identificar la condición de parada, la reducción del problema y el riesgo de desbordamiento de pila.",
                recursivo?.Ejemplo ?? "Llamada recursiva detectada.");
        }

        if (estructura.UsaLinq)
        {
            Agregar("LINQ",
                "El código encadena operaciones LINQ. Explique si la consulta es diferida o se materializa, y qué diferencia habría entre usar ToList() y recorrer el resultado dos veces.",
                "Debe explicar ejecución diferida, enumeración múltiple y costo de materializar.",
                "Invocaciones LINQ detectadas (Where/Select/OrderBy/...).");
        }

        if (preguntas.Count < 3)
        {
            Agregar("Diseño",
                $"Explique por qué el archivo «{nombreArchivo}» está organizado en {estructura.Metricas.Metodos} método(s) y {estructura.Metricas.Clases} tipo(s). ¿Qué separaría o uniría si el programa creciera?",
                "Se espera una justificación de cohesión, responsabilidades y mantenibilidad.",
                $"Métricas: {estructura.Metricas.LineasCodigo} líneas de código.");

            Agregar("Prueba de modificación",
                "Elija una constante o valor literal del código y explique qué cambiaría en la salida si se modificara. Después escriba la modificación en vivo.",
                "Debe localizar un valor real y predecir el efecto sin ejecutar a ciegas.",
                "Sin evidencia adicional; se evalúa manipulación en vivo.");
        }

        if (preguntas.Count < 3)
        {
            Agregar("Comprensión general",
                $"Describa con sus palabras el propósito del archivo «{nombreArchivo}» y los pasos principales que ejecuta.",
                "Debe explicar el objetivo del programa y su secuencia principal.",
                "Archivo sin estructura detectable suficiente.");
        }

        var ficha = new FichaDefensa
        {
            Preguntas = preguntas,
            GeneradoEn = DateTime.Now,
            ResumenAnalisis = $"Archivo de {estructura.Metricas.LineasCodigo} líneas de código, {estructura.Metricas.Metodos} método(s), " +
                               $"{estructura.Metricas.Bucles} bucle(s). Similitud {similitud:F1}% · Probabilidad IA {probabilidadIa:F1}%."
        };

        ficha.TextoPlano = ConstruirTexto(ficha, nombreArchivo, estudiante, grupo, estructura);
        ficha.Html = ConstruirHtml(ficha, nombreArchivo, estudiante, grupo, estructura);
        return ficha;
    }

    private static string ConstruirTexto(
        FichaDefensa ficha,
        string nombreArchivo,
        string? estudiante,
        string? grupo,
        EstructuraCodigo estructura)
    {
        var sb = new StringBuilder();
        sb.AppendLine("==============================================================");
        sb.AppendLine("       FICHA DE DEFENSA ORAL ANTI-FRAUDE · ANÁLISIS DE CÓDIGO");
        sb.AppendLine("==============================================================");
        sb.AppendLine($"Generada:            {ficha.GeneradoEn:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"Estudiante:         {estudiante ?? "No asignado"}");
        sb.AppendLine($"Grupo:              {grupo ?? "No asignado"}");
        sb.AppendLine($"Archivo analizado:  {nombreArchivo}");
        sb.AppendLine($"Métricas:           {ficha.ResumenAnalisis}");
        sb.AppendLine();
        sb.AppendLine("INSTRUCCIONES PARA EL DOCENTE");
        sb.AppendLine("1. Aplique la defensa de forma individual y sin acceso previo a las preguntas.");
        sb.AppendLine("2. Pida al estudiante que abra su propio código y explique, no que lo lea.");
        sb.AppendLine("3. Solicite una modificación breve en vivo para verificar comprensión.");
        sb.AppendLine("4. Registre la puntuación de cada pregunta (0 a 5).");
        sb.AppendLine("5. La probabilidad de IA es una señal heurística: no constituye prueba por sí sola.");
        sb.AppendLine();
        sb.AppendLine("PREGUNTAS");
        sb.AppendLine("--------------------------------------------------------------");

        foreach (var pregunta in ficha.Preguntas)
        {
            sb.AppendLine($"{pregunta.Numero}. [{pregunta.Categoria}] {pregunta.Pregunta}");
            sb.AppendLine($"   Evidencia: {pregunta.Evidencia}");
            sb.AppendLine($"   Respuesta esperada: {pregunta.RespuestaEsperada}");
            sb.AppendLine($"   Puntaje: ____ / 5");
            sb.AppendLine();
        }

        sb.AppendLine("--------------------------------------------------------------");
        sb.AppendLine("RÚBRICA SUGERIDA");
        sb.AppendLine("0-1: No comprende el código o no puede localizar los elementos.");
        sb.AppendLine("2-3: Explica parcialmente, con errores conceptuales o ayuda del docente.");
        sb.AppendLine("4-5: Explica con precisión, justifica decisiones y modifica el código correctamente.");
        sb.AppendLine();
        sb.AppendLine($"Puntaje total: ______ / {ficha.Preguntas.Count * 5}    Observaciones: ______________________________");
        sb.AppendLine();
        sb.AppendLine("Firma del docente: __________________________    Fecha: ____________");
        sb.AppendLine();
        sb.AppendLine("Documento generado localmente por el Analizador de Plagios.");
        return sb.ToString();
    }

    private static string ConstruirHtml(
        FichaDefensa ficha,
        string nombreArchivo,
        string? estudiante,
        string? grupo,
        EstructuraCodigo estructura)
    {
        static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine("<title>Ficha de defensa oral</title><style>");
        sb.AppendLine("""
            :root{color-scheme:light}
            *{box-sizing:border-box}
            body{font-family:Segoe UI,Arial,sans-serif;color:#1c2430;background:#fff;margin:0;padding:32px;line-height:1.5}
            .sheet{max-width:820px;margin:0 auto}
            header{border-bottom:3px solid #0f766e;padding-bottom:14px;margin-bottom:20px}
            h1{font-size:22px;margin:0 0 4px;color:#0f3d3a}
            .sub{color:#607080;font-size:12px;text-transform:uppercase;letter-spacing:.08em}
            .grid{display:grid;grid-template-columns:1fr 1fr;gap:8px 24px;font-size:13px;margin:14px 0 20px}
            .grid b{color:#334155}
            .note{background:#f0fdfa;border-left:4px solid #0f766e;padding:10px 14px;font-size:12px;margin:16px 0}
            .q{border:1px solid #dbe4ea;border-radius:10px;padding:14px 16px;margin:12px 0;break-inside:avoid}
            .q h3{margin:0 0 6px;font-size:14px;color:#0f3d3a}
            .tag{display:inline-block;background:#e6fffa;color:#0f766e;border-radius:20px;padding:2px 9px;font-size:10px;text-transform:uppercase;letter-spacing:.06em;margin-right:6px}
            .evidence{font-family:Consolas,monospace;background:#f8fafc;border:1px dashed #cbd5e1;padding:7px 9px;border-radius:6px;font-size:11px;margin:8px 0;overflow-wrap:anywhere}
            .expected{font-size:12px;color:#475569}
            .score{margin-top:10px;font-size:12px;color:#64748b}
            .line{border-bottom:1px solid #94a3b8;display:inline-block;width:110px}
            table{width:100%;border-collapse:collapse;font-size:12px;margin-top:18px}
            td,th{border:1px solid #dbe4ea;padding:7px 9px;text-align:left}
            th{background:#f8fafc}
            footer{margin-top:26px;font-size:10px;color:#94a3b8;text-align:center}
            @media print{body{padding:0}.sheet{max-width:none}.note{background:#fff}}
            """);
        sb.AppendLine("</style></head><body><div class=\"sheet\">");
        sb.AppendLine("<header><div class=\"sub\">Analizador de Plagios · Documento de evaluación</div>");
        sb.AppendLine("<h1>Ficha de defensa oral anti-fraude</h1>");
        sb.AppendLine($"<div class=\"sub\">Generada el {ficha.GeneradoEn:dd/MM/yyyy HH:mm}</div></header>");
        sb.AppendLine("<div class=\"grid\">");
        sb.AppendLine($"<div><b>Estudiante:</b> {H(estudiante ?? "No asignado")}</div>");
        sb.AppendLine($"<div><b>Grupo:</b> {H(grupo ?? "No asignado")}</div>");
        sb.AppendLine($"<div><b>Archivo:</b> {H(nombreArchivo)}</div>");
        sb.AppendLine($"<div><b>Métricas:</b> {H(ficha.ResumenAnalisis)}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"note\"><b>Instrucciones:</b> realice la defensa de forma individual, sin entrega previa de las preguntas. " +
                      "Pida explicaciones y una modificación breve en vivo. La probabilidad de IA es una señal heurística y no una prueba concluyente.</div>");

        foreach (var pregunta in ficha.Preguntas)
        {
            sb.AppendLine("<div class=\"q\">");
            sb.AppendLine($"<h3><span class=\"tag\">{H(pregunta.Categoria)}</span>{pregunta.Numero}. {H(pregunta.Pregunta)}</h3>");
            sb.AppendLine($"<div class=\"evidence\">{H(pregunta.Evidencia)}</div>");
            sb.AppendLine($"<div class=\"expected\"><b>Respuesta esperada:</b> {H(pregunta.RespuestaEsperada)}</div>");
            sb.AppendLine("<div class=\"score\">Puntaje: ____ / 5 · Observación: <span class=\"line\"></span></div>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("<table><tr><th>Puntaje total</th><th>Observaciones</th><th>Firma</th></tr>");
        sb.AppendLine($"<tr><td>______ / {ficha.Preguntas.Count * 5}</td><td style=\"height:52px\"></td><td style=\"width:190px\"></td></tr></table>");
        sb.AppendLine("<footer>Documento generado localmente. No se envió información a servicios externos.</footer>");
        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }
}
