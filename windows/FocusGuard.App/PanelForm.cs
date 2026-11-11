using FocusGuard.Core;

namespace FocusGuard.App;

/// 트레이 아이콘을 누르면 뜨는 작은 패널 (맥의 메뉴 막대 패널).
/// [일시정지 · 재개] [앱 열기] [중지] = 2 : 1 : 1, 나중에 할 것 입력, 업데이트 알림, 오늘 할 일 (바로 체크).
sealed class PanelForm : AppWindow
{
    const int W = 340;
    enum Ask { None, Pause, OpenApp }
    Ask asking = Ask.None;
    bool choosingPlay;

    readonly Label phase = Ui.Text("", null, Ui.Muted), clock = Ui.Text("", Ui.Big), task = Ui.Text("", null, null, W - 32);
    readonly FlowLayoutPanel body = Ui.Column(W - 32), buttons = Ui.Column(W - 32), todos = Ui.Column(W - 32), extras = Ui.Column(W - 32);
    readonly TextBox later;
    readonly Label laterHint = Ui.Text("", Ui.Small, Ui.Green);
    string buttonsKey = "";

    public PanelForm(Controller app) : base(app, "aADHD")
    {
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ControlBox = false;
        Text = "";
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);
        Deactivate += (_, _) => { if (asking == Ask.None) Hide(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };

        later = Ui.Input(L("💭 나중에 할 것 적기 (Enter)", "💭 Jot down for later (Enter)"), W - 32, text =>
        {
            if (string.IsNullOrWhiteSpace(text) || !Store.AddLater(text)) return;
            laterHint.Text = L("✓ 적어뒀어요. 다시 집중!", "✓ Saved. Back to focus!");
            App_Later_Clear();
            var t = new System.Windows.Forms.Timer { Interval = 700 };
            t.Tick += (_, _) => { t.Dispose(); laterHint.Text = ""; Hide(); };
            t.Start();
        });

        var head = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = W - 32, Margin = new Padding(0) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        phase.Anchor = AnchorStyles.Left;
        clock.Anchor = AnchorStyles.Right;
        head.Controls.Add(phase, 0, 0);
        head.Controls.Add(clock, 1, 0);

        body.Controls.Add(head);
        body.Controls.Add(task);
        body.Controls.Add(buttons);
        body.Controls.Add(later);
        body.Controls.Add(laterHint);
        body.Controls.Add(extras);
        body.Controls.Add(todos);
        Controls.Add(body);
    }

    void App_Later_Clear() => later.Text = "";

    public void ShowNearTray()
    {
        choosingPlay = false;
        RefreshView(true);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        Show();
        Activate();
        later.Focus();
    }

    public override void RefreshView(bool dataChanged)
    {
        var play = Store.PlayLeft;
        if (play is double left)
        {
            phase.Text = left > 0 ? L("🎮 딴짓 중", "🎮 Free time") : L("🎮 딴짓 시간 끝", "🎮 Free time's up");
            clock.Text = left > 0 ? Fmt.Clock(left) : "+" + Fmt.Clock(-left); // 넘긴 시간
            clock.ForeColor = left > 0 ? ForeColor : Ui.Red;
        }
        else
        {
            phase.Text = Store.Phase.Label() + (Store.IsPaused ? L(" · 일시정지", " · Paused") : "");
            clock.Text = Store.Phase == Phase.Idle ? "" : Fmt.Clock(Store.Remaining);
            clock.ForeColor = ForeColor;
        }
        task.Text = Store.Phase.IsStudy() && Store.Timer.Task.Length > 0 ? "📌 " + Store.Timer.Task : "";
        task.Visible = task.Text.Length > 0;
        if (Store.Phase != Phase.Focus || Store.IsPaused) asking = Ask.None;

        var key = $"{Store.Phase}|{Store.IsPaused}|{asking}|{play != null}|{choosingPlay}|{I18n.Current}";
        if (key != buttonsKey) { buttonsKey = key; BuildButtons(); }
        if (dataChanged) { BuildExtras(); BuildTodos(); }
    }

    void BuildButtons()
    {
        buttons.SuspendLayout();
        buttons.Controls.Clear();
        if (asking != Ask.None)
        {
            // 집중 중 일시정지 · 앱 열기 전에 한 번 묻는다
            var box = Ui.Card("", W - 32);
            box.BackColor = Color.FromArgb(252, 236, 236);
            box.Controls.Add(Ui.Text(asking == Ask.Pause ? L("너가 쉴 자격이 있을까?", "Have you earned this break?") : L("공부나 해라.", "Get back to studying."), Ui.Bold));
            var row = Ui.Row();
            row.Controls.Add(Ui.Button(L("계속 집중", "Keep Focusing"), () => { asking = Ask.None; RefreshView(false); }, primary: true));
            var a = asking;
            row.Controls.Add(Ui.Button(a == Ask.Pause ? L("그래도 일시정지", "Pause Anyway") : L("그래도 열기", "Open Anyway"), () =>
            {
                asking = Ask.None;
                if (a == Ask.Pause) App.TogglePause(); else { Hide(); App.ShowDashboard(); }
                RefreshView(false);
            }));
            box.Controls.Add(row);
            buttons.Controls.Add(box);
            buttons.ResumeLayout();
            return;
        }
        var grid = new TableLayoutPanel { Width = W - 32, Height = 40, Margin = new Padding(0, 6, 0, 6) };
        Control fill(Control c) { c.Dock = DockStyle.Fill; c.Margin = new Padding(0, 0, 6, 0); return c; }
        if (Store.Phase == Phase.Idle && Store.PlayLeft != null)
        {
            grid.ColumnCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.Controls.Add(fill(Ui.Button(L("🍅 집중 시작", "🍅 Start Focus"), () => { Hide(); App.PrimaryAction(); }, primary: true)), 0, 0);
            grid.Controls.Add(fill(Ui.Button(L("딴짓 그만", "End Free Time"), () => Store.EndPlay())), 1, 0);
        }
        else if (Store.Phase == Phase.Idle)
        {
            grid.ColumnCount = 3;
            for (var i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            grid.Controls.Add(fill(Ui.Button(L("🍅 시작", "🍅 Start"), () => { Hide(); App.PrimaryAction(); }, primary: true)), 0, 0);
            grid.Controls.Add(fill(Ui.Button(L("🎮 딴짓", "🎮 Free Time"), () => { choosingPlay = !choosingPlay; RefreshView(false); })), 1, 0);
            grid.Controls.Add(fill(Ui.Button(L("앱 열기", "Open App"), () => { Hide(); App.ShowDashboard(); })), 2, 0);
            if (choosingPlay)
            {
                buttons.Controls.Add(grid);
                buttons.Controls.Add(Ui.Text(L("몇 분만 할까요? 끝나면 알려드려요.", "How many minutes? You'll be told when it's over."), Ui.Small, Ui.Muted));
                var row = Ui.Row();
                foreach (var m in Store.PlayChoices)
                    row.Controls.Add(Ui.Button(L($"{m}분", $"{m} min"), () => { choosingPlay = false; Store.StartPlay(m); Hide(); }));
                buttons.Controls.Add(row);
                buttons.ResumeLayout();
                return;
            }
        }
        else
        {
            // 일시정지 : 앱 열기 : 중지 = 2 : 1 : 1
            grid.ColumnCount = 3;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var focusing = Store.Phase == Phase.Focus && !Store.IsPaused;
            grid.Controls.Add(fill(Ui.Button(Store.IsPaused ? L("▶ 재개", "▶ Resume") : L("⏸ 일시정지", "⏸ Pause"), () =>
            {
                if (focusing) { asking = Ask.Pause; RefreshView(false); } else App.TogglePause();
            })), 0, 0);
            grid.Controls.Add(fill(Ui.Button(L("앱 열기", "Open App"), () =>
            {
                if (focusing) { asking = Ask.OpenApp; RefreshView(false); } else { Hide(); App.ShowDashboard(); }
            })), 1, 0);
            var stop = Store.Phase switch { Phase.Wrap => L("완료", "Done"), Phase.Rest => L("다음 집중", "Next Focus"), _ => L("중지", "Stop") };
            grid.Controls.Add(fill(Ui.Button(stop, () => { Hide(); App.PrimaryAction(); })), 2, 0);
        }
        buttons.Controls.Add(grid);
        buttons.ResumeLayout();
    }

    void BuildExtras()
    {
        extras.SuspendLayout();
        extras.Controls.Clear();
        if (Store.SaveError is string err) extras.Controls.Add(Ui.Text("⚠️ " + err, Ui.Small, Ui.Red, W - 32));
        if (App.Updater.Latest is string v)
        {
            var row = Ui.Row();
            row.Controls.Add(Ui.Text(L($"🆕 새 버전 {v}", $"🆕 Version {v} available"), Ui.Bold));
            row.Controls.Add(Ui.Button(L("업데이트", "Update"), () => { Hide(); App.ShowUpdate(); }));
            extras.Controls.Add(row);
        }
        extras.ResumeLayout();
    }

    void BuildTodos()
    {
        todos.SuspendLayout();
        todos.Controls.Clear();
        var list = Store.Today.Todos;
        if (list.Count > 0)
        {
            todos.Controls.Add(new Label { Height = 1, Width = W - 32, BackColor = Color.Gainsboro, Margin = new Padding(0, 6, 0, 6) });
            todos.Controls.Add(Ui.Text(L("오늘 할 일", "Today's to-dos") + $"  {list.Count(t => t.Done)}/{list.Count}", Ui.Small, Ui.Muted));
            foreach (var item in list.Where(t => !t.Done).Concat(list.Where(t => t.Done)))
            {
                var cb = new CheckBox { Text = item.Text, Checked = item.Done, AutoSize = true, Font = Ui.Base, UseMnemonic = false, MaximumSize = new Size(W - 32, 0) };
                if (item.Done) cb.ForeColor = Ui.Muted;
                var id = item.Id;
                cb.Click += (_, _) => Store.ToggleTodo(id, Store.TodayKey);
                todos.Controls.Add(cb);
            }
        }
        // 6개를 넘으면 그 부분만 스크롤
        var many = list.Count > 6;
        todos.AutoSize = !many;
        todos.AutoScroll = many;
        if (many) todos.Size = new Size(W - 32, 260);
        todos.ResumeLayout();
    }
}
