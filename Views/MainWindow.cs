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
        MinWidth = 960;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowLogin();
    }

    private void ShowLogin()
    {
        _currentUser = null;
        SetContent(new LoginView(_database, ShowCompanySelection));
    }

    private void ShowCompanySelection(AppUser user)
    {
        _currentUser = user;
        SetContent(new CompanySelectionView(_database, user, ShowLogin, ShowDashboard));
    }

    private void ShowDashboard(AppUser user)
    {
        _currentUser = user;
        SetContent(new DashboardView(_database, user, ShowLogin, () => ShowSubAccountForm(), ShowAccountForm, ShowBusinessPartnerForm, ShowUserForm, ShowJournalForm, ShowJournalBook, ShowCashbook, ShowGeneralLedger, ShowTrialBalance, ShowBalanceSheet, ShowProfitAndLoss, ShowCompanySettings));
    }

    private void ShowSubAccountForm(int? accountId = null, bool returnToAccountForm = false)
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        Action? backToAccountForm = returnToAccountForm ? ShowAccountForm : null;
        SetContent(new SubAccountFormView(_database, _currentUser, () => ShowDashboard(_currentUser), backToAccountForm, accountId));
    }

    private void ShowAccountForm()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new AccountFormView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowSubAccountForm));
    }

    private void ShowUserForm()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new UserFormView(_database, _currentUser, () => ShowDashboard(_currentUser)));
    }

    private void ShowBusinessPartnerForm()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new BusinessPartnerFormView(_database, _currentUser, () => ShowDashboard(_currentUser)));
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

        SetContent(new JournalEntryFormView(_database, _currentUser, () => ShowDashboard(_currentUser), entryNumber));
    }

    private void ShowJournalFormFromJournalBook(string? entryNumber)
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new JournalEntryFormView(_database, _currentUser, ShowJournalBook, entryNumber));
    }

    private void ShowCashbook()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new CashbookView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowJournalForm));
    }

    private void ShowGeneralLedger()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new GeneralLedgerView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowJournalForm));
    }

    private void ShowTrialBalance()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new TrialBalanceView(_database, _currentUser, () => ShowDashboard(_currentUser)));
    }

    private void ShowBalanceSheet()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new BalanceSheetView(_database, _currentUser, () => ShowDashboard(_currentUser)));
    }

    private void ShowProfitAndLoss()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new ProfitAndLossView(_database, _currentUser, () => ShowDashboard(_currentUser)));
    }

    private void ShowCompanySettings()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new CompanySettingsView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowDashboard));
    }

    private void ShowJournalBook()
    {
        if (_currentUser is null)
        {
            ShowLogin();
            return;
        }

        SetContent(new JournalBookView(_database, _currentUser, () => ShowDashboard(_currentUser), ShowJournalFormFromJournalBook));
    }

    private void SetContent(Control view)
    {
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = view
        };
    }
}
