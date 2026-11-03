import Combine
import EventKit
import Foundation

/// 할 일 · 나중에 할 것을 미리 알림 목록 두 개와 맞춘다 (아이클라우드가 아이폰 · 아이패드로 옮긴다).
/// 설정에서 켠 맥에서만 동작한다. 무엇을 할지는 SyncPlan 이 정하고, 여기서는 읽고 쓰기만 한다.
/// 켜기 · 연결 정보는 이 맥에만 저장한다 (기록 파일 형식은 그대로).
final class RemindersSync: ObservableObject {
    static let shared = RemindersSync()

    @Published private(set) var enabled = UserDefaults.standard.bool(forKey: Keys.enabled)
    @Published private(set) var status = ""

    private enum Keys { static let enabled = "syncReminders", state = "syncRemindersState" }
    private enum Slot: String { case todos, later }

    private struct State: Codable {
        var lists: [String: String] = [:] // 목록 종류 → 미리 알림 목록 id
        var links: [String: UUID] = [:] // 미리 알림 id → 앱 항목 id
        var last: [String: [UUID: SyncItem]] = [:] // 목록 종류 → 지난번에 맞춘 상태
    }

    private let ek = EKEventStore()
    private weak var store: Store?
    private var bag: Set<AnyCancellable> = []
    private var timer: Timer?
    private var running = false, again = false

    private static var deniedText: String {
        L("미리 알림 접근이 꺼져 있어요. 시스템 설정 → 개인정보 보호 및 보안 → 미리 알림에서 aADHD를 켜 주세요.",
          "Reminders access is off. Turn on aADHD in System Settings → Privacy & Security → Reminders.")
    }

    func start(_ store: Store) {
        self.store = store
        if enabled { begin() }
    }

    func setEnabled(_ on: Bool) {
        guard on != enabled else { return }
        guard on else {
            enabled = false
            UserDefaults.standard.set(false, forKey: Keys.enabled)
            bag.removeAll()
            timer?.invalidate()
            timer = nil
            status = ""
            return
        }
        ek.requestFullAccessToReminders { granted, _ in
            DispatchQueue.main.async {
                guard granted else { self.status = Self.deniedText; return }
                self.enabled = true
                UserDefaults.standard.set(true, forKey: Keys.enabled)
                self.begin()
            }
        }
    }

    /// 앱에서 바뀌면 · 미리 알림이 바뀌면 · 1분마다 맞춘다
    private func begin() {
        guard let store, bag.isEmpty else { return }
        store.$data
            .map { d in Self.localItems(d, today: dayKey()) }
            .removeDuplicates { $0.todos == $1.todos && $0.later == $1.later }
            .debounce(for: .seconds(1.5), scheduler: RunLoop.main)
            .sink { [weak self] _ in self?.sync() }
            .store(in: &bag)
        NotificationCenter.default.publisher(for: .EKEventStoreChanged, object: ek)
            .debounce(for: .seconds(1), scheduler: RunLoop.main)
            .sink { [weak self] _ in self?.sync() }
            .store(in: &bag)
        timer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            self?.ek.refreshSourcesIfNecessary()
            self?.sync()
        }
        sync()
    }

    func sync() {
        guard enabled, store != nil else { return }
        if running { again = true; return }
        guard EKEventStore.authorizationStatus(for: .reminder) == .fullAccess else { status = Self.deniedText; return }
        var state = loadState()
        guard let todoList = list(.todos, &state), let laterList = list(.later, &state) else {
            status = L("미리 알림 목록을 만들지 못했어요. 미리 알림 앱에 아이클라우드 계정이 있는지 확인해 주세요.",
                       "Couldn't create the Reminders lists. Check that the Reminders app has an iCloud account.")
            return
        }
        saveState(state)
        running = true
        ek.fetchReminders(matching: ek.predicateForReminders(in: [todoList, laterList])) { found in
            DispatchQueue.main.async {
                self.running = false
                if let found { self.apply(found, todoList: todoList, laterList: laterList) } else { self.status = L("미리 알림을 읽지 못했어요", "Couldn't read Reminders") }
                if self.again { self.again = false; self.sync() }
            }
        }
    }

    private func apply(_ found: [EKReminder], todoList: EKCalendar, laterList: EKCalendar) {
        guard let store else { return }
        var state = loadState()
        let today = dayKey()
        let local = Self.localItems(store.data, today: today)
        let byKey = Dictionary(found.map { ($0.calendarItemIdentifier, $0) }, uniquingKeysWith: { a, _ in a })
        var links = state.links
        var created = Set<String>()
        var last: [String: [UUID: SyncItem]] = [:]
        var failed = false

        for (slot, cal, items) in [(Slot.todos, todoList, local.todos), (.later, laterList, local.later)] {
            let dated = slot == .todos
            let remote = found.filter { $0.calendar?.calendarIdentifier == cal.calendarIdentifier }.map { r in
                let title = (r.title ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
                let p = SyncPlan.parse(title)
                return RemoteSyncItem(key: r.calendarItemIdentifier, id: links[r.calendarItemIdentifier],
                                      item: SyncItem(text: dated ? SyncPlan.title(p.text, kind: p.kind) : title,
                                                     done: r.isCompleted, day: dated ? Self.day(r.dueDateComponents) : nil))
            }
            let plan = SyncPlan.reconcile(local: items, remote: remote, last: state.last[slot.rawValue] ?? [:], today: dated ? today : nil)
            var settled = plan.settled

            func save(_ r: EKReminder, _ item: SyncItem) -> Bool {
                r.title = item.text
                r.isCompleted = item.done
                if dated, let day = item.day {
                    r.dueDateComponents = Self.components(day)
                    r.priority = SyncPlan.parse(item.text).kind == .must ? 1 : 0 // 반드시 할 일 → 우선순위 높음
                }
                do { try ek.save(r, commit: false); return true } catch { failed = true; return false }
            }

            for action in plan.actions {
                switch action {
                case let .link(key, id):
                    links[key] = id
                case let .createRemote(id, item):
                    let r = EKReminder(eventStore: ek)
                    r.calendar = cal
                    if save(r, item) { links[r.calendarItemIdentifier] = id; created.insert(r.calendarItemIdentifier); settled[id] = item }
                case let .updateRemote(key, item):
                    if let r = byKey[key] { _ = save(r, item) }
                case let .deleteRemote(key):
                    guard let r = byKey[key] else { break }
                    do { try ek.remove(r, commit: false); links[key] = nil } catch { failed = true }
                case let .updateLocal(id, item):
                    if dated {
                        let p = SyncPlan.parse(item.text)
                        store.applySyncedTodo(id, text: p.text, kind: p.kind, done: item.done, day: item.day ?? today)
                    } else {
                        store.applySyncedLater(id, text: item.text, done: item.done)
                    }
                case let .deleteLocal(id):
                    if dated { store.deleteTodo(id) } else { store.deleteLater(id) }
                case let .createLocal(key, item):
                    if dated {
                        let p = SyncPlan.parse(item.text)
                        let day = item.day ?? today
                        guard let id = store.addTodo(p.text, kind: p.kind, on: day, done: item.done) else { break }
                        let added = SyncItem(text: SyncPlan.title(p.text, kind: p.kind), done: item.done, day: day)
                        links[key] = id
                        settled[id] = added
                        if let r = byKey[key] { _ = save(r, added) } // 날짜 · 우선순위를 앱과 맞춘다
                    } else {
                        guard let id = store.addSyncedLater(item.text, done: item.done) else { break }
                        links[key] = id
                        settled[id] = item
                    }
                }
            }
            last[slot.rawValue] = settled
        }

        do {
            guard !failed else { throw CocoaError(.fileWriteUnknown) }
            try ek.commit()
        } catch {
            ek.reset() // 다음에 다시 맞춘다 (지난 상태를 그대로 두면 같은 판단을 다시 한다)
            status = L("미리 알림에 쓰지 못했어요. 잠시 뒤 다시 시도해요.", "Couldn't write to Reminders. Will try again shortly.")
            return
        }
        state.links = links.filter { byKey[$0.key] != nil || created.contains($0.key) } // 없어진 미리 알림의 연결은 버린다
        state.last = last
        saveState(state)
        status = L("✓ 맞췄어요 · ", "✓ Synced · ") + Date().formatted(date: .omitted, time: .shortened)
    }

    /// 오늘 이후의 할 일 · 나중에 할 것
    static func localItems(_ d: DataFile, today: String) -> (todos: [LocalSyncItem], later: [LocalSyncItem]) {
        let todos = d.days.filter { $0.key >= today }.sorted { $0.key < $1.key }.flatMap { day in
            day.value.todos.map { LocalSyncItem(id: $0.id, item: SyncItem(text: SyncPlan.title($0.text, kind: $0.kind), done: $0.done, day: day.key)) }
        }
        let later = d.later.map { LocalSyncItem(id: $0.id, item: SyncItem(text: $0.text, done: $0.done, day: nil)) }
        return (todos, later)
    }

    // MARK: - 미리 알림 목록 · 날짜

    private func list(_ slot: Slot, _ state: inout State) -> EKCalendar? {
        let title = slot == .todos ? L("aADHD 할 일", "aADHD To-dos") : L("aADHD 나중에 할 것", "aADHD Later")
        if let id = state.lists[slot.rawValue], let c = ek.calendar(withIdentifier: id) {
            if c.title != title { c.title = title; try? ek.saveCalendar(c, commit: true) } // 앱 이름이 바뀌면 목록 이름도 따라간다
            return c
        }
        var cal = ek.calendars(for: .reminder).first { $0.title == title }
        if cal == nil {
            guard let source = ek.defaultCalendarForNewReminders()?.source else { return nil }
            let c = EKCalendar(for: .reminder, eventStore: ek)
            c.title = title
            c.source = source
            guard (try? ek.saveCalendar(c, commit: true)) != nil else { return nil }
            cal = c
        }
        state.lists[slot.rawValue] = cal!.calendarIdentifier
        state.last[slot.rawValue] = nil // 새 목록 → 처음부터 맞춘다
        return cal
    }

    static func day(_ c: DateComponents?) -> String? {
        guard let c, let y = c.year, let m = c.month, let d = c.day else { return nil }
        return String(format: "%04d-%02d-%02d", y, m, d)
    }

    static func components(_ day: String) -> DateComponents? {
        let p = day.split(separator: "-").compactMap { Int($0) }
        guard p.count == 3 else { return nil }
        return DateComponents(year: p[0], month: p[1], day: p[2])
    }

    private func loadState() -> State {
        UserDefaults.standard.data(forKey: Keys.state).flatMap { try? JSONDecoder().decode(State.self, from: $0) } ?? State()
    }

    private func saveState(_ s: State) {
        if let d = try? JSONEncoder().encode(s) { UserDefaults.standard.set(d, forKey: Keys.state) }
    }
}
