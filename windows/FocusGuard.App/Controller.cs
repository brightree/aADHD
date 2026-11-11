using System.Drawing.Drawing2D;
using System.Drawing.Text;
using FocusGuard.Core;

namespace FocusGuard.App;

/// 앱의 중심: 트레이 아이콘, 1초 타이머, 창 관리, 알림, 로컬 서버 · 브라우저 감시 · 업데이트 시작.
sealed class Controller : ApplicationContext
{
    public static readonly Icon AppIcon = LoadIcon();
    public const string CloseTabKey = "Ctrl+W";

    public readonly Store Store;
    public readonly Updater Updater;
    public readonly NotifyIcon Tray;
    readonly LocalServer server;
    readonly BrowserWatcher watcher;
    readonly HotKey? hotKey;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly Dictionary<string, AppWindow> windows = new();
    readonly Control invoker = new();
    PanelForm? panel;
    string lastTrayText = "";
    bool dataChanged, refreshQueued;

    /// 브라우저 감시 스레드가 읽는 지금 상태 (화면 스레드에서만 바꾼다)
    public bool ServerListening { get; }

    public volatile WatchSnapshot Snapshot = new(false, Array.Empty<string>(), Array.Empty<string>());

    public Controller(Store store, IPrefs prefs, bool selfTest = false, int port = LocalServer.Port)
    {
        Store = store;
        invoker.CreateControl();
        Updater = new Updater(this, prefs);
        Tray = new NotifyIcon { Icon = AppIcon, Text = "aADHD", Visible = !selfTest };
        Tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) TogglePanel(); };
        Tray.BalloonTipClicked += (_, _) => TogglePanel();
        var menu = new ContextMenuStrip();
        menu.Items.Add(L("앱 열기", "Open App"), null, (_, _) => ShowDashboard());
        menu.Items.Add(L("종료", "Quit"), null, (_, _) => Quit());
        Tray.ContextMenuStrip = menu;

        Store.Notified += (t, b) => { if (!selfTest) Tray.ShowBalloonTip(5000, t, b, ToolTipIcon.None); };
        Store.Changed += () => { dataChanged = true; QueueRefresh(); };
        Store.PhaseChanged += OnPhaseChanged;
        Store.PlayOver += ShowStart;

        server = new LocalServer(store, f => OnUi(f), CloseTabKey);
        ServerListening = server.Start(port); // 포트가 이미 쓰이면 차단 페이지만 안 뜬다 (차단은 계속)
        watcher = new BrowserWatcher(this);
        if (!selfTest)
        {
            hotKey = new HotKey(ShowCapture);
            watcher.Start();
            Updater.Start();
        }
        timer.Tick += (_, _) => Second();
        timer.Start();
        UpdateSnapshot();
        RenderTray();
    }

    static string L(string ko, string en) => I18n.L(ko, en);

    static Icon LoadIcon()
    {
        using var s = typeof(Controller).Assembly.GetManifestResourceStream("tomato.ico");
        return s != null ? new Icon(s) : SystemIcons.Application;
    }

    /// 백그라운드 스레드에서 화면 스레드로 (서버 · 브라우저 감시가 Store 를 만질 때)
    public object? OnUi(Func<object?> f) => invoker.InvokeRequired ? invoker.Invoke(f) : f();

    public void Post(Action a) { if (!invoker.IsDisposed) invoker.BeginInvoke(a); }

    void Second()
    {
        Store.Tick();
        UpdateSnapshot();
        RenderTray();
        if (Store.ShouldShowMorningPlan(Store.Now)) ShowPlan(Store.TodayKey);
        RefreshAll();
    }

    void UpdateSnapshot()
    {
        var s = Store.Settings;
        Snapshot = new WatchSnapshot(Store.IsBlocking, s.Sites.ToArray(), s.AllowSites.ToArray());
    }

    /// 기록 밖의 상태(업데이트 등)가 바뀌었을 때 창을 다시 그리게 한다
    public void RequestRefresh() { dataChanged = true; QueueRefresh(); }

    void QueueRefresh()
    {
        if (refreshQueued) return;
        refreshQueued = true;
        Post(() => { refreshQueued = false; RefreshAll(); });
    }

    void RefreshAll()
    {
        var changed = dataChanged;
        dataChanged = false;
        foreach (var w in windows.Values.ToList()) if (!w.IsDisposed && w.Visible) w.RefreshView(changed);
        if (panel is { IsDisposed: false, Visible: true }) panel.RefreshView(changed);
    }

    // MARK: 트레이 아이콘 (남은 분을 아이콘 위에 쓴다)

    void RenderTray()
    {
        var play = Store.PlayLeft;
        var minutes = play is double left ? (left > 0 ? ((int)Math.Ceiling(left / 60)).ToString() : "+" + (int)(-left / 60)) // 넘긴 시간
            : Store.Phase == Phase.Idle ? "" : ((int)Math.Ceiling(Store.Remaining / 60)).ToString();
        var key = $"{Store.Phase}|{Store.IsPaused}|{play != null}|{minutes}";
        if (key == lastTrayText) return;
        lastTrayText = key;
        var tip = play is double l2 ? (l2 > 0 ? L($"🎮 딴짓 중 · {minutes}분", $"🎮 Free time · {minutes} min") : L($"🎮 딴짓 시간 {minutes[1..]}분 넘김", $"🎮 {minutes[1..]} min over free time"))
            : Store.Phase == Phase.Idle ? "aADHD" : $"{Store.Phase.Label()} · {minutes}{L("분", " min")}" + (Store.Timer.Task.Length > 0 ? $" · {Store.Timer.Task}" : "");
        Tray.Text = tip.Length > 63 ? tip[..63] : tip;
        var old = Tray.Icon;
        Tray.Icon = play is double l3 ? DrawBadge(minutes, l3 > 0 ? Ui.Blue : Ui.Red)
            : Store.Phase == Phase.Idle ? AppIcon : DrawBadge(minutes, Store.IsPaused ? Color.Gray : Store.Phase switch { Phase.Focus => Ui.Red, Phase.Wrap => Ui.Orange, _ => Ui.Green });
        if (old != AppIcon) old?.Dispose();
    }

    static Icon DrawBadge(string text, Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 0, 0, 31, 31);
            using var f = new Font("Segoe UI", text.Length >= 3 ? 11f : 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            var sz = g.MeasureString(text, f);
            g.DrawString(text, f, Brushes.White, (32 - sz.Width) / 2, (32 - sz.Height) / 2);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // MARK: 창

    public T Show<T>(string id, Func<T> make) where T : AppWindow
    {
        if (windows.TryGetValue(id, out var w) && !w.IsDisposed && w is T existing)
        {
            if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
            existing.RefreshView(true);
            existing.Show();
            existing.Activate();
            return existing;
        }
        var n = make();
        windows[id] = n;
        n.FormClosed += (_, _) => windows.Remove(id);
        n.RefreshView(true);
        n.Show();
        n.Activate();
        return n;
    }

    public void Close(string id) { if (windows.TryGetValue(id, out var w) && !w.IsDisposed) w.Close(); }

    public IEnumerable<AppWindow> OpenWindows => windows.Values;

    public void TogglePanel()
    {
        if (panel is { IsDisposed: false, Visible: true }) { panel.Hide(); return; }
        panel ??= new PanelForm(this);
        if (panel.IsDisposed) panel = new PanelForm(this);
        panel.ShowNearTray();
    }

    public void HidePanel() { if (panel is { IsDisposed: false }) panel.Hide(); }

    public void ShowStart() => Show("start", () => new StartForm(this));
    public void ShowWrap() => Show("wrap", () => new WrapForm(this));
    public void ShowCapture() => Show("capture", () => new CaptureForm(this));
    public void ShowGiveUp(bool thenQuit = false) => Show("giveup", () => new GiveUpForm(this, thenQuit));
    public void ShowDashboard() => Show("dashboard", () => new DashboardForm(this));
    public void ShowStats() => Show("stats", () => new StatsForm(this));
    public void ShowSettings() => Show("settings", () => new SettingsForm(this));
    public void ShowUpdate() => Show("update", () => new UpdateForm(this));
    public void ShowPlan(string day) => Show("plan", () => new PlanForm(this, day)).SetDay(day);
    public void ShowWarning(int min, string site) => Show("warn", () => new WarnForm(this, min, site));

    void OnPhaseChanged(Phase old, Phase now)
    {
        UpdateSnapshot();
        RenderTray();
        switch (now)
        {
            case Phase.Wrap: ShowWrap(); break;
            case Phase.Rest: Close("wrap"); break;
            case Phase.Idle when old == Phase.Rest: ShowStart(); break; // 휴식이 끝나면 바로 다음 할 일을 묻는다
        }
    }

    /// 단계별 시작 / 중지 버튼
    public void PrimaryAction()
    {
        switch (Store.Phase)
        {
            case Phase.Idle: ShowStart(); break;
            case Phase.Focus: ShowGiveUp(); break;
            case Phase.Wrap: Store.FinishWrap(); Close("wrap"); break;
            case Phase.Rest: Store.Stop(); break;
        }
    }

    public void TogglePause() { if (Store.IsPaused) Store.Resume(); else Store.Pause(); RenderTray(); }

    /// 집중 · 정리 중에 끄면 차단이 풀리므로 포기 절차를 거친다
    public void Quit()
    {
        if (Store.IsBlocking) { ShowGiveUp(thenQuit: true); return; }
        ExitNow();
    }

    public void ExitNow()
    {
        Store.SaveNow();
        Shutdown();
        ExitThread();
    }

    public void Shutdown()
    {
        timer.Stop();
        watcher.Dispose();
        hotKey?.Dispose();
        server.Dispose();
        Tray.Visible = false;
        Tray.Dispose();
    }

    public void CloseTabIfStillOn(string site) => watcher.CloseTabIfStillOn(site);
}

/// 브라우저 감시 스레드에 넘기는 상태
sealed record WatchSnapshot(bool Blocking, string[] Sites, string[] Allow);
