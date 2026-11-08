import AppKit
import ServiceManagement

// 새 버전 확인과 버튼 하나로 업데이트.
// - 하루 한 번 깃허브의 최신 릴리스(버전 번호 · 바뀐 내용)만 읽어 내 버전과 비교한다. 보내는 정보는 없다.
// - 업데이트:
//   · 다운로드해서 설치한 앱 → 릴리스의 aADHD.zip 을 받아 지금 앱 자리에 바꿔 넣고 다시 켠다.
//   · git clone 으로 설치한 앱 → 그 폴더에서 git pull → build.sh 후 다시 켠다.

final class Updater: ObservableObject {
    static let shared = Updater()

    enum State: Equatable {
        case idle
        case checking
        case upToDate
        case available(String) // 새 버전 번호
        case updating
        case failed(String)
    }

    @Published private(set) var state: State = .idle
    @Published private(set) var notes = "" // CHANGELOG 중 새 버전 부분

    private static let latestAPI = "https://api.github.com/repos/brightree/aADHD/releases/latest"
    private static let latestZip = "https://github.com/brightree/aADHD/releases/latest/download/aADHD.zip"
    private static let lastCheckKey = "lastUpdateCheck"
    private static let notifiedKey = "notifiedVersion"
    private static let loginAfterMoveKey = "registerLoginAfterRename"

    let current = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"

    /// 이 앱이 들어 있는 저장소 (…/focus-guard 등 받은 폴더). 앱이 app/aADHD.app 에 있을 때만.
    var repoDir: URL? {
        let repo = Bundle.main.bundleURL.deletingLastPathComponent().deletingLastPathComponent()
        return FileManager.default.fileExists(atPath: repo.appendingPathComponent(".git").path) ? repo : nil
    }

    var latest: String? {
        if case .available(let v) = state { return v }
        return nil
    }

    func start() {
        // 켤 때 한 번(하루가 지났으면), 그 뒤로는 한 시간마다 "하루 지났나"만 본다
        DispatchQueue.main.asyncAfter(deadline: .now() + 10) { self.checkIfDue() }
        let t = Timer.scheduledTimer(withTimeInterval: 3600, repeats: true) { [weak self] _ in self?.checkIfDue() }
        t.tolerance = 600
    }

    private func checkIfDue() {
        let last = UserDefaults.standard.object(forKey: Self.lastCheckKey) as? Date ?? .distantPast
        if Date().timeIntervalSince(last) >= 24 * 3600 { check() }
    }

    /// 지금 바로 확인 (설정의 "업데이트 확인" 버튼)
    func check() {
        guard state != .checking, state != .updating else { return }
        state = .checking
        var req = URLRequest(url: URL(string: Self.latestAPI)!, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 15)
        req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        URLSession.shared.dataTask(with: req) { data, resp, _ in
            let json = (resp as? HTTPURLResponse)?.statusCode == 200
                ? data.flatMap { try? JSONSerialization.jsonObject(with: $0) as? [String: Any] } : nil
            DispatchQueue.main.async { self.handle(release: json) }
        }.resume()
    }

    private func handle(release json: [String: Any]?) {
        UserDefaults.standard.set(Date(), forKey: Self.lastCheckKey)
        guard let tag = json?["tag_name"] as? String, !tag.isEmpty else {
            state = .failed(L("새 버전을 확인하지 못했어요. 인터넷 연결을 확인해 주세요.", "Couldn't check for updates. Check your internet connection."))
            return
        }
        let remote = tag.hasPrefix("v") ? String(tag.dropFirst()) : tag
        guard Self.isNewer(remote, than: current) else {
            state = .upToDate
            return
        }
        notes = (json?["body"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        state = .available(remote)
        if UserDefaults.standard.string(forKey: Self.notifiedKey) != remote {
            UserDefaults.standard.set(remote, forKey: Self.notifiedKey)
            Store.shared.notify(L("🍅 새 버전 \(remote)이 나왔어요", "🍅 aADHD \(remote) is available"), L("메뉴 막대 🍅 → 업데이트를 누르면 바로 설치돼요.", "Click 🍅 in the menu bar, then Update to install it."))
        }
    }

    /// 최신 코드를 받아 빌드하고, 성공하면 새 버전으로 다시 켠다. 실패하면 지금 버전 그대로 둔다.
    func update() {
        guard let repo = repoDir else { return downloadUpdate() }
        state = .updating
        Store.shared.saveNow()
        DispatchQueue.global(qos: .userInitiated).async {
            let (ok, log) = Self.shell("git pull -q && cd app && ./build.sh", in: repo)
            DispatchQueue.main.async {
                guard ok else {
                    self.state = .failed(L("업데이트하지 못했어요. 지금 버전은 그대로예요.\n", "Update failed. You're still on the current version.\n") + log.suffix(400))
                    return
                }
                self.relaunch()
            }
        }
    }

    /// 릴리스의 aADHD.zip 을 받아 풀고, 버전을 확인한 뒤 지금 앱과 바꿔 넣는다.
    private func downloadUpdate() {
        state = .updating
        Store.shared.saveNow()
        let expected = latest
        URLSession.shared.downloadTask(with: URL(string: Self.latestZip)!) { file, resp, error in
            // 받은 임시 파일은 이 블록이 끝나면 지워지므로 여기서 바로 푼다
            let result = Self.install(zip: file, ok: (resp as? HTTPURLResponse)?.statusCode == 200, expected: expected)
            DispatchQueue.main.async {
                if let msg = result {
                    self.state = .failed(L("업데이트하지 못했어요. 지금 버전은 그대로예요.\n", "Update failed. You're still on the current version.\n") + msg)
                } else {
                    self.relaunch()
                }
            }
        }.resume()
    }

    /// 성공하면 nil, 실패하면 이유
    static func install(zip: URL?, ok: Bool, expected: String?, target: URL = Bundle.main.bundleURL) -> String? {
        guard let zip, ok else { return L("다운로드에 실패했어요.", "The download failed.") }
        let fm = FileManager.default
        let work = fm.temporaryDirectory.appendingPathComponent("aADHDUpdate-\(UUID().uuidString)")
        defer { try? fm.removeItem(at: work) }
        let (unzipped, log) = shell("/usr/bin/ditto -x -k \"\(zip.path)\" \"\(work.path)\"", in: fm.temporaryDirectory)
        let newApp = work.appendingPathComponent("aADHD.app")
        guard unzipped, let info = NSDictionary(contentsOf: newApp.appendingPathComponent("Contents/Info.plist")),
              info["CFBundleIdentifier"] as? String == "local.focusguard.app" else {
            return L("받은 파일이 올바르지 않아요.", "The downloaded file isn't valid.") + " " + log.suffix(200)
        }
        if let expected, info["CFBundleShortVersionString"] as? String != expected {
            return L("받은 버전이 예상과 달라요.", "The downloaded version doesn't match.")
        }
        do {
            _ = try fm.replaceItemAt(target, withItemAt: newApp)
        } catch {
            return L("앱을 바꿔 넣지 못했어요: ", "Couldn't replace the app: ") + error.localizedDescription
        }
        return nil
    }

    /// 이 앱이 꺼진 뒤 새 앱을 켜도록 맡겨 두고 종료한다 (포트가 겹치지 않게)
    private func relaunch() { Self.relaunch(Bundle.main.bundleURL) }

    private static func relaunch(_ app: URL) {
        _ = shell("(sleep 1; open \"\(app.path)\") >/dev/null 2>&1 &", in: FileManager.default.temporaryDirectory, wait: false)
        NSApp.terminate(nil)
    }

    /// 예전 이름(FocusGuard.app)으로 켜졌으면 같은 폴더의 aADHD.app 으로 옮겨 다시 켠다. 다시 켜면 true.
    /// 옮기지 못하면 (폴더에 쓸 수 없는 등) 지금 자리 그대로 쓴다.
    static func moveToNewName() -> Bool {
        let old = Bundle.main.bundleURL, fm = FileManager.default
        guard old.lastPathComponent == "FocusGuard.app" else {
            // 소스 빌드 폴더에 남은 예전 이름의 앱을 치운다 (build.sh 가 이전 버전용으로 넣어 둔 것)
            let legacy = old.deletingLastPathComponent().appendingPathComponent("FocusGuard.app")
            if Bundle(url: legacy)?.bundleIdentifier == Bundle.main.bundleIdentifier {
                let login = SMAppService.mainApp.status == .enabled
                try? fm.removeItem(at: legacy)
                if login { try? SMAppService.mainApp.unregister(); try? SMAppService.mainApp.register() }
            }
            return false
        }
        let new = old.deletingLastPathComponent().appendingPathComponent("aADHD.app")
        // 로그인 항목은 앱 위치에 묶여 있어 옮기기 전에 풀고, 새 자리에서 켜질 때 다시 건다
        let login = SMAppService.mainApp.status == .enabled
        if login { try? SMAppService.mainApp.unregister() }
        do {
            let newVersion = (NSDictionary(contentsOf: new.appendingPathComponent("Contents/Info.plist"))?["CFBundleShortVersionString"] as? String) ?? "0"
            let current = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"
            if fm.fileExists(atPath: new.path) && !isNewer(current, than: newVersion) {
                try fm.removeItem(at: old) // 새 이름으로 빌드한 앱이 이미 있다 (소스로 설치)
            } else {
                if fm.fileExists(atPath: new.path) { try fm.removeItem(at: new) }
                try fm.moveItem(at: old, to: new)
            }
        } catch {
            if login { try? SMAppService.mainApp.register() }
            return false
        }
        if login { UserDefaults.standard.set(true, forKey: loginAfterMoveKey) }
        relaunch(new)
        return true
    }

    /// 옮기기 전에 로그인 시 자동 실행이 켜져 있었으면 새 자리에서 다시 켠다
    static func restoreLoginAfterMove() {
        guard UserDefaults.standard.bool(forKey: loginAfterMoveKey) else { return }
        UserDefaults.standard.removeObject(forKey: loginAfterMoveKey)
        try? SMAppService.mainApp.register()
    }

    // MARK: - 내부

    @discardableResult
    static func shell(_ command: String, in dir: URL, wait: Bool = true) -> (Bool, String) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/zsh")
        p.arguments = ["-c", command]
        p.currentDirectoryURL = dir
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = pipe
        do { try p.run() } catch { return (false, error.localizedDescription) }
        guard wait else { return (true, "") }
        let out = String(data: pipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
        p.waitUntilExit()
        return (p.terminationStatus == 0, out)
    }

    /// "1.10.0" > "1.9.2" 처럼 숫자 단위로 비교한다
    static func isNewer(_ a: String, than b: String) -> Bool {
        let x = a.split(separator: ".").map { Int($0) ?? 0 }, y = b.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(x.count, y.count) {
            let l = i < x.count ? x[i] : 0, r = i < y.count ? y[i] : 0
            if l != r { return l > r }
        }
        return false
    }
}
