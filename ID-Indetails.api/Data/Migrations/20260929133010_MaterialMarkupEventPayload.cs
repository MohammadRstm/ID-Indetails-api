using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ID_Indetails.api.Data.Migrations
{
    /// <summary>
    /// Moves the markup of existing <c>material_markup_set</c> events from the amount columns (2 decimals, so 12.5% was
    /// saved as 0.13) into the payload as <c>markup</c> and <c>prevMarkup</c>. Each project's latest event gets the
    /// project's current markup, which is exact; older events and every <c>prevMarkup</c> keep the rounded value, since
    /// the exact one wasn't stored. Events already in the new shape are left alone.
    /// </summary>
    public partial class MaterialMarkupEventPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The latest event set the markup the project still has: take it exactly when it matches the rounded amount.
            migrationBuilder.Sql("""
                UPDATE activity_events e
                JOIN (SELECT project_id, MAX(id) AS id FROM activity_events WHERE kind = 'material_markup_set' GROUP BY project_id) latest
                    ON latest.id = e.id
                JOIN projects p ON p.id = e.project_id
                SET e.payload = JSON_SET(COALESCE(e.payload, JSON_OBJECT()), '$.markup', p.material_markup)
                WHERE NOT JSON_CONTAINS_PATH(COALESCE(e.payload, JSON_OBJECT()), 'one', '$.markup')
                  AND ROUND(p.material_markup, 2) = e.amount;
                """);

            // Every other old event keeps the rounded markup it was saved with.
            migrationBuilder.Sql("""
                UPDATE activity_events
                SET payload = JSON_SET(COALESCE(payload, JSON_OBJECT()), '$.markup', amount)
                WHERE kind = 'material_markup_set'
                  AND NOT JSON_CONTAINS_PATH(COALESCE(payload, JSON_OBJECT()), 'one', '$.markup');
                """);

            // The previous markup, then the amount columns are emptied. New-shape events already have prevMarkup.
            migrationBuilder.Sql("""
                UPDATE activity_events
                SET payload = JSON_SET(payload, '$.prevMarkup', prev_amount), amount = NULL, prev_amount = NULL
                WHERE kind = 'material_markup_set'
                  AND NOT JSON_CONTAINS_PATH(payload, 'one', '$.prevMarkup');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to the amount columns, rounded to their 2 decimals; assignments run left to right, payload last.
            migrationBuilder.Sql("""
                UPDATE activity_events
                SET amount = ROUND(JSON_VALUE(payload, '$.markup' RETURNING DECIMAL(18, 4)), 2),
                    prev_amount = ROUND(JSON_VALUE(payload, '$.prevMarkup' RETURNING DECIMAL(18, 4)), 2),
                    payload = NULLIF(JSON_REMOVE(payload, '$.markup', '$.prevMarkup'), JSON_OBJECT())
                WHERE kind = 'material_markup_set';
                """);
        }
    }
}
