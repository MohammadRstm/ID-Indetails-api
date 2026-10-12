using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MaterialClientRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing materials keep no reference; the unique index allows any number of NULLs.
            migrationBuilder.AddColumn<Guid>(
                name: "client_ref",
                table: "project_materials",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            // The new index starts with added_by_id, so it serves the foreign key; MySQL only lets the old index go once
            // another one does.
            migrationBuilder.CreateIndex(
                name: "ix_project_materials_added_by_id_client_ref",
                table: "project_materials",
                columns: new[] { "added_by_id", "client_ref" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "ix_project_materials_added_by_id",
                table: "project_materials");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_project_materials_added_by_id",
                table: "project_materials",
                column: "added_by_id");

            migrationBuilder.DropIndex(
                name: "ix_project_materials_added_by_id_client_ref",
                table: "project_materials");

            migrationBuilder.DropColumn(
                name: "client_ref",
                table: "project_materials");
        }
    }
}
