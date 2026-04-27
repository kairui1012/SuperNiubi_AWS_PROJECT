using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyMvcApp.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeEventBridgePaymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripeEventId",
                table: "Payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripePaymentIntentId",
                table: "Payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeReceiptUrl",
                table: "Payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeSessionId",
                table: "Payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_StripeEventId",
                table: "Payments",
                column: "StripeEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_StripePaymentIntentId",
                table: "Payments",
                column: "StripePaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_StripeSessionId",
                table: "Payments",
                column: "StripeSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_StripeEventId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_StripePaymentIntentId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_StripeSessionId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "StripeEventId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "StripePaymentIntentId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "StripeReceiptUrl",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "StripeSessionId",
                table: "Payments");
        }
    }
}
