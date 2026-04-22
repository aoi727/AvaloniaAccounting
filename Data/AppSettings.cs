using System.Text.Json;

namespace AccountingApp.Data;

public static class AppSettings
{
    private const string ConnectionEnvironmentVariable = "ACCOUNTING_APP_CONNECTION";
    private const string SettingsFileName = "appsettings.json";

    public static string GetConnectionString()
    {
        var env = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        foreach (var path in GetCandidateSettingsPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var connection = ReadConnectionString(path);
            if (!string.IsNullOrWhiteSpace(connection))
            {
                return connection;
            }
        }

        throw new InvalidOperationException(
            "PostgreSQL の接続情報が設定されていません。" + Environment.NewLine +
            $"環境変数 {ConnectionEnvironmentVariable} を設定するか、AccountingApp フォルダに {SettingsFileName} を作成してください。");
    }

    private static IEnumerable<string> GetCandidateSettingsPaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, SettingsFileName);
        yield return Path.Combine(Environment.CurrentDirectory, SettingsFileName);
        yield return Path.Combine(Environment.CurrentDirectory, "AccountingApp", SettingsFileName);
    }

    private static string? ReadConnectionString(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) &&
            connectionStrings.TryGetProperty("Default", out var defaultConnection))
        {
            return defaultConnection.GetString();
        }

        return null;
    }
}
