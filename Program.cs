using Microsoft.EntityFrameworkCore;
using web_analizador_de_plagios.Components;
using web_analizador_de_plagios.Data;
using web_analizador_de_plagios.Endpoints;
using web_analizador_de_plagios.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var cadenaConexion = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Falta la cadena de conexión ConnectionStrings:DefaultConnection para SQL Server.");

builder.Services.AddDbContextFactory<AppDbContext>(opciones =>
    opciones.UseSqlServer(cadenaConexion, sqlServer =>
    {
        sqlServer.CommandTimeout(30);
    }));

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
        await db.Database.MigrateAsync();
        app.Logger.LogInformation(
            "Base de datos SQL Server lista: {Servidor}/{BaseDatos}",
            db.Database.GetDbConnection().DataSource,
            db.Database.GetDbConnection().Database);
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "No se pudo inicializar la base de datos SQL Server");
        throw new InvalidOperationException(
            "La aplicación no pudo inicializar la base de datos SQL Server. Revise ConnectionStrings:DefaultConnection y que SQL Server esté disponible.",
            ex);
    }
}
