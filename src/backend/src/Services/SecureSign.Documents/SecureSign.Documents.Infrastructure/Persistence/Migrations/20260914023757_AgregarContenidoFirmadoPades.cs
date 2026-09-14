using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecureSign.Documents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarContenidoFirmadoPades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "ContenidoFirmadoPades",
                table: "Documentos",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContenidoFirmadoPades",
                table: "Documentos");
        }
    }
}
