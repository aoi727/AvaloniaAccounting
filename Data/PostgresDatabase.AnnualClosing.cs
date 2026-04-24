using Npgsql;
using NpgsqlTypes;

namespace AccountingApp.Data;

public sealed partial class PostgresDatabase
{
    public async Task CloseFiscalYearAsync(int companyId, int userId, DateTime today)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await EnsureAnnualCarryForwardSchemaAsync(connection, transaction);
            await EnsureAnnualClosingSchemaAsync(connection, transaction);
            await EnsureOperationLogSchemaAsync(connection, transaction);
            await EnsureDefaultSubAccountsAsync(connection, transaction, companyId);

            var settings = await GetCompanySettingsAsync(companyId);
            var nextFiscalYearStart = GetFiscalYearStartForDate(settings.FiscalYearStart, today.Date);
            var sourceFiscalYearStart = ShiftFiscalYearStart(settings.FiscalYearStart, nextFiscalYearStart, -1);
            var sourceFiscalYearEnd = nextFiscalYearStart.AddDays(-1);

            var closing = await GetAnnualClosingAsync(connection, transaction, companyId, sourceFiscalYearStart);
            if (closing is not null && string.Equals(closing.Status, "closed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"この年度は締め済みです。解除してから再度締めを実行してください。繰越仕訳番号: {closing.CarryForwardEntryNumber}");
            }

            var existing = await GetAnnualCarryForwardExecutionAsync(connection, transaction, companyId, nextFiscalYearStart);
            if (existing is not null)
            {
                await DeleteAnnualCarryForwardExecutionAsync(connection, transaction, companyId, existing.EntryNumber);
                await DeleteJournalVoucherAsync(connection, transaction, companyId, existing.EntryNumber);
            }

            var equityAccount = await FindCarryForwardEquityAccountAsync(connection, transaction, companyId);
            var effectiveSubAccountId = await EnsureDefaultSubAccountAsync(connection, transaction, companyId, equityAccount.AccountId, equityAccount.Name);
            var closingLines = await GetCarryForwardClosingLinesAsync(connection, transaction, companyId, sourceFiscalYearStart, sourceFiscalYearEnd);

            if (closingLines.Count == 0)
            {
                throw new InvalidOperationException("繰越対象の損益残高がありません。");
            }

            var totalDebit = closingLines.Where(x => x.Side == "debit").Sum(x => x.Amount);
            var totalCredit = closingLines.Where(x => x.Side == "credit").Sum(x => x.Amount);
            var offsetAmount = Math.Abs(totalDebit - totalCredit);
            if (offsetAmount <= 0)
            {
                throw new InvalidOperationException("年度繰越の差額を計算できませんでした。");
            }

            var counterSide = totalDebit > totalCredit ? "credit" : "debit";
            closingLines.Add(new(
                counterSide,
                equityAccount.AccountId,
                effectiveSubAccountId,
                offsetAmount,
                null,
                null,
                0,
                0,
                0,
                "excluded",
                "年度繰越"));

            var entryNumber = await GenerateCarryForwardEntryNumberAsync(connection, transaction, companyId, nextFiscalYearStart);
            var sourceKey = sourceFiscalYearStart.ToString("yyyy-MM-dd");
            var voucherId = await InsertJournalVoucherAsync(
                connection,
                transaction,
                companyId,
                entryNumber,
                nextFiscalYearStart,
                "年度繰越",
                userId,
                "annual_carry_forward",
                sourceKey);

            var lineNo = 1;
            foreach (var line in closingLines)
            {
                await InsertJournalLineAsync(connection, transaction, voucherId, companyId, lineNo++, line);
            }

            await InsertAnnualCarryForwardExecutionAsync(
                connection,
                transaction,
                companyId,
                sourceFiscalYearStart,
                sourceFiscalYearEnd,
                nextFiscalYearStart,
                entryNumber,
                equityAccount.AccountId,
                totalCredit - totalDebit,
                userId);

            await UpsertAnnualClosingAsync(
                connection,
                transaction,
                companyId,
                sourceFiscalYearStart,
                sourceFiscalYearEnd,
                nextFiscalYearStart,
                entryNumber,
                userId);

            await WriteOperationLogAsync(
                connection,
                transaction,
                companyId,
                userId,
                "annual_close",
                "annual_closing",
                sourceKey,
                $"年度締めを実行しました: {sourceFiscalYearStart:yyyy/MM/dd} - {sourceFiscalYearEnd:yyyy/MM/dd}");

            await transaction.CommitAsync();
            committed = true;
            await RebuildSubAccountBalancesAsync(companyId);
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

    public async Task UnlockAnnualClosingAsync(int companyId, int userId, DateTime sourceFiscalYearStart, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("締め解除の理由を入力してください。");
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await EnsureAnnualClosingSchemaAsync(connection, transaction);
            await EnsureOperationLogSchemaAsync(connection, transaction);

            var closing = await GetAnnualClosingAsync(connection, transaction, companyId, sourceFiscalYearStart.Date);
            if (closing is null || !string.Equals(closing.Status, "closed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("解除できる締め済み年度が見つかりません。");
            }

            const string sql = @"
    UPDATE annual_closings
    SET status = 'open',
        unlocked_by = @unlocked_by,
        unlocked_at = CURRENT_TIMESTAMP,
        unlock_reason = @unlock_reason,
        updated_at = CURRENT_TIMESTAMP
    WHERE company_id = @company_id
      AND fiscal_year_start = @fiscal_year_start";

            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("unlocked_by", userId);
            command.Parameters.AddWithValue("unlock_reason", reason.Trim());
            command.Parameters.Add(new NpgsqlParameter<DateTime>("fiscal_year_start", NpgsqlDbType.Date) { TypedValue = sourceFiscalYearStart.Date });
            await command.ExecuteNonQueryAsync();

            await WriteOperationLogAsync(
                connection,
                transaction,
                companyId,
                userId,
                "annual_unlock",
                "annual_closing",
                sourceFiscalYearStart.ToString("yyyy-MM-dd"),
                $"年度締めを解除しました: {sourceFiscalYearStart:yyyy/MM/dd}",
                "{\"reason\":\"" + reason.Trim().Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");

            await transaction.CommitAsync();
            committed = true;
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

    private static async Task EnsureJournalVoucherEditableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int companyId,
        string entryNumber,
        DateTime newEntryDate)
    {
        await EnsureJournalDateOpenAsync(connection, transaction, companyId, newEntryDate);

        var existingDate = await GetJournalVoucherDateAsync(connection, transaction, companyId, entryNumber);
        if (existingDate.HasValue)
        {
            await EnsureJournalDateOpenAsync(connection, transaction, companyId, existingDate.Value);
        }
    }

    private static async Task<DateTime?> GetJournalVoucherDateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int companyId,
        string entryNumber)
    {
        const string sql = @"
    SELECT entry_date
    FROM journal_vouchers
    WHERE company_id = @company_id
      AND entry_number = @entry_number";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("entry_number", entryNumber.Trim());
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : ToDateTimeValue(result);
    }

    private static async Task EnsureJournalDateOpenAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int companyId,
        DateTime entryDate)
    {
        await EnsureAnnualClosingSchemaAsync(connection, transaction);

        const string sql = @"
    SELECT fiscal_year_start, fiscal_year_end
    FROM annual_closings
    WHERE company_id = @company_id
      AND status = 'closed'
      AND @entry_date BETWEEN fiscal_year_start AND fiscal_year_end
    LIMIT 1";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.Add(new NpgsqlParameter<DateTime>("entry_date", NpgsqlDbType.Date) { TypedValue = entryDate.Date });
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var from = reader.GetDateTime(0);
            var to = reader.GetDateTime(1);
            throw new InvalidOperationException($"締め済み年度の仕訳は変更できません。対象年度: {from:yyyy/MM/dd} - {to:yyyy/MM/dd}");
        }
    }

    private static async Task EnsureAnnualClosingSchemaAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        const string sql = @"
    CREATE TABLE IF NOT EXISTS annual_closings (
        closing_id               BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
        company_id               INTEGER NOT NULL REFERENCES companies(company_id),
        fiscal_year_start        DATE NOT NULL,
        fiscal_year_end          DATE NOT NULL,
        next_fiscal_year_start   DATE NOT NULL,
        carry_forward_entry_number VARCHAR(30),
        status                   VARCHAR(20) NOT NULL DEFAULT 'open',
        closed_by                INTEGER REFERENCES users(user_id),
        closed_at                TIMESTAMP,
        unlocked_by              INTEGER REFERENCES users(user_id),
        unlocked_at              TIMESTAMP,
        unlock_reason            TEXT,
        created_at               TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at               TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, fiscal_year_start),
        UNIQUE(company_id, next_fiscal_year_start),
        CHECK (status IN ('open','closed'))
    );
    CREATE INDEX IF NOT EXISTS idx_annual_closings_company_year ON annual_closings(company_id, fiscal_year_start);
    CREATE INDEX IF NOT EXISTS idx_annual_closings_company_status ON annual_closings(company_id, status);

    ALTER TABLE journal_vouchers ADD COLUMN IF NOT EXISTS source_type VARCHAR(40) DEFAULT 'manual';
    ALTER TABLE journal_vouchers ADD COLUMN IF NOT EXISTS source_key VARCHAR(100);
    UPDATE journal_vouchers
    SET source_type = 'manual'
    WHERE source_type IS NULL;
    ALTER TABLE journal_vouchers ALTER COLUMN source_type SET DEFAULT 'manual';
    ALTER TABLE journal_vouchers ALTER COLUMN source_type SET NOT NULL;
    CREATE INDEX IF NOT EXISTS idx_journal_vouchers_company_source
        ON journal_vouchers(company_id, source_type, source_key);";

        await using var command = transaction is null
            ? new NpgsqlCommand(sql, connection)
            : new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureOperationLogSchemaAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        const string sql = @"
    CREATE TABLE IF NOT EXISTS operation_logs (
        log_id              BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
        company_id          INTEGER NOT NULL REFERENCES companies(company_id),
        user_id             INTEGER REFERENCES users(user_id),
        operation_type      VARCHAR(60) NOT NULL,
        target_type         VARCHAR(60) NOT NULL,
        target_key          VARCHAR(120),
        summary             TEXT NOT NULL,
        metadata_json       JSONB,
        occurred_at         TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    );
    CREATE INDEX IF NOT EXISTS idx_operation_logs_company_time ON operation_logs(company_id, occurred_at DESC);
    CREATE INDEX IF NOT EXISTS idx_operation_logs_company_target ON operation_logs(company_id, target_type, target_key);";

        await using var command = transaction is null
            ? new NpgsqlCommand(sql, connection)
            : new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<AnnualClosing?> GetAnnualClosingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        int companyId,
        DateTime fiscalYearStart)
    {
        const string sql = @"
    SELECT fiscal_year_start, fiscal_year_end, next_fiscal_year_start,
           carry_forward_entry_number, status, unlocked_at, unlock_reason
    FROM annual_closings
    WHERE company_id = @company_id
      AND fiscal_year_start = @fiscal_year_start";

        await using var command = transaction is null
            ? new NpgsqlCommand(sql, connection)
            : new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.Add(new NpgsqlParameter<DateTime>("fiscal_year_start", NpgsqlDbType.Date) { TypedValue = fiscalYearStart.Date });

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new AnnualClosing(
            reader.GetDateTime(0),
            reader.GetDateTime(1),
            reader.GetDateTime(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetDateTime(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    private static async Task UpsertAnnualClosingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int companyId,
        DateTime fiscalYearStart,
        DateTime fiscalYearEnd,
        DateTime nextFiscalYearStart,
        string carryForwardEntryNumber,
        int closedBy)
    {
        const string sql = @"
    INSERT INTO annual_closings (
        company_id, fiscal_year_start, fiscal_year_end, next_fiscal_year_start,
        carry_forward_entry_number, status, closed_by, closed_at, updated_at
    )
    VALUES (
        @company_id, @fiscal_year_start, @fiscal_year_end, @next_fiscal_year_start,
        @carry_forward_entry_number, 'closed', @closed_by, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
    )
    ON CONFLICT (company_id, fiscal_year_start)
    DO UPDATE SET
        fiscal_year_end = EXCLUDED.fiscal_year_end,
        next_fiscal_year_start = EXCLUDED.next_fiscal_year_start,
        carry_forward_entry_number = EXCLUDED.carry_forward_entry_number,
        status = 'closed',
        closed_by = EXCLUDED.closed_by,
        closed_at = CURRENT_TIMESTAMP,
        updated_at = CURRENT_TIMESTAMP";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.Add(new NpgsqlParameter<DateTime>("fiscal_year_start", NpgsqlDbType.Date) { TypedValue = fiscalYearStart.Date });
        command.Parameters.Add(new NpgsqlParameter<DateTime>("fiscal_year_end", NpgsqlDbType.Date) { TypedValue = fiscalYearEnd.Date });
        command.Parameters.Add(new NpgsqlParameter<DateTime>("next_fiscal_year_start", NpgsqlDbType.Date) { TypedValue = nextFiscalYearStart.Date });
        command.Parameters.AddWithValue("carry_forward_entry_number", carryForwardEntryNumber);
        command.Parameters.AddWithValue("closed_by", closedBy);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WriteOperationLogAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int companyId,
        int userId,
        string operationType,
        string targetType,
        string? targetKey,
        string summary,
        string? metadataJson = null)
    {
        const string sql = @"
    INSERT INTO operation_logs (
        company_id, user_id, operation_type, target_type, target_key, summary, metadata_json
    )
    VALUES (
        @company_id, @user_id, @operation_type, @target_type, @target_key, @summary, CAST(@metadata_json AS jsonb)
    )";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("operation_type", operationType);
        command.Parameters.AddWithValue("target_type", targetType);
        command.Parameters.Add(new NpgsqlParameter<string?>("target_key", NpgsqlDbType.Varchar) { TypedValue = targetKey });
        command.Parameters.AddWithValue("summary", summary);
        command.Parameters.Add(new NpgsqlParameter<string?>("metadata_json", NpgsqlDbType.Jsonb) { TypedValue = metadataJson });
        await command.ExecuteNonQueryAsync();
    }

    private sealed record AnnualClosing(
        DateTime FiscalYearStart,
        DateTime FiscalYearEnd,
        DateTime NextFiscalYearStart,
        string? CarryForwardEntryNumber,
        string Status,
        DateTime? UnlockedAt,
        string? UnlockReason);
}
