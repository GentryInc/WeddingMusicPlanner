using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeddingMusicData.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackVibeAttributes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mood",
                table: "Tracks",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Energy",
                table: "Tracks",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Danceability",
                table: "Tracks",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_Genre",
                table: "Tracks",
                column: "Genre");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tracks_Genre",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "Mood",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "Energy",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "Danceability",
                table: "Tracks");
        }
    }
}
