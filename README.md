# AccountingApp

Avalonia UI と PostgreSQL を使った会計ソフトの最小スターターです。

## 接続情報の設定

接続情報はソースコードに書きません。次のどちらかで設定してください。

### 方法1: appsettings.json

`AccountingApp/appsettings.example.json` を `AccountingApp/appsettings.json` にコピーし、値を変更します。

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=accounting_app;Username=YOUR_USER;Password=YOUR_PASSWORD"
  }
}
```

`AccountingApp/appsettings.json` は `.gitignore` に含めているため、Git 管理されません。

### 方法2: 環境変数

```powershell
$env:ACCOUNTING_APP_CONNECTION="Host=localhost;Port=5432;Database=accounting_app;Username=YOUR_USER;Password=YOUR_PASSWORD"
```

環境変数が設定されている場合は、`appsettings.json` より優先されます。

## 起動前の準備

1. PostgreSQL にデータベースを作成します。
2. 接続情報を設定します。
3. アプリを起動します。
4. ログイン画面の「DBスキーマを初期化」を押します。
5. 初期ユーザーがいない場合、「初期管理者を作成」で作成します。

## 実行例

```powershell
dotnet run --project AccountingApp/AccountingApp.csproj -p:UsedAvaloniaProducts=
```
