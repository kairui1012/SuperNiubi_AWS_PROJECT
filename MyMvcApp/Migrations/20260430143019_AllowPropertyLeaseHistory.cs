using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyMvcApp.Migrations
{
    /// <inheritdoc />
    public partial class AllowPropertyLeaseHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_PropertyId",
                table: "Tenants");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_PropertyId",
                table: "Tenants",
                column: "PropertyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_PropertyId",
                table: "Tenants");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_PropertyId",
                table: "Tenants",
                column: "PropertyId",
                unique: true);
        }
    }
}
