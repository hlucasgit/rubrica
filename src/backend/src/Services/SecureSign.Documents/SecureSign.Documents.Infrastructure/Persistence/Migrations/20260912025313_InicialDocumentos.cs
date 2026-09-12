using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureSign.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InicialDocumentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodigoExterno = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NombreArchivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TipoContenido = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HashSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TamanoBytes = table.Column<long>(type: "bigint", nullable: false),
                    UrlAlmacenamiento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreadoPor = table.Column<Guid>(type: "uuid", nullable: false),
                    CreadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documentos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_CodigoExterno",
                table: "Documentos",
                column: "CodigoExterno");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_TenantId_Estado",
                table: "Documentos",
                columns: new[] { "TenantId", "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Documentos");
        }
    }
}
