using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AvaliaSales.Infrastructure.Banco.Migrations
{
    /// <inheritdoc />
    public partial class CatalogoServicosENotificacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "placa_id",
                table: "vendas",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "produto_da_plataforma_id",
                table: "produtos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "producao_em",
                table: "pedidos_placas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "notificacoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    para_operadora = table.Column<bool>(type: "boolean", nullable: false),
                    titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    texto = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    link = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notificacoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_notificacoes_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "produtos_da_plataforma",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chave = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    descricao = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    preco_sugerido_cents = table.Column<long>(type: "bigint", nullable: false),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ordem = table.Column<int>(type: "integer", nullable: false),
                    foto_padrao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    foto = table.Column<byte[]>(type: "bytea", nullable: true),
                    foto_tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    foto_versao = table.Column<int>(type: "integer", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_produtos_da_plataforma", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ordens_de_servico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    revendedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    venda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    produto_da_plataforma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    produto = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    cliente = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    situacao = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    observacao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    iniciada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entregue_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ordens_de_servico", x => x.id);
                    table.ForeignKey(
                        name: "fk_ordens_de_servico_produtos_da_plataforma_produto_da_platafo",
                        column: x => x.produto_da_plataforma_id,
                        principalTable: "produtos_da_plataforma",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ordens_de_servico_revendedores_revendedor_id",
                        column: x => x.revendedor_id,
                        principalTable: "revendedores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ordens_de_servico_vendas_venda_id",
                        column: x => x.venda_id,
                        principalTable: "vendas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_produtos_produto_da_plataforma_id",
                table: "produtos",
                column: "produto_da_plataforma_id");

            migrationBuilder.CreateIndex(
                name: "ix_produtos_revendedor_id_produto_da_plataforma_id",
                table: "produtos",
                columns: new[] { "revendedor_id", "produto_da_plataforma_id" },
                unique: true,
                filter: "produto_da_plataforma_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_para_operadora_revendedor_id_criada_em",
                table: "notificacoes",
                columns: new[] { "para_operadora", "revendedor_id", "criada_em" });

            migrationBuilder.CreateIndex(
                name: "ix_notificacoes_revendedor_id",
                table: "notificacoes",
                column: "revendedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_ordens_de_servico_numero",
                table: "ordens_de_servico",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ordens_de_servico_produto_da_plataforma_id",
                table: "ordens_de_servico",
                column: "produto_da_plataforma_id");

            migrationBuilder.CreateIndex(
                name: "ix_ordens_de_servico_revendedor_id",
                table: "ordens_de_servico",
                column: "revendedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_ordens_de_servico_situacao_criada_em",
                table: "ordens_de_servico",
                columns: new[] { "situacao", "criada_em" });

            migrationBuilder.CreateIndex(
                name: "ix_ordens_de_servico_venda_id",
                table: "ordens_de_servico",
                column: "venda_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_produtos_da_plataforma_chave",
                table: "produtos_da_plataforma",
                column: "chave",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_produtos_produtos_da_plataforma_produto_da_plataforma_id",
                table: "produtos",
                column: "produto_da_plataforma_id",
                principalTable: "produtos_da_plataforma",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Escrito à mão: o EF não gera RLS. A operadora atende as ordens e recebe as notificações dela.
            migrationBuilder.IsolarPorRevendedor("ordens_de_servico", operadoraAtende: true);
            migrationBuilder.IsolarPorRevendedor("notificacoes", operadoraAtende: true);

            // O catálogo da Avalia como estava no site em 22/10/2026; a operadora ajusta pela tela.
            migrationBuilder.Sql("""
                INSERT INTO produtos_da_plataforma (id, chave, nome, descricao, tipo, preco_sugerido_cents, situacao, ordem, foto_padrao, foto_versao, criado_em) VALUES
                (gen_random_uuid(), 'placa-avaliacao-google', 'Placa Avaliação Google NFC + QR Code', 'Placa em acrílico com NFC e QR Code que leva o cliente direto à avaliação no Google.', 'Placa', 9990, 'Disponivel', 1, '/produtos/placa-avaliacao-google.jpg', 0, now()),
                (gen_random_uuid(), 'perfil-google-placa', 'Perfil no Google com placa de avaliação', 'Criação e otimização do Perfil da Empresa no Google, com a placa de avaliação NFC inclusa.', 'PlacaComServico', 49900, 'Disponivel', 2, '/produtos/perfil-google-placa.jpg', 0, now()),
                (gen_random_uuid(), 'kit-mesa', 'Kit Mesa: cardápio, QR Code e NFC', 'Placa de mesa frente e verso com cardápio, QR Code e NFC. Preço por unidade, pedido mínimo de 10.', 'Servico', 3990, 'Disponivel', 3, '/produtos/kit-mesa.jpg', 0, now()),
                (gen_random_uuid(), 'placa-cardapio-digital', 'Placa Cardápio Digital NFC + QR Code', 'Placa com NFC e QR Code que abre o cardápio digital. Preço por unidade, pedido mínimo de 10.', 'Servico', 3990, 'Disponivel', 4, '/produtos/placa-cardapio-digital.jpg', 0, now()),
                (gen_random_uuid(), 'cardapio-digital-profissional', 'Cardápio Digital Profissional', 'Cardápio digital com link exclusivo, categorias, fotos e preços.', 'Servico', 49700, 'Disponivel', 5, '/produtos/cardapio-digital-profissional.jpg', 0, now()),
                (gen_random_uuid(), 'sistema-de-gestao', 'Sistema de Gestão e Controle de Caixa', 'Sistema de gestão com controle de caixa. Manutenção à parte.', 'Servico', 89900, 'Disponivel', 6, '/produtos/sistema-de-gestao.jpg', 0, now()),
                (gen_random_uuid(), 'landing-page', 'Landing Page', 'Página com produtos, formulário e WhatsApp. Manutenção à parte.', 'Servico', 89900, 'Disponivel', 7, '/produtos/landing-page.jpg', 0, now()),
                (gen_random_uuid(), 'qr-dinamico-pro', 'QR Code Dinâmico Pro', 'QR Code com destino editável e link avaliaone.com.br/seu-negocio. Preço por ano.', 'Servico', 3990, 'Disponivel', 8, NULL, 0, now());
                """);

            // Pacotes de placas da revenda: 20, 50 e 100 a R$ 23,90, 16,90 e 13,90 a placa. Os outros ficam como estão.
            migrationBuilder.Sql("""
                INSERT INTO pacotes (id, quantidade, preco_cents, ativo) VALUES
                (gen_random_uuid(), 20, 47800, true), (gen_random_uuid(), 50, 84500, true), (gen_random_uuid(), 100, 139000, true)
                ON CONFLICT (quantidade) DO UPDATE SET preco_cents = EXCLUDED.preco_cents, ativo = true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_produtos_produtos_da_plataforma_produto_da_plataforma_id",
                table: "produtos");

            migrationBuilder.DropTable(
                name: "notificacoes");

            migrationBuilder.DropTable(
                name: "ordens_de_servico");

            migrationBuilder.DropTable(
                name: "produtos_da_plataforma");

            migrationBuilder.DropIndex(
                name: "ix_produtos_produto_da_plataforma_id",
                table: "produtos");

            migrationBuilder.DropIndex(
                name: "ix_produtos_revendedor_id_produto_da_plataforma_id",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "produto_da_plataforma_id",
                table: "produtos");

            migrationBuilder.DropColumn(
                name: "producao_em",
                table: "pedidos_placas");

            migrationBuilder.AlterColumn<Guid>(
                name: "placa_id",
                table: "vendas",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
