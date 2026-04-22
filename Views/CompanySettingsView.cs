using AccountingApp.Data;
using AccountingApp.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AccountingApp.Views;

public sealed class CompanySettingsView : UserControl
{
    private sealed record ClosingDayOption(int Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record TaxEntryMethodOption(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly PostgresDatabase _database;
    private readonly AppUser _user;
    private readonly Action _backToDashboard;
    private readonly Action<AppUser> _switchCompany;
    private readonly TextBox _companyName = new();
    private readonly DatePicker _fiscalYearStart = new();
    private readonly ComboBox _closingDay = new() { ItemsSource = CreateClosingDayOptions(), SelectedIndex = 30 };
    private readonly ComboBox _taxEntryMethod = new() { ItemsSource = CreateTaxEntryMethodOptions(), SelectedIndex = 0 };
    private readonly CheckBox _isTaxExempt = new() { Content = "免税事業者" };
    private readonly TextBox _newCompanyName = new();
    private readonly DatePicker _newFiscalYearStart = new() { SelectedDate = new DateTimeOffset(GetDefaultFiscalYearStart(DateTime.Today)) };
    private readonly ComboBox _newClosingDay = new() { ItemsSource = CreateClosingDayOptions(), SelectedIndex = 30 };
    private readonly ComboBox _newTaxEntryMethod = new() { ItemsSource = CreateTaxEntryMethodOptions(), SelectedIndex = 0 };
    private readonly CheckBox _newIsTaxExempt = new() { Content = "免税事業者" };
    private readonly TextBlock _carryForwardPeriod = ViewHelpers.Body("");
    private readonly TextBlock _carryForwardAccount = ViewHelpers.Body("");
    private readonly TextBlock _carryForwardAmount = ViewHelpers.Body("");
    private readonly TextBlock _carryForwardStatus = ViewHelpers.Body("");
    private readonly TextBlock _message = ViewHelpers.Body("会社設定を読み込み中です。");
    private readonly Button _saveButton = ViewHelpers.PrimaryButton("会社情報を保存");
    private readonly Button _addCompanyButton = ViewHelpers.SecondaryButton("新しい会社を追加");
    private readonly Button _carryForwardButton = ViewHelpers.SecondaryButton("年次繰越を実行");

    public CompanySettingsView(PostgresDatabase database, AppUser user, Action backToDashboard, Action<AppUser> switchCompany)
    {
        _database = database;
        _user = user;
        _backToDashboard = backToDashboard;
        _switchCompany = switchCompany;
        Content = Build();
        _saveButton.Click += async (_, _) => await SaveAsync();
        _addCompanyButton.Click += async (_, _) => await AddCompanyAsync();
        _carryForwardButton.Click += async (_, _) => await ExecuteCarryForwardAsync();
        _isTaxExempt.IsCheckedChanged += (_, _) => ApplyTaxExemptState(_taxEntryMethod, _isTaxExempt);
        _newIsTaxExempt.IsCheckedChanged += (_, _) => ApplyTaxExemptState(_newTaxEntryMethod, _newIsTaxExempt);
        _ = LoadAsync();
    }

    private Control Build()
    {
        var backButton = ViewHelpers.SecondaryButton("ホームに戻る");
        backButton.Width = 140;
        backButton.Click += (_, _) => _backToDashboard();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new StackPanel
                {
                    Children =
                    {
                        ViewHelpers.Heading(_user.CompanyName),
                        ViewHelpers.Body("会社設定")
                    }
                },
                backButton
            }
        };
        Grid.SetColumn(backButton, 1);

        var settingsPanel = ViewHelpers.Panel(new StackPanel
        {
            Width = 420,
            Spacing = 4,
            Children =
            {
                ViewHelpers.Heading("現在の会社", 20),
                ViewHelpers.Body("会社名、期首日、締め日、消費税の設定を変更できます。免税事業者にすると総額方式に固定され、仕訳入力から税関連欄が非表示になります。"),
                ViewHelpers.Label("会社名"),
                _companyName,
                ViewHelpers.Label("会計年度期首日"),
                _fiscalYearStart,
                ViewHelpers.Label("締め日"),
                _closingDay,
                _isTaxExempt,
                ViewHelpers.Label("消費税記帳方式"),
                _taxEntryMethod,
                ViewHelpers.Body("免税事業者を選択した場合は総額方式に固定されます。"),
                new Border { Height = 8 },
                _saveButton
            }
        });

        var addCompanyPanel = ViewHelpers.Panel(new StackPanel
        {
            Width = 420,
            Spacing = 4,
            Children =
            {
                ViewHelpers.Heading("会社を追加", 20),
                ViewHelpers.Body("現在のユーザーに新しい会社を追加します。免税事業者にすると新会社でも総額方式が固定されます。"),
                ViewHelpers.Label("会社名"),
                _newCompanyName,
                ViewHelpers.Label("会計年度期首日"),
                _newFiscalYearStart,
                ViewHelpers.Label("締め日"),
                _newClosingDay,
                _newIsTaxExempt,
                ViewHelpers.Label("消費税記帳方式"),
                _newTaxEntryMethod,
                new Border { Height = 8 },
                _addCompanyButton
            }
        });

        _carryForwardButton.Width = 180;

        var carryForwardPanel = ViewHelpers.Panel(new StackPanel
        {
            Width = 420,
            Spacing = 6,
            Children =
            {
                ViewHelpers.Heading("年次繰越", 20),
                ViewHelpers.Body("前期の損益残高を資本勘定へ振り替える年次繰越仕訳を作成します。"),
                ViewHelpers.Label("対象期間"),
                _carryForwardPeriod,
                ViewHelpers.Label("繰越先勘定"),
                _carryForwardAccount,
                ViewHelpers.Label("純利益"),
                _carryForwardAmount,
                ViewHelpers.Label("実行状態"),
                _carryForwardStatus,
                new Border { Height = 8 },
                _carryForwardButton
            }
        });

        var body = new StackPanel
        {
            Spacing = 18,
            Children = { header, settingsPanel, addCompanyPanel, carryForwardPanel, _message }
        };

        return new ScrollViewer
        {
            Content = new Grid
            {
                Margin = new Thickness(28),
                Children = { body }
            }
        };
    }

    private async Task LoadAsync()
    {
        try
        {
            var settings = await _database.GetCompanySettingsAsync(_user.CompanyId);
            _companyName.Text = settings.CompanyName;
            _fiscalYearStart.SelectedDate = new DateTimeOffset(settings.FiscalYearStart.Date);
            _closingDay.SelectedItem = CreateClosingDayOptions().FirstOrDefault(x => x.Value == settings.ClosingDay);
            _taxEntryMethod.SelectedItem = CreateTaxEntryMethodOptions().FirstOrDefault(x => x.Value == settings.TaxEntryMethod);
            _isTaxExempt.IsChecked = settings.IsTaxExempt;
            ApplyTaxExemptState(_taxEntryMethod, _isTaxExempt);

            _newCompanyName.Text = "";
            _newFiscalYearStart.SelectedDate = new DateTimeOffset(GetDefaultFiscalYearStart(DateTime.Today));
            _newClosingDay.SelectedItem = CreateClosingDayOptions().FirstOrDefault(x => x.Value == 31);
            _newTaxEntryMethod.SelectedItem = CreateTaxEntryMethodOptions().FirstOrDefault(x => x.Value == "gross");
            _newIsTaxExempt.IsChecked = false;
            ApplyTaxExemptState(_newTaxEntryMethod, _newIsTaxExempt);

            await LoadCarryForwardStatusAsync();
            _message.Text = "会社設定を表示しています。";
            _message.Foreground = Brush.Parse("#4A5568");
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
    }

    private async Task SaveAsync()
    {
        if (_closingDay.SelectedItem is not ClosingDayOption closingDay)
        {
            _message.Text = "締め日を選択してください。";
            _message.Foreground = Brush.Parse("#B42318");
            return;
        }

        if (_taxEntryMethod.SelectedItem is not TaxEntryMethodOption taxEntryMethod)
        {
            _message.Text = "消費税記帳方式を選択してください。";
            _message.Foreground = Brush.Parse("#B42318");
            return;
        }

        try
        {
            _saveButton.IsEnabled = false;
            var fiscalYearStart = _fiscalYearStart.SelectedDate?.DateTime.Date ?? DateTime.Today;
            var isTaxExempt = _isTaxExempt.IsChecked == true;
            await _database.UpdateCompanySettingsAsync(
                _user.CompanyId,
                _companyName.Text ?? "",
                fiscalYearStart,
                closingDay.Value,
                taxEntryMethod.Value,
                isTaxExempt);

            var updatedUser = _user with { CompanyName = (_companyName.Text ?? "").Trim() };
            _message.Text = "会社情報を更新しました。";
            _message.Foreground = Brush.Parse("#1E6B52");
            _switchCompany(updatedUser);
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
        finally
        {
            _saveButton.IsEnabled = true;
        }
    }

    private async Task AddCompanyAsync()
    {
        if (_newClosingDay.SelectedItem is not ClosingDayOption closingDay)
        {
            _message.Text = "新しい会社の締め日を選択してください。";
            _message.Foreground = Brush.Parse("#B42318");
            return;
        }

        if (_newTaxEntryMethod.SelectedItem is not TaxEntryMethodOption taxEntryMethod)
        {
            _message.Text = "新しい会社の消費税記帳方式を選択してください。";
            _message.Foreground = Brush.Parse("#B42318");
            return;
        }

        try
        {
            _addCompanyButton.IsEnabled = false;
            var fiscalYearStart = _newFiscalYearStart.SelectedDate?.DateTime.Date ?? DateTime.Today;
            var isTaxExempt = _newIsTaxExempt.IsChecked == true;
            var company = await _database.CreateCompanyForUserAsync(
                _user.UserId,
                _newCompanyName.Text ?? "",
                fiscalYearStart,
                closingDay.Value,
                taxEntryMethod.Value,
                isTaxExempt);

            _message.Text = $"会社を追加しました: {company.CompanyName}";
            _message.Foreground = Brush.Parse("#1E6B52");

            _switchCompany(_user with
            {
                CompanyId = company.CompanyId,
                CompanyName = company.CompanyName,
                Role = company.Role
            });
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
        }
        finally
        {
            _addCompanyButton.IsEnabled = true;
        }
    }

    private async Task LoadCarryForwardStatusAsync()
    {
        var status = await _database.GetAnnualCarryForwardStatusAsync(_user.CompanyId, DateTime.Today);
        _carryForwardPeriod.Text = $"{status.SourceFiscalYearStart:yyyy/MM/dd} から {status.SourceFiscalYearEnd:yyyy/MM/dd} を締めて {status.NextFiscalYearStart:yyyy/MM/dd} に繰越";
        _carryForwardAccount.Text = status.EquityAccountDisplayName;
        _carryForwardAmount.Text = FormatFinancialStatementAmount(status.NetIncome);

        if (status.AlreadyExecuted)
        {
            _carryForwardStatus.Text = status.ExecutedAt.HasValue
                ? $"実行済み: {status.EntryNumber} ({status.ExecutedAt:yyyy/MM/dd HH:mm})"
                : $"実行済み: {status.EntryNumber}";
            _carryForwardStatus.Foreground = Brush.Parse("#1E6B52");
            _carryForwardButton.IsEnabled = false;
            return;
        }

        _carryForwardStatus.Text = "未実行";
        _carryForwardStatus.Foreground = Brush.Parse("#4A5568");
        _carryForwardButton.IsEnabled = true;
    }

    private async Task ExecuteCarryForwardAsync()
    {
        try
        {
            _carryForwardButton.IsEnabled = false;
            await _database.ExecuteAnnualCarryForwardAsync(_user.CompanyId, _user.UserId, DateTime.Today);
            await LoadCarryForwardStatusAsync();
            _message.Text = "年次繰越を実行しました。";
            _message.Foreground = Brush.Parse("#1E6B52");
        }
        catch (Exception ex)
        {
            _message.Text = ex.Message;
            _message.Foreground = Brush.Parse("#B42318");
            await LoadCarryForwardStatusAsync();
        }
    }

    private static void ApplyTaxExemptState(ComboBox taxEntryMethod, CheckBox isTaxExempt)
    {
        var exempt = isTaxExempt.IsChecked == true;
        taxEntryMethod.IsEnabled = !exempt;
        if (exempt)
        {
            taxEntryMethod.SelectedItem = CreateTaxEntryMethodOptions().First(x => x.Value == "gross");
        }
    }

    private static IReadOnlyList<ClosingDayOption> CreateClosingDayOptions()
    {
        var options = Enumerable.Range(1, 30)
            .Select(day => new ClosingDayOption(day, $"{day}日"))
            .ToList();
        options.Add(new ClosingDayOption(31, "末日"));
        return options;
    }

    private static IReadOnlyList<TaxEntryMethodOption> CreateTaxEntryMethodOptions()
    {
        return
        [
            new("gross", "総額方式"),
            new("net", "税抜方式")
        ];
    }

    private static string FormatFinancialStatementAmount(decimal amount)
    {
        return amount < 0
            ? $"△{Math.Abs(amount):N0}"
            : amount.ToString("N0");
    }

    private static DateTime GetDefaultFiscalYearStart(DateTime referenceDate)
    {
        return new DateTime(referenceDate.Year, 1, 1);
    }
}
