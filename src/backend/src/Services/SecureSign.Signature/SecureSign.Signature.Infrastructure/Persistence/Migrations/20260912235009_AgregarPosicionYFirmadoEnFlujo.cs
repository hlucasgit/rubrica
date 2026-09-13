using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureSign.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPosicionYFirmadoEnFlujo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirmadoEn",
                table: "FlujosFirma",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PosicionAlto",
                table: "FlujosFirma",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PosicionAncho",
                table: "FlujosFirma",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PosicionNumeroPagina",
                table: "FlujosFirma",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PosicionX",
                table: "FlujosFirma",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PosicionY",
                table: "FlujosFirma",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirmadoEn",
                table: "FlujosFirma");

            migrationBuilder.DropColumn(
                name: "PosicionAlto",
                table: "FlujosFirma");

            migrationBuilder.DropColumn(
                name: "PosicionAncho",
                table: "FlujosFirma");

            migrationBuilder.DropColumn(
                name: "PosicionNumeroPagina",
                table: "FlujosFirma");

            migrationBuilder.DropColumn(
                name: "PosicionX",
                table: "FlujosFirma");

            migrationBuilder.DropColumn(
                name: "PosicionY",
                table: "FlujosFirma");
        }
    }
}
