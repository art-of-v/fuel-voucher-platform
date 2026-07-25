using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "providers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Config_LogoText = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Config_DefaultColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Config_Template = table.Column<string>(type: "text", nullable: false),
                    Config_StationIds = table.Column<string>(type: "jsonb", nullable: false),
                    Config_Settings = table.Column<Dictionary<string, JsonElement>>(type: "jsonb", nullable: false),
                    Config_ParsingRules = table.Column<Dictionary<string, JsonElement>>(type: "jsonb", nullable: false),
                    Config_DetectionKeywords = table.Column<string>(type: "jsonb", nullable: false),
                    Config_FuelTypePatterns = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_providers", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "providers");
        }
    }
}
