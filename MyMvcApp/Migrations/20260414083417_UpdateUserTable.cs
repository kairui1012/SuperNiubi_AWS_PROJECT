using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyMvcApp.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUserTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "Users");

            migrationBuilder.AddColumn<bool>(
                name: "IsDisabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDisabled",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
