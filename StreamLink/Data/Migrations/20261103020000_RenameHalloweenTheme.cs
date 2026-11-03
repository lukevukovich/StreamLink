using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StreamLink.Data;

#nullable disable

namespace StreamLink.Data.Migrations;

[DbContext(typeof(StreamLinkDbContext))]
[Migration("20261103020000_RenameHalloweenTheme")]
public sealed class RenameHalloweenTheme : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            UPDATE "ThemePreferences"
            SET "Theme" = 'halloween'
            WHERE "Theme" = 'jack-o-lantern';
            """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            UPDATE "ThemePreferences"
            SET "Theme" = 'jack-o-lantern'
            WHERE "Theme" = 'halloween';
            """);
}
