using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// Every project takes a colour of the new thirty-colour palette (<see cref="Business.Projects.ProjectColorRules.Palette"/>,
    /// 18 November 2026), which replaces the sixteen colours and any picked on the colour wheel. Each business on its
    /// own: its active and on-hold projects first, oldest first, so up to thirty of them all differ; then the others.
    /// The palette is walked seven at a time, so projects next to each other get colours far apart. No column changes.
    /// </summary>
    public partial class ProjectPalette : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE `projects` p
                JOIN (
                    SELECT `id`, ROW_NUMBER() OVER (PARTITION BY `domain_code` ORDER BY `status` IN ('active', 'hold') DESC, `id`) - 1 AS `n`
                    FROM `projects`
                ) x ON x.`id` = p.`id`
                SET p.`tint` = ELT(MOD(x.`n` * 7, 30) + 1,
                    '#A53860', '#DA627D', '#3D2C2E', '#6E0D25', '#A26769', '#81171B',
                    '#FE5F55', '#C75146', '#AD2E24', '#6A381F', '#DA7422', '#6F5E53',
                    '#8E6E53', '#A59132', '#625834', '#48483A', '#808F85', '#2A7F62',
                    '#26413C', '#407076', '#0B525B', '#1B9AAA', '#4C5760', '#748CAB',
                    '#1D2D44', '#4B4E6D', '#6A6B83', '#4D194D', '#6D466B', '#A28F9D');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // the old colours aren't kept: the projects stay in the new palette
        }
    }
}
