using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace web_analizador_de_plagios.Data;

/// <summary>
/// Permite crear migraciones con `dotnet ef` sin levantar el servidor web ni
/// necesitar una conexión activa a SQL Server en tiempo de diseño.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string CadenaPredeterminada =
        "Server=(localdb)\\MSSQLLocalDB;Database=AnalizadorPlagios;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public AppDbContext CreateDbContext(string[] args)
    {
        var configuracion = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var cadena = configuracion.GetConnectionString("DefaultConnection")
            ?? CadenaPredeterminada;

        var opciones = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(cadena)
            .Options;

        return new AppDbContext(opciones);
    }
}
