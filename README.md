# Analizador de Plagios para Entregas de Código C#

Aplicación web **local-first** para apoyar la revisión de entregas de programación. Organiza grupos y estudiantes, compara archivos `.cs`, texto o capturas de pantalla, muestra un diff lado a lado y documenta señales explicables de similitud y de generación automática.

La herramienta **no emite un veredicto automático**: entrega evidencia para que el docente revise, converse con el estudiante y tome la decisión final.

## Documento del avance

- [Avance 1 completo (PDF)](docs/avance-1/Avance1.pdf)
- [Fuente HTML del documento](docs/avance-1/Avance1.html)
- [Bocetos de baja fidelidad y mapa de navegación](docs/avance-1/bocetos.html)
- [Capturas de la aplicación](docs/avance-1/img)

## Funcionalidad principal

- Grupos por año, ciclo y cómputo, con estudiantes y validación de carnets.
- Importación de estudiantes desde CSV con previsualización de filas válidas e inválidas.
- Respaldo y restauración de un grupo completo en JSON.
- Comparación directa entre dos entregas o contra el mejor candidato del grupo.
- Soporte para código C#, texto e imágenes mediante OCR local.
- Similitud estructural: 55% Levenshtein de tokens, 25% Jaccard de shingles de 4 tokens y 20% Levenshtein de líneas.
- Detección heurística y explicable de señales compatibles con IA, sin veredicto automático.
- Diff lado a lado, metadatos forenses, ficha de defensa y observaciones del docente.
- Histórico por cómputo y seguimiento por carnet, con exportación CSV.
- Modo a ciegas para revisar la evidencia sin mostrar nombres ni carnets.

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows para la ejecución probada con las dependencias nativas de Tesseract.
- No requiere servidor de base de datos ni servicios en la nube.

## Ejecutar en local

```powershell
dotnet restore
dotnet run --urls http://localhost:5135
```

Abrir <http://localhost:5135>.

En el primer arranque se crea automáticamente `sistema_plagio.db` y la carpeta `App_Data/uploads`. Ambos se excluyen de Git porque son datos locales.

## Rutas principales

| Ruta | Pantalla |
| --- | --- |
| `/` | Inicio y panorama general |
| `/grupos` | Grupos, estudiantes, CSV y respaldos |
| `/analisis` | Carga y ejecución del análisis |
| `/resultados` | Tabla filtrable de evaluaciones |
| `/resultados/{id}` | Detalle, diff, IA, quiz y observaciones |
| `/historico` | Tendencias y ejecuciones por periodo |

## Estructura

```text
Components/          Componentes Razor y layout
Data/                DbContext, modelos y esquema relacional
Endpoints/           Descargas CSV y respaldos
Services/            Análisis, similitud, OCR, importación y respaldo
docs/avance-1/       Documento del avance, PDF, bocetos y capturas
wwwroot/             Estilos, scripts e imágenes
tessdata/            Datos de idioma para OCR
```

## Privacidad y límites

- La base SQLite y los archivos de las entregas permanecen en el equipo local.
- No se envían archivos a APIs externas.
- La probabilidad de IA es una señal heurística; el OCR puede equivocarse y su confianza se informa.
- Esta versión no incluye autenticación multiusuario ni integración con un LMS.

## Estado

- Compilación verificada con **0 warnings / 0 errores**.
- Rutas principales verificadas en ejecución.
- El repositorio no incluye datos de demostración; la base se crea vacía.
- El PDF de `docs/avance-1/` es el entregable del primer avance.
