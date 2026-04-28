using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MyMvcApp.Data;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MyMvcApp.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260427090000_AddLeaseHistories")]
    public partial class AddLeaseHistories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeaseHistories",
                columns: table => new
                {
                    LeaseHistoryId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ChangedByEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseHistories", x => x.LeaseHistoryId);
                    table.ForeignKey(
                        name: "FK_LeaseHistories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaseHistories_Action",
                table: "LeaseHistories",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_LeaseHistories_CreatedAt",
                table: "LeaseHistories",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LeaseHistories_TenantId",
                table: "LeaseHistories",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeaseHistories");
        }
    }
}
