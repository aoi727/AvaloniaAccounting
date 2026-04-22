using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class CompanySelectionView : UserControl
{
    private readonly PostgresDatabase _database;
    private readonly AppUser _user;
    private readonly Action _signOut;
    private readonly Action<AppUser> _selected;
    private readonly ListBox _companyList = new() { MinWidth = 360, MinHeight = 260 };
    private readonly TextBlock _message = ViewHelpers.Body("会社一覧を読み込み中です。");
    private readonly Button _selectButton = ViewHelpers.PrimaryButton("この会社で開始");

    public CompanySelectionView(PostgresDatabase database, AppUser user, Action signOut, Action<AppUser> selected)
    {
        _database = database;
        _user = user;
        _signOut = signOut;
        _selected = selected;
        Content = Build();
        _ = LoadAsync();
    }

    private Control Build()
    {
        _companyList.SelectionChanged += (_, _) => UpdateSelectionState();
        _companyList.DoubleTapped += (_, _) => SelectCurrentCompany();

        _selectButton.Width = 160;
        _selectButton.IsEnabled = false;
        _selectButton.Click += (_, _) => SelectCurrentCompany();

        var signOutButton = ViewHelpers.SecondaryButton("ログアウト");
        signOutButton.Width = 120;
        signOutButton.Click += (_, _) => _signOut();

        var form = new StackPanel
        {
            Width = 520,
            Spacing = 12,
            Children =
            {
                ViewHelpers.Heading("会社を選択"),
                ViewHelpers.Body($"{_user.DisplayName} さんが利用する会社を選んでください。"),
                _companyList,
                _message,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children =
                    {
                        signOutButton,
                        _selectButton
                    }
                }
            }
        };

        return new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,*"),
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
            Children =
            {
                Place(ViewHelpers.Panel(form), 1, 1)
            }
        };
    }

    private static Control Place(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    private async Task LoadAsync()
    {
        try
        {
            var companies = await _database.GetUserCompaniesAsync(_user.UserId);
            if (companies.Count == 0)
            {
                _message.Text = "利用可能な会社が割り当てられていません。";
                _message.Foreground = Brush.Parse("#B42318");
                _companyList.ItemsSource = Array.Empty<UserCompanySummary>();
                return;
            }

            _companyList.ItemsSource = companies;
            _companyList.SelectedItem = companies.FirstOrDefault(x => x.CompanyId == _user.CompanyId) ?? companies[0];
            _message.Text = $"{companies.Count:N0} 件の会社から選択できます。";
            _message.Foreground = Brush.Parse("#4A5568");

            if (companies.Count == 1)
            {
                SelectCurrentCompany();
            }
            else
            {
                UpdateSelectionState();
            }
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
    }

    private void UpdateSelectionState()
    {
        _selectButton.IsEnabled = _companyList.SelectedItem is UserCompanySummary;
    }

    private void SelectCurrentCompany()
    {
        if (_companyList.SelectedItem is not UserCompanySummary company)
        {
            return;
        }

        _selected(_user with
        {
            CompanyId = company.CompanyId,
            CompanyName = company.CompanyName,
            Role = company.Role
        });
    }
}
