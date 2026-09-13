using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiarSpeicher.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEpubProfilesToUserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EpubProfilesJson",
                table: "UserPreferences",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EpubProfilesJson",
                table: "UserPreferences");
        }
    }
}
