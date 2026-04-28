using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyMvcApp.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyGovernanceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "Properties",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AvailabilityStatus",
                table: "Properties",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Properties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Properties",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Properties_ApprovalStatus",
                table: "Properties",
                column: "ApprovalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_AvailabilityStatus",
                table: "Properties",
                column: "AvailabilityStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_IsDeleted",
                table: "Properties",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Properties_ApprovalStatus",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_AvailabilityStatus",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Properties_IsDeleted",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "AvailabilityStatus",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Properties");
        }
    }
}
