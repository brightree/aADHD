using FocusGuard.Core;

namespace FocusGuard.App;

/// 학습 통계: 일 · 주 전환, 공부 시간 · 뽀모도로 · 집중 성공률, 집중 중 많이 쓴 앱 · 사이트 TOP 5
sealed class StatsForm : AppWindow
{
    const int W = 600;
    bool week;
    int offset;
    readonly Label title = Ui.Text("", Ui.Title);
    readonly RadioButton dayRadio, weekRadio;
    readonly FlowLayoutPanel content = Ui.Column(W);
    readonly Button next;

    public StatsForm(Controller app) : base(app, I18n.L("학습 통계", "Study Stats"))
    {
        AutoScroll = true;
        Size = new Size(W + 70, 720);
        var root = Ui.Column(W);
        var top = Ui.Row();
        dayRadio = new RadioButton { Text = L("일", "Day"), Checked = true, AutoSize = true, Appearance = Appearance.Button };
        weekRadio = new RadioButton { Text = L("주", "Week"), AutoSize = true, Appearance = Appearance.Button };
        dayRadio.CheckedChanged += (_, _) => { if (dayRadio.Checked) { week = false; offset = 0; RefreshView(true); } };
        weekRadio.CheckedChanged += (_, _) => { if (weekRadio.Checked) { week = true; offset = 0; RefreshView(true); } };
        top.Controls.Add(dayRadio);
        top.Controls.Add(weekRadio);
        top.Controls.Add(new Label { Width = 20 });
        top.Controls.Add(Ui.Button("◀", () => { offset--; RefreshView(true); }));
        top.Controls.Add(title);
        next = Ui.Button("▶", () => { offset++; RefreshView(true); });
        top.Controls.Add(next);
        root.Controls.Add(top);
        root.Controls.Add(content);
        Controls.Add(root);
    }

    string[] Keys()
    {
        if (!week) return new[] { Fmt.DayKey(Store.Now.AddDays(offset)) };
        var monday = Fmt.Monday(Store.Now).AddDays(7 * offset);
        return Enumerable.Range(0, 7).Select(i => Fmt.DayKey(monday.AddDays(i))).ToArray();
    }

    public override void RefreshView(bool dataChanged)
    {
        var keys = Keys();
        next.Enabled = offset < 0;
        if (week)
        {
            string md(string k) { var d = Fmt.ParseDay(k)!.Value; return $"{d.Month}/{d.Day}"; }
            title.Text = (offset == 0 ? L("이번 주 ", "This week ") : "") + $"{md(keys[0])} – {md(keys[6])}";
        }
        else
        {
            var rel = Fmt.Relative(keys[0], Store.TodayKey);
            title.Text = Fmt.DayTitle(keys[0]) + (rel != null ? $" ({rel})" : "");
        }
        if (!dataChanged) return;

        var days = keys.Select(Store.DayOf).ToList();
        var study = keys.Sum(Store.StudySec);
        var sessions = days.Sum(d => d.Sessions);
        var tries = sessions + days.Sum(d => d.Abandoned);
        var usage = Store.StudyUsage(keys);
        content.SuspendLayout();
        content.Controls.Clear();
        var kpis = Ui.Row();
        kpis.Controls.Add(Ui.Kpi(Fmt.Minutes(study), L("공부 시간", "Study time"), 140));
        kpis.Controls.Add(Ui.Kpi(sessions.ToString(), L("완료한 뽀모도로", "Pomodoros done"), 140));
        kpis.Controls.Add(Ui.Kpi(tries == 0 ? "–" : $"{Math.Round(100.0 * sessions / tries)}%", L("집중 성공률", "Focus success rate"), 140));
        kpis.Controls.Add(week
            ? Ui.Kpi(L($"{keys.Count(k => Store.StudySec(k) >= 60)}/7일", $"{keys.Count(k => Store.StudySec(k) >= 60)}/7 days"), L("공부한 날", "Days studied"), 140)
            : Ui.Kpi(Fmt.Minutes(days.Sum(d => d.DistractTotal)), L("딴짓", "Distractions"), 140));
        content.Controls.Add(kpis);
        content.Controls.Add(TopCard(L("집중 중 많이 쓴 앱", "Top apps while focusing"), usage.Apps, Ui.Orange));
        content.Controls.Add(TopCard(L("집중 중 많이 본 사이트", "Top sites while focusing"), usage.Sites, Ui.Blue));
        content.Controls.Add(Ui.Text(Store.Settings.TrackStudyUsage
            ? L("집중·정리 중(일시정지·자리 비움 제외) 맨 앞에 있던 앱과 사이트 도메인만 세요. 이 컴퓨터에만 저장돼요.",
                "Counts only the frontmost app and site domain during focus and wrap-up (not while paused or away). Stored only on this computer.")
            : L("앱·사이트 기록이 꺼져 있어요. 설정에서 켤 수 있어요.", "App and site tracking is off. You can turn it on in Settings."), Ui.Small, Ui.Muted, W));
        content.ResumeLayout();
    }

    static Control TopCard(string title, Dictionary<string, double> items, Color color)
    {
        var card = Ui.Card(title, W);
        var total = items.Values.Sum();
        if (total < 60) { card.Controls.Add(Ui.Text(I18n.L("아직 기록이 없어요. 집중을 시작하면 쌓여요.", "Nothing yet. Start a focus session to see data here."), null, Ui.Muted)); return card; }
        var sorted = items.OrderByDescending(kv => kv.Value).ToList();
        card.Controls.Add(new BarList(sorted.Take(5).Select(kv => (kv.Key, kv.Value)).ToList(), color, W - 24));
        if (sorted.Count > 5)
        {
            var rest = sorted.Skip(5).Sum(kv => kv.Value);
            card.Controls.Add(Ui.Text(I18n.L($"기타 {sorted.Count - 5}개 · {Math.Round(rest / total * 100)}%", $"{sorted.Count - 5} more · {Math.Round(rest / total * 100)}%"), Ui.Small, Ui.Muted));
        }
        return card;
    }
}

/// 설정: 언어(바로 적용), 시간, 포기 문장, 차단 · 허용 사이트, 기록 · 아침 창 · 자동 실행, 저장 위치, 앱 삭제
sealed class SettingsForm : AppWindow
{
    const int W = 480;
    readonly NumericUpDown focus, wrap, rest, warn;
    readonly TextBox phrase, sites, allow;
    readonly CheckBox track, morning, login;
    readonly Label saved = Ui.Text("", Ui.Small, Ui.Muted), rejected = Ui.Text("", Ui.Small, Ui.Red, W), dir = Ui.Text("", Ui.Small, Ui.Muted, W);

    public SettingsForm(Controller app) : base(app, I18n.L("aADHD 설정", "aADHD Settings"))
    {
        AutoScroll = true;
        Size = new Size(W + 70, 820);
        var s = Store.Settings;
        var root = Ui.Column(W);

        var langRow = Ui.Row();
        langRow.Controls.Add(Ui.Text("언어 / Language   ", Ui.Bold));
        var ko = new RadioButton { Text = "한국어", AutoSize = true, Checked = s.Language == Lang.Ko };
        var en = new RadioButton { Text = "English", AutoSize = true, Checked = s.Language == Lang.En };
        // 바로 적용: 이 창을 그 언어로 다시 연다
        ko.CheckedChanged += (_, _) => { if (ko.Checked && Store.Settings.Language != Lang.Ko) SwitchLanguage(Lang.Ko); };
        en.CheckedChanged += (_, _) => { if (en.Checked && Store.Settings.Language != Lang.En) SwitchLanguage(Lang.En); };
        langRow.Controls.Add(ko);
        langRow.Controls.Add(en);
        root.Controls.Add(langRow);

        root.Controls.Add(Ui.Text(L("시간", "Timer"), Ui.Bold));
        NumericUpDown num(string label, int value, (int Min, int Max) r)
        {
            var row = Ui.Row();
            row.Controls.Add(new Label { Text = label, Width = 260, Font = Ui.Base, Margin = new Padding(0, 6, 0, 0) });
            var n = new NumericUpDown { Minimum = r.Min, Maximum = r.Max, Value = Math.Clamp(value, r.Min, r.Max), Width = 80 };
            row.Controls.Add(n);
            root.Controls.Add(row);
            return n;
        }
        focus = num(L("집중 (분)", "Focus (min)"), s.FocusMin, Settings.FocusRange);
        wrap = num(L("정리 (분)", "Wrap-up (min)"), s.WrapMin, Settings.WrapRange);
        rest = num(L("휴식 (분)", "Break (min)"), s.BreakMin, Settings.BreakRange);
        warn = num(L("연속 사용 경고 (분)", "Distraction alert after (min)"), s.WarnAfterMin, Settings.WarnRange);
        var reset = new LinkLabel { Text = L("기본값으로 (35 · 3 · 7)", "Reset to Defaults (35 · 3 · 7)"), AutoSize = true };
        reset.Click += (_, _) => { focus.Value = 35; wrap.Value = 3; rest.Value = 7; };
        root.Controls.Add(reset);

        root.Controls.Add(Ui.Text(L("포기할 때 입력할 문장", "Phrase to type when giving up"), Ui.Bold));
        phrase = new TextBox { Text = s.StopPhrase, Width = W };
        root.Controls.Add(phrase);
        root.Controls.Add(Ui.Text(L("차단할 사이트 (한 줄에 하나, 하위 도메인 포함)", "Blocked sites (one per line, includes subdomains)"), Ui.Bold));
        sites = new TextBox { Multiline = true, Height = 130, Width = W, ScrollBars = ScrollBars.Vertical, Text = string.Join(Environment.NewLine, s.Sites) };
        root.Controls.Add(sites);
        root.Controls.Add(Ui.Text(L("허용할 사이트 (위 목록의 하위 도메인이어도 막지 않음)", "Allowed sites (never blocked, even under a blocked domain)"), Ui.Bold));
        allow = new TextBox { Multiline = true, Height = 50, Width = W, ScrollBars = ScrollBars.Vertical, Text = string.Join(Environment.NewLine, s.AllowSites) };
        root.Controls.Add(allow);
        track = new CheckBox { Text = L("집중 중 쓴 앱 · 사이트 기록 (학습 통계용, 이 컴퓨터에만 저장)", "Track apps and sites used while focusing (for stats, stored only on this computer)"), Checked = s.TrackStudyUsage, AutoSize = true, MaximumSize = new Size(W, 0) };
        morning = new CheckBox { Text = L("아침 6시 이후 처음 컴퓨터를 쓸 때 할 일 창 띄우기", "Show to-dos the first time I use my computer after 6 a.m."), Checked = s.MorningPlan, AutoSize = true, MaximumSize = new Size(W, 0) };
        root.Controls.Add(track);
        root.Controls.Add(morning);

        var saveRow = Ui.Row();
        saveRow.Controls.Add(Ui.Button(L("저장", "Save"), Save, primary: true));
        saveRow.Controls.Add(saved);
        root.Controls.Add(saveRow);
        root.Controls.Add(rejected);

        root.Controls.Add(new Label { Height = 1, Width = W, BackColor = Color.Gainsboro, Margin = new Padding(0, 8, 0, 8) });
        login = new CheckBox { Text = L("로그인 시 자동 실행 (꺼져 있으면 사이트 차단도 안 돼요)", "Launch at login (blocking only works while the app is running)"), AutoSize = true, MaximumSize = new Size(W, 0) };
        try { login.Checked = Win32.LaunchAtLogin; } catch { }
        login.CheckedChanged += (_, _) => { try { Win32.LaunchAtLogin = login.Checked; } catch { } };
        root.Controls.Add(login);

        root.Controls.Add(new Label { Height = 1, Width = W, BackColor = Color.Gainsboro, Margin = new Padding(0, 8, 0, 8) });
        root.Controls.Add(Ui.Text(L("기록 저장 위치", "Data location"), Ui.Bold));
        root.Controls.Add(dir);
        root.Controls.Add(Ui.Text(L("매일 자동으로 백업해요. 앱을 업데이트해도 기록은 그대로예요.", "Backed up automatically every day. Updating the app keeps your data."), Ui.Small, Ui.Muted, W));
        var dirRow = Ui.Row();
        dirRow.Controls.Add(Ui.Button(L("다른 폴더 선택…", "Choose Folder…"), ChooseFolder));
        dirRow.Controls.Add(Ui.Button(L("탐색기에서 보기", "Show in Explorer"), () =>
        {
            Store.SaveNow();
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{Store.DataPath}\"");
        }));
        root.Controls.Add(dirRow);

        root.Controls.Add(new Label { Height = 1, Width = W, BackColor = Color.Gainsboro, Margin = new Padding(0, 8, 0, 8) });
        var bottom = Ui.Row();
        bottom.Controls.Add(Ui.Button(L("aADHD 삭제…", "Delete aADHD…"), () => Uninstaller.Run(App)));
        bottom.Controls.Add(Ui.Text($"   v{Updater.Current} · Made by Anonymous, KAIST IS", Ui.Small, Ui.Muted));
        root.Controls.Add(bottom);
        Controls.Add(root);
    }

    void SwitchLanguage(Lang lang)
    {
        Store.SetLanguage(lang);
        BeginInvoke(() => { Close(); App.ShowSettings(); }); // 창 글자를 새 언어로
    }

    void Save()
    {
        var s = Store.Settings.Copy();
        s.FocusMin = (int)focus.Value; s.WrapMin = (int)wrap.Value; s.BreakMin = (int)rest.Value; s.WarnAfterMin = (int)warn.Value;
        s.StopPhrase = phrase.Text;
        s.Sites = sites.Lines.ToList();
        s.AllowSites = allow.Lines.ToList();
        s.TrackStudyUsage = track.Checked;
        s.MorningPlan = morning.Checked;
        var bad = Store.UpdateSettings(s);
        phrase.Text = Store.Settings.StopPhrase;
        sites.Text = string.Join(Environment.NewLine, Store.Settings.Sites);
        allow.Text = string.Join(Environment.NewLine, Store.Settings.AllowSites);
        saved.Text = L("저장됨 ✓", "Saved ✓");
        rejected.Text = bad.Count > 0 ? L("사이트로 인식하지 못해 뺐어요: ", "Removed entries that aren't valid sites: ") + string.Join(", ", bad) : "";
    }

    void ChooseFolder()
    {
        using var d = new FolderBrowserDialog { Description = L("기록을 저장할 폴더", "Folder for your data"), UseDescriptionForTitle = true };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        if (Store.ChangeDataDir(d.SelectedPath) is string err)
            MessageBox.Show(this, err, L("저장 위치를 바꾸지 않았어요", "Storage location unchanged"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        RefreshView(true);
    }

    public override void RefreshView(bool dataChanged) => dir.Text = Store.DataDir;
}
