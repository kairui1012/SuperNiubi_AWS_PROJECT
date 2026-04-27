using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyMvcApp.Migrations
{
    /// <inheritdoc />
    public partial class TenantOpsEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IssueImageKey",
                table: "MaintenanceRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TenantConfirmedAt",
                table: "MaintenanceRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantFeedbackComment",
                table: "MaintenanceRequests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantFeedbackRating",
                table: "MaintenanceRequests",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IssueImageKey",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "TenantConfirmedAt",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "TenantFeedbackComment",
                table: "MaintenanceRequests");

            migrationBuilder.DropColumn(
                name: "TenantFeedbackRating",
                table: "MaintenanceRequests");
        }
    }
}
