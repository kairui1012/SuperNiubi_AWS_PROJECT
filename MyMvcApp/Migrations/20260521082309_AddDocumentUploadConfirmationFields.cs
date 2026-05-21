using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyMvcApp.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentUploadConfirmationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "Documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "S3ETag",
                table: "Documents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploadId",
                table: "Documents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UploadStatus",
                table: "Documents",
                type: "text",
                nullable: false,
                defaultValue: "Confirmed");

            migrationBuilder.AddColumn<DateTime>(
                name: "UploadUrlExpiresAt",
                table: "Documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationMessage",
                table: "Documents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_FileKey",
                table: "Documents",
                column: "FileKey");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UploadId",
                table: "Documents",
                column: "UploadId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UploadStatus",
                table: "Documents",
                column: "UploadStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_FileKey",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_UploadId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_UploadStatus",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "S3ETag",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "UploadId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "UploadStatus",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "UploadUrlExpiresAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ValidationMessage",
                table: "Documents");
        }
    }
}
