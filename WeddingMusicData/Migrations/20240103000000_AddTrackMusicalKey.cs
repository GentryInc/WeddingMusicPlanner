using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeddingMusicData.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackMusicalKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MusicalKey",
                table: "Tracks",
                type: "TEXT",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MusicalKey",
                table: "Tracks");
        }
    }
}
