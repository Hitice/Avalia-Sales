using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class LinksCurtos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "codigos_de_link",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codigos_de_link", x => x.codigo);
                    table.ForeignKey(
                        name: "fk_codigos_de_link_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character(6)", fixedLength: true, maxLength: 6, nullable: false),
                    titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    destino = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    cliques = table.Column<long>(type: "bigint", nullable: false),
                    ultimo_clique_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_links_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_links_codigos_de_link_codigo",
                        column: x => x.codigo,
                        principalTable: "codigos_de_link",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_links_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_codigos_de_link_revendedor_id",
                table: "codigos_de_link",
                column: "revendedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_links_cliente_id",
                table: "links",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_links_codigo",
                table: "links",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_links_revendedor_id_destino",
                table: "links",
                columns: new[] { "revendedor_id", "destino" });

            // Escrito à mão: o EF não gera RLS. codigos_de_link fica sem, como placas_emitidas: o clique chega sem login.
            migrationBuilder.IsolarPorRevendedor("links");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "links");

            migrationBuilder.DropTable(
                name: "codigos_de_link");
        }
    }
}
