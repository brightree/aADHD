using FocusGuard.Core;

namespace FocusGuard.App;

/// 작은 창 공통: 고정 크기, 내용에 맞춰 커지고, 항상 위
abstract class SmallWindow : AppWindow
{
    protected const int W = 440;
    protected readonly FlowLayoutPanel Body = Ui.Column(W);

    protected SmallWindow(Controller app, string title) : base(app, title)
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Controls.Add(Body);
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }
}

/// 집중 시작: 할 일을 한 줄로 적어야 시작된다
sealed class StartForm : SmallWindow
{
    readonly TextBox task;
    readonly FlowLayoutPanel picks = Ui.Column(W);
    readonly Label question = Ui.Text("", Ui.Title, null, W), lastNote = Ui.Text("", null, Ui.Muted, W), info = Ui.Text("", Ui.Small, Ui.Muted, W);

    public StartForm(Controller app) : base(app, I18n.L("집중 시작", "Start Focus"))
    {
        task = Ui.Input(L("구체적으로 한 줄 (예: pset1 3번 회귀 결과 해석)", "One specific thing (e.g., interpret the regression results for Q3)"), W, _ => Start());
        task.Text = Store.Today.Log.LastOrDefault()?.Task ?? "";
        var row = Ui.Row();
        row.Controls.Add(Ui.Button(L("취소", "Cancel"), Close));
        row.Controls.Add(Ui.Button(L("🍅 집중 시작", "🍅 Start Focus"), Start, primary: true));
        Body.Controls.AddRange(new Control[] { question, lastNote, task, picks, info, row });
        Shown += (_, _) => { task.SelectAll(); if (Quiet) ActiveControl = task; else task.Focus(); }; // 저절로 뜬 창은 누를 때까지 키보드를 가져오지 않는다
    }

    void Start()
    {
        if (string.IsNullOrWhiteSpace(task.Text)) { task.Focus(); return; }
        Store.StartFocus(task.Text);
        Close();
    }

    public override void RefreshView(bool dataChanged)
    {
        var s = Store.Settings;
        question.Text = L($"이번 {s.FocusMin}분 동안 무엇을 할까요?", $"What will you work on for the next {s.FocusMin} minutes?");
        lastNote.Text = Store.LastNote.Length > 0 ? L("지난 정리 메모: ", "Last wrap-up note: ") + Store.LastNote : "";
        lastNote.Visible = lastNote.Text.Length > 0;
        info.Text = s.CycleText();
        if (!dataChanged) return;
        picks.SuspendLayout();
        picks.Controls.Clear();
        var open = Store.Today.Todos.Where(t => !t.Done && t.Kind != TodoKind.Other).Take(5).ToList();
        if (open.Count > 0) picks.Controls.Add(Ui.Text(L("오늘 할 일에서 고르기", "Pick from today's to-dos"), Ui.Small, Ui.Muted));
        foreach (var t in open)
        {
            var text = t.Text;
            picks.Controls.Add(Ui.Button((t.Kind == TodoKind.Must ? "🔥 " : "") + text, () => { task.Text = text; task.Focus(); }));
        }
        picks.ResumeLayout();
    }
}

/// 정리 3분: 어디까지 했는지 메모 + 나중에 할 것 훑기
sealed class WrapForm : SmallWindow
{
    readonly Label left = Ui.Text("", Ui.Title, Ui.Orange), taskLabel = Ui.Text("", null, Ui.Muted, W), laterTitle = Ui.Text("", Ui.Bold);
    readonly TextBox note = new() { Multiline = true, Height = 80, Width = W, Font = Ui.Base, ScrollBars = ScrollBars.Vertical };
    readonly FlowLayoutPanel laterList = Ui.Column(W);

    public WrapForm(Controller app) : base(app, I18n.L("정리", "Wrap-up"))
    {
        note.Text = Store.LastNote;
        note.TextChanged += (_, _) => Store.SaveNote(note.Text);
        var head = Ui.Row();
        head.Controls.Add(Ui.Text(L("📝 정리 시간", "📝 Wrap-up"), Ui.Title));
        head.Controls.Add(left);
        var done = Ui.Button(L("정리 완료 → 휴식", "Done → Start Break"), () => { Store.SaveNote(note.Text); Store.FinishWrap(); Close(); }, primary: true);
        Body.Controls.AddRange(new Control[] { head, taskLabel, Ui.Text(L("어디까지 했고, 다음엔 뭘 하나요?", "Where did you leave off, and what's next?"), Ui.Bold), note, laterTitle, laterList, done });
        Shown += (_, _) => { if (Quiet) ActiveControl = note; else note.Focus(); };
    }

    public override void RefreshView(bool dataChanged)
    {
        left.Text = Store.Phase == Phase.Wrap ? "  " + Fmt.Clock(Store.Remaining) : "";
        taskLabel.Text = Store.Timer.Task.Length > 0 ? "📌 " + Store.Timer.Task : "";
        if (!dataChanged) return;
        var open = Store.OpenLater;
        laterTitle.Text = L($"💭 나중에 할 것 {open.Count}개 — 처리한 건 체크", $"💭 Later ({open.Count}) — check off what's handled");
        laterList.SuspendLayout();
        laterList.Controls.Clear();
        if (open.Count == 0) laterList.Controls.Add(Ui.Text(L("없음 👏", "Nothing here 👏"), null, Ui.Muted));
        foreach (var item in open.Take(12))
        {
            var cb = new CheckBox { Text = item.Text, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(W, 0) };
            var id = item.Id;
            cb.Click += (_, _) => Store.ToggleLater(id);
            laterList.Controls.Add(cb);
        }
        laterList.ResumeLayout();
    }
}

/// 나중에 할 것 빠르게 적기 (Ctrl+Alt+L)
sealed class CaptureForm : SmallWindow
{
    readonly TextBox box;
    readonly Label hint = Ui.Text("", Ui.Small, Ui.Muted, W);

    public CaptureForm(Controller app) : base(app, I18n.L("나중에 할 것", "Later"))
    {
        box = Ui.Input(L("💭 떠오른 생각을 적고 다시 공부로", "💭 Write it down and get back to work"), W, text =>
        {
            if (string.IsNullOrWhiteSpace(text)) { Close(); return; }
            if (!Store.AddLater(text)) return;
            box!.Text = "";
            hint.ForeColor = Ui.Green;
            hint.Text = L("✓ 적어뒀어요. 이제 내려놓고 다시 집중!", "✓ Saved. Let it go and get back to it!");
            var t = new System.Windows.Forms.Timer { Interval = 700 };
            t.Tick += (_, _) => { t.Dispose(); Close(); };
            t.Start();
        });
        box.Font = new Font("Segoe UI", 12f);
        Body.Controls.AddRange(new Control[] { box, hint });
        Shown += (_, _) => { Activate(); box.Focus(); };
    }

    public override void RefreshView(bool dataChanged)
    {
        if (hint.ForeColor == Ui.Green) return;
        hint.ForeColor = Store.SaveError != null ? Ui.Red : Ui.Muted;
        hint.Text = Store.SaveError is string err
            ? L("⚠️ 지금은 저장할 수 없어요. ", "⚠️ Can't save right now. ") + err
            : L("Enter 저장 · Esc 닫기 · 정리 시간에 다시 보여드려요", "Enter to save · Esc to close · You'll see it again at wrap-up");
    }
}

/// 포기하기: 설정한 문장을 입력해야 한다 (띄어쓰기 · 문장부호는 무시)
sealed class GiveUpForm : SmallWindow
{
    readonly bool thenQuit;
    readonly Label target = Ui.Text("", Ui.Bold, null, W), status = Ui.Text(" ", Ui.Small, Ui.Muted, W);
    readonly TextBox phrase;
    readonly Button giveUp;

    public GiveUpForm(Controller app, bool thenQuit) : base(app, I18n.L("포기하기", "Give Up"))
    {
        this.thenQuit = thenQuit;
        phrase = Ui.Input(L("위 문장을 그대로 입력", "Type the phrase above"), W, _ => { if (Matches) GiveUp(); });
        phrase.TextChanged += (_, _) => RefreshView(false);
        giveUp = Ui.Button(L("포기", "Give Up"), GiveUp);
        var keep = Ui.Button(L("계속 집중하기", "Keep Focusing"), Close, primary: true);
        var row = Ui.Row();
        row.Controls.Add(keep);
        row.Controls.Add(giveUp);
        Body.Controls.AddRange(new Control[]
        {
            Ui.Text(L("정말 그만둘까요?", "Give up on this session?"), Ui.Title),
            Ui.Text(L($"하고 싶은 게 생겼다면 {HotKey.Label}로 적어두고 계속해도 돼요. 그래도 그만두려면 아래 문장을 그대로 입력하세요.",
                      $"If something came up, jot it down with {HotKey.Label} and keep going. To quit anyway, type the phrase below."), null, Ui.Muted, W),
            target, phrase, status, row,
        });
        Shown += (_, _) => phrase.Focus();
    }

    bool Matches => SiteRules.NormalizePhrase(phrase.Text) == SiteRules.NormalizePhrase(Store.Settings.StopPhrase);

    void GiveUp()
    {
        if (!Matches) return;
        Store.Stop();
        Close();
        if (thenQuit) App.ExitNow();
    }

    public override void RefreshView(bool dataChanged)
    {
        target.Text = Store.Settings.StopPhrase;
        giveUp.Enabled = Matches;
        status.ForeColor = Matches ? Ui.Green : Ui.Muted;
        status.Text = phrase.Text.Length == 0 ? " " : Matches ? L("✓ 일치해요", "✓ Matches") : L("아직 달라요 (띄어쓰기·문장부호는 상관없어요)", "Not yet (spacing and punctuation don't matter)");
    }
}

/// 딴짓 경고 (집중 외 시간에 한 사이트를 오래 쓸 때)
sealed class WarnForm : SmallWindow
{
    public WarnForm(Controller app, int min, string site) : base(app, I18n.L("딴짓 경고", "Distraction Alert"))
    {
        var row = Ui.Row();
        row.Controls.Add(Ui.Button(L("알고 계속 보기", "Keep Browsing"), Close));
        row.Controls.Add(Ui.Button(L("이 탭 닫기", "Close This Tab"), () => { Close(); App.CloseTabIfStillOn(site); }, primary: true));
        Body.Controls.AddRange(new Control[]
        {
            Ui.Text("⏰", Ui.Big),
            Ui.Text(L($"{site}를 {min}분째 쓰고 있어요", $"You've been on {site} for {min} minutes"), Ui.Title, null, W),
            Ui.Text(L("지금 이게 정말 하려던 일인가요? 뽀모도로 하나만 시작해볼까요?", "Is this what you meant to be doing? How about one Pomodoro?"), null, Ui.Muted, W),
            row,
        });
    }

    public override void RefreshView(bool dataChanged) { }
}

/// 업데이트: 바뀐 내용 보여주고, 받아서 바꿔 넣고 다시 켠다
sealed class UpdateForm : SmallWindow
{
    readonly Label title = Ui.Text("", Ui.Title), current = Ui.Text("", null, Ui.Muted), status = Ui.Text("", Ui.Small, Ui.Muted, W);
    readonly TextBox notes = new() { Multiline = true, ReadOnly = true, Height = 180, Width = W, ScrollBars = ScrollBars.Vertical, Font = Ui.Base };
    readonly Button now;

    public UpdateForm(Controller app) : base(app, I18n.L("업데이트", "Update"))
    {
        now = Ui.Button(L("지금 업데이트", "Update Now"), () => App.Updater.Update(), primary: true);
        var row = Ui.Row();
        row.Controls.Add(Ui.Button(L("나중에", "Later"), Close));
        row.Controls.Add(now);
        Body.Controls.AddRange(new Control[] { title, current, notes, status, row });
    }

    public override void RefreshView(bool dataChanged)
    {
        var u = App.Updater;
        title.Text = L("🍅 새 버전 ", "🍅 Version ") + (u.Latest ?? "") + L("", " is available");
        current.Text = L($"지금 버전 {Updater.Current}", $"You have {Updater.Current}");
        notes.Text = u.Notes.Length > 0 ? u.Notes.Replace("\n", Environment.NewLine) : L("바뀐 내용을 불러오는 중…", "Loading what's new…");
        status.ForeColor = u.Error != null ? Ui.Red : Ui.Muted;
        status.Text = u.Installing ? L("받아서 바꿔 넣는 중… 끝나면 자동으로 다시 켜져요", "Downloading and installing… The app restarts when it's done.") : u.Error ?? "";
        now.Enabled = !u.Installing && u.Latest != null;
    }
}
