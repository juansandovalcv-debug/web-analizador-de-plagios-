using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using web_analizador_de_plagios.Data.Models;

namespace web_analizador_de_plagios.Services;

public sealed class MetodoAnalizado
{
    public string Nombre { get; set; } = string.Empty;
    public string TipoRetorno { get; set; } = string.Empty;
    public string Parametros { get; set; } = string.Empty;
    public int LineaInicio { get; set; }
    public int LineaFin { get; set; }
    public int Complejidad { get; set; }
    public bool EsAsync { get; set; }
    public bool EsRecursivo { get; set; }
    public bool EsExpresion { get; set; }
    public bool EsConstructor { get; set; }
    public string? Ejemplo { get; set; }
}

public sealed class BucleAnalizado
{
    public string Tipo { get; set; } = string.Empty;
    public int Linea { get; set; }
    public string Variable { get; set; } = string.Empty;
    public string Condicion { get; set; } = string.Empty;
    public string? Coleccion { get; set; }
    public string? Ejemplo { get; set; }
}

public sealed class CondicionalAnalizado
{
    public string Tipo { get; set; } = string.Empty;
    public int Linea { get; set; }
    public string Condicion { get; set; } = string.Empty;
    public string? Ejemplo { get; set; }
}

public sealed class VariableAnalizada
{
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public int Linea { get; set; }
    public string? ValorInicial { get; set; }
}

public sealed class ClaseAnalizada
{
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = "class";
    public int Linea { get; set; }
    public int Metodos { get; set; }
}

public sealed class EstructuraCodigo
{
    public MetricasCodigo Metricas { get; set; } = new();
    public List<MetodoAnalizado> Metodos { get; set; } = new();
    public List<BucleAnalizado> Bucles { get; set; } = new();
    public List<CondicionalAnalizado> Condicionales { get; set; } = new();
    public List<VariableAnalizada> Variables { get; set; } = new();
    public List<ClaseAnalizada> Clases { get; set; } = new();
    public List<string> DirectivasUsing { get; set; } = new();
    public List<string> Invocaciones { get; set; } = new();
    public bool UsaAsync { get; set; }
    public bool UsaLinq { get; set; }
    public bool UsaArchivos { get; set; }
    public bool UsaColecciones { get; set; }
    public bool TieneRecursion { get; set; }
    public string? ErrorSintaxis { get; set; }
}

/// <summary>
/// Extrae estructura real de C# con Roslyn. Tolera archivos incompletos y
/// nunca lanza excepciones por errores de sintaxis del estudiante.
/// </summary>
public sealed class AnalizadorEstructura
{
    private readonly NormalizadorCodigo _normalizador;

    public AnalizadorEstructura(NormalizadorCodigo normalizador)
    {
        _normalizador = normalizador;
    }

    public EstructuraCodigo Analizar(string codigo, ResultadoNormalizacion? normalizacion = null)
    {
        var estructura = new EstructuraCodigo();
        codigo ??= string.Empty;
        normalizacion ??= _normalizador.Normalizar(codigo);

        estructura.Metricas = new MetricasCodigo
        {
            LineasTotales = normalizacion.LineasTotales,
            LineasCodigo = normalizacion.LineasCodigo,
            LineasComentario = normalizacion.LineasComentario,
            Caracteres = normalizacion.Caracteres
        };

        try
        {
            var arbol = CSharpSyntaxTree.ParseText(
                codigo,
                new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse),
                path: "Entrega.cs");

            var raiz = arbol.GetRoot();
            var lineas = AnalizadorSimilitud.NormalizarLineas(codigo);

            string? Ejemplo(int linea, int extra = 1)
            {
                if (linea <= 0 || linea > lineas.Count)
                {
                    return null;
                }

                var fin = Math.Min(lineas.Count, linea + extra);
                var texto = string.Join(" ", lineas.Skip(linea - 1).Take(fin - linea + 1))
                    .Trim();
                if (texto.Length > 170)
                {
                    texto = texto[..167] + "...";
                }

                return texto;
            }

            estructura.DirectivasUsing = raiz.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Select(u => u.Name?.ToString() ?? u.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var metodos = new List<MetodoAnalizado>();

            foreach (var metodo in raiz.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var nombre = metodo.Identifier.Text;
                var inicio = metodo.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var fin = metodo.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
                var recursion = metodo.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(i => i.Expression.ToString().EndsWith(nombre, StringComparison.Ordinal));

                metodos.Add(new MetodoAnalizado
                {
                    Nombre = nombre,
                    TipoRetorno = metodo.ReturnType.ToString(),
                    Parametros = metodo.ParameterList.ToString(),
                    LineaInicio = inicio,
                    LineaFin = fin,
                    Complejidad = CalcularComplejidad(metodo),
                    EsAsync = metodo.Modifiers.Any(SyntaxKind.AsyncKeyword),
                    EsRecursivo = recursion,
                    EsExpresion = metodo.ExpressionBody is not null,
                    Ejemplo = Ejemplo(inicio, 2)
                });
            }

            foreach (var constructor in raiz.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                var inicio = constructor.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var fin = constructor.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
                metodos.Add(new MetodoAnalizado
                {
                    Nombre = constructor.Identifier.Text + " (constructor)",
                    TipoRetorno = "constructor",
                    Parametros = constructor.ParameterList.ToString(),
                    LineaInicio = inicio,
                    LineaFin = fin,
                    Complejidad = CalcularComplejidad(constructor),
                    EsConstructor = true,
                    Ejemplo = Ejemplo(inicio, 2)
                });
            }

            foreach (var local in raiz.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
            {
                var inicio = local.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var fin = local.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
                metodos.Add(new MetodoAnalizado
                {
                    Nombre = local.Identifier.Text + " (función local)",
                    TipoRetorno = local.ReturnType.ToString(),
                    Parametros = local.ParameterList.ToString(),
                    LineaInicio = inicio,
                    LineaFin = fin,
                    Complejidad = CalcularComplejidad(local),
                    EsAsync = local.Modifiers.Any(SyntaxKind.AsyncKeyword),
                    EsExpresion = local.ExpressionBody is not null,
                    Ejemplo = Ejemplo(inicio, 2)
                });
            }

            estructura.Metodos = metodos.OrderBy(m => m.LineaInicio).ToList();
            estructura.Metricas.Metodos = estructura.Metodos.Count;

            foreach (var bucle in raiz.DescendantNodes().OfType<ForEachStatementSyntax>())
            {
                var linea = bucle.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                estructura.Bucles.Add(new BucleAnalizado
                {
                    Tipo = "foreach",
                    Linea = linea,
                    Variable = bucle.Identifier.Text,
                    Condicion = bucle.Expression.ToString(),
                    Coleccion = bucle.Expression.ToString(),
                    Ejemplo = Ejemplo(linea)
                });
            }

            foreach (var bucle in raiz.DescendantNodes().OfType<ForStatementSyntax>())
            {
                var linea = bucle.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                estructura.Bucles.Add(new BucleAnalizado
                {
                    Tipo = "for",
                    Linea = linea,
                    Variable = bucle.Declaration?.Variables.FirstOrDefault()?.Identifier.Text ?? "i",
                    Condicion = bucle.Condition?.ToString() ?? "sin condición",
                    Ejemplo = Ejemplo(linea)
                });
            }

            foreach (var bucle in raiz.DescendantNodes().OfType<WhileStatementSyntax>())
            {
                var linea = bucle.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                estructura.Bucles.Add(new BucleAnalizado
                {
                    Tipo = "while",
                    Linea = linea,
                    Condicion = bucle.Condition.ToString(),
                    Ejemplo = Ejemplo(linea)
                });
            }

            foreach (var bucle in raiz.DescendantNodes().OfType<DoStatementSyntax>())
            {
                var linea = bucle.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                estructura.Bucles.Add(new BucleAnalizado
                {
                    Tipo = "do-while",
                    Linea = linea,
                    Condicion = bucle.Condition.ToString(),
                    Ejemplo = Ejemplo(linea)
                });
            }

            estructura.Bucles = estructura.Bucles.OrderBy(b => b.Linea).ToList();
            estructura.Metricas.Bucles = estructura.Bucles.Count;

            foreach (var condicional in raiz.DescendantNodes().OfType<IfStatementSyntax>())
            {
                var linea = condicional.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                estructura.Condicionales.Add(new CondicionalAnalizado
                {
                    Tipo = "if",
                    Linea = linea,
                    Condicion = condicional.Condition.ToString(),
                    Ejemplo = Ejemplo(linea)
                });
            }

            foreach (var condicional in raiz.DescendantNodes().OfType<SwitchStatementSyntax>())
            {
                var linea = condicional.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                estructura.Condicionales.Add(new CondicionalAnalizado
                {
                    Tipo = "switch",
                    Linea = linea,
                    Condicion = condicional.Expression.ToString(),
                    Ejemplo = Ejemplo(linea)
                });
            }

            foreach (var condicional in raiz.DescendantNodes().OfType<SwitchExpressionSyntax>())
            {
                var linea = condicional.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                estructura.Condicionales.Add(new CondicionalAnalizado
                {
                    Tipo = "switch expression",
                    Linea = linea,
                    Condicion = condicional.GoverningExpression.ToString(),
                    Ejemplo = Ejemplo(linea)
                });
            }

            estructura.Condicionales = estructura.Condicionales.OrderBy(c => c.Linea).ToList();
            estructura.Metricas.Condicionales = estructura.Condicionales.Count;

            foreach (var declaracion in raiz.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                var padre = declaracion.Parent as VariableDeclarationSyntax;
                var linea = declaracion.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                estructura.Variables.Add(new VariableAnalizada
                {
                    Nombre = declaracion.Identifier.Text,
                    Tipo = padre?.Type.ToString() ?? "var",
                    Linea = linea,
                    ValorInicial = declaracion.Initializer?.Value.ToString()
                });
            }

            // Evita ruido de variables repetidas en el mismo ámbito.
            estructura.Variables = estructura.Variables
                .GroupBy(v => new { v.Nombre, v.Linea })
                .Select(g => g.First())
                .OrderBy(v => v.Linea)
                .ToList();
            estructura.Metricas.VariablesDeclaradas = estructura.Variables.Count;

            foreach (var clase in raiz.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                estructura.Clases.Add(new ClaseAnalizada
                {
                    Nombre = clase.Identifier.Text,
                    Tipo = "class",
                    Linea = clase.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    Metodos = clase.Members.OfType<MethodDeclarationSyntax>().Count()
                });
            }

            foreach (var registro in raiz.DescendantNodes().OfType<RecordDeclarationSyntax>())
            {
                estructura.Clases.Add(new ClaseAnalizada
                {
                    Nombre = registro.Identifier.Text,
                    Tipo = "record",
                    Linea = registro.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    Metodos = registro.Members.OfType<MethodDeclarationSyntax>().Count()
                });
            }

            foreach (var estructuraTipo in raiz.DescendantNodes().OfType<StructDeclarationSyntax>())
            {
                estructura.Clases.Add(new ClaseAnalizada
                {
                    Nombre = estructuraTipo.Identifier.Text,
                    Tipo = "struct",
                    Linea = estructuraTipo.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    Metodos = estructuraTipo.Members.OfType<MethodDeclarationSyntax>().Count()
                });
            }

            estructura.Metricas.Clases = estructura.Clases.Count;

            estructura.Invocaciones = raiz.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Select(i => i.Expression.ToString())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            estructura.UsaAsync = raiz.DescendantNodes().Any(n =>
                n is AwaitExpressionSyntax ||
                (n is MethodDeclarationSyntax m && m.Modifiers.Any(SyntaxKind.AsyncKeyword)));

            estructura.UsaLinq = estructura.Invocaciones.Any(i =>
                i.EndsWith(".Where", StringComparison.Ordinal) || i.EndsWith(".Select", StringComparison.Ordinal) ||
                i.EndsWith(".OrderBy", StringComparison.Ordinal) || i.EndsWith(".Any", StringComparison.Ordinal) ||
                i.EndsWith(".All", StringComparison.Ordinal) || i.EndsWith(".GroupBy", StringComparison.Ordinal) ||
                i.EndsWith(".Sum", StringComparison.Ordinal) || i.EndsWith(".FirstOrDefault", StringComparison.Ordinal));

            estructura.UsaArchivos = estructura.Invocaciones.Any(i =>
                i.StartsWith("File.", StringComparison.Ordinal) ||
                i.StartsWith("Directory.", StringComparison.Ordinal) ||
                i is "StreamReader" or "StreamWriter");

            estructura.UsaColecciones = estructura.Variables.Any(v =>
                v.Tipo.Contains("List<", StringComparison.Ordinal) ||
                v.Tipo.Contains("Dictionary<", StringComparison.Ordinal) ||
                v.Tipo.Contains("HashSet<", StringComparison.Ordinal) ||
                v.Tipo.EndsWith("[]", StringComparison.Ordinal)) ||
                estructura.Invocaciones.Any(i => i is "new List" or "new Dictionary");

            estructura.TieneRecursion = estructura.Metodos.Any(m => m.EsRecursivo);

            var diagnosticos = arbol.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(3)
                .Select(d => $"Línea {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}")
                .ToList();

            if (diagnosticos.Count > 0)
            {
                estructura.ErrorSintaxis = string.Join(" | ", diagnosticos);
            }
        }
        catch (Exception ex)
        {
            estructura.ErrorSintaxis = $"No se pudo analizar la sintaxis: {ex.Message}";
        }

        return estructura;
    }

    private static int CalcularComplejidad(SyntaxNode nodo)
    {
        var complejidad = 1;
        foreach (var descendiente in nodo.DescendantNodes())
        {
            complejidad += descendiente switch
            {
                IfStatementSyntax => 1,
                ForStatementSyntax => 1,
                ForEachStatementSyntax => 1,
                WhileStatementSyntax => 1,
                DoStatementSyntax => 1,
                SwitchSectionSyntax => 1,
                SwitchExpressionArmSyntax => 1,
                CatchClauseSyntax => 1,
                ConditionalExpressionSyntax => 1,
                BinaryExpressionSyntax binario when binario.IsKind(SyntaxKind.LogicalAndExpression) || binario.IsKind(SyntaxKind.LogicalOrExpression) => 1,
                _ => 0
            };
        }

        return complejidad;
    }
}
