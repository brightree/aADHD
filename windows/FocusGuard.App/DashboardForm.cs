using System.Drawing.Drawing2D;
using FocusGuard.Core;

namespace FocusGuard.App;

/// 앱 창 (기록): 위쪽 버튼 · 주간 막대 차트 · 고른 날 상세 · 나중에 할 것 · 버전과 업데이트
sealed class DashboardForm : AppWindow
{
    const int W = 640;
    int weekOffset;
    string selected;
    bool editing;
    readonly Label weekTitle = Ui.Text("", Ui.Title), weekSum = Ui.Text("", null, Ui.Muted), footer = Ui.Text("", Ui.Small, Ui.Muted);
    readonly WeekChart chart = new() { Width = W, Height = 200 };
    readonly FlowLayoutPanel detail = Ui.Column(W), laterList = Ui.Column(W - 24), footerRow = Ui.Row();
    readonly Button nextWeek;
    readonly TextBox laterAdd;

    public DashboardForm(Controller app) : base(app, "aADHD")
    {
        selected = Store.TodayKey;
        AutoScroll = true;
        Size = new Size(W + 70, 820);
        MinimumSize = new Size(W + 70, 500);

        var root = Ui.Column(W);
        var top = Ui.Row();
        top.Controls.Add(Ui.Button(L("📋 할 일", "📋 To-dos"), () => App.ShowPlan(Store.TodayKey)));
        top.Controls.Add(Ui.Button(L("📊 통계", "📊 Stats"), App.ShowStats));
        top.Controls.Add(Ui.Button(L("⚙ 설정", "⚙ Settings"), App.ShowSettings));
        top.Controls.Add(Ui.Button(L("종료", "Quit"), App.Quit));
        root.Controls.Add(top);

        var nav = Ui.Row();
        nav.Controls.Add(Ui.Button("◀", () => ChangeWeek(-1)));
        nav.Controls.Add(weekTitle);
        nextWeek = Ui.Button("▶", () => ChangeWeek(1));
        nav.Controls.Add(nextWeek);
        nav.Controls.Add(weekSum);
        root.Controls.Add(nav);

        chart.Selected += key => { selected = key; RefreshView(true); };
        root.Controls.Add(chart);
        root.Controls.Add(detail);

        var later = Ui.Card(L("💭 나중에 할 것", "💭 Later"), W);
        var addRow = Ui.Row();
        laterAdd = Ui.Input(L("새로 적기 (Enter)", "Add new (Enter)"), W - 200, text => { if (Store.AddLater(text)) laterAdd!.Text = ""; });
        addRow.Controls.Add(laterAdd);
        addRow.Controls.Add(Ui.Button(L("완료 항목 지우기", "Clear Completed"), Store.ClearDoneLater));
        later.Controls.Add(addRow);
        later.Controls.Add(laterList);
        root.Controls.Add(later);

        root.Controls.Add(new Label { Height = 1, Width = W, BackColor = Color.Gainsboro, Margin = new Padding(0, 8, 0, 8) });
        footerRow.Controls.Add(footer);
        root.Controls.Add(footerRow);
        Controls.Add(root);
    }

    DateTime[] WeekDates()
    {
        var monday = Fmt.Monday(Store.Now).AddDays(7 * weekOffset);
        return Enumerable.Range(0, 7).Select(i => monday.AddDays(i)).ToArray();
    }

    void ChangeWeek(int delta)
    {
        weekOffset += delta;
        // 이번 주로 돌아오면 오늘, 다른 주면 그 주 월요일
        selected = weekOffset == 0 ? Store.TodayKey : Fmt.DayKey(WeekDates()[0]);
        RefreshView(true);
    }

    public override void RefreshView(bool dataChanged)
    {
        var dates = WeekDates();
        var keys = dates.Select(Fmt.DayKey).ToArray();
        var secs = keys.Select(Store.StudySec).ToArray();
        string md(DateTime d) => $"{d.Month}/{d.Day}";
        weekTitle.Text = (weekOffset == 0 ? L("이번 주 ", "This week ") : "") + $"{md(dates[0])} – {md(dates[6])}";
        weekSum.Text = "   " + L($"이번 주 공부 {Fmt.Minutes(secs.Sum())} · 🍅 {keys.Sum(k => Store.DayOf(k).Sessions)}개", $"This week: {Fmt.Minutes(secs.Sum())} · 🍅 {keys.Sum(k => Store.DayOf(k).Sessions)}");
        chart.SetData(dates, secs, selected, Store.TodayKey);

        var u = App.Updater;
        footer.Text = $"aADHD v{Updater.Current} · Made by Anonymous, KAIST IS"
                    + (u.Checking ? "   " + L("확인 중…", "Checking…") : u.UpToDate ? "   " + L("최신 버전이에요 ✓", "You're up to date ✓") : u.Error != null ? "   " + u.Error : "");
        if (!dataChanged || editing) return;
        BuildFooterButton();
        BuildDetail();
        BuildLater();
    }

    void BuildFooterButton()
    {
        while (footerRow.Controls.Count > 1) footerRow.Controls.RemoveAt(1);
        footerRow.Controls.Add(App.Updater.Latest is string v
            ? Ui.Button(L($"업데이트 {v}", $"Update to {v}"), App.ShowUpdate, primary: true)
            : Ui.Button(L("업데이트 확인", "Check for Updates"), () => App.Updater.Check()));
    }

    void BuildDetail()
    {
        detail.SuspendLayout();
        detail.Controls.Clear();
        var day = Store.DayOf(selected);
        var today = Store.TodayKey;
        var future = string.CompareOrdinal(selected, today) > 0;
        var rel = Fmt.Relative(selected, today);
        detail.Controls.Add(Ui.Text(Fmt.DayTitle(selected) + (rel != null ? $" ({rel})" : ""), Ui.Title));
        if (!future)
        {
            var kpis = Ui.Row();
            kpis.Controls.Add(Ui.Kpi(Fmt.Minutes(Store.StudySec(selected)), L("공부", "Study"), 200));
            kpis.Controls.Add(Ui.Kpi(day.Sessions.ToString(), L("완료한 뽀모도로", "Pomodoros done"), 200));
            kpis.Controls.Add(Ui.Kpi(Fmt.Minutes(day.DistractTotal), L("딴짓", "Distractions"), 200));
            detail.Controls.Add(kpis);
        }

        var todos = Ui.Card(L("할 일", "To-dos"), W);
        var key = selected;
        todos.Controls.Add(Ui.Button(day.Todos.Count == 0 ? L("✏️ 할 일 적기", "✏️ Add To-dos") : L("✏️ 편집", "✏️ Edit"), () => App.ShowPlan(key)));
        if (day.Todos.Count == 0) todos.Controls.Add(Ui.Text(future ? L("아직 적은 할 일이 없어요.", "No to-dos yet.") : L("이 날은 적은 할 일이 없어요.", "No to-dos for this day."), null, Ui.Muted));
        foreach (var kind in new[] { TodoKind.Must, TodoKind.Nice, TodoKind.Other })
        {
            var items = day.Todos.Where(t => t.Kind == kind).ToList();
            if (items.Count == 0) continue;
            todos.Controls.Add(Ui.Text(Ui.KindTitle(kind) + $"  {items.Count(t => t.Done)}/{items.Count}", Ui.Bold, Ui.KindColor(kind)));
            foreach (var item in items) todos.Controls.Add(new TodoRow(Store, item, key, W - 24, () => editing = true, () => { editing = false; RefreshView(true); }));
        }
        detail.Controls.Add(todos);

        if (!future)
        {
            var log = Ui.Card(L("한 일", "Sessions"), W);
            if (day.Log.Count == 0) log.Controls.Add(Ui.Text(L("이 날은 뽀모도로 기록이 없어요.", "No Pomodoros this day."), null, Ui.Muted));
            foreach (var l in day.Log)
            {
                var task = l.Task.Length == 0 ? L("(할 일 없음)", "(no task)") : l.Task;
                var mins = l.Done ? L($"({l.Minutes ?? 0}분)", $"({l.Minutes ?? 0} min)") : L("(포기)", "(gave up)");
                log.Controls.Add(Ui.Text($"{(l.Done ? "🍅" : "✖")} {l.End.ToString("t", I18n.Culture)} — {task} {mins}", null, null, W - 24));
                if (l.Note.Length > 0) log.Controls.Add(Ui.Text("📝 " + l.Note, Ui.Small, Ui.Muted, W - 24));
            }
            detail.Controls.Add(log);

            var sites = Ui.Card(L("딴짓 사이트", "Distracting sites"), W);
            var entries = day.DistractSec.OrderByDescending(kv => kv.Value).ToList();
            if (entries.Count == 0) sites.Controls.Add(Ui.Text(L("딴짓 기록이 없어요 👏", "No distractions 👏"), null, Ui.Muted));
            else sites.Controls.Add(new BarList(entries.Select(kv => (kv.Key, kv.Value)).ToList(), Color.Gray, W - 24, showShare: false));
            sites.Controls.Add(Ui.Text(L($"연속 사용 경고 {day.Warnings}회 · 중도 포기 {day.Abandoned}회", $"{day.Warnings} distraction alerts · {day.Abandoned} sessions abandoned"), Ui.Small, Ui.Muted));
            detail.Controls.Add(sites);
        }
        detail.ResumeLayout();
    }

    void BuildLater()
    {
        laterList.SuspendLayout();
        laterList.Controls.Clear();
        var items = Store.Data.Later.OrderBy(x => x.Done).ThenByDescending(x => x.Created).ToList();
        if (items.Count == 0) laterList.Controls.Add(Ui.Text(L($"공부 중 떠오른 생각을 {HotKey.Label}로 적으면 여기 모여요.", $"Thoughts you jot down with {HotKey.Label} while studying show up here."), null, Ui.Muted, W - 24));
        foreach (var item in items)
        {
            var row = Ui.Row();
            var id = item.Id;
            var cb = new CheckBox { Text = item.Text, Checked = item.Done, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(W - 120, 0), ForeColor = item.Done ? Ui.Muted : SystemColors.ControlText };
            cb.Click += (_, _) => Store.ToggleLater(id);
            row.Controls.Add(cb);
            var del = new LinkLabel { Text = L("삭제", "Delete"), AutoSize = true, Margin = new Padding(8, 4, 0, 0) };
            del.Click += (_, _) => Store.DeleteLater(id);
            row.Controls.Add(del);
            laterList.Controls.Add(row);
            var meta = item.Created.ToString("g", I18n.Culture) + (item.During.Length > 0 ? L($" · \"{item.During}\" 하다가", $" · during \"{item.During}\"") : "");
            laterList.Controls.Add(Ui.Text("     " + meta, Ui.Small, Ui.Muted));
        }
        laterList.ResumeLayout();
    }
}

/// 월~일 공부 시간 막대. 세로축은 1시간 눈금, 맨 위는 가장 긴 날을 정시로 올림. 누르면 그 날을 고른다.
sealed class WeekChart : Control
{
    DateTime[] dates = Array.Empty<DateTime>();
    double[] secs = Array.Empty<double>();
    string selected = "", today = "";
    public event Action<string>? Selected;

    public WeekChart() { DoubleBuffered = true; Cursor = Cursors.Hand; Margin = new Padding(0, 6, 0, 6); }

    public void SetData(DateTime[] d, double[] s, string sel, string todayKey)
    {
        dates = d; secs = s; selected = sel; today = todayKey;
        Invalidate();
    }

    const int AxisW = 32, PadTop = 22, PadBottom = 26;

    RectangleF Column(int i)
    {
        var cw = (Width - AxisW) / 7f;
        return new RectangleF(AxisW + i * cw, 0, cw, Height);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        for (var i = 0; i < dates.Length; i++)
            if (Column(i).Contains(e.Location)) Selected?.Invoke(Fmt.DayKey(dates[i]));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (dates.Length == 0) return;
        var topHours = Math.Max(1, (int)Math.Ceiling(secs.Max() / 3600 - 0.0001));
        var h = Height - PadTop - PadBottom;
        float y(double sec) => PadTop + h - (float)(sec / (topHours * 3600) * h);
        using var grid = new Pen(Color.FromArgb(225, 225, 225));
        using var small = new Font("Segoe UI", 8.5f);
        for (var hh = 0; hh <= topHours; hh++)
        {
            g.DrawLine(grid, AxisW, y(hh * 3600), Width, y(hh * 3600));
            var label = hh == 0 ? "0" : $"{hh}h";
            var sz = g.MeasureString(label, small);
            g.DrawString(label, small, Brushes.Gray, AxisW - sz.Width - 4, y(hh * 3600) - sz.Height / 2);
        }
        for (var i = 0; i < dates.Length; i++)
        {
            var c = Column(i);
            var key = Fmt.DayKey(dates[i]);
            var isSel = key == selected;
            var future = string.CompareOrdinal(key, today) > 0;
            if (isSel) { using var sb = new SolidBrush(Color.FromArgb(40, Ui.Blue)); g.FillRectangle(sb, c.X + 3, 2, c.Width - 6, Height - 4); }
            if (secs[i] >= 60)
            {
                var bw = c.Width * 0.55f;
                using var b = new SolidBrush(Color.FromArgb(isSel ? 230 : 150, Ui.Red));
                g.FillRectangle(b, c.X + (c.Width - bw) / 2, y(secs[i]), bw, PadTop + h - y(secs[i]));
                var m = (int)Math.Round(secs[i] / 60);
                var v = m >= 60 ? $"{m / 60}h {m % 60}m" : $"{m}m";
                var vs = g.MeasureString(v, small);
                g.DrawString(v, small, Brushes.Gray, c.X + (c.Width - vs.Width) / 2, y(secs[i]) - vs.Height - 1);
            }
            var label = $"{Fmt.Weekday(dates[i])} {dates[i].Day}";
            using var lf = new Font("Segoe UI", 9f, isSel ? FontStyle.Bold : FontStyle.Regular);
            var ls = g.MeasureString(label, lf);
            g.DrawString(label, lf, future ? Brushes.LightGray : isSel ? Brushes.Black : Brushes.Gray, c.X + (c.Width - ls.Width) / 2, Height - PadBottom + 4);
        }
    }
}

/// 이름 · 막대 · (비율) · 시간 목록
sealed class BarList : Control
{
    readonly List<(string Name, double Sec)> items;
    readonly Color color;
    readonly bool showShare;
    const int RowH = 26;

    public BarList(List<(string, double)> items, Color color, int width, bool showShare = true)
    {
        this.items = items; this.color = color; this.showShare = showShare;
        DoubleBuffered = true;
        Size = new Size(width, Math.Max(1, items.Count) * RowH);
        Margin = new Padding(0, 4, 0, 4);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var total = items.Sum(i => i.Sec);
        var max = items.Count > 0 ? items.Max(i => i.Sec) : 1;
        using var name = new Font("Segoe UI", 9.5f);
        using var bold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        const int nameW = 170, rightW = 150;
        var barW = Width - nameW - rightW - 12;
        for (var i = 0; i < items.Count; i++)
        {
            var (n, s) = items[i];
            var top = i * RowH;
            g.DrawString(Trim(g, n, name, nameW - 8), name, Brushes.Black, 0, top + 4);
            using var back = new SolidBrush(Color.FromArgb(230, 230, 230));
            g.FillRectangle(back, nameW, top + 8, barW, 10);
            using var fill = new SolidBrush(Color.FromArgb(i == 0 ? 230 : 160, color));
            g.FillRectangle(fill, nameW, top + 8, Math.Max(3, (float)(barW * (showShare ? s / total : s / max))), 10);
            var right = (showShare ? $"{Math.Round(s / total * 100)}%   " : "") + (s < 60 ? I18n.L("1분 미만", "<1 min") : Fmt.Minutes(s));
            var rs = g.MeasureString(right, bold);
            g.DrawString(right, bold, Brushes.DimGray, Width - rs.Width, top + 4);
        }
    }

    static string Trim(Graphics g, string s, Font f, int w)
    {
        if (g.MeasureString(s, f).Width <= w) return s;
        while (s.Length > 1 && g.MeasureString(s + "…", f).Width > w) s = s[..^1];
        return s + "…";
    }
}
