using AccountingApp.Models;
using Npgsql;
using NpgsqlTypes;

namespace AccountingApp.Data;

public sealed partial class PostgresDatabase
{
    public async Task<IReadOnlyList<BusinessPartner>> GetBusinessPartnersAsync(int companyId)
    {
        const string sql = @"
    SELECT partner_id, code, name, partner_type, invoice_status, registration_number, is_active
    FROM business_partners
    WHERE company_id = @company_id
    ORDER BY code";

        var partners = new List<BusinessPartner>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            partners.Add(new BusinessPartner(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetBoolean(6)));
        }

        return partners;
    }

    public async Task<int> CreateBusinessPartnerAsync(
            int companyId,
            string code,
            string name,
            string partnerType,
            string invoiceStatus,
            string? registrationNumber,
            bool isActive)
    {
        const string sql = @"
    INSERT INTO business_partners (
        company_id, code, name, partner_type, invoice_status, registration_number, is_active
    )
    VALUES (
        @company_id, @code, @name, @partner_type, @invoice_status, @registration_number, @is_active
    )
    RETURNING partner_id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddBusinessPartnerParameters(command, companyId, code, name, partnerType, invoiceStatus, registrationNumber, isActive);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task UpdateBusinessPartnerAsync(
            int companyId,
            int partnerId,
            string code,
            string name,
            string partnerType,
            string invoiceStatus,
            string? registrationNumber,
            bool isActive)
    {
        const string sql = @"
    UPDATE business_partners
    SET code = @code,
        name = @name,
        partner_type = @partner_type,
        invoice_status = @invoice_status,
        registration_number = @registration_number,
        is_active = @is_active,
        updated_at = CURRENT_TIMESTAMP
    WHERE company_id = @company_id
      AND partner_id = @partner_id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        AddBusinessPartnerParameters(command, companyId, code, name, partnerType, invoiceStatus, registrationNumber, isActive);
        command.Parameters.AddWithValue("partner_id", partnerId);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddBusinessPartnerParameters(
        NpgsqlCommand command,
        int companyId,
        string code,
        string name,
        string partnerType,
        string invoiceStatus,
        string? registrationNumber,
        bool isActive)
    {
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("code", code.Trim());
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("partner_type", partnerType);
        command.Parameters.AddWithValue("invoice_status", invoiceStatus);
        command.Parameters.Add(new NpgsqlParameter<string?>("registration_number", NpgsqlDbType.Varchar)
        {
            TypedValue = NormalizeRegistrationNumber(registrationNumber)
        });
        command.Parameters.AddWithValue("is_active", isActive);
    }

    private static string? NormalizeRegistrationNumber(string? registrationNumber)
    {
        if (string.IsNullOrWhiteSpace(registrationNumber))
        {
            return null;
        }

        var normalized = registrationNumber.Trim().Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
        return normalized.StartsWith('T') ? normalized : "T" + normalized;
    }
}
