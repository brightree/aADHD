using System.Text;
using FocusGuard.Core;
using Xunit;

namespace FocusGuard.Core.Tests;

sealed class MemPrefs : IPrefs
{
    readonly Dictionary<string, string> v = new();
    public string? Get(string k) => v.TryGetValue(k, out var x) ? x : null;
    public void Set(string k, string? val) { if (val == null) v.Remove(k); else v[k] = val; }
}

/// 시계를 마음대로 움직일 수 있는 Store (실제 기록 폴더는 절대 쓰지 않는다)
sealed class Harness : IDisposable
{
    public DateTime Clock;
    public readonly string Dir;
    public readonly Store Store;
    public readonly List<string> Notes = new();

    public Harness(DateTime? start = null, string? dir = null)
    {
        Clock = start ?? new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Local);
        Dir = dir ?? Path.Combine(Path.GetTempPath(), "fg-test-" + Guid.NewGuid().ToString("N"));
        Store = new Store(new MemPrefs(), () => Clock, Dir);
        Store.Notified += (t, b) => Notes.Add(t);
    }

    public void Advance(double sec) { Clock = Clock.AddSeconds(sec); Store.Tick(); }

    public void Dispose() { try { Directory.Delete(Dir, true); } catch { } }
}

public class ModelTests
{
    [Fact]
    public void ReadsMacFormatAndFillsDefaults()
    {
        const string mac = """
        {
          "days" : { "2026-10-26" : { "focusSec" : 4380, "sessions" : 2, "log" : [ { "id" : "E621E1F8-C36C-495A-93FC-0C247A3E6E5F", "task" : "ppt", "minutes" : 35, "end" : "2026-10-26T06:01:05Z", "done" : true, "note" : "" } ] } },
          "settings" : { "focusMin" : 35, "language" : "ko", "sites" : ["youtube.com"] },
          "timer" : { "phase" : "break", "endTime" : "2026-10-26T06:10:00Z", "task" : "ppt" },
          "version" : 1
        }
        """;
        var d = Json.Read(mac);
        Assert.Equal(Phase.Rest, d.Timer.Phase);
        Assert.NotNull(d.Timer.EndTime);
        Assert.Equal(2, d.Days["2026-10-26"].Sessions);
        Assert.Equal(Guid.Parse("E621E1F8-C36C-495A-93FC-0C247A3E6E5F"), d.Days["2026-10-26"].Log[0].Id);
        Assert.Equal(3, d.Settings.WrapMin); // 없는 필드는 기본값
        Assert.Contains("music.youtube.com", d.Settings.AllowSites);
        Assert.True(d.Settings.MorningPlan);
        Assert.Empty(d.Later);
    }

    [Fact]
    public void WritesSecondsPrecisionUtcDates()
    {
        var d = new DataFile();
        d.Timer.EndTime = new DateTime(2026, 10, 27, 9, 30, 15, 123, DateTimeKind.Local);
        var json = Json.Write(d);
        Assert.Matches("\"endTime\": \"\\d{4}-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\dZ\"", json);
        Assert.Contains("\"phase\": \"idle\"", json);
        var back = Json.Read(json);
        Assert.Equal(d.Timer.EndTime.Value.AddMilliseconds(-123), back.Timer.EndTime);
    }

    [Fact]
    public void ClampsSettings()
    {
        var d = Json.Read("""{ "settings": { "focusMin": 100000, "wrapMin": -3, "breakMin": 0, "warnAfterMin": 999 } }""");
        Assert.Equal(180, d.Settings.FocusMin);
        Assert.Equal(0, d.Settings.WrapMin);
        Assert.Equal(0, d.Settings.BreakMin);
        Assert.Equal(120, d.Settings.WarnAfterMin);
    }
}

public class SiteTests
{
    [Theory]
    [InlineData("https://youtube.com?feature=share", "youtube.com")]
    [InlineData("youtube.com:443", "youtube.com")]
    [InlineData("https://www.youtube.com/watch?v=1", "youtube.com")]
    [InlineData("m.youtube.com#x", "m.youtube.com")]
    [InlineData("Reddit.com/r/x", "reddit.com")]
    public void NormalizesSites(string input, string expected) => Assert.Equal(expected, SiteRules.NormalizeSite(input));

    [Theory]
    [InlineData("hello world")]
    [InlineData("localhost")]
    [InlineData("")]
    public void RejectsNonSites(string input) => Assert.Null(SiteRules.NormalizeSite(input));

    [Theory]
    [InlineData("youtube.com/watch?v=abc", "youtube.com")] // 크롬 주소창은 https:// 를 숨긴다
    [InlineData("https://www.instagram.com/", "instagram.com")]
    [InlineData("http://127.0.0.1:47321/blocked#x", null)]
    [InlineData("how to study", null)] // 검색어
    [InlineData("about:blank", null)]
    public void ExtractsDomains(string url, string? expected) => Assert.Equal(expected, SiteRules.Domain(url));

    [Fact]
    public void AllowListWins()
    {
        var sites = new[] { "youtube.com" }; var allow = new[] { "music.youtube.com" };
        Assert.Equal("youtube.com", SiteRules.Match("m.youtube.com", sites, allow));
        Assert.Null(SiteRules.Match("music.youtube.com", sites, allow));
        Assert.Null(SiteRules.Match("notyoutube.com", sites, allow));
    }

    [Theory]
    [InlineData("꿈꾸던 어른이 되었나요", true)]
    [InlineData("꿈꾸던어른이되었나요", true)]
    [InlineData("꿈꾸던 어른이 되었나요?", true)]
    [InlineData("지금 꿈꾸던 어른이 되었나요", false)]
    [InlineData("꿈꾸던 어른이 되었나", false)]
    public void PhraseMatchIgnoresSpacingAndPunctuation(string typed, bool ok) =>
        Assert.Equal(ok, SiteRules.NormalizePhrase(typed) == SiteRules.NormalizePhrase("꿈꾸던 어른이 되었나요"));
}

public class TimerTests
{
    [Fact]
    public void FocusWrapBreakIdle()
    {
        using var h = new Harness();
        var s = h.Store;
        s.StartFocus("pset");
        Assert.Equal(Phase.Focus, s.Phase);
        Assert.True(s.IsBlocking);
        h.Advance(35 * 60);
        Assert.Equal(Phase.Wrap, s.Phase);
        Assert.True(s.IsBlocking);
        Assert.Equal(1, s.Today.Sessions);
        h.Advance(3 * 60);
        Assert.Equal(Phase.Rest, s.Phase);
        Assert.False(s.IsBlocking);
        h.Advance(7 * 60);
        Assert.Equal(Phase.Idle, s.Phase);
        Assert.Equal(38 * 60, s.Today.FocusSec, 0); // 집중 35 + 정리 3
    }

    [Theory]
    [InlineData(0, 7, Phase.Rest)]
    [InlineData(3, 0, Phase.Wrap)]
    [InlineData(0, 0, Phase.Idle)]
    public void ZeroMinuteWrapOrBreakIsSkipped(int wrap, int rest, Phase afterFocus)
    {
        using var h = new Harness();
        var s = h.Store;
        var st = s.Settings; st.WrapMin = wrap; st.BreakMin = rest; s.UpdateSettings(st);
        var changes = new List<(Phase, Phase)>();
        s.PhaseChanged += (a, b) => changes.Add((a, b));
        s.StartFocus("x");
        h.Advance(35 * 60);
        Assert.Equal(afterFocus, s.Phase);
        Assert.Equal(1, s.Today.Sessions);
        h.Advance(Math.Max(wrap, rest) * 60 + 1);
        h.Advance(Math.Max(wrap, rest) * 60 + 1);
        Assert.Equal(Phase.Idle, s.Phase);
        Assert.Equal((Phase.Rest, Phase.Idle), changes.Last()); // 다음 집중 창이 뜬다
        Assert.DoesNotContain(changes, c => (c.Item2 == Phase.Wrap && wrap == 0) || (c.Item2 == Phase.Rest && rest == 0));
    }

    [Fact]
    public void PauseKeepsBlockingAndStopsTheClock()
    {
        using var h = new Harness();
        var s = h.Store;
        s.StartFocus("x");
        h.Advance(60);
        s.Pause();
        h.Advance(600);
        Assert.True(s.IsPaused);
        Assert.True(s.IsBlocking);
        Assert.Equal(34 * 60, s.Remaining, 0);
        s.Resume();
        h.Advance(60);
        Assert.Equal(33 * 60, s.Remaining, 0);
        Assert.Equal(120, s.LiveStudySec, 0);
    }

    [Fact]
    public void GivingUpLogsAbandoned()
    {
        using var h = new Harness();
        h.Store.StartFocus("x");
        h.Advance(120);
        h.Store.Stop();
        Assert.Equal(Phase.Idle, h.Store.Phase);
        Assert.Equal(1, h.Store.Today.Abandoned);
        Assert.False(h.Store.Today.Log[0].Done);
    }

    [Fact]
    public void StudyTimeSplitsAtMidnight()
    {
        using var h = new Harness(new DateTime(2026, 10, 27, 23, 50, 0, DateTimeKind.Local));
        h.Store.StartFocus("late");
        h.Advance(20 * 60); // 00:10
        h.Store.Stop();
        Assert.Equal(600, h.Store.DayOf("2026-10-27").FocusSec, 0);
        Assert.Equal(600, h.Store.DayOf("2026-10-28").FocusSec, 0);
    }

    [Fact]
    public void WrapNoteAttachesToSessionEvenAfterMidnight()
    {
        using var h = new Harness(new DateTime(2026, 10, 27, 23, 20, 0, DateTimeKind.Local));
        h.Store.StartFocus("late");
        h.Advance(35 * 60 + 1); // 23:55 → 정리
        h.Clock = h.Clock.AddMinutes(10); // 자정 넘어서 메모
        h.Store.SaveNote("자정 넘어 쓴 메모");
        Assert.Equal("자정 넘어 쓴 메모", h.Store.DayOf("2026-10-27").Log.Single().Note);
        Assert.Equal("자정 넘어 쓴 메모", h.Store.LastNote);
    }
}

public class TodoTests
{
    [Fact]
    public void DragReordersTodos()
    {
        using var h = new Harness();
        var s = h.Store;
        var k = s.TodayKey;
        foreach (var t in new[] { "a", "b", "c" }) s.AddTodo(t, TodoKind.Must, k);
        Guid id(string t) => s.TodosOn(k).Single(x => x.Text == t).Id;
        s.MoveTodo(id("a"), id("c"), k); // 아래로 → c 뒤
        Assert.Equal(new[] { "b", "c", "a" }, s.TodosOn(k).Select(x => x.Text));
        s.MoveTodo(id("a"), id("b"), k); // 위로 → b 앞
        Assert.Equal(new[] { "a", "b", "c" }, s.TodosOn(k).Select(x => x.Text));
    }

    [Fact]
    public void TodosLiveOnTheirOwnDay()
    {
        using var h = new Harness();
        var s = h.Store;
        s.AddTodo("오늘 할 일", TodoKind.Must, "2026-10-27");
        s.AddTodo("내일 발표 준비", TodoKind.Must, "2026-10-28");
        var id = s.TodosOn("2026-10-28")[0].Id;
        s.ToggleTodo(id, "2026-10-28");
        Assert.True(s.TodosOn("2026-10-28")[0].Done);
        s.ToggleTodo(id, "2026-10-27"); // 다른 날로는 바뀌지 않는다
        Assert.True(s.TodosOn("2026-10-28")[0].Done);
        s.RenameTodo(id, "  발표 리허설 ", "2026-10-28");
        Assert.Equal("발표 리허설", s.TodosOn("2026-10-28")[0].Text);
        s.RenameTodo(id, "   ", "2026-10-28"); // 비우면 그대로
        Assert.Equal("발표 리허설", s.TodosOn("2026-10-28")[0].Text);
        s.DeleteTodo(id, "2026-10-28");
        Assert.Empty(s.TodosOn("2026-10-28"));
        Assert.Single(s.TodosOn("2026-10-27"));
    }

    [Fact]
    public void CarryOverBringsUnfinished()
    {
        using var h = new Harness();
        h.Store.AddTodo("못 한 일", TodoKind.Must, "2026-10-27");
        h.Store.AddTodo("한 일", TodoKind.Nice, "2026-10-27");
        h.Store.ToggleTodo(h.Store.TodosOn("2026-10-27")[1].Id, "2026-10-27");
        Assert.Equal(new[] { "못 한 일" }, h.Store.UnfinishedBefore("2026-10-28").Select(t => t.Text));
        h.Store.CarryOver("2026-10-28");
        Assert.Equal("못 한 일", h.Store.TodosOn("2026-10-28").Single().Text);
    }

    [Fact]
    public void MorningPlanAfterSixOncePerDay()
    {
        using var h = new Harness();
        var s = h.Store;
        DateTime at(int day, int hh, int mm = 0) => new DateTime(2026, 10, day, hh, mm, 0, DateTimeKind.Local);
        Assert.False(s.ShouldShowMorningPlan(at(28, 5, 59)));
        Assert.True(s.ShouldShowMorningPlan(at(28, 6)));
        Assert.False(s.ShouldShowMorningPlan(at(28, 9)));
        Assert.False(s.ShouldShowMorningPlan(at(29, 1, 30)));
        Assert.True(s.ShouldShowMorningPlan(at(29, 7, 10)));
        var st = s.Settings.Copy(); st.MorningPlan = false; s.UpdateSettings(st);
        Assert.False(s.ShouldShowMorningPlan(at(30, 8)));
    }

    [Fact]
    public void LaterRejectedWhileSavingBlocked()
    {
        using var h = new Harness();
        Assert.True(h.Store.AddLater("논문 찾기"));
        Assert.False(h.Store.AddLater("   "));
        h.Store.StopSaving();
        Assert.False(h.Store.AddLater("저장 안 됨"));
        Assert.Single(h.Store.Data.Later);
    }
}

public class TrackingTests
{
    [Fact]
    public void StudyUsageOnlyDuringActiveFocus()
    {
        using var h = new Harness();
        var s = h.Store;
        void tick(string? app, string? site, bool active = true, int n = 3) { for (var i = 0; i < n; i++) { h.Clock = h.Clock.AddSeconds(1); s.ReportStudyUsage(app, site, active); } }
        tick("Chrome", "scholar.google.com");
        Assert.Empty(s.StudyUsage(new[] { s.TodayKey }).Apps); // 대기 중
        s.StartFocus("x");
        tick("Visual Studio Code", null, n: 4);
        tick("Google Chrome", "chatgpt.com");
        var u = s.StudyUsage(new[] { s.TodayKey });
        Assert.True(u.Apps["Visual Studio Code"] >= 3);
        Assert.True(u.Sites["chatgpt.com"] >= 2);
        tick("Notes", null, active: false); // 자리 비움
        s.Pause(); tick("Notes", null); s.Resume();
        var st = s.Settings.Copy(); st.TrackStudyUsage = false; s.UpdateSettings(st); tick("Notes", null);
        Assert.False(s.StudyUsage(new[] { s.TodayKey }).Apps.ContainsKey("Notes"));
    }

    [Fact]
    public void DistractionIsBatchedAndWarnsAtTenMinutes()
    {
        using var h = new Harness();
        var s = h.Store;
        (int, string)? warn = null;
        for (var i = 0; i < 10 * 60 + 2; i++)
        {
            h.Clock = h.Clock.AddSeconds(1);
            var w = s.ReportBrowsing("youtube.com", true);
            if (w != null) warn = w;
        }
        Assert.Equal((10, "youtube.com"), warn);
        Assert.Equal(1, s.Today.Warnings);
        Assert.Contains(h.Notes, n => n.Contains("딴짓") || n.Contains("Distraction"));
        Assert.Equal(0, s.Today.DistractSec.GetValueOrDefault("youtube.com")); // 아직 반영 전
        s.SaveNow();
        Assert.True(s.Today.DistractSec["youtube.com"] >= 600);
    }

    [Fact]
    public void NoDistractionWarningWhileFocusing()
    {
        using var h = new Harness();
        h.Store.StartFocus("x");
        for (var i = 0; i < 11 * 60; i++) { h.Clock = h.Clock.AddSeconds(1); Assert.Null(h.Store.ReportBrowsing("youtube.com", true)); }
    }

    [Fact]
    public void TimedFreeTimeWarnsWhenOverAndEveryFiveMinutesAfter()
    {
        using var h = new Harness();
        var s = h.Store;
        var over = 0;
        s.PlayOver += () => over++;
        s.StartPlay(15);
        Assert.Equal(15 * 60, s.PlayLeft);
        for (var i = 0; i < 15 * 60 - 1; i++) { h.Advance(1); Assert.Null(s.ReportBrowsing("youtube.com", true)); } // 정해 둔 시간 안에는 경고 없음
        Assert.Equal(0, over);
        h.Advance(1);
        Assert.Equal(1, over);
        Assert.Single(h.Notes, n => n.Contains("🎮"));
        for (var i = 0; i < 4 * 60 + 59; i++) h.Advance(1);
        Assert.Single(h.Notes, n => n.Contains("🎮"));
        h.Advance(1); // 5분 넘김
        Assert.Equal(2, h.Notes.Count(n => n.Contains("🎮")));
        Assert.Equal(1, over);
        Assert.True(s.PlayLeft < 0);

        s.StartFocus("x");
        Assert.Null(s.PlayLeft);
        Assert.Null(s.Timer.PlayUntil);
    }

    [Fact]
    public void EndingFreeTimeEarlyStopsTheTimer()
    {
        using var h = new Harness();
        var s = h.Store;
        s.StartPlay(5);
        h.Advance(60);
        s.EndPlay();
        Assert.Null(s.PlayLeft);
        h.Advance(10 * 60);
        Assert.DoesNotContain(h.Notes, n => n.Contains("🎮"));
    }

    [Fact]
    public void FreeTimeOnlyStartsWhenIdle()
    {
        using var h = new Harness();
        h.Store.StartFocus("x");
        h.Store.StartPlay(10);
        Assert.Null(h.Store.Timer.PlayUntil);
    }
}

public class StorageTests
{
    [Fact]
    public void SavesAndReloads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fg-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var h = new Harness(dir: dir); // Dispose 하면 폴더가 지워지므로 여기선 하지 않는다
            h.Store.AddTodo("저장될 일", TodoKind.Must, h.Store.TodayKey);
            Assert.True(h.Store.SaveNow());
            var again = new Store(new MemPrefs(), () => new DateTime(2026, 10, 27, 11, 0, 0), dir);
            Assert.Equal("저장될 일", again.TodosOn("2026-10-27").Single().Text);
            Assert.True(File.Exists(Path.Combine(dir, "backups", "data-2026-10-27.json")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void DamagedFileIsKeptBeforeStartingFresh()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "data.json"), "{ not json");
            var s = new Store(new MemPrefs(), () => DateTime.Now, dir);
            var copies = Directory.GetFiles(dir, "data.unreadable-*.json");
            Assert.Single(copies);
            Assert.Equal("{ not json", File.ReadAllText(copies[0]));
            Assert.True(s.CanPersist);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void UnreadableFileBlocksSavingAndFolderSwitchRecovers()
    {
        var root = Path.Combine(Path.GetTempPath(), "fg-test-" + Guid.NewGuid().ToString("N"));
        var bad = Path.Combine(root, "bad"); var fresh = Path.Combine(root, "fresh");
        Directory.CreateDirectory(Path.Combine(bad, "data.json")); // data.json 이 폴더라서 읽을 수 없음
        try
        {
            var s = new Store(new MemPrefs(), () => DateTime.Now, bad);
            Assert.False(s.CanPersist);
            Assert.False(s.SaveNow());
            Assert.Null(s.ChangeDataDir(fresh));
            Assert.True(s.CanPersist);
            Assert.True(File.Exists(Path.Combine(fresh, "data.json")));
            Assert.NotNull(s.ChangeDataDir(bad)); // 읽을 수 없는 폴더로는 바꾸지 않는다
            Assert.Equal(fresh, s.DataDir);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void LanguageSwitchSwapsOnlyDefaultPhrase()
    {
        using var h = new Harness();
        var s = h.Store;
        s.SetLanguage(Lang.Ko);
        Assert.Equal("일해야 졸업하지", s.Settings.StopPhrase);
        s.SetLanguage(Lang.En);
        Assert.Equal("Gotta work to graduate", s.Settings.StopPhrase);
        Assert.Equal("Focusing", Phase.Focus.Label());
        Assert.Equal("1h 5m", Fmt.Minutes(3900));
        var st = s.Settings.Copy(); st.StopPhrase = "나만의 문장"; s.UpdateSettings(st);
        s.SetLanguage(Lang.Ko);
        Assert.Equal("나만의 문장", s.Settings.StopPhrase);
        Assert.Equal("1시간 5분", Fmt.Minutes(3900));
    }

    [Fact]
    public void UpdateSettingsNormalizesAndReportsRejected()
    {
        using var h = new Harness();
        var st = h.Store.Settings.Copy();
        st.Sites = new() { "youtube.com?x=1", "not a site", "youtube.com" };
        var rejected = h.Store.UpdateSettings(st);
        Assert.Equal(new[] { "youtube.com" }, h.Store.Settings.Sites);
        Assert.Equal(new[] { "not a site" }, rejected);
    }
}

public class ServerTests
{
    static LocalServer.Request Parse(string raw) => LocalServer.ReadRequest(new MemoryStream(Encoding.UTF8.GetBytes(raw)));

    static (string, string, byte[]) Call(Store store, string raw) =>
        new LocalServer(store, f => f()).Handle(Parse(raw));

    [Theory]
    [InlineData("POST /later HTTP/1.1\r\nHost: 127.0.0.1:47321\r\nContent-Length:\r\n\r\n")]
    [InlineData("POST /later HTTP/1.1\r\nHost: 127.0.0.1:47321\r\nContent-Length: -5\r\n\r\n")]
    [InlineData("POST /later HTTP/1.1\r\nHost: 127.0.0.1:47321\r\nContent-Length: 2\r\nContent-Length: 2\r\n\r\n{}")]
    public void MalformedLengthIs400(string raw) => Assert.Equal("400 Bad Request", Parse(raw).Error);

    [Fact]
    public void HugeBodyIs413() =>
        Assert.Equal("413 Payload Too Large", Parse("POST /later HTTP/1.1\r\nHost: 127.0.0.1:47321\r\nContent-Length: 999999\r\n\r\n").Error);

    [Fact]
    public void OnlyOurOwnHostAndOrigin()
    {
        using var h = new Harness();
        Assert.Equal("200 OK", Call(h.Store, "GET /status HTTP/1.1\r\nHost: 127.0.0.1:47321\r\n\r\n").Item1);
        Assert.Equal("403 Forbidden", Call(h.Store, "GET /status HTTP/1.1\r\nHost: evil.example:47321\r\n\r\n").Item1);
        Assert.Equal("403 Forbidden", Call(h.Store, "GET /status HTTP/1.1\r\nHost: localhost:47321\r\n\r\n").Item1);
        Assert.Equal("403 Forbidden", Call(h.Store, "POST /later HTTP/1.1\r\nHost: 127.0.0.1:47321\r\nOrigin: https://evil.example\r\nContent-Length: 15\r\n\r\n{\"text\":\"spam\"}").Item1);
        Assert.Empty(h.Store.Data.Later);
    }

    [Fact]
    public void LaterAndStatus()
    {
        using var h = new Harness();
        h.Store.StartFocus("WDS 발표자료");
        Assert.Equal("400 Bad Request", Call(h.Store, "POST /later HTTP/1.1\r\nHost: 127.0.0.1:47321\r\nContent-Length: 11\r\n\r\n{\"text\":\"\"}").Item1);
        var body = "{\"text\":\"강의 다시 보기\"}";
        var len = Encoding.UTF8.GetByteCount(body);
        Assert.Equal("200 OK", Call(h.Store, $"POST /later HTTP/1.1\r\nHost: 127.0.0.1:47321\r\nContent-Length: {len}\r\n\r\n{body}").Item1);
        Assert.Equal("강의 다시 보기", h.Store.Data.Later.Single().Text);
        var status = Encoding.UTF8.GetString(Call(h.Store, "GET /status HTTP/1.1\r\nHost: 127.0.0.1:47321\r\n\r\n").Item3);
        Assert.Contains("\"block\":true", status);
        Assert.Contains("\"phase\":\"focus\"", status);
        h.Store.StopSaving();
        Assert.Equal("503 Service Unavailable", Call(h.Store, $"POST /later HTTP/1.1\r\nHost: 127.0.0.1:47321\r\nContent-Length: {len}\r\n\r\n{body}").Item1);
    }

    [Fact]
    public void BlockedPageMatchesLanguage()
    {
        I18n.Current = Lang.En;
        var en = BlockedPage.Html();
        Assert.DoesNotMatch("[가-힣]", en);
        Assert.Contains("<html lang=\"en\">", en);
        Assert.Contains("Ctrl+W", en);
        I18n.Current = Lang.Ko;
        Assert.Contains("지금은 집중 시간이에요", BlockedPage.Html());
    }
}

public class ReleaseTests
{
    [Theory]
    [InlineData("1.2.0", "1.1.1", true)]
    [InlineData("1.10.0", "1.9.2", true)]
    [InlineData("1.1.1", "1.1.1", false)]
    [InlineData("1.1.0", "1.1.1", false)]
    [InlineData("1.2", "1.2.0", false)]
    public void ComparesVersionsNumerically(string a, string b, bool newer) => Assert.Equal(newer, ReleaseInfo.IsNewer(a, b));

    [Fact]
    public void ParsesReleaseAndFindsWindowsAsset()
    {
        var json = """{"tag_name":"v1.7.0","body":"- 윈도우 지원","assets":[{"name":"FocusGuard.zip","browser_download_url":"https://x/mac.zip"},{"name":"FocusGuard-Windows.zip","browser_download_url":"https://x/win.zip"}]}""";
        var r = ReleaseInfo.Parse(json)!;
        Assert.Equal("1.7.0", r.Version);
        Assert.Equal("- 윈도우 지원", r.Notes);
        Assert.Equal("https://x/win.zip", r.AssetUrl);
        Assert.Null(ReleaseInfo.Parse("""{"tag_name":"v1.7.0","assets":[]}""")!.AssetUrl); // 아직 안 올라옴
        Assert.Null(ReleaseInfo.Parse("not json"));
    }
}
