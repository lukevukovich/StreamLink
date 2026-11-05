using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StreamLink.Data;

#nullable disable

namespace StreamLink.Data.Migrations;

[DbContext(typeof(StreamLinkDbContext))]
[Migration("20261105053200_DefaultShareSortToNewest")]
public sealed class DefaultShareSortToNewest : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            UPDATE "ThemePreferences"
            SET "ShareSort" = 'newest'
            WHERE "ShareSort" = 'expiring';
            """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            UPDATE "ThemePreferences"
            SET "ShareSort" = 'expiring'
            WHERE "ShareSort" = 'newest';
            """);
}
