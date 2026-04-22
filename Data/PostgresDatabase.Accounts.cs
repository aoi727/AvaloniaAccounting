using AccountingApp.Models;
using Npgsql;
using NpgsqlTypes;

namespace AccountingApp.Data;

public sealed partial class PostgresDatabase
{
    public async Task<IReadOnlyList<Account>> GetAccountsAsync(int companyId)
    {
        const string sql = @"
    SELECT account_id, code, name, account_type, balance_side, is_control_account, default_tax_code_id
    FROM accounts
    WHERE company_id = @company_id
    ORDER BY code";

        var accounts = new List<Account>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            accounts.Add(new Account(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6)));
        }

        return accounts;
    }

    public async Task<IReadOnlyList<Account>> GetControlAccountsAsync(int companyId)
    {
        const string sql = @"
    SELECT account_id, code, name, account_type, balance_side, is_control_account, default_tax_code_id
    FROM accounts
    WHERE company_id = @company_id
      AND is_control_account = TRUE
    ORDER BY code";

        var accounts = new List<Account>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            accounts.Add(new Account(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6)));
        }

        return accounts;
    }

    public async Task<int> CreateAccountAsync(
            int companyId,
            string code,
            string name,
            string accountType,
            string balanceSide,
            bool isControlAccount,
            int? defaultTaxCodeId)
    {
        const string sql = @"
    INSERT INTO accounts (company_id, code, name, account_type, balance_side, is_control_account, default_tax_code_id)
    VALUES (@company_id, @code, @name, @account_type, @balance_side, @is_control_account, @default_tax_code_id)
    RETURNING account_id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("code", code);
            command.Parameters.AddWithValue("name", name);
            command.Parameters.AddWithValue("account_type", accountType);
            command.Parameters.AddWithValue("balance_side", balanceSide);
            command.Parameters.AddWithValue("is_control_account", isControlAccount);
            command.Parameters.Add(new NpgsqlParameter<int?>("default_tax_code_id", NpgsqlDbType.Integer)
            {
                TypedValue = defaultTaxCodeId
            });

            var result = await command.ExecuteScalarAsync();
            var accountId = Convert.ToInt32(result);

            await InsertDefaultSubAccountAsync(connection, transaction, companyId, accountId, name);
            await transaction.CommitAsync();
            committed = true;

            await RebuildSubAccountBalancesAsync(companyId);
            return accountId;
        }
        catch
        {
            if (!committed)
            {
                await transaction.RollbackAsync();
            }

            throw;
        }
    }

    public async Task UpdateAccountAsync(
            int companyId,
            int accountId,
            string code,
            string name,
            string accountType,
            string balanceSide,
            bool isControlAccount,
            int? defaultTaxCodeId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        if (!isControlAccount && await HasSubAccountsAsync(connection, companyId, accountId))
        {
            throw new InvalidOperationException("補助科目が登録されているため、「補助科目あり」を外せません。");
        }

        const string sql = @"
    UPDATE accounts
        SET code = @code,
        name = @name,
        account_type = @account_type,
        balance_side = @balance_side,
        is_control_account = @is_control_account,
        default_tax_code_id = @default_tax_code_id
    WHERE company_id = @company_id
      AND account_id = @account_id";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("account_type", accountType);
        command.Parameters.AddWithValue("balance_side", balanceSide);
        command.Parameters.AddWithValue("is_control_account", isControlAccount);
        command.Parameters.Add(new NpgsqlParameter<int?>("default_tax_code_id", NpgsqlDbType.Integer)
        {
            TypedValue = defaultTaxCodeId
        });
        await command.ExecuteNonQueryAsync();
        await RebuildSubAccountBalancesAsync(companyId);
    }

    private static async Task<bool> HasSubAccountsAsync(NpgsqlConnection connection, int companyId, int accountId)
    {
        const string sql = @"
    SELECT COUNT(*)
    FROM sub_accounts
    WHERE company_id = @company_id
      AND account_id = @account_id
      AND code <> '0'";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("account_id", accountId);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    private static async Task InsertDefaultSubAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int companyId,
        int accountId,
        string accountName)
    {
        const string sql = @"
    INSERT INTO sub_accounts (company_id, account_id, code, name, external_code, balance, is_active)
    VALUES (@company_id, @account_id, '0', @name, NULL, 0, TRUE)";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("name", accountName.Trim());
        await command.ExecuteNonQueryAsync();
    }
}

