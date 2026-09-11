using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiarSpeicher.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaPages",
                columns: table => new
                {
                    MediaId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaPages", x => new { x.MediaId, x.Number });
                    table.ForeignKey(
                        name: "FK_MediaPages_Media_MediaId",
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
                name: "MediaPages");
        }
    }
}
