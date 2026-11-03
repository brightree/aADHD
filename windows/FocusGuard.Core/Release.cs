using System.Text.Json;

namespace FocusGuard.Core;

/// 깃허브 최신 릴리스 정보 (버전 · 바뀐 내용 · 윈도우용 파일 주소)
public sealed record ReleaseInfo(string Version, string Notes, string? AssetUrl)
{
    public const string LatestApi = "https://api.github.com/repos/brightree/aADHD/releases/latest";
    public const string WindowsAsset = "FocusGuard-Windows.zip";

    /// 릴리스 JSON 을 읽는다. 윈도우용 파일이 아직 안 올라왔으면 AssetUrl 은 null.
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            var root = JsonDocument.Parse(json).RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (tag.Length == 0) return null;
            var version = tag.StartsWith("v") ? tag[1..] : tag;
            var notes = root.TryGetProperty("body", out var b) ? (b.GetString() ?? "").Trim() : "";
            string? url = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var a in assets.EnumerateArray())
                    if (a.GetProperty("name").GetString() == WindowsAsset) url = a.GetProperty("browser_download_url").GetString();
            return new ReleaseInfo(version, notes, url);
        }
        catch { return null; }
    }

    /// "1.10.0" > "1.9.2" 처럼 숫자 단위로 비교한다
    public static bool IsNewer(string a, string b)
    {
        int[] p(string s) => s.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        var (x, y) = (p(a), p(b));
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var l = i < x.Length ? x[i] : 0; var r = i < y.Length ? y[i] : 0;
            if (l != r) return l > r;
        }
        return false;
    }
}

/// 작은 설정 파일 (%LOCALAPPDATA%\FocusGuard\prefs.json). 기록 폴더 위치 · 업데이트 확인 시각 등.
public sealed class JsonPrefs : IPrefs
{
    readonly string path;
    readonly Dictionary<string, string> values;

    public JsonPrefs(string path)
    {
        this.path = path;
        try { values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(); }
        catch { values = new(); }
    }

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusGuard", "prefs.json");

    public string? Get(string key) => values.TryGetValue(key, out var v) ? v : null;

    public void Set(string key, string? value)
    {
        if (value == null) values.Remove(key); else values[key] = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(values));
        }
        catch { /* 설정을 못 써도 앱은 계속 동작 */ }
    }
}
