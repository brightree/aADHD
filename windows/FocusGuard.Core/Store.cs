using System.Text;

namespace FocusGuard.Core;

/// 작은 설정 저장소 (저장 폴더 위치, 업데이트 확인 시각 등). 기록(data.json)과 따로 둔다.
public interface IPrefs
{
    string? Get(string key);
    void Set(string key, string? value);
}

/// 앱의 두뇌: 타이머 진행, 기록, 나중에 할 것, 할 일, 딴짓 · 학습 통계, 파일 저장. 맥 앱의 Store 와 같은 규칙.
/// 모든 메서드는 화면(UI) 스레드에서 부른다. Tick() 은 1초마다.
public sealed class Store
{
    public DataFile Data { get; private set; } = new();
    public DateTime Now { get; private set; }
    public string DataDir { get; private set; }
    /// 저장에 실패했거나 막혀 있으면 그 이유
    public string? SaveError { get; private set; }

    /// (이전 단계, 새 단계)
    public event Action<Phase, Phase>? PhaseChanged;
    /// (제목, 내용)
    public event Action<string, string>? Notified;
    /// 기록이 바뀔 때마다 (화면 갱신용)
    public event Action? Changed;
    /// 정해 둔 딴짓 시간이 끝났을 때 (집중 시작 창을 띄운다)
    public event Action? PlayOver;

    readonly Func<DateTime> clock;
    readonly IPrefs prefs;
    sealed class Streak { public string Site = ""; public double Sec; public DateTime LastSeen; public int WarnedMin; }
    Streak? streak;
    /// 딴짓 시간을 넘긴 뒤 마지막으로 알린 분 (null = 끝났다는 알림 전)
    int? playNagMin;
    DateTime? lastTickAt, lastStudyTickAt;
    bool saveBlocked;
    DateTime? dirtySince, retryAt;
    DateTime lastPendingFlush;
    string lastBackupKey = "";
    // 딴짓 · 학습 기록은 매초 파일에 쓰지 않고 모았다가 30초마다 반영한다 (날짜 → 이름 → 초)
    readonly Dictionary<string, Dictionary<string, double>> pendingDistract = new(), pendingStudyApps = new(), pendingStudySites = new();

    const string DirKey = "dataDir";
    const double StreakGapSec = 180;
    public const int MorningHour = 6;
    public static readonly int[] PlayChoices = { 5, 10, 15, 30 }; // 딴짓 시간 (분)
    const int PlayNagEveryMin = 5;

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FocusGuard");

    public Store(IPrefs prefs, Func<DateTime>? clock = null, string? defaultDir = null)
    {
        this.prefs = prefs;
        this.clock = clock ?? (() => DateTime.Now);
        Now = this.clock();
        lastPendingFlush = Now;
        DataDir = prefs.Get(DirKey) ?? defaultDir ?? DefaultDir;
        Load();
    }

    // MARK: 파생 값

    public Settings Settings => Data.Settings;
    public TimerState Timer => Data.Timer;
    public Phase Phase => Data.Timer.Phase;
    public bool IsPaused => Data.Timer.PausedRemaining != null;
    public bool IsBlocking => Phase.IsStudy();
    /// 시간을 정해 둔 딴짓 중이면 남은 초 (시간을 넘겼으면 음수)
    public double? PlayLeft => Phase == Phase.Idle && Timer.PlayUntil is DateTime end ? (end - Now).TotalSeconds : null;
    public string TodayKey => Fmt.DayKey(Now);
    public DayStats Today => Data.Days.TryGetValue(TodayKey, out var d) ? d : new DayStats();
    public DayStats DayOf(string key) => Data.Days.TryGetValue(key, out var d) ? d : new DayStats();
    public List<LaterItem> OpenLater => Data.Later.Where(x => !x.Done).ToList();
    public bool CanPersist => !saveBlocked && SaveError == null;
    public string DataPath => Path.Combine(DataDir, "data.json");
    /// 무언가 있으면 true (폴더 · 잠긴 파일처럼 읽을 수 없는 것도) — 덮어쓰지 않기 위해
    bool DataExists => File.Exists(DataPath) || Directory.Exists(DataPath);

    public double Remaining
    {
        get
        {
            if (Timer.PausedRemaining is double p) return p;
            if (Timer.EndTime is DateTime e) return Math.Max(0, (e - Now).TotalSeconds);
            return Settings.FocusMin * 60;
        }
    }

    public double LiveStudySec => StudySec(TodayKey);

    public double StudySec(string key) =>
        DayOf(key).FocusSec + (PendingStudyByDay().TryGetValue(key, out var s) ? s : 0);

    /// 아직 기록에 반영되지 않은 진행 중 공부 시간을 날짜별로 (자정을 넘으면 두 날로 나눈다)
    Dictionary<string, double> PendingStudyByDay()
    {
        var o = new Dictionary<string, double>();
        if (!Phase.IsStudy() || Timer.StartedAt is not DateTime start) return o;
        var until = Timer.EndTime is DateTime e && e < Now ? e : Now;
        var s = start;
        while (s < until)
        {
            var next = s.Date.AddDays(1);
            var end = until < next ? until : next;
            var k = Fmt.DayKey(s);
            o[k] = (o.TryGetValue(k, out var v) ? v : 0) + (end - s).TotalSeconds;
            s = end;
        }
        return o;
    }

    // MARK: 변경 헬퍼

    void Mutate(Action<DataFile> f)
    {
        f(Data);
        dirtySince ??= Now;
        Changed?.Invoke();
    }

    void UpdateDay(string key, Action<DayStats> f) => Mutate(d =>
    {
        if (!d.Days.TryGetValue(key, out var day)) d.Days[key] = day = new DayStats();
        f(day);
    });

    void UpdateToday(Action<DayStats> f) => UpdateDay(TodayKey, f);

    void FlushStudyTime()
    {
        foreach (var (k, sec) in PendingStudyByDay()) if (sec > 0) UpdateDay(k, d => d.FocusSec += sec);
        Mutate(d => d.Timer.StartedAt = null);
    }

    // MARK: 뽀모도로

    void Enter(Phase phase, int minutes, string? task = null)
    {
        var old = Phase;
        Mutate(d =>
        {
            d.Timer.Phase = phase;
            d.Timer.EndTime = Now.AddMinutes(minutes);
            d.Timer.PausedRemaining = null;
            d.Timer.StartedAt = phase.IsStudy() ? Now : null;
            d.Timer.PlayUntil = null;
            if (task != null) d.Timer.Task = task;
        });
        PhaseChanged?.Invoke(old, phase);
    }

    // MARK: 시간을 정해 둔 딴짓

    public void StartPlay(int minutes)
    {
        if (Phase != Phase.Idle) return;
        streak = null;
        playNagMin = null;
        Now = clock();
        Mutate(d => d.Timer.PlayUntil = Now.AddMinutes(minutes));
    }

    public void EndPlay() => Mutate(d => d.Timer.PlayUntil = null);

    /// 시간이 끝나면 한 번 알리고 집중 시작 창을 띄운다. 그 뒤로 계속하면 5분마다 다시 알린다.
    void CheckPlay()
    {
        if (PlayLeft is not double left || left > 0) return;
        var over = (int)(-left / 60);
        if (playNagMin is not int nagged)
        {
            playNagMin = 0;
            streak = null; // 딴짓 경고는 지금부터 다시 센다
            Notify(I18n.L("🎮 딴짓 시간 끝!", "🎮 Free time's up!"), I18n.L("정해 둔 시간이 지났어요. 다음 뽀모도로를 시작해요.", "The time you set is up. Start the next Pomodoro."));
            PlayOver?.Invoke();
            return;
        }
        if (over - nagged < PlayNagEveryMin) return;
        playNagMin = over - over % PlayNagEveryMin;
        Notify(I18n.L($"🎮 딴짓 시간을 {playNagMin}분 넘겼어요", $"🎮 {playNagMin} minutes over your free time"), I18n.L("지금 멈추고 집중을 시작해요.", "Stop here and start focusing."));
    }

    public void StartFocus(string task)
    {
        streak = null;
        Enter(Phase.Focus, Settings.FocusMin, task.Trim());
    }

    void PhaseEnded()
    {
        FlushStudyTime();
        switch (Phase)
        {
            case Phase.Focus:
                var entry = new LogEntry { Task = Timer.Task, Minutes = Settings.FocusMin, End = Now, Done = true };
                var k = TodayKey;
                UpdateDay(k, d => { d.Sessions += 1; d.Log.Add(entry); });
                Mutate(d => { d.Timer.LastLogDay = k; d.Timer.LastLogID = entry.Id; });
                if (Settings.WrapMin > 0)
                {
                    Enter(Phase.Wrap, Settings.WrapMin);
                    Notify(I18n.L($"🍅 집중 완료! 정리 {Settings.WrapMin}분", $"🍅 Focus done! {Settings.WrapMin}-minute wrap-up"),
                           I18n.L("어디까지 했는지, 다음에 뭘 할지 적고 나중에 할 것을 훑어보세요.", "Note where you left off and what's next, then skim your Later list."));
                }
                else StartBreak();
                break;
            case Phase.Wrap: StartBreak(); break;
            case Phase.Rest: EndCycle(); break;
        }
    }

    /// 정리 · 휴식이 0분이면 그 단계는 건너뛴다
    void StartBreak()
    {
        if (Settings.BreakMin <= 0) { EndCycle(); return; }
        Enter(Phase.Rest, Settings.BreakMin);
        Notify(I18n.L($"☕ 휴식 {Settings.BreakMin}분", $"☕ {Settings.BreakMin}-minute break"),
               I18n.L("화면에서 눈을 떼고 일어나 보세요.", "Step away from the screen and stretch."));
    }

    void EndCycle()
    {
        var title = Settings.BreakMin > 0 ? I18n.L("휴식 끝", "Break's over")
                  : Settings.WrapMin > 0 ? I18n.L("정리 끝", "Wrap-up over") : I18n.L("🍅 집중 완료!", "🍅 Focus done!");
        Mutate(d => { d.Timer.Phase = Phase.Idle; d.Timer.EndTime = null; d.Timer.PausedRemaining = null; });
        Notify(title, I18n.L($"다음 뽀모도로를 시작할 시간이에요. 딱 {Settings.FocusMin}분만!", $"Time for the next Pomodoro. Just {Settings.FocusMin} minutes!"));
        PhaseChanged?.Invoke(Phase.Rest, Phase.Idle); // 한 바퀴가 끝났다 (휴식이 0분이어도) → 다음 집중 창
    }

    public void FinishWrap() { if (Phase == Phase.Wrap) PhaseEnded(); }

    /// 방금 끝난 집중 세션에 정리 메모를 붙인다
    public void SaveNote(string note)
    {
        if (Timer.LastLogDay is not string k || Timer.LastLogID is not Guid id) return;
        UpdateDay(k, d => { var e = d.Log.FirstOrDefault(x => x.Id == id); if (e != null) e.Note = note.Trim(); });
    }

    public string LastNote =>
        Timer.LastLogDay is string k && Timer.LastLogID is Guid id ? DayOf(k).Log.FirstOrDefault(x => x.Id == id)?.Note ?? "" : "";

    public void Pause()
    {
        if (Phase == Phase.Idle || IsPaused) return;
        var rem = Remaining;
        FlushStudyTime();
        // 일시정지해도 단계는 그대로라 차단은 유지된다
        Mutate(d => { d.Timer.PausedRemaining = rem; d.Timer.EndTime = null; });
    }

    public void Resume()
    {
        if (Timer.PausedRemaining is not double rem) return;
        Mutate(d =>
        {
            d.Timer.EndTime = Now.AddSeconds(rem);
            d.Timer.PausedRemaining = null;
            d.Timer.StartedAt = d.Timer.Phase.IsStudy() ? Now : null;
        });
    }

    /// 집중 포기 / 휴식 건너뛰기
    public void Stop()
    {
        var old = Phase;
        FlushStudyTime();
        if (old == Phase.Focus)
        {
            var t = Timer.Task;
            UpdateToday(d => { d.Abandoned += 1; d.Log.Add(new LogEntry { Task = t, Minutes = null, End = Now, Done = false }); });
        }
        Mutate(d => { d.Timer.Phase = Phase.Idle; d.Timer.EndTime = null; d.Timer.PausedRemaining = null; });
        PhaseChanged?.Invoke(old, Phase.Idle);
    }

    /// 1초마다 부른다
    public void Tick()
    {
        Now = clock();
        if (Timer.EndTime is DateTime end && Timer.PausedRemaining == null && Now >= end) PhaseEnded();
        CheckPlay();
        if (HasPending && (Now - lastPendingFlush).TotalSeconds >= 30) { FlushPending(); dirtySince ??= Now; }
        // 변경이 계속 들어와도 1초 안에 한 번은 저장한다. 실패하면 30초 뒤 다시.
        if (dirtySince is DateTime ds && (Now - ds).TotalSeconds >= 1 && (retryAt is null || Now >= retryAt)) SaveNow();
    }

    // MARK: 설정

    /// 설정을 저장하고, 사이트로 인식하지 못한 줄을 돌려준다
    public List<string> UpdateSettings(Settings input)
    {
        var s = input.Clamped();
        var rejected = new List<string>();
        List<string> norm(IEnumerable<string> list)
        {
            var o = new List<string>();
            foreach (var raw in list)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var site = SiteRules.NormalizeSite(raw);
                if (site == null) rejected.Add(raw); else if (!o.Contains(site)) o.Add(site);
            }
            return o;
        }
        s.Sites = norm(s.Sites);
        s.AllowSites = norm(s.AllowSites);
        if (string.IsNullOrWhiteSpace(s.StopPhrase) || IsDefaultPhrase(s.StopPhrase)) s.StopPhrase = Settings.DefaultStopPhrase(s.Language);
        Mutate(d => d.Settings = s);
        I18n.Current = s.Language;
        return rejected;
    }

    static bool IsDefaultPhrase(string p) => p == Settings.DefaultStopPhrase(Lang.Ko) || p == Settings.DefaultStopPhrase(Lang.En);

    /// 언어를 바로 바꾼다. 기본 포기 문장을 쓰고 있었다면 그 언어의 기본 문장으로.
    public void SetLanguage(Lang lang)
    {
        Mutate(d =>
        {
            var wasDefault = IsDefaultPhrase(d.Settings.StopPhrase);
            d.Settings.Language = lang;
            if (wasDefault) d.Settings.StopPhrase = Settings.DefaultStopPhrase(lang);
        });
        I18n.Current = lang;
    }

    // MARK: 나중에 할 것

    /// 저장할 수 없으면 받지 않는다 (사라질 항목을 받은 척하지 않게)
    public bool AddLater(string text)
    {
        var t = (text ?? "").Trim();
        if (t.Length > 500) t = t[..500];
        if (t.Length == 0 || !CanPersist) return false;
        var during = Phase.IsStudy() ? Timer.Task : "";
        Mutate(d => d.Later.Add(new LaterItem { Text = t, Created = Now, During = during }));
        return true;
    }

    public void ToggleLater(Guid id) => Mutate(d => { var x = d.Later.FirstOrDefault(i => i.Id == id); if (x != null) x.Done = !x.Done; });
    public void DeleteLater(Guid id) => Mutate(d => d.Later.RemoveAll(i => i.Id == id));
    public void ClearDoneLater() => Mutate(d => d.Later.RemoveAll(i => i.Done));

    // MARK: 할 일 (오늘 또는 미리 적어두는 다른 날)

    public List<TodoItem> TodosOn(string day) => DayOf(day).Todos;

    public void AddTodo(string text, TodoKind kind, string day)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return;
        UpdateDay(day, d => d.Todos.Add(new TodoItem { Text = t, Kind = kind }));
    }

    public void ToggleTodo(Guid id, string day) => UpdateDay(day, d => { var x = d.Todos.FirstOrDefault(i => i.Id == id); if (x != null) x.Done = !x.Done; });

    /// 비우면 원래 글 유지
    public void RenameTodo(Guid id, string text, string day)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return;
        UpdateDay(day, d => { var x = d.Todos.FirstOrDefault(i => i.Id == id); if (x != null) x.Text = t; });
    }

    /// 끌어서 순서 바꾸기: id 를 target 자리로 (아래로 끌면 target 뒤, 위로 끌면 앞)
    public void MoveTodo(Guid id, Guid target, string day)
    {
        if (id == target) return;
        UpdateDay(day, d =>
        {
            int from = d.Todos.FindIndex(i => i.Id == id), to = d.Todos.FindIndex(i => i.Id == target);
            if (from < 0 || to < 0) return;
            var item = d.Todos[from];
            d.Todos.RemoveAt(from);
            d.Todos.Insert(to, item);
        });
    }

    public void DeleteTodo(Guid id, string day) => UpdateDay(day, d => d.Todos.RemoveAll(i => i.Id == id));

    /// 그 날 이전에 할 일을 적었던 가장 가까운 날의 못 한 일
    public List<TodoItem> UnfinishedBefore(string day)
    {
        var key = Data.Days.Where(kv => string.CompareOrdinal(kv.Key, day) < 0 && kv.Value.Todos.Count > 0)
                           .Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).LastOrDefault();
        return key == null ? new() : Data.Days[key].Todos.Where(t => !t.Done).ToList();
    }

    public void CarryOver(string day)
    {
        var items = UnfinishedBefore(day).Select(t => new TodoItem { Text = t.Text, Kind = t.Kind }).ToList();
        UpdateDay(day, d => d.Todos.AddRange(items));
    }

    /// 아침 6시 이후 처음이면 true — 하루 한 번 할 일 창을 띄운다
    public bool ShouldShowMorningPlan(DateTime at)
    {
        var k = Fmt.DayKey(at);
        if (!Settings.MorningPlan || Data.PlanShownDay == k || at.Hour < MorningHour) return false;
        Mutate(d => d.PlanShownDay = k);
        return true;
    }

    // MARK: 딴짓 경고 · 학습 통계

    /// 1초마다. 경고를 띄워야 하면 (분, 사이트)
    public (int Min, string Site)? ReportBrowsing(string? site, bool active)
    {
        var t = clock();
        var delta = lastTickAt is DateTime l ? Math.Min((t - l).TotalSeconds, 5) : 0;
        lastTickAt = t;
        if (streak != null && (t - streak.LastSeen).TotalSeconds > StreakGapSec) streak = null;
        if (!active || site == null || delta <= 0) return null;

        Add(pendingDistract, Fmt.DayKey(t), site, delta);
        if (streak == null || streak.Site != site) streak = new Streak { Site = site, LastSeen = t };
        var s = streak;
        s.Sec += delta;
        s.LastSeen = t;

        // 10분, 20분, 30분... 마다 (시간을 정해 둔 딴짓 중에는 하지 않는다)
        var used = (int)(s.Sec / 60);
        var step = Settings.WarnAfterMin;
        if (IsBlocking || (PlayLeft ?? 0) > 0 || step <= 0 || used < step || used - s.WarnedMin < step) return null;
        s.WarnedMin = used - used % step;
        UpdateToday(d => d.Warnings += 1);
        Notify(I18n.L("⏰ 딴짓 경고", "⏰ Distraction alert"),
               I18n.L($"{site}를 {s.WarnedMin}분째 쓰고 있어요.", $"You've been on {site} for {s.WarnedMin} minutes."));
        return (s.WarnedMin, site);
    }

    /// 1초마다. 집중 · 정리 중이고, 일시정지가 아니고, 사용자가 움직이고 있을 때만 센다.
    public void ReportStudyUsage(string? app, string? site, bool active)
    {
        var t = clock();
        var delta = lastStudyTickAt is DateTime l ? Math.Min((t - l).TotalSeconds, 5) : 0;
        lastStudyTickAt = t;
        if (!Settings.TrackStudyUsage || !Phase.IsStudy() || IsPaused || !active || delta <= 0) return;
        var k = Fmt.DayKey(t);
        if (app != null) Add(pendingStudyApps, k, app, delta);
        if (site != null) Add(pendingStudySites, k, site, delta);
    }

    /// 여러 날의 앱 · 사이트 사용 시간 (아직 반영 안 된 몫 포함)
    public (Dictionary<string, double> Apps, Dictionary<string, double> Sites) StudyUsage(IEnumerable<string> days)
    {
        var apps = new Dictionary<string, double>(); var sites = new Dictionary<string, double>();
        foreach (var k in days)
        {
            Merge(apps, DayOf(k).StudyApps); Merge(sites, DayOf(k).StudySites);
            if (pendingStudyApps.TryGetValue(k, out var pa)) Merge(apps, pa);
            if (pendingStudySites.TryGetValue(k, out var ps)) Merge(sites, ps);
        }
        return (apps, sites);
    }

    static void Add(Dictionary<string, Dictionary<string, double>> m, string day, string name, double sec)
    {
        if (!m.TryGetValue(day, out var d)) m[day] = d = new();
        d[name] = (d.TryGetValue(name, out var v) ? v : 0) + sec;
    }

    static void Merge(Dictionary<string, double> into, Dictionary<string, double> from)
    {
        foreach (var (n, s) in from) into[n] = (into.TryGetValue(n, out var v) ? v : 0) + s;
    }

    bool HasPending => pendingDistract.Count > 0 || pendingStudyApps.Count > 0 || pendingStudySites.Count > 0;

    /// 모아둔 기록을 반영한다 (저장 예약은 하지 않음)
    void FlushPending()
    {
        lastPendingFlush = Now;
        if (!HasPending) return;
        void apply(Dictionary<string, Dictionary<string, double>> src, Func<DayStats, Dictionary<string, double>> pick)
        {
            foreach (var (k, items) in src)
            {
                if (!Data.Days.TryGetValue(k, out var day)) Data.Days[k] = day = new DayStats();
                Merge(pick(day), items);
            }
            src.Clear();
        }
        apply(pendingDistract, d => d.DistractSec);
        apply(pendingStudyApps, d => d.StudyApps);
        apply(pendingStudySites, d => d.StudySites);
        Changed?.Invoke();
    }

    void Notify(string title, string body) => Notified?.Invoke(title, body);

    // MARK: 저장 (data.json + 날짜별 백업)

    void Load()
    {
        saveBlocked = false;
        if (!DataExists)
        {
            Data = new DataFile();
            SaveError = null;
            I18n.Current = Data.Settings.Language;
            return;
        }
        string raw;
        try { raw = File.ReadAllText(DataPath, Encoding.UTF8); }
        catch
        {
            BlockSaving(I18n.L("기록 파일을 읽지 못해서 원본을 지키려고 저장을 멈췄어요. 파일을 확인한 뒤 앱을 다시 켜주세요.",
                               "Couldn't read your data file, so saving is paused to protect it. Check the file, then restart the app."));
            return;
        }
        try
        {
            Data = Json.Read(raw);
            SaveError = null;
        }
        catch
        {
            // 해석할 수 없는 파일은 겹치지 않는 이름으로 복사해 두고, 복사본이 원본과 같을 때만 새로 시작한다
            var copy = Path.Combine(DataDir, $"data.unreadable-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}-{Guid.NewGuid().ToString()[..8]}.json");
            try
            {
                File.Copy(DataPath, copy);
                if (File.ReadAllText(copy, Encoding.UTF8) != raw) throw new IOException("copy mismatch");
                Data = new DataFile();
                SaveError = null;
                Notify(I18n.L("기록 파일이 손상돼 새로 시작해요", "Your data file was damaged, so you are starting fresh"),
                       I18n.L($"원래 파일은 {Path.GetFileName(copy)} 로 보관했어요.", $"The original was kept as {Path.GetFileName(copy)}."));
            }
            catch
            {
                BlockSaving(I18n.L($"기록 파일이 손상됐는데 보관 복사본을 만들지 못해서 저장을 멈췄어요. 원본: {DataPath}",
                                   $"Your data file is damaged and a backup copy couldn't be made, so saving is paused. Original: {DataPath}"));
            }
        }
        I18n.Current = Data.Settings.Language;
    }

    void BlockSaving(string reason)
    {
        saveBlocked = true;
        SaveError = reason;
        Data = new DataFile();
        Notify(I18n.L("기록을 저장하지 않고 있어요", "Your data isn't being saved"), reason);
    }

    /// 앱을 삭제할 때: 이후로는 아무것도 저장하지 않는다
    public void StopSaving() { dirtySince = null; saveBlocked = true; }

    public bool SaveNow()
    {
        FlushPending();
        dirtySince = null;
        if (saveBlocked) return false;
        try
        {
            Directory.CreateDirectory(DataDir);
            var tmp = DataPath + ".tmp";
            File.WriteAllText(tmp, Json.Write(Data), new UTF8Encoding(false));
            File.Move(tmp, DataPath, overwrite: true); // 원자적으로 바꿔 쓰기
        }
        catch (Exception e)
        {
            SaveError = I18n.L($"기록을 저장하지 못했어요: {e.Message}", $"Couldn't save your data: {e.Message}");
            retryAt = Now.AddSeconds(30);
            dirtySince = Now;
            Changed?.Invoke();
            return false;
        }
        var hadError = SaveError != null;
        SaveError = null;
        retryAt = null;
        try { BackupIfNeeded(); } catch { /* 백업 실패는 기록 저장을 막지 않는다 */ }
        if (hadError) Changed?.Invoke();
        return true;
    }

    /// 하루 한 번 backups/data-YYYY-MM-DD.json 을 남기고 최근 14개만 유지한다
    void BackupIfNeeded()
    {
        var key = DataDir + "|" + TodayKey;
        if (key == lastBackupKey) return;
        lastBackupKey = key;
        var dir = Path.Combine(DataDir, "backups");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, $"data-{TodayKey}.json");
        if (!File.Exists(target)) File.Copy(DataPath, target);
        var all = Directory.GetFiles(dir, "data-*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
        foreach (var old in all.Take(Math.Max(0, all.Count - 14))) { try { File.Delete(old); } catch { } }
    }

    /// 저장 폴더를 바꾼다. 새 폴더에 data.json 이 있으면 그걸 불러오고, 없으면 지금 기록을 옮겨 쓴다.
    /// 실패하면 폴더를 바꾸지 않고 이유를 돌려준다.
    public string? ChangeDataDir(string dir)
    {
        if (!saveBlocked && !SaveNow())
            return I18n.L("지금 기록을 저장하지 못해서 폴더를 바꾸지 않았어요.", "Couldn't save your current data, so the folder wasn't changed.") + " " + (SaveError ?? "");
        var (oldDir, oldBlocked, oldError) = (DataDir, saveBlocked, SaveError);
        DataDir = dir;
        if (DataExists)
        {
            Load();
            if (saveBlocked)
            {
                DataDir = oldDir;
                Load();
                return I18n.L("그 폴더의 기록 파일을 읽지 못해서 폴더를 바꾸지 않았어요.", "Couldn't read the data file in that folder, so the folder wasn't changed.");
            }
        }
        else
        {
            saveBlocked = false;
            if (!SaveNow())
            {
                DataDir = oldDir; saveBlocked = oldBlocked; SaveError = oldError;
                return I18n.L("새 폴더에 저장하지 못해서 폴더를 바꾸지 않았어요.", "Couldn't save to the new folder, so the folder wasn't changed.");
            }
        }
        prefs.Set(DirKey, dir);
        Changed?.Invoke();
        return null;
    }
}
