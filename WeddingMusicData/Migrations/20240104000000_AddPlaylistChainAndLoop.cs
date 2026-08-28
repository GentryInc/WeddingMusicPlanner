using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeddingMusicData.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistChainAndLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLooping",
                table: "PlaylistSections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NextSectionId",
                table: "PlaylistSections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistSections_NextSectionId",
                table: "PlaylistSections",
                column: "NextSectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlaylistSections_PlaylistSections_NextSectionId",
                table: "PlaylistSections",
                column: "NextSectionId",
                principalTable: "PlaylistSections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlaylistSections_PlaylistSections_NextSectionId",
                table: "PlaylistSections");

            migrationBuilder.DropIndex(
                name: "IX_PlaylistSections_NextSectionId",
                table: "PlaylistSections");

            migrationBuilder.DropColumn(
                name: "IsLooping",
                table: "PlaylistSections");

            migrationBuilder.DropColumn(
                name: "NextSectionId",
                table: "PlaylistSections");
        }
    }
}
