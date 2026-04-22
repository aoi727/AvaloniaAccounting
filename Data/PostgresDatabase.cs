namespace AccountingApp.Data;

public sealed partial class PostgresDatabase
{
    private readonly string _connectionString;

    public PostgresDatabase(string connectionString)
    {
        _connectionString = connectionString;
    }
}

