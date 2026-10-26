using Microsoft.EntityFrameworkCore;
using web_analizador_de_plagios.Data.Models;

namespace web_analizador_de_plagios.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Grupo> Grupos => Set<Grupo>();
    public DbSet<Estudiante> Estudiantes => Set<Estudiante>();
    public DbSet<Evaluacion> Evaluaciones => Set<Evaluacion>();
    public DbSet<HistorialAnalisis> Historiales => Set<HistorialAnalisis>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Grupo>(entity =>
        {
            entity.ToTable("Grupos");
            entity.HasIndex(g => new { g.Codigo, g.Anio, g.Ciclo, g.Computo }).IsUnique();
            entity.HasIndex(g => new { g.Anio, g.Ciclo });
            entity.Property(g => g.CreadoEn).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        });

        modelBuilder.Entity<Estudiante>(entity =>
        {
            entity.ToTable("Estudiantes");
            entity.HasIndex(e => new { e.GrupoId, e.Carnet }).IsUnique();
            entity.HasIndex(e => e.Carnet);
            entity.HasOne(e => e.Grupo)
                .WithMany(g => g.Estudiantes)
                .HasForeignKey(e => e.GrupoId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.CreadoEn).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        });

        modelBuilder.Entity<Evaluacion>(entity =>
        {
            entity.ToTable("Evaluaciones");
            entity.HasIndex(e => new { e.GrupoId, e.FechaAnalisis });
            entity.HasIndex(e => e.EstudianteId);
            entity.HasIndex(e => e.HashSha256);
            entity.HasOne(e => e.Grupo)
                .WithMany(g => g.Evaluaciones)
                .HasForeignKey(e => e.GrupoId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Estudiante)
                .WithMany(s => s.Evaluaciones)
                .HasForeignKey(e => e.EstudianteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ReferenciaEvaluacion)
                .WithMany()
                .HasForeignKey(e => e.ReferenciaEvaluacionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.FechaAnalisis).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        });

        modelBuilder.Entity<HistorialAnalisis>(entity =>
        {
            entity.ToTable("Historiales");
            entity.HasIndex(h => new { h.GrupoId, h.Computo });
            entity.HasIndex(h => new { h.EstudianteId, h.Fecha });
            entity.HasOne(h => h.Evaluacion)
                .WithMany(e => e.Historiales)
                .HasForeignKey(h => h.EvaluacionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(h => h.Estudiante)
                .WithMany(e => e.Historiales)
                .HasForeignKey(h => h.EstudianteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(h => h.Grupo)
                .WithMany(g => g.Historiales)
                .HasForeignKey(h => h.GrupoId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(h => h.Fecha).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        });
    }
}
