using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiarSpeicher.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEpubPageMapAndRenderedProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RenderedPage",
                table: "ReadingSessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenderedProfileKey",
                table: "ReadingSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenderedTotalPages",
                table: "ReadingSessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EpubPageMaps",
                columns: table => new
                {
                    MediaId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProfileKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    TotalPages = table.Column<int>(type: "INTEGER", nullable: false),
                    FileModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    BuiltAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpubPageMaps", x => new { x.MediaId, x.ProfileKey });
                    table.ForeignKey(
                        name: "FK_EpubPageMaps_Media_MediaId",
                        column: x => x.MediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EpubPageMaps");

            migrationBuilder.DropColumn(
                name: "RenderedPage",
                table: "ReadingSessions");

            migrationBuilder.DropColumn(
                name: "RenderedProfileKey",
                table: "ReadingSessions");

            migrationBuilder.DropColumn(
                name: "RenderedTotalPages",
                table: "ReadingSessions");
        }
    }
}
