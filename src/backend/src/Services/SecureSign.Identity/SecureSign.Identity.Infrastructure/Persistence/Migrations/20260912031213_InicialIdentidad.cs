using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureSign.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InicialIdentidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsuariosIdentidad",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TieneValidacionExitosa = table.Column<bool>(type: "boolean", nullable: false),
                    TieneCertificadoVigente = table.Column<bool>(type: "boolean", nullable: false),
                    EsCuentaInstitucional = table.Column<bool>(type: "boolean", nullable: false),
                    RechazosPorSospechaFraude = table.Column<int>(type: "integer", nullable: false),
                    CreadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActualizadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuariosIdentidad", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosIdentidad_TenantId_UsuarioId",
                table: "UsuariosIdentidad",
                columns: new[] { "TenantId", "UsuarioId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsuariosIdentidad");
        }
    }
}
