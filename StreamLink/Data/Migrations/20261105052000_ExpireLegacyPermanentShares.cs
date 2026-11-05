using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StreamLink.Data;

#nullable disable

namespace StreamLink.Data.Migrations;

[DbContext(typeof(StreamLinkDbContext))]
[Migration("20261105052000_ExpireLegacyPermanentShares")]
public sealed class ExpireLegacyPermanentShares : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("""
            UPDATE "ShareLinks"
            SET "ExpiresUtc" = strftime('%Y-%m-%d %H:%M:%f', 'now', '+24 hours')
            WHERE "ExpiresUtc" LIKE '9999-12-31%';
            """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
