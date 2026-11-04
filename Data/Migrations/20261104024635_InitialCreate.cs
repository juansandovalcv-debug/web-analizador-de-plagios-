using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace web_analizador_de_plagios.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Grupos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Anio = table.Column<int>(type: "int", nullable: false),
                    Ciclo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Computo = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grupos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Estudiantes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrupoId = table.Column<int>(type: "int", nullable: false),
                    Carnet = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Correo = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Estudiantes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Estudiantes_Grupos_GrupoId",
                        column: x => x.GrupoId,
                        principalTable: "Grupos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Evaluaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrupoId = table.Column<int>(type: "int", nullable: false),
                    EstudianteId = table.Column<int>(type: "int", nullable: false),
                    NombreArchivo = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    NombreAlmacenado = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TipoContenido = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TamanoBytes = table.Column<long>(type: "bigint", nullable: false),
                    HashSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CodigoFuente = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrigenArchivo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ConfianzaOcr = table.Column<double>(type: "float", nullable: false),
                    LineasCodigo = table.Column<int>(type: "int", nullable: false),
                    Metodos = table.Column<int>(type: "int", nullable: false),
                    Bucles = table.Column<int>(type: "int", nullable: false),
                    Condicionales = table.Column<int>(type: "int", nullable: false),
                    Similitud = table.Column<double>(type: "float", nullable: false),
                    ProbabilidadIA = table.Column<double>(type: "float", nullable: false),
                    NivelRiesgo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FechaAnalisis = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReferenciaEvaluacionId = table.Column<int>(type: "int", nullable: true),
                    SimilitudReferencia = table.Column<double>(type: "float", nullable: false),
                    MetadatosJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnalisisIaJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnalisisSimilitudJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evaluaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Evaluaciones_Estudiantes_EstudianteId",
                        column: x => x.EstudianteId,
                        principalTable: "Estudiantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Evaluaciones_Evaluaciones_ReferenciaEvaluacionId",
                        column: x => x.ReferenciaEvaluacionId,
                        principalTable: "Evaluaciones",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Evaluaciones_Grupos_GrupoId",
                        column: x => x.GrupoId,
                        principalTable: "Grupos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Historiales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EvaluacionId = table.Column<int>(type: "int", nullable: false),
                    EstudianteId = table.Column<int>(type: "int", nullable: false),
                    GrupoId = table.Column<int>(type: "int", nullable: false),
                    Computo = table.Column<int>(type: "int", nullable: false),
                    Anio = table.Column<int>(type: "int", nullable: false),
                    Ciclo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Similitud = table.Column<double>(type: "float", nullable: false),
                    ProbabilidadIA = table.Column<double>(type: "float", nullable: false),
                    Origen = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Resumen = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    ReferenciaEvaluacionId = table.Column<int>(type: "int", nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Historiales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Historiales_Estudiantes_EstudianteId",
                        column: x => x.EstudianteId,
                        principalTable: "Estudiantes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Historiales_Evaluaciones_EvaluacionId",
                        column: x => x.EvaluacionId,
                        principalTable: "Evaluaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Historiales_Grupos_GrupoId",
                        column: x => x.GrupoId,
                        principalTable: "Grupos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Estudiantes_Carnet",
                table: "Estudiantes",
                column: "Carnet");

            migrationBuilder.CreateIndex(
                name: "IX_Estudiantes_GrupoId_Carnet",
                table: "Estudiantes",
                columns: new[] { "GrupoId", "Carnet" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Evaluaciones_EstudianteId",
                table: "Evaluaciones",
                column: "EstudianteId");

            migrationBuilder.CreateIndex(
                name: "IX_Evaluaciones_GrupoId_FechaAnalisis",
                table: "Evaluaciones",
                columns: new[] { "GrupoId", "FechaAnalisis" });

            migrationBuilder.CreateIndex(
                name: "IX_Evaluaciones_HashSha256",
                table: "Evaluaciones",
                column: "HashSha256");

            migrationBuilder.CreateIndex(
                name: "IX_Evaluaciones_ReferenciaEvaluacionId",
                table: "Evaluaciones",
                column: "ReferenciaEvaluacionId");

            migrationBuilder.CreateIndex(
                name: "IX_Grupos_Anio_Ciclo",
                table: "Grupos",
                columns: new[] { "Anio", "Ciclo" });

            migrationBuilder.CreateIndex(
                name: "IX_Grupos_Codigo_Anio_Ciclo_Computo",
                table: "Grupos",
                columns: new[] { "Codigo", "Anio", "Ciclo", "Computo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Historiales_EstudianteId_Fecha",
                table: "Historiales",
                columns: new[] { "EstudianteId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_Historiales_EvaluacionId",
                table: "Historiales",
                column: "EvaluacionId");

            migrationBuilder.CreateIndex(
                name: "IX_Historiales_GrupoId_Computo",
                table: "Historiales",
                columns: new[] { "GrupoId", "Computo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Historiales");

            migrationBuilder.DropTable(
                name: "Evaluaciones");

            migrationBuilder.DropTable(
                name: "Estudiantes");

            migrationBuilder.DropTable(
                name: "Grupos");
        }
    }
}
