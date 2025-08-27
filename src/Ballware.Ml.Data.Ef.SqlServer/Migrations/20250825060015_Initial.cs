using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ballware.Ml.Data.Ef.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ml_model",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    uuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    identifier = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    type = table.Column<int>(type: "int", nullable: false),
                    train_sql = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    train_state = table.Column<int>(type: "int", nullable: false),
                    train_result = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    options = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    creator_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    create_stamp = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_changer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    last_change_stamp = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ml_model", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ml_model_tenant_id",
                table: "ml_model",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ml_model_tenant_id_identifier",
                table: "ml_model",
                columns: new[] { "tenant_id", "identifier" },
                unique: true,
                filter: "[identifier] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ml_model_tenant_id_uuid",
                table: "ml_model",
                columns: new[] { "tenant_id", "uuid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ml_model");
        }
    }
}
