using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureSign.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InicialFirma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SolicitudesFirma",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoFirma = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CodigoVerificacionPublico = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequiereOrdenSecuencial = table.Column<bool>(type: "boolean", nullable: false),
                    FechaLimite = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClienteIntegradorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreadoPor = table.Column<Guid>(type: "uuid", nullable: false),
                    CreadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudesFirma", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FlujosFirma",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SolicitudFirmaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmanteUsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrdenFirma = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TokenAccesoUnico = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NotificadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VisualizadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RechazadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MotivoRechazo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlujosFirma", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlujosFirma_SolicitudesFirma_SolicitudFirmaId",
                        column: x => x.SolicitudFirmaId,
                        principalTable: "SolicitudesFirma",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlujosFirma_FirmanteUsuarioId_Estado",
                table: "FlujosFirma",
                columns: new[] { "FirmanteUsuarioId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_FlujosFirma_SolicitudFirmaId",
                table: "FlujosFirma",
                column: "SolicitudFirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_FlujosFirma_TokenAccesoUnico",
                table: "FlujosFirma",
                column: "TokenAccesoUnico",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesFirma_CodigoVerificacionPublico",
                table: "SolicitudesFirma",
                column: "CodigoVerificacionPublico",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesFirma_TenantId_Estado",
                table: "SolicitudesFirma",
                columns: new[] { "TenantId", "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlujosFirma");

            migrationBuilder.DropTable(
                name: "SolicitudesFirma");
        }
    }
}
