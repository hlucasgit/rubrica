using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureSign.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InicialAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventosAuditoria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    TipoEvento = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Detalle = table.Column<string>(type: "text", nullable: false),
                    OrigenIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OcurridoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosAuditoria", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosAuditoria_TenantId_OcurridoEn",
                table: "EventosAuditoria",
                columns: new[] { "TenantId", "OcurridoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_EventosAuditoria_TipoEvento",
                table: "EventosAuditoria",
                column: "TipoEvento");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosAuditoria");
        }
    }
}
