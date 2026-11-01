using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Triage.Servidor.Banco.Migracoes
{
    /// <inheritdoc />
    public partial class Avisos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "avisos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    maquina_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revisao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    de = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    para = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    detalhe = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    enviado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tentativas = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos", x => x.id);
                    table.ForeignKey(
                        name: "fk_avisos_revisoes_maquina_id_revisao_id",
                        columns: x => new { x.maquina_id, x.revisao_id },
                        principalTable: "revisoes",
                        principalColumns: new[] { "maquina_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_enviado_em",
                table: "avisos",
                column: "enviado_em");

            migrationBuilder.CreateIndex(
                name: "ix_avisos_maquina_id_revisao_id_item",
                table: "avisos",
                columns: new[] { "maquina_id", "revisao_id", "item" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avisos");
        }
    }
}
