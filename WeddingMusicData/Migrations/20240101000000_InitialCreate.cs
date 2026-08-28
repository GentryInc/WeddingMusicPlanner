using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeddingMusicData.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaylistSections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    TransitionMode = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultCrossfadeMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistSections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tracks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ExternalUri = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Format = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsCachedOffline = table.Column<bool>(type: "INTEGER", nullable: false),
                    CachedFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    CachedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Artist = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Album = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Genre = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    TrackNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Bpm = table.Column<double>(type: "REAL", nullable: true),
                    LoudnessLufs = table.Column<double>(type: "REAL", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastIngestedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsAvailable = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tracks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CueSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartMs = table.Column<long>(type: "INTEGER", nullable: false),
                    StopMs = table.Column<long>(type: "INTEGER", nullable: true),
                    FadeInMs = table.Column<int>(type: "INTEGER", nullable: false),
                    FadeOutMs = table.Column<int>(type: "INTEGER", nullable: false),
                    FadeInCurve = table.Column<int>(type: "INTEGER", nullable: false),
                    FadeOutCurve = table.Column<int>(type: "INTEGER", nullable: false),
                    MixInPointMs = table.Column<long>(type: "INTEGER", nullable: true),
                    MixOutPointMs = table.Column<long>(type: "INTEGER", nullable: true),
                    GainTrimDb = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CueSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CueSettings_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaylistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlaylistSectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TrackId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    TransitionModeOverride = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaylistItems_PlaylistSections_PlaylistSectionId",
                        column: x => x.PlaylistSectionId,
                        principalTable: "PlaylistSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaylistItems_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CueSettings_TrackId",
                table: "CueSettings",
                column: "TrackId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_PlaylistSectionId_Position",
                table: "PlaylistItems",
                columns: new[] { "PlaylistSectionId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_TrackId",
                table: "PlaylistItems",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistSections_Order",
                table: "PlaylistSections",
                column: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_Artist_Title",
                table: "Tracks",
                columns: new[] { "Artist", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_Bpm",
                table: "Tracks",
                column: "Bpm");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_ContentHash",
                table: "Tracks",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_FilePath",
                table: "Tracks",
                column: "FilePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_IsAvailable",
                table: "Tracks",
                column: "IsAvailable");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_IsCachedOffline",
                table: "Tracks",
                column: "IsCachedOffline");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_Title",
                table: "Tracks",
                column: "Title");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CueSettings");

            migrationBuilder.DropTable(
                name: "PlaylistItems");

            migrationBuilder.DropTable(
                name: "PlaylistSections");

            migrationBuilder.DropTable(
                name: "Tracks");
        }
    }
}
