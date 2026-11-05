using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StreamLink.Data;

#nullable disable

namespace StreamLink.Data.Migrations;

[DbContext(typeof(StreamLinkDbContext))]
[Migration("20261104220000_AddShareSortPreference")]
public sealed class AddShareSortPreference : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(
            name: "ShareSort",
            table: "ThemePreferences",
            type: "TEXT",
            maxLength: 16,
            nullable: false,
            defaultValue: "expiring");

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(
            name: "ShareSort",
            table: "ThemePreferences");
}
