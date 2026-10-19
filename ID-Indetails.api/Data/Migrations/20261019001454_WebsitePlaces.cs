using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class WebsitePlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hero_credit_photographer",
                table: "website_projects");

            migrationBuilder.DropColumn(
                name: "hero_credit_source",
                table: "website_projects");

            migrationBuilder.DropColumn(
                name: "hero_credit_url",
                table: "website_projects");

            migrationBuilder.DropColumn(
                name: "credit_photographer",
                table: "website_project_images");

            migrationBuilder.DropColumn(
                name: "credit_source",
                table: "website_project_images");

            migrationBuilder.DropColumn(
                name: "credit_url",
                table: "website_project_images");

            migrationBuilder.AddColumn<bool>(
                name: "position_approximate",
                table: "website_projects",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "position_approximate",
                table: "website_locations",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "position_approximate",
                table: "website_projects");

            migrationBuilder.DropColumn(
                name: "position_approximate",
                table: "website_locations");

            migrationBuilder.AddColumn<string>(
                name: "hero_credit_photographer",
                table: "website_projects",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "hero_credit_source",
                table: "website_projects",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "hero_credit_url",
                table: "website_projects",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "credit_photographer",
                table: "website_project_images",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "credit_source",
                table: "website_project_images",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "credit_url",
                table: "website_project_images",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
