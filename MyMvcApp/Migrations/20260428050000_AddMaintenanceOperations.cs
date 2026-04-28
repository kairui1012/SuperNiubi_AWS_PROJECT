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
    [Migration("20260428050000_AddMaintenanceOperations")]
    public partial class AddMaintenanceOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedVendor",
                table: "MaintenanceRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedRepairCost",
                table: "MaintenanceRequests",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepairImageKey",
                table: "MaintenanceRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MaintenanceTimelines",
                columns: table => new
                {
                    MaintenanceTimelineId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ActorEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceTimelines", x => x.MaintenanceTimelineId);
                    table.ForeignKey(
                        name: "FK_MaintenanceTimelines_MaintenanceRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "MaintenanceRequests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTimelines_CreatedAt",
                table: "MaintenanceTimelines",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTimelines_RequestId",
                table: "MaintenanceTimelines",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceTimelines");

            migrationBuilder.DropColumn(
                name: "AssignedVendor",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "EstimatedRepairCost",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "RepairImageKey",
                table: "MaintenanceRequests");
        }
    }
}
