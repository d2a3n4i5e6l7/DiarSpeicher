using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiarSpeicher.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LibraryId = table.Column<string>(type: "TEXT", nullable: true),
                    ConvertRarToZip = table.Column<bool>(type: "INTEGER", nullable: false),
                    HardDeleteConversions = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultReadingDir = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultReadingMode = table.Column<string>(type: "TEXT", nullable: false),
                    GenerateFileHashes = table.Column<bool>(type: "INTEGER", nullable: false),
                    GenerateKoreaderHashes = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProcessMetadata = table.Column<bool>(type: "INTEGER", nullable: false),
                    Watch = table.Column<bool>(type: "INTEGER", nullable: false),
                    LibraryPattern = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultLibraryViewMode = table.Column<string>(type: "TEXT", nullable: false),
                    HideSeriesView = table.Column<bool>(type: "INTEGER", nullable: false),
                    LibraryType = table.Column<string>(type: "TEXT", nullable: false),
                    ThumbnailWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    ThumbnailHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    IgnoreRules = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScannedDirectories",
                columns: table => new
                {
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    LastMTime = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScannedDirectories", x => x.Path);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsServerOwner = table.Column<bool>(type: "INTEGER", nullable: false),
                    AvatarPath = table.Column<string>(type: "TEXT", nullable: true),
                    AvatarMeta = table.Column<string>(type: "TEXT", nullable: true),
                    AvatarUpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Libraries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Emoji = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailPath = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailMeta = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastScannedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConfigId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Libraries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Libraries_LibraryConfigs_ConfigId",
                        column: x => x.ConfigId,
                        principalTable: "LibraryConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgeRestrictions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Age = table.Column<int>(type: "INTEGER", nullable: false),
                    RestrictOnUnset = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgeRestrictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgeRestrictions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Locale = table.Column<string>(type: "TEXT", nullable: false),
                    AppTheme = table.Column<string>(type: "TEXT", nullable: false),
                    AppFont = table.Column<string>(type: "TEXT", nullable: false),
                    EnableCompactDisplay = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibraryExclusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LibraryId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryExclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryExclusions_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LibraryExclusions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Series",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ThumbnailPath = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailMeta = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LibraryId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Series_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Media",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Pages = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: true),
                    KoreaderHash = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailPath = table.Column<string>(type: "TEXT", nullable: true),
                    ThumbnailMeta = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SeriesId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Media", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Media_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SeriesMetadata",
                columns: table => new
                {
                    SeriesId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AgeRating = table.Column<int>(type: "INTEGER", nullable: true),
                    Characters = table.Column<string>(type: "TEXT", nullable: true),
                    Booktype = table.Column<string>(type: "TEXT", nullable: true),
                    Comicid = table.Column<int>(type: "INTEGER", nullable: true),
                    ComicImage = table.Column<string>(type: "TEXT", nullable: true),
                    DescriptionFormatted = table.Column<string>(type: "TEXT", nullable: true),
                    Genres = table.Column<string>(type: "TEXT", nullable: true),
                    Imprint = table.Column<string>(type: "TEXT", nullable: true),
                    Links = table.Column<string>(type: "TEXT", nullable: true),
                    MetaType = table.Column<string>(type: "TEXT", nullable: true),
                    PublicationRun = table.Column<string>(type: "TEXT", nullable: true),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    TotalIssues = table.Column<int>(type: "INTEGER", nullable: true),
                    Volume = table.Column<int>(type: "INTEGER", nullable: true),
                    Writers = table.Column<string>(type: "TEXT", nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesMetadata", x => x.SeriesId);
                    table.ForeignKey(
                        name: "FK_SeriesMetadata_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeriesTags",
                columns: table => new
                {
                    SeriesId = table.Column<string>(type: "TEXT", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeriesTags", x => new { x.SeriesId, x.TagId });
                    table.ForeignKey(
                        name: "FK_SeriesTags_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeriesTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaMetadata",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    AgeRating = table.Column<int>(type: "INTEGER", nullable: true),
                    Characters = table.Column<string>(type: "TEXT", nullable: true),
                    Colorists = table.Column<string>(type: "TEXT", nullable: true),
                    CoverArtists = table.Column<string>(type: "TEXT", nullable: true),
                    Format = table.Column<string>(type: "TEXT", nullable: true),
                    Day = table.Column<int>(type: "INTEGER", nullable: true),
                    Editors = table.Column<string>(type: "TEXT", nullable: true),
                    Genres = table.Column<string>(type: "TEXT", nullable: true),
                    IdentifierAmazon = table.Column<string>(type: "TEXT", nullable: true),
                    IdentifierCalibre = table.Column<string>(type: "TEXT", nullable: true),
                    IdentifierGoogle = table.Column<string>(type: "TEXT", nullable: true),
                    IdentifierIsbn = table.Column<string>(type: "TEXT", nullable: true),
                    IdentifierMobiAsin = table.Column<string>(type: "TEXT", nullable: true),
                    IdentifierUuid = table.Column<string>(type: "TEXT", nullable: true),
                    Inkers = table.Column<string>(type: "TEXT", nullable: true),
                    Language = table.Column<string>(type: "TEXT", nullable: true),
                    Letterers = table.Column<string>(type: "TEXT", nullable: true),
                    Links = table.Column<string>(type: "TEXT", nullable: true),
                    Month = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Number = table.Column<decimal>(type: "TEXT", nullable: true),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Pencillers = table.Column<string>(type: "TEXT", nullable: true),
                    Publisher = table.Column<string>(type: "TEXT", nullable: true),
                    Series = table.Column<string>(type: "TEXT", nullable: true),
                    SeriesGroup = table.Column<string>(type: "TEXT", nullable: true),
                    StoryArc = table.Column<string>(type: "TEXT", nullable: true),
                    StoryArcNumber = table.Column<decimal>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    Teams = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    TitleSort = table.Column<string>(type: "TEXT", nullable: true),
                    Volume = table.Column<int>(type: "INTEGER", nullable: true),
                    Writers = table.Column<string>(type: "TEXT", nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaMetadata", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaMetadata_Media_MediaId",
                        column: x => x.MediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaTags",
                columns: table => new
                {
                    MediaId = table.Column<string>(type: "TEXT", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaTags", x => new { x.MediaId, x.TagId });
                    table.ForeignKey(
                        name: "FK_MediaTags_Media_MediaId",
                        column: x => x.MediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartPage = table.Column<int>(type: "INTEGER", nullable: true),
                    EndPage = table.Column<int>(type: "INTEGER", nullable: true),
                    StartPercentage = table.Column<decimal>(type: "TEXT", nullable: true),
                    EndPercentage = table.Column<decimal>(type: "TEXT", nullable: true),
                    KoreaderProgress = table.Column<string>(type: "TEXT", nullable: true),
                    ElapsedSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                    ReadthroughNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    MediaId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingSessions_Media_MediaId",
                        column: x => x.MediaId,
                        principalTable: "Media",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadingSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgeRestrictions_UserId",
                table: "AgeRestrictions",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Libraries_ConfigId",
                table: "Libraries",
                column: "ConfigId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryExclusions_LibraryId",
                table: "LibraryExclusions",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryExclusions_UserId",
                table: "LibraryExclusions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Media_CreatedAt",
                table: "Media",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Media_DeletedAt",
                table: "Media",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Media_Hash",
                table: "Media",
                column: "Hash");

            migrationBuilder.CreateIndex(
                name: "IX_Media_KoreaderHash",
                table: "Media",
                column: "KoreaderHash");

            migrationBuilder.CreateIndex(
                name: "IX_Media_Path",
                table: "Media",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_Media_SeriesId",
                table: "Media",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaMetadata_MediaId",
                table: "MediaMetadata",
                column: "MediaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaTags_TagId",
                table: "MediaTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_MediaId",
                table: "ReadingSessions",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_UserId_MediaId",
                table: "ReadingSessions",
                columns: new[] { "UserId", "MediaId" });

            migrationBuilder.CreateIndex(
                name: "IX_Series_LibraryId",
                table: "Series",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_Series_Path",
                table: "Series",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesTags_TagId",
                table: "SeriesTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_UserId",
                table: "UserPreferences",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgeRestrictions");

            migrationBuilder.DropTable(
                name: "LibraryExclusions");

            migrationBuilder.DropTable(
                name: "MediaMetadata");

            migrationBuilder.DropTable(
                name: "MediaTags");

            migrationBuilder.DropTable(
                name: "ReadingSessions");

            migrationBuilder.DropTable(
                name: "ScannedDirectories");

            migrationBuilder.DropTable(
                name: "SeriesMetadata");

            migrationBuilder.DropTable(
                name: "SeriesTags");

            migrationBuilder.DropTable(
                name: "UserPreferences");

            migrationBuilder.DropTable(
                name: "Media");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Series");

            migrationBuilder.DropTable(
                name: "Libraries");

            migrationBuilder.DropTable(
                name: "LibraryConfigs");
        }
    }
}
