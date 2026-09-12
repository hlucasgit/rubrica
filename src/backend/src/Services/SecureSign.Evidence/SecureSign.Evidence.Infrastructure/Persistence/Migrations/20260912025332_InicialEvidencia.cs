using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureSign.Evidence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InicialEvidencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Evidencias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaId = table.Column<Guid>(type: "uuid", nullable: true),
                    TipoEvidencia = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    HashEventoAnterior = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HashEvento = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DatosContextuales = table.Column<string>(type: "jsonb", nullable: false),
                    RegistradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evidencias", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Evidencias_DocumentoId",
                table: "Evidencias",
                column: "DocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Evidencias_TenantId_RegistradoEn",
                table: "Evidencias",
                columns: new[] { "TenantId", "RegistradoEn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Evidencias");
        }
    }
}
