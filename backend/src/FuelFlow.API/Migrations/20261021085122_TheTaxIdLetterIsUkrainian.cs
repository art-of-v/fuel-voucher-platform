using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheTaxIdLetterIsUkrainian : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "tax_id",
                table: "natural_persons",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "ІПН - the personal tax number. Nullable: most users never provide one, and a guessed tax idis worse than none.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "IПН - the personal tax number. Nullable: most users never provide one, and a guessed tax idis worse than none.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "tax_id",
                table: "natural_persons",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "IПН - the personal tax number. Nullable: most users never provide one, and a guessed tax idis worse than none.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "ІПН - the personal tax number. Nullable: most users never provide one, and a guessed tax idis worse than none.");
        }
    }
}
