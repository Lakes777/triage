using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Triage.Servidor.Banco.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maquinas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    apelido = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    chave_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    inscrita_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    revogada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    demo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maquinas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "codigos_inscricao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    maquina_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codigos_inscricao", x => x.id);
                    table.ForeignKey(
                        name: "fk_codigos_inscricao_maquinas_maquina_id",
                        column: x => x.maquina_id,
                        principalTable: "maquinas",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "revisoes",
                columns: table => new
                {
                    maquina_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feita_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recebida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    versao_do_agente = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    administrador = table.Column<bool>(type: "boolean", nullable: false),
                    conteudo_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    foto = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revisoes", x => new { x.maquina_id, x.id });
                    table.ForeignKey(
                        name: "fk_revisoes_maquinas_maquina_id",
                        column: x => x.maquina_id,
                        principalTable: "maquinas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resultados",
                columns: table => new
                {
                    maquina_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revisao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    gravidade = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    motivo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resultados", x => new { x.maquina_id, x.revisao_id, x.item });
                    table.ForeignKey(
                        name: "fk_resultados_revisoes_maquina_id_revisao_id",
                        columns: x => new { x.maquina_id, x.revisao_id },
                        principalTable: "revisoes",
                        principalColumns: new[] { "maquina_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_codigos_inscricao_codigo_hash",
                table: "codigos_inscricao",
                column: "codigo_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_codigos_inscricao_maquina_id",
                table: "codigos_inscricao",
                column: "maquina_id");

            migrationBuilder.CreateIndex(
                name: "ix_maquinas_chave_hash",
                table: "maquinas",
                column: "chave_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_revisoes_maquina_id_feita_em",
                table: "revisoes",
                columns: new[] { "maquina_id", "feita_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "codigos_inscricao");

            migrationBuilder.DropTable(
                name: "resultados");

            migrationBuilder.DropTable(
                name: "revisoes");

            migrationBuilder.DropTable(
                name: "maquinas");
        }
    }
}
