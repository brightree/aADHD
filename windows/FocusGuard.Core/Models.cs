using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusGuard.Core;

// data.json 의 구조. 맥 앱과 같은 이름 · 형식을 쓴다 (camelCase, 날짜는 초 단위 ISO 8601 UTC).
// 필드가 없으면 기본값을 쓰므로 옛 파일도 읽힌다.

[JsonConverter(typeof(LangConverter))]
public enum Lang { Ko, En }

[JsonConverter(typeof(PhaseConverter))]
public enum Phase { Idle, Focus, Wrap, Rest }

[JsonConverter(typeof(TodoKindConverter))]
public enum TodoKind { Must, Nice, Other }

public static class PhaseInfo
{
    public static bool IsStudy(this Phase p) => p == Phase.Focus || p == Phase.Wrap;

    public static string Label(this Phase p) => p switch
    {
        Phase.Focus => I18n.L("집중 중", "Focusing"),
        Phase.Wrap => I18n.L("정리 중", "Wrapping up"),
        Phase.Rest => I18n.L("휴식 중", "On a break"),
        _ => I18n.L("대기 중", "Idle"),
    };
}

public sealed class Settings
{
    public static readonly (int Min, int Max) FocusRange = (1, 180), WrapRange = (0, 30), BreakRange = (0, 60), WarnRange = (1, 120);

    public int FocusMin { get; set; } = 35;
    public int WrapMin { get; set; } = 3;
    public int BreakMin { get; set; } = 7;
    /// 집중 외 시간에 한 사이트를 연속으로 이만큼 쓰면 경고
    public int WarnAfterMin { get; set; } = 10;
    public Lang Language { get; set; } = I18n.SystemLang;
    public string StopPhrase { get; set; } = DefaultStopPhrase(I18n.SystemLang);
    public List<string> Sites { get; set; } = new() { "youtube.com", "linkedin.com", "instagram.com", "facebook.com", "x.com", "twitter.com", "reddit.com", "netflix.com" };
    /// 차단 목록의 하위 도메인이어도 막지 않는 사이트
    public List<string> AllowSites { get; set; } = new() { "music.youtube.com" };
    public bool TrackStudyUsage { get; set; } = true;
    public bool MorningPlan { get; set; } = true;

    public static string DefaultStopPhrase(Lang lang) => lang == Lang.En ? "Gotta work to graduate" : "일해야 졸업하지";

    /// "집중 35분 → 정리 3분 → 휴식 7분 · …차단돼요" (0분인 단계는 뺀다)
    public string CycleText()
    {
        var parts = new List<string> { I18n.L($"집중 {FocusMin}분", $"Focus {FocusMin} min") };
        if (WrapMin > 0) parts.Add(I18n.L($"정리 {WrapMin}분", $"wrap up {WrapMin} min"));
        if (BreakMin > 0) parts.Add(I18n.L($"휴식 {BreakMin}분", $"break {BreakMin} min"));
        var blocked = WrapMin > 0 ? I18n.L("집중·정리 중에는 사이트가 차단돼요", "Sites are blocked during focus and wrap-up")
                                  : I18n.L("집중 중에는 사이트가 차단돼요", "Sites are blocked during focus");
        return string.Join(" → ", parts) + " · " + blocked;
    }

    /// 모든 숫자 설정을 허용 범위 안으로 (화면 입력, 저장, 파일 읽기 모두 여기를 거친다)
    public Settings Clamped()
    {
        var s = (Settings)MemberwiseClone();
        s.FocusMin = Math.Clamp(FocusMin, FocusRange.Min, FocusRange.Max);
        s.WrapMin = Math.Clamp(WrapMin, WrapRange.Min, WrapRange.Max);
        s.BreakMin = Math.Clamp(BreakMin, BreakRange.Min, BreakRange.Max);
        s.WarnAfterMin = Math.Clamp(WarnAfterMin, WarnRange.Min, WarnRange.Max);
        s.Sites = new List<string>(Sites ?? new());
        s.AllowSites = new List<string>(AllowSites ?? new());
        s.StopPhrase = StopPhrase ?? DefaultStopPhrase(Language);
        return s;
    }

    public Settings Copy() => Clamped();
}

public sealed class TimerState
{
    public Phase Phase { get; set; } = Phase.Idle;
    public DateTime? EndTime { get; set; }
    /// 일시정지 중 남은 시간 (초)
    public double? PausedRemaining { get; set; }
    /// 현재 공부 구간 시작 (공부 시간 누적용)
    public DateTime? StartedAt { get; set; }
    public string Task { get; set; } = "";
    /// 마지막으로 끝난 집중 세션이 기록된 날 · id (정리 메모를 붙일 곳)
    public string? LastLogDay { get; set; }
    public Guid? LastLogID { get; set; }
    /// 시간을 정해 둔 딴짓이 끝나는 시각 (대기 중에만)
    public DateTime? PlayUntil { get; set; }
}

public sealed class LogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Task { get; set; } = "";
    /// null 이면 중도 포기
    public int? Minutes { get; set; }
    public DateTime End { get; set; }
    public bool Done { get; set; }
    public string Note { get; set; } = "";
}

public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public TodoKind Kind { get; set; }
    public bool Done { get; set; }
}

public sealed class LaterItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public DateTime Created { get; set; }
    /// 어떤 공부 중에 떠오른 생각인지
    public string During { get; set; } = "";
    public bool Done { get; set; }
}

public sealed class DayStats
{
    public List<TodoItem> Todos { get; set; } = new();
    public double FocusSec { get; set; }
    public int Sessions { get; set; }
    public int Abandoned { get; set; }
    public int Warnings { get; set; }
    public Dictionary<string, double> DistractSec { get; set; } = new();
    /// 집중 · 정리 중 맨 앞에 있던 앱 → 초
    public Dictionary<string, double> StudyApps { get; set; } = new();
    /// 집중 · 정리 중 본 사이트(도메인) → 초
    public Dictionary<string, double> StudySites { get; set; } = new();
    public List<LogEntry> Log { get; set; } = new();

    [JsonIgnore] public double DistractTotal => DistractSec.Values.Sum();
}

public sealed class DataFile
{
    public int Version { get; set; } = 1;
    public Settings Settings { get; set; } = new();
    public TimerState Timer { get; set; } = new();
    public Dictionary<string, DayStats> Days { get; set; } = new();
    public List<LaterItem> Later { get; set; } = new();
    /// 아침 할 일 창을 마지막으로 자동으로 띄운 날
    public string PlanShownDay { get; set; } = "";
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null, // 사이트 · 앱 · 날짜 키는 그대로
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new UtcSecondsConverter(), new NullableUtcSecondsConverter() },
    };

    public static DataFile Read(string text)
    {
        var d = JsonSerializer.Deserialize<DataFile>(text, Options) ?? throw new JsonException("empty");
        d.Settings = (d.Settings ?? new()).Clamped();
        d.Timer ??= new();
        d.Days ??= new();
        d.Later ??= new();
        d.PlanShownDay ??= "";
        foreach (var day in d.Days.Values)
        {
            day.Todos ??= new(); day.DistractSec ??= new(); day.StudyApps ??= new(); day.StudySites ??= new(); day.Log ??= new();
        }
        return d;
    }

    public static string Write(DataFile d) => JsonSerializer.Serialize(d, Options);
}

/// "2026-10-26T05:24:00Z" (맥 앱과 같은 형식). 읽을 땐 소수점 · 시간대가 있어도 받는다. 메모리에서는 지역 시각.
public sealed class UtcSecondsConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
        DateTimeOffset.Parse(r.GetString()!, CultureInfo.InvariantCulture).LocalDateTime;

    public override void Write(Utf8JsonWriter w, DateTime v, JsonSerializerOptions o) =>
        w.WriteStringValue(v.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}

public sealed class NullableUtcSecondsConverter : JsonConverter<DateTime?>
{
    readonly UtcSecondsConverter inner = new();
    public override DateTime? Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
        r.TokenType == JsonTokenType.Null ? null : inner.Read(ref r, typeof(DateTime), o);

    public override void Write(Utf8JsonWriter w, DateTime? v, JsonSerializerOptions o)
    {
        if (v is null) w.WriteNullValue(); else inner.Write(w, v.Value, o);
    }
}

public abstract class StringEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    protected abstract (T Value, string Name)[] Map { get; }
    protected abstract T Fallback { get; }

    public override T Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        var s = r.GetString();
        foreach (var (v, n) in Map) if (n == s) return v;
        return Fallback;
    }

    public override void Write(Utf8JsonWriter w, T v, JsonSerializerOptions o)
    {
        foreach (var (x, n) in Map) if (x.Equals(v)) { w.WriteStringValue(n); return; }
        w.WriteStringValue(Map[0].Name);
    }
}

public sealed class LangConverter : StringEnumConverter<Lang>
{
    protected override (Lang, string)[] Map => new[] { (Lang.Ko, "ko"), (Lang.En, "en") };
    protected override Lang Fallback => Lang.Ko;
}

public sealed class PhaseConverter : StringEnumConverter<Phase>
{
    protected override (Phase, string)[] Map => new[] { (Phase.Idle, "idle"), (Phase.Focus, "focus"), (Phase.Wrap, "wrap"), (Phase.Rest, "break") };
    protected override Phase Fallback => Phase.Idle;
}

public sealed class TodoKindConverter : StringEnumConverter<TodoKind>
{
    protected override (TodoKind, string)[] Map => new[] { (TodoKind.Must, "must"), (TodoKind.Nice, "nice"), (TodoKind.Other, "other") };
    protected override TodoKind Fallback => TodoKind.Other;
}
