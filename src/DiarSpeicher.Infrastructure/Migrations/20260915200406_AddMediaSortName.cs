using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiarSpeicher.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaSortName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SortName",
                table: "Media",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Media_SeriesId_SortName",
                table: "Media",
                columns: new[] { "SeriesId", "SortName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Media_SeriesId_SortName",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "SortName",
                table: "Media");
        }
    }
}
