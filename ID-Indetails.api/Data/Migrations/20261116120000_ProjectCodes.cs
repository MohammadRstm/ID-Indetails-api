using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// Every project gets a project code (ID26Q4001, unique in its domain) and a building code (the name's initials).
    /// Existing projects are numbered per domain and per quarter of their start date, in start date order, and their
    /// building codes follow <see cref="Business.Projects.ProjectCodeRules.BuildingCodeOf"/>, except that an accented
    /// first letter is left out rather than unaccented. A name without initials gets X.
    /// </summary>
    public partial class ProjectCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "building_code",
                table: "projects",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "project_code",
                table: "projects",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                """
                UPDATE `projects` p
                JOIN (
                    SELECT `id`,
                        CONCAT('ID', DATE_FORMAT(`start_date`, '%y'), 'Q', QUARTER(`start_date`),
                            LPAD(ROW_NUMBER() OVER (PARTITION BY `domain_code`, YEAR(`start_date`), QUARTER(`start_date`)
                                ORDER BY `start_date`, `created_at`, `id`), 3, '0')) AS `code`
                    FROM `projects`
                ) n ON n.`id` = p.`id`
                SET p.`project_code` = n.`code`;
                """);

            // each word of the name in turn: its first letter, or all of a number; small words are skipped
            migrationBuilder.Sql(
                """
                WITH RECURSIVE `words` (`id`, `n`, `word`) AS (
                    SELECT `id`, 1, REGEXP_SUBSTR(`title`, '[[:alnum:]]+', 1, 1) FROM `projects`
                    UNION ALL
                    SELECT w.`id`, w.`n` + 1, REGEXP_SUBSTR(t.`title`, '[[:alnum:]]+', 1, w.`n` + 1)
                    FROM `words` w JOIN `projects` t ON t.`id` = w.`id`
                    WHERE w.`word` IS NOT NULL
                ),
                `initials` AS (
                    SELECT `id`,
                        LEFT(REGEXP_REPLACE(UPPER(GROUP_CONCAT(
                            CASE WHEN `word` REGEXP '^[0-9]' THEN REGEXP_SUBSTR(`word`, '^[0-9]+') ELSE LEFT(`word`, 1) END
                            ORDER BY `n` SEPARATOR '')), '[^A-Z0-9]', '', 1, 0, 'c'), 10) AS `code`
                    FROM `words`
                    WHERE `word` IS NOT NULL
                        AND LOWER(`word`) NOT IN ('of', 'the', 'and', 'an', 'de', 'du', 'des', 'la', 'le', 'el', 'al')
                    GROUP BY `id`
                )
                UPDATE `projects` p
                LEFT JOIN `initials` i ON i.`id` = p.`id`
                SET p.`building_code` = COALESCE(NULLIF(i.`code`, ''), 'X');
                """);

            migrationBuilder.AlterColumn<string>(
                name: "building_code",
                table: "projects",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(10)",
                oldMaxLength: 10,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "project_code",
                table: "projects",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(16)",
                oldMaxLength: 16,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_projects_domain_code_project_code",
                table: "projects",
                columns: new[] { "domain_code", "project_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_projects_domain_code_project_code",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "building_code",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "project_code",
                table: "projects");
        }
    }
}
