using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using FocusGuard.Core;

namespace FocusGuard.App;

/// 새 버전 확인 (하루 한 번, 깃허브 최신 릴리스) · 버튼 하나로 업데이트 (FocusGuard-Windows.zip 을 받아 실행 파일을 바꿔 넣고 다시 켠다).
/// 보내는 정보는 없다.
sealed class Updater
{
    readonly Controller app;
    readonly IPrefs prefs;
    static readonly HttpClient http = MakeClient();

    public string? Latest { get; private set; }
    public string Notes { get; private set; } = "";
    public string? Error { get; private set; }
    public bool Checking { get; private set; }
    public bool Installing { get; private set; }
    public bool UpToDate { get; private set; }
    string? assetUrl;

    /// 지금 버전 ("1.7.0"). 빌드 정보 뒤의 +커밋은 뗀다.
    public static string Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";
            var plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }

    public Updater(Controller app, IPrefs prefs) { this.app = app; this.prefs = prefs; }

    static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"FocusGuard/{Current}"); // 깃허브 API 는 User-Agent 가 필요하다
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public void Start()
    {
        // 켤 때 한 번(하루가 지났으면), 그 뒤로는 한 시간마다 "하루 지났나"만 본다
        var t = new System.Windows.Forms.Timer { Interval = 10_000 };
        t.Tick += (_, _) => { t.Interval = 3_600_000; CheckIfDue(); };
        t.Start();
    }

    void CheckIfDue()
    {
        var last = DateTime.TryParse(prefs.Get("lastUpdateCheck"), out var d) ? d : DateTime.MinValue;
        if ((DateTime.Now - last).TotalHours >= 24) Check();
    }

    /// 지금 바로 확인 (앱 창의 "업데이트 확인")
    public async void Check()
    {
        if (Checking || Installing) return;
        Checking = true; Error = null; UpToDate = false;
        app.RequestRefresh();
        ReleaseInfo? r = null;
        try { r = ReleaseInfo.Parse(await http.GetStringAsync(ReleaseInfo.LatestApi)); } catch { }
        Checking = false;
        prefs.Set("lastUpdateCheck", DateTime.Now.ToString("o"));
        if (r == null) { Error = I18n.L("새 버전을 확인하지 못했어요. 인터넷 연결을 확인해 주세요.", "Couldn't check for updates. Check your internet connection."); app.RequestRefresh(); return; }
        // 윈도우용 파일이 아직 안 올라왔으면 다음에 다시 본다
        if (!ReleaseInfo.IsNewer(r.Version, Current) || r.AssetUrl == null) { UpToDate = true; app.RequestRefresh(); return; }
        Latest = r.Version; Notes = r.Notes; assetUrl = r.AssetUrl;
        if (prefs.Get("notifiedVersion") != r.Version)
        {
            prefs.Set("notifiedVersion", r.Version);
            app.Tray.ShowBalloonTip(8000, I18n.L($"🍅 새 버전 {r.Version}이 나왔어요", $"🍅 aADHD {r.Version} is available"),
                I18n.L("트레이의 🍅 → 업데이트를 누르면 바로 설치돼요.", "Click the 🍅 tray icon, then Update to install it."), ToolTipIcon.None);
        }
        app.RequestRefresh();
    }

    /// 받아서 풀고, 버전을 확인한 뒤 실행 파일을 바꿔 넣고 다시 켠다. 실패하면 지금 버전 그대로.
    public async void Update()
    {
        if (Installing || assetUrl == null || Latest == null) return;
        Installing = true; Error = null;
        app.RequestRefresh();
        var work = Path.Combine(Path.GetTempPath(), "FocusGuardUpdate-" + Guid.NewGuid().ToString("N"));
        var exe = Environment.ProcessPath!;
        var old = exe + ".old";
        try
        {
            Directory.CreateDirectory(work);
            var zip = Path.Combine(work, "update.zip");
            await using (var f = File.Create(zip)) await (await http.GetStreamAsync(assetUrl)).CopyToAsync(f);
            ZipFile.ExtractToDirectory(zip, Path.Combine(work, "x"));
            var fresh = Directory.GetFiles(Path.Combine(work, "x"), "FocusGuard.exe", SearchOption.AllDirectories).FirstOrDefault()
                        ?? throw new IOException(I18n.L("받은 파일이 올바르지 않아요.", "The downloaded file isn't valid."));
            var v = FileVersionInfo.GetVersionInfo(fresh).ProductVersion ?? "";
            if (!v.StartsWith(Latest)) throw new IOException(I18n.L("받은 버전이 예상과 달라요.", "The downloaded version doesn't match."));
            app.Store.SaveNow();
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old); // 실행 중인 파일은 지울 수 없지만 이름은 바꿀 수 있다
            try { File.Copy(fresh, exe); }
            catch { File.Move(old, exe); throw; }
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            app.ExitNow();
        }
        catch (Exception e)
        {
            Installing = false;
            Error = I18n.L("업데이트하지 못했어요. 지금 버전은 그대로예요. ", "Update failed. You're still on the current version. ") + e.Message;
            app.RequestRefresh();
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    /// 업데이트 뒤 남은 옛 실행 파일 정리
    public static void CleanupOld()
    {
        try { var old = Environment.ProcessPath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
    }
}

/// 앱 삭제: 로그인 시 자동 실행 끄기, (선택) 기록을 휴지통으로, 실행 파일 지우고 종료
static class Uninstaller
{
    public static void Run(Controller app)
    {
        var withData = new TaskDialogVerificationCheckBox(I18n.L($"기록도 함께 휴지통으로 옮기기 ({app.Store.DataDir})", $"Also move my data to the Recycle Bin ({app.Store.DataDir})"));
        var delete = new TaskDialogButton(I18n.L("삭제", "Delete"));
        var page = new TaskDialogPage
        {
            Caption = "aADHD",
            Heading = I18n.L("aADHD를 삭제할까요?", "Delete aADHD?"),
            Text = I18n.L("앱을 지우고 종료해요. 로그인 시 자동 실행도 꺼져요.", "This deletes the app and quits it. Launch at login will also be turned off."),
            Verification = withData,
            Buttons = { delete, TaskDialogButton.Cancel },
        };
        if (TaskDialog.ShowDialog(page) != delete) return;

        try { Win32.LaunchAtLogin = false; } catch { }
        if (withData.Checked)
        {
            app.Store.StopSaving(); // 종료하면서 기록을 다시 만들지 않게
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(app.Store.DataDir,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            catch { }
            try { Directory.Delete(Path.GetDirectoryName(JsonPrefs.DefaultPath)!, true); } catch { }
        }
        // 실행 중인 파일은 바로 못 지우므로, 끈 뒤 지우도록 맡긴다
        var exe = Environment.ProcessPath!;
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{exe}\" \"{exe}.old\"")
        { CreateNoWindow = true, UseShellExecute = false });
        if (withData.Checked) { app.Shutdown(); Application.Exit(); } else app.ExitNow();
    }
}
