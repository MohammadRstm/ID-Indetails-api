using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectPhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "photo_file_id",
                table: "projects",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_projects_photo_file_id",
                table: "projects",
                column: "photo_file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_projects_files_photo_file_id",
                table: "projects",
                column: "photo_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_projects_files_photo_file_id",
                table: "projects");

            migrationBuilder.DropIndex(
                name: "ix_projects_photo_file_id",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "photo_file_id",
                table: "projects");
        }
    }
}
