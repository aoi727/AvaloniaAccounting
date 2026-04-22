using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia.Controls;

namespace AccountingApp.Views;

public sealed class MainWindow : Window
{
    private readonly PostgresDatabase _database;
    private AppUser? _currentUser;

    public MainWindow(PostgresDatabase database)
    {
        _database = database;
        Title = "会計ソフト";
        Width = 1500;
        Height = 820;
        MinWidth = 1280;
        MinHeight = 720;
        ShowLogin();
    }

    private void ShowLogin()
    {
        _currentUser = null;
        Content = new LoginView(_database, ShowCompanySelection);
    }

    private void ShowCompanySelection(AppUser user)
    {
        _currentUser = user;
        Content = new CompanySelectionView(_database, user, ShowLogin, ShowDashboard);
    }

    private void ShowDashboard(AppUser user)
    {
        _currentUser = user;
        Content = new DashboardView(_database, user, ShowLogin, () => ShowSubAccountForm(), ShowAccountForm, ShowBusinessPartnerForm, ShowUserForm, ShowJournalForm, ShowJournalBook, ShowCashbook, ShowGeneralLedger, ShowTrialBalance, ShowBalanceSheet, ShowProfitAndLoss, ShowCompanySettings);
    }

    private void ShowSubAccountForm(int? accountId = null)
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new SubAccountFormView(_database, _currentUser, () => ShowDashboard(_currentUser), accountId);
    }

    private void ShowAccountForm()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new AccountFormView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowSubAccountForm);
    }

    private void ShowUserForm()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new UserFormView(_database, _currentUser, () => ShowDashboard(_currentUser));
    }

    private void ShowBusinessPartnerForm()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new BusinessPartnerFormView(_database, _currentUser, () => ShowDashboard(_currentUser));
    }

    private void ShowJournalForm()
    {
        ShowJournalForm(null);
    }

    private void ShowJournalForm(string? entryNumber)
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new JournalEntryFormView(_database, _currentUser, () => ShowDashboard(_currentUser), entryNumber);
    }

    private void ShowJournalFormFromJournalBook(string? entryNumber)
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new JournalEntryFormView(_database, _currentUser, ShowJournalBook, entryNumber);
    }

    private void ShowCashbook()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new CashbookView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowJournalForm);
    }

    private void ShowGeneralLedger()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new GeneralLedgerView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowJournalForm);
    }

    private void ShowTrialBalance()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new TrialBalanceView(_database, _currentUser, () => ShowDashboard(_currentUser));
    }

    private void ShowBalanceSheet()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new BalanceSheetView(_database, _currentUser, () => ShowDashboard(_currentUser));
    }

    private void ShowProfitAndLoss()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new ProfitAndLossView(_database, _currentUser, () => ShowDashboard(_currentUser));
    }

    private void ShowCompanySettings()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new CompanySettingsView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowDashboard);
    }

    private void ShowJournalBook()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Content = new JournalBookView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowJournalFormFromJournalBook);
    }
}
