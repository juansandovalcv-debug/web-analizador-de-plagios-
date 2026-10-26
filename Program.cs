using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using web_analizador_de_plagios.Components;
using web_analizador_de_plagios.Data;
using web_analizador_de_plagios.Endpoints;
using web_analizador_de_plagios.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var rutaBaseDatos = Path.Combine(builder.Environment.ContentRootPath, "sistema_plagio.db");
var cadenaConexion = new SqliteConnectionStringBuilder
{
    DataSource = rutaBaseDatos,
    Mode = SqliteOpenMode.ReadWriteCreate,
    ForeignKeys = true,
    Pooling = true
}.ToString();

builder.Services.AddDbContextFactory<AppDbContext>(opciones =>
    opciones.UseSqlite(cadenaConexion, sqlite => sqlite.CommandTimeout(30)));

builder.Services.AddSingleton<NormalizadorCodigo>();
builder.Services.AddSingleton<AnalizadorEstructura>();
builder.Services.AddSingleton<AnalizadorSimilitud>();
builder.Services.AddSingleton<DetectorIAPatrones>();
builder.Services.AddSingleton<GeneradorQuizDefensa>();
builder.Services.AddSingleton<AlmacenArchivos>();
builder.Services.AddSingleton<InspectorMetadatos>();
builder.Services.AddSingleton<ILectorOCR, LectorOCR>();
builder.Services.AddScoped<ServicioAnalisis>();
builder.Services.AddScoped<GestorRespaldos>();
builder.Services.AddScoped<ImportadorEstudiantes>();
builder.Services.AddScoped<PreferenciasUi>();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data", "uploads"));
await InicializarBaseDatosAsync(app);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapDescargasEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static async Task InicializarBaseDatosAsync(WebApplication app)
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;");
        app.Logger.LogInformation("Base de datos SQLite lista en {Ruta}", db.Database.GetDbConnection().DataSource);
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "No se pudo inicializar la base de datos SQLite");
        throw new InvalidOperationException("La aplicación no pudo inicializar sistema_plagio.db. Revise permisos de escritura en la carpeta del proyecto.", ex);
    }
}
