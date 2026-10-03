using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace PlantOps.BuildingBlocks.Infrastructure;

public static class UniqueViolation
{
    // SQL Server error numbers for unique index (2601) and unique constraint (2627) violations.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public static bool Is(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation };
}
