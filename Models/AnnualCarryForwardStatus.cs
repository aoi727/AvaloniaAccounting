namespace AccountingApp.Models;

public sealed record AnnualCarryForwardStatus(
    DateTime SourceFiscalYearStart,
    DateTime SourceFiscalYearEnd,
    DateTime NextFiscalYearStart,
    string EquityAccountDisplayName,
    decimal NetIncome,
    bool AlreadyExecuted,
    string? EntryNumber,
    DateTime? ExecutedAt);
