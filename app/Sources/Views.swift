import ServiceManagement
import SwiftUI

// MARK: - 메뉴 막대 패널

struct PopoverView: View {
    @EnvironmentObject var store: Store
    var primary: () -> Void
    var togglePause: () -> Void
    var openApp: () -> Void
    var openUpdate: () -> Void
    var close: () -> Void
    @ObservedObject private var updater = Updater.shared
    @State private var text = ""
    @State private var saved = false
    enum Ask { case pause, openApp }
    @State private var asking: Ask?
    @FocusState private var focused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline) {
                Text(store.phase.label + (store.isPaused ? L(" · 일시정지", " · Paused") : "")).foregroundStyle(.secondary)
                Spacer()
                if store.phase != .idle {
                    Text(fmtClock(store.remaining)).font(.title2.bold().monospacedDigit())
                }
            }
            if store.phase.isStudy, !store.timer.task.isEmpty {
                Text("📌 \(store.timer.task)").lineLimit(2)
            }

            // 집중 중 일시정지 · 앱 열기 전에 한 번 묻는다
            if let ask = asking {
                VStack(alignment: .leading, spacing: 8) {
                    Text(ask == .pause ? L("너가 쉴 자격이 있을까?", "Have you earned this break?")
                                       : L("공부나 해라.", "Get back to studying."))
                        .font(.headline)
                    HStack {
                        Button { asking = nil } label: { Text(L("계속 집중", "Keep Focusing")).frame(maxWidth: .infinity) }
                            .buttonStyle(.borderedProminent)
                            .tint(.red)
                            .keyboardShortcut(.defaultAction)
                        Button {
                            asking = nil
                            ask == .pause ? togglePause() : openApp()
                        } label: {
                            Text(ask == .pause ? L("그래도 일시정지", "Pause Anyway") : L("그래도 열기", "Open Anyway"))
                                .frame(maxWidth: .infinity)
                        }
                    }
                    .controlSize(.large)
                }
                .padding(10)
                .background(RoundedRectangle(cornerRadius: 10).fill(Color.red.opacity(0.08)))
            } else if store.phase == .idle {
                HStack {
                    Button(action: primary) { Text(L("🍅 시작", "🍅 Start")).frame(maxWidth: .infinity) }
                        .buttonStyle(.borderedProminent)
                        .tint(.red)
                    Button(action: openApp) { Text(L("앱 열기", "Open App")).frame(maxWidth: .infinity) }
                }
                .controlSize(.large)
            } else {
                // 진행 중: [일시정지/재개] [앱 열기] [중지] = 2 : 1 : 1
                GeometryReader { g in
                    let unit = (g.size.width - 16) / 4
                    HStack(spacing: 8) {
                        Button {
                            if store.phase == .focus, !store.isPaused { asking = .pause } else { togglePause() }
                        } label: {
                            Label(store.isPaused ? L("재개", "Resume") : L("일시정지", "Pause"), systemImage: store.isPaused ? "play.fill" : "pause.fill")
                                .frame(maxWidth: .infinity)
                        }
                        .frame(width: unit * 2)
                        .help(store.phase == .focus ? L("집중 중에는 일시정지해도 차단이 유지돼요", "Sites stay blocked while focus is paused") : "")
                        Button {
                            if store.phase == .focus, !store.isPaused { asking = .openApp } else { openApp() }
                        } label: { Text(L("앱 열기", "Open App")).lineLimit(1).frame(maxWidth: .infinity) }
                        .frame(width: unit)
                        Button(action: primary) { Text(stopTitle).lineLimit(1).frame(maxWidth: .infinity) }
                            .frame(width: unit)
                    }
                    .controlSize(.large)
                }
                .frame(height: 32)
            }

            VStack(alignment: .leading, spacing: 4) {
                TextField(L("💭 나중에 할 것 적기 (Enter)", "💭 Jot down for later (Enter)"), text: $text)
                    .textFieldStyle(.roundedBorder)
                    .focused($focused)
                    .onSubmit {
                        guard !text.trimmingCharacters(in: .whitespaces).isEmpty, store.canPersist else { return }
                        store.addLater(text)
                        text = ""
                        saved = true
                        DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) { close() }
                    }
                if saved {
                    Text(L("✓ 적어뒀어요. 다시 집중!", "✓ Saved. Back to focus!")).font(.caption).foregroundStyle(.green)
                }
            }

            if let err = store.saveError {
                Text("⚠️ \(err)").font(.caption).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true)
            }

            if let v = updater.latest {
                HStack {
                    Text(L("🆕 새 버전 \(v)", "🆕 Version \(v) available")).font(.callout.bold())
                    Spacer()
                    Button(L("업데이트", "Update"), action: openUpdate).controlSize(.small)
                }
                .padding(8)
                .background(RoundedRectangle(cornerRadius: 8).fill(Color.accentColor.opacity(0.12)))
            }

            if store.browserAccessDenied {
                VStack(alignment: .leading, spacing: 4) {
                    Text(L("⚠️ 브라우저 제어 권한이 꺼져 있어서 사이트 차단이 안 돼요.", "⚠️ Browser control is off, so sites can't be blocked."))
                        .font(.caption).fixedSize(horizontal: false, vertical: true)
                    Button(L("자동화 설정 열기", "Open Automation Settings")) {
                        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation")!)
                    }
                    .controlSize(.small)
                }
            }

            let todos = store.today.todos
            if !todos.isEmpty {
                Divider()
                Text(L("오늘 할 일", "Today's to-dos") + "  \(todos.filter(\.done).count)/\(todos.count)")
                    .font(.caption.bold()).foregroundStyle(.secondary)
                // 6개를 넘으면 그 부분만 스크롤
                let list = VStack(alignment: .leading, spacing: 6) {
                    ForEach(todos.filter { !$0.done } + todos.filter(\.done)) { item in
                        Button {
                            withAnimation(.easeInOut(duration: 0.2)) { store.toggleTodo(item.id, on: dayKey()) }
                        } label: {
                            HStack(spacing: 8) {
                                Image(systemName: item.done ? "checkmark.circle.fill" : "circle")
                                    .foregroundStyle(item.done ? item.kind.color : Color.secondary)
                                Text(item.text).strikethrough(item.done)
                                    .foregroundStyle(item.done ? .secondary : .primary)
                                    .lineLimit(1)
                                Spacer(minLength: 0)
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
                if todos.count > 6 {
                    ScrollView { list.frame(maxWidth: .infinity, alignment: .leading) }.frame(height: 200)
                } else {
                    list
                }
            }
        }
        .padding(14)
        .frame(width: 320)
        .onAppear { DispatchQueue.main.async { focused = true } }
    }

    var stopTitle: String {
        switch store.phase {
        case .wrap: return L("완료", "Done")
        case .rest: return L("다음 집중", "Next Focus") // 휴식을 끝내고 바로 다음 집중 시작 창을 연다
        default: return L("중지", "Stop")
        }
    }
}

// MARK: - 딴짓 경고 (집중 외 시간에 한 사이트를 오래 쓸 때)

struct WarnView: View {
    let min: Int
    let site: String
    var closeTab: () -> Void
    var keep: () -> Void

    var body: some View {
        VStack(spacing: 12) {
            Text("⏰").font(.system(size: 40))
            Text(L("\(site)를 \(min)분째 쓰고 있어요", "You've been on \(site) for \(min) minutes")).font(.title3.bold())
            Text(L("지금 이게 정말 하려던 일인가요? 뽀모도로 하나만 시작해볼까요?", "Is this what you meant to be doing? How about one Pomodoro?"))
                .foregroundStyle(.secondary).multilineTextAlignment(.center)
            HStack {
                Button(L("알고 계속 보기", "Keep Browsing"), action: keep)
                Button(L("이 탭 닫기", "Close This Tab"), action: closeTab).keyboardShortcut(.defaultAction)
            }
            .controlSize(.large)
        }
        .padding(24)
        .frame(width: 380)
    }
}

// MARK: - 업데이트

struct UpdateView: View {
    @ObservedObject private var updater = Updater.shared
    var close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(L("🍅 새 버전 ", "🍅 Version ") + (updater.latest ?? "") + L("", " is available")).font(.title3.bold())
            Text(L("지금 버전 \(updater.current)", "You have \(updater.current)")).foregroundStyle(.secondary)
            ScrollView {
                Text(updater.notes.isEmpty ? L("바뀐 내용을 불러오는 중…", "Loading what's new…") : updater.notes)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .textSelection(.enabled)
            }
            .frame(height: 180)
            .padding(10)
            .background(RoundedRectangle(cornerRadius: 8).fill(.background.secondary))
            switch updater.state {
            case .updating:
                HStack { ProgressView().controlSize(.small); Text(L("받아서 빌드하는 중… (1분 정도) 끝나면 자동으로 다시 켜져요", "Downloading and building… (about a minute). The app restarts when it's done.")) }
            case .failed(let msg):
                Text(msg).font(.caption).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true)
            default:
                EmptyView()
            }
            HStack {
                Spacer()
                Button(L("나중에", "Later"), action: close).keyboardShortcut(.cancelAction)
                Button(L("지금 업데이트", "Update Now")) { updater.update() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(updater.state == .updating || updater.latest == nil)
            }
        }
        .padding(20)
        .frame(width: 460)
    }
}

// MARK: - 집중 시작

struct StartView: View {
    @EnvironmentObject var store: Store
    @State var task: String
    var close: () -> Void

    var body: some View {
        let s = store.settings
        VStack(alignment: .leading, spacing: 12) {
            Text(L("이번 \(s.focusMin)분 동안 무엇을 할까요?", "What will you work on for the next \(s.focusMin) minutes?")).font(.title3.bold())
            if !store.lastNote.isEmpty {
                Text(L("지난 정리 메모: \(store.lastNote)", "Last wrap-up note: \(store.lastNote)")).font(.callout).foregroundStyle(.secondary)
            }
            TextField(L("구체적으로 한 줄 (예: pset1 3번 회귀 결과 해석)", "One specific thing (e.g., interpret the regression results for Q3)"), text: $task)
                .textFieldStyle(.roundedBorder)
                .onSubmit(start)
            let picks = store.today.todos.filter { !$0.done && $0.kind != .other }.prefix(5)
            if !picks.isEmpty {
                Text(L("오늘 할 일에서 고르기", "Pick from today's to-dos")).font(.caption).foregroundStyle(.secondary)
                VStack(alignment: .leading, spacing: 4) {
                    ForEach(Array(picks)) { item in
                        Button((item.kind == .must ? "🔥 " : "") + item.text) { task = item.text }
                            .buttonStyle(.bordered)
                            .controlSize(.small)
                    }
                }
            }
            Text(s.cycleText)
                .font(.caption).foregroundStyle(.secondary)
            HStack {
                Spacer()
                Button(L("취소", "Cancel"), action: close).keyboardShortcut(.cancelAction)
                Button(L("🍅 집중 시작", "🍅 Start Focus"), action: start)
                    .keyboardShortcut(.defaultAction)
                    .disabled(task.trimmingCharacters(in: .whitespaces).isEmpty)
            }
        }
        .padding(20)
        .frame(width: 440)
    }

    func start() {
        guard !task.trimmingCharacters(in: .whitespaces).isEmpty else { return }
        store.startFocus(task: task)
        close()
    }
}

// MARK: - 오늘 할 일 (아침 계획)

extension TodoKind {
    var title: String {
        switch self {
        case .must: return L("반드시 할 일", "Must do")
        case .nice: return L("하면 좋은 일", "Nice to do")
        case .other: return L("기타 업무", "Other")
        }
    }
    var symbol: String {
        switch self {
        case .must: return "flame.fill"
        case .nice: return "hand.thumbsup.fill"
        case .other: return "paperclip"
        }
    }
    var color: Color {
        switch self {
        case .must: return .red
        case .nice: return .blue
        case .other: return .gray
        }
    }
    var placeholder: String {
        switch self {
        case .must: return L("꼭 끝낼 일 추가 (1~3개 추천)", "Add a must-do (1–3 is plenty)")
        case .nice: return L("하면 좋은 일 추가", "Add a nice-to-do")
        case .other: return L("기타 업무 추가", "Add another task")
        }
    }
}

struct PlanView: View {
    @EnvironmentObject var store: Store
    @State private var day: String

    init(day: String = dayKey()) { _day = State(initialValue: day) }

    var body: some View {
        let today = dayKey(store.now)
        let todos = store.todos(on: day)
        let doneCount = todos.filter(\.done).count
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                HStack(alignment: .center) {
                    Button { move(-1) } label: { Image(systemName: "chevron.left") }
                    VStack(alignment: .leading, spacing: 2) {
                        HStack(spacing: 6) {
                            Text(dayTitle(day)).font(.system(size: 24, weight: .bold))
                            if let badge = relativeLabel(day, today: today) {
                                Text(badge).font(.caption.bold()).padding(.horizontal, 6).padding(.vertical, 2)
                                    .background(Capsule().fill(Color.accentColor.opacity(0.15)))
                            }
                        }
                        Text(subtitle(todos: todos, done: doneCount, today: today)).foregroundStyle(.secondary)
                    }
                    Button { move(1) } label: { Image(systemName: "chevron.right") }
                    Spacer()
                    if day != today { Button(L("오늘", "Today")) { day = today } }
                    ProgressRing(progress: todos.isEmpty ? 0 : Double(doneCount) / Double(todos.count))
                }
                .buttonStyle(.borderless)
                .padding(.bottom, 4)

                let carry = store.unfinished(before: day)
                if day >= today && todos.isEmpty && !carry.isEmpty {
                    HStack {
                        Image(systemName: "arrow.uturn.forward.circle.fill").foregroundStyle(.orange)
                        Text(L("지난번에 못 한 일 \(carry.count)개가 있어요", "\(carry.count) left over from last time"))
                        Spacer()
                        Button(L("가져오기", "Bring Over")) { withAnimation { store.carryOver(to: day) } }
                    }
                    .padding(12)
                    .background(RoundedRectangle(cornerRadius: 12).fill(Color.orange.opacity(0.12)))
                }

                ForEach(TodoKind.allCases, id: \.self) { kind in
                    TodoSection(kind: kind, day: day, items: todos.filter { $0.kind == kind })
                }
            }
            .padding(20)
        }
        .frame(width: 480)
        .frame(minHeight: 520, idealHeight: 620)
    }

    private func move(_ delta: Int) {
        guard let d = dayKeyFormatter.date(from: day),
              let next = Calendar.current.date(byAdding: .day, value: delta, to: d) else { return }
        day = dayKey(next)
    }

    private func subtitle(todos: [TodoItem], done: Int, today: String) -> String {
        if !todos.isEmpty { return L("\(todos.count)개 중 \(done)개 완료", "\(done) of \(todos.count) done") }
        if day == today { return L("오늘 할 일을 정해볼까요?", "What's on your plate today?") }
        return day > today ? L("이 날 할 일을 미리 적어둘까요?", "Plan this day ahead?") : L("이 날은 적은 할 일이 없어요", "Nothing was planned for this day")
    }
}

/// "10월 27일 화요일"
func dayTitle(_ key: String) -> String {
    guard let d = dayKeyFormatter.date(from: key) else { return key }
    return d.formatted(.dateTime.month().day().weekday(.wide).locale(Store.shared.settings.language.locale))
}

/// 오늘 · 내일 · 어제 (그 외엔 nil)
func relativeLabel(_ key: String, today: String) -> String? {
    guard let d = dayKeyFormatter.date(from: key), let t = dayKeyFormatter.date(from: today),
          let diff = Calendar.current.dateComponents([.day], from: t, to: d).day else { return nil }
    return [-1: L("어제", "Yesterday"), 0: L("오늘", "Today"), 1: L("내일", "Tomorrow")][diff]
}

struct ProgressRing: View {
    let progress: Double
    var body: some View {
        ZStack {
            Circle().stroke(Color.secondary.opacity(0.2), lineWidth: 5)
            Circle()
                .trim(from: 0, to: progress)
                .stroke(Color.green, style: StrokeStyle(lineWidth: 5, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .animation(.easeOut, value: progress)
            Text("\(Int((progress * 100).rounded()))%").font(.caption.bold().monospacedDigit())
        }
        .frame(width: 48, height: 48)
    }
}

struct TodoSection: View {
    @EnvironmentObject var store: Store
    let kind: TodoKind
    let day: String
    let items: [TodoItem]
    @State private var draft = ""
    @State private var dragging: UUID?

    var body: some View {
        // 끝낸 일은 아래로
        let sorted = items.filter { !$0.done } + items.filter(\.done)
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 6) {
                Image(systemName: kind.symbol).foregroundStyle(kind.color)
                Text(kind.title).font(.headline)
                Spacer()
                if !items.isEmpty {
                    Text("\(items.filter(\.done).count)/\(items.count)")
                        .font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                }
            }
            .padding(.bottom, 8)

            // 끌어서 순서 바꾸기 (같은 칸 · 끝낸 일은 끝낸 일끼리)
            ForEach(sorted) { item in
                VStack(spacing: 0) {
                    TodoRow(item: item, day: day, color: kind.color)
                    Divider().opacity(0.5)
                }
                .onDrag {
                    dragging = item.id
                    return NSItemProvider(object: item.id.uuidString as NSString)
                }
                .onDrop(of: [.text], delegate: TodoDrop(target: item.id, group: sorted.filter { $0.done == item.done }.map(\.id), dragging: $dragging) { from, to in
                    withAnimation(.easeInOut(duration: 0.15)) { store.moveTodo(from, to: to, on: day) }
                })
            }

            HStack(spacing: 10) {
                Image(systemName: "plus").foregroundStyle(kind.color).frame(width: 20)
                TextField(kind.placeholder, text: $draft)
                    .textFieldStyle(.plain)
                    .onSubmit {
                        withAnimation { _ = store.addTodo(draft, kind: kind, on: day) }
                        draft = ""
                    }
            }
            .padding(.vertical, 8)
        }
        .padding(14)
        .background(RoundedRectangle(cornerRadius: 12).fill(.background.secondary))
        .overlay(alignment: .leading) {
            RoundedRectangle(cornerRadius: 2).fill(kind.color).frame(width: 4).padding(.vertical, 14)
        }
    }
}

/// 다른 줄 위로 끌고 가면 그 자리로 옮긴다 (놓기 전에도 바로 보이게)
struct TodoDrop: DropDelegate {
    let target: UUID
    let group: [UUID]
    @Binding var dragging: UUID?
    let move: (UUID, UUID) -> Void

    func dropEntered(info: DropInfo) {
        guard let d = dragging, d != target, group.contains(d) else { return }
        move(d, target)
    }
    func dropUpdated(info: DropInfo) -> DropProposal? { DropProposal(operation: .move) }
    func performDrop(info: DropInfo) -> Bool {
        dragging = nil
        return true
    }
}

struct TodoRow: View {
    @EnvironmentObject var store: Store
    let item: TodoItem
    let day: String
    let color: Color
    @State private var hover = false
    @State private var editing = false
    @State private var draft = ""
    @FocusState private var fieldFocused: Bool

    var body: some View {
        HStack(spacing: 10) {
            Button {
                withAnimation(.easeInOut(duration: 0.2)) { store.toggleTodo(item.id, on: day) }
            } label: {
                Image(systemName: item.done ? "checkmark.circle.fill" : "circle")
                    .font(.system(size: 18))
                    .foregroundStyle(item.done ? color : Color.secondary)
                    .frame(width: 20)
            }
            .buttonStyle(.plain)

            if editing {
                // Enter 저장 · Esc 취소 · 다른 곳을 누르면 저장
                TextField("", text: $draft)
                    .textFieldStyle(.roundedBorder)
                    .focused($fieldFocused)
                    .onSubmit(commit)
                    .onExitCommand { editing = false }
                    .onChange(of: fieldFocused) { _, focused in if !focused && editing { commit() } }
                    .onAppear { fieldFocused = true }
            } else {
                Text(item.text)
                    .strikethrough(item.done)
                    .foregroundStyle(item.done ? .secondary : .primary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .contentShape(Rectangle())
                    .onTapGesture(count: 2) {
                        draft = item.text
                        editing = true
                    }
                    .help(L("더블 클릭해서 고치기", "Double-click to edit"))
            }

            Button {
                withAnimation { store.deleteTodo(item.id, on: day) }
            } label: {
                Image(systemName: "trash").foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
            .opacity(hover ? 1 : 0)
            .help(L("삭제", "Delete"))
        }
        .padding(.vertical, 7)
        .contentShape(Rectangle())
        .onHover { hover = $0 }
        .opacity(item.done && !editing ? 0.6 : 1)
    }

    private func commit() {
        editing = false
        store.renameTodo(item.id, to: draft, on: day) // 비우면 원래 글 유지
    }
}

// MARK: - 정리 3분

struct WrapView: View {
    @EnvironmentObject var store: Store
    @State private var note = ""
    var close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text(L("📝 정리 시간", "📝 Wrap-up")).font(.title3.bold())
                Spacer()
                if store.phase == .wrap {
                    Text(fmtClock(store.remaining)).font(.title3.monospacedDigit()).foregroundStyle(.orange)
                }
            }
            if !store.timer.task.isEmpty {
                Text("📌 \(store.timer.task)").foregroundStyle(.secondary)
            }
            Text(L("어디까지 했고, 다음엔 뭘 하나요?", "Where did you leave off, and what's next?")).font(.headline)
            TextEditor(text: $note)
                .font(.body)
                .frame(height: 80)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(.quaternary))
                .onChange(of: note) { _, new in store.saveNote(new) }

            let open = store.openLater
            Text(L("💭 나중에 할 것 \(open.count)개 — 처리한 건 체크", "💭 Later (\(open.count)) — check off what's handled")).font(.headline)
            if open.isEmpty {
                Text(L("없음 👏", "Nothing here 👏")).foregroundStyle(.secondary)
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 6) {
                        ForEach(open) { item in
                            Toggle(isOn: Binding(get: { item.done }, set: { _ in store.toggleLater(item.id) })) {
                                Text(item.text)
                            }
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .frame(maxHeight: 160)
            }
            HStack {
                Spacer()
                Button(L("정리 완료 → 휴식", "Done → Start Break")) {
                    store.saveNote(note)
                    store.finishWrap()
                    close()
                }
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(20)
        .frame(width: 460)
        .onAppear { note = store.lastNote }
    }
}

// MARK: - 나중에 할 것 빠르게 적기 (⌥⇧L)

struct QuickCaptureView: View {
    @EnvironmentObject var store: Store
    @State private var text = ""
    @State private var saved = false
    var close: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            TextField(L("💭 떠오른 생각을 적고 다시 공부로", "💭 Write it down and get back to work"), text: $text)
                .textFieldStyle(.roundedBorder)
                .font(.title3)
                .onSubmit {
                    guard !text.trimmingCharacters(in: .whitespaces).isEmpty else { return close() }
                    guard store.canPersist else { return }
                    store.addLater(text)
                    text = ""
                    saved = true
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) { close() }
                }
            if let err = store.saveError {
                Text(L("⚠️ 지금은 저장할 수 없어요. \(err)", "⚠️ Can't save right now. \(err)")).font(.caption).foregroundStyle(.red)
                    .fixedSize(horizontal: false, vertical: true)
            } else {
                Text(saved ? L("✓ 적어뒀어요. 이제 내려놓고 다시 집중!", "✓ Saved. Let it go and get back to it!") : L("Enter 저장 · Esc 닫기 · 정리 시간에 다시 보여드려요", "Enter to save · Esc to close · You'll see it again at wrap-up"))
                    .font(.caption)
                    .foregroundStyle(saved ? .green : .secondary)
            }
        }
        .padding(16)
        .frame(width: 460)
        .onExitCommand(perform: close)
    }
}

// MARK: - 포기하기 (30초 대기 + 문장 입력)

struct GiveUpView: View {
    @EnvironmentObject var store: Store
    @State private var phrase = ""
    var onGiveUp: () -> Void
    var close: () -> Void

    var body: some View {
        let target = store.settings.stopPhrase
        let matches = Self.normalize(phrase) == Self.normalize(target)
        let canGiveUp = matches
        VStack(alignment: .leading, spacing: 12) {
            Text(L("정말 그만둘까요?", "Give up on this session?")).font(.title3.bold())
            Text(L("하고 싶은 게 생겼다면 ⌥⇧L로 적어두고 계속해도 돼요. 그래도 그만두려면 아래 문장을 그대로 입력하세요.", "If something came up, jot it down with ⌥⇧L and keep going. To quit anyway, type the phrase below."))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            // 입력하면 사라지는 안내 글자 대신, 따라 칠 문장을 항상 보여준다
            Text(target).font(.headline).textSelection(.enabled)
            TextField(L("위 문장을 그대로 입력", "Type the phrase above"), text: $phrase)
                .textFieldStyle(.roundedBorder)
                .onSubmit { if canGiveUp { giveUp() } }
            Text(phrase.isEmpty ? " " : (matches ? L("✓ 일치해요", "✓ Matches") : L("아직 달라요 (띄어쓰기·문장부호는 상관없어요)", "Not yet (spacing and punctuation don't matter)")))
                .font(.caption)
                .foregroundStyle(matches ? .green : .secondary)
            HStack {
                Spacer()
                // Enter 는 입력칸에서 포기를 처리하므로, 계속하기는 Esc 로 연결한다
                Button(L("계속 집중하기", "Keep Focusing"), action: close).keyboardShortcut(.cancelAction)
                Button(L("포기", "Give Up"), action: giveUp).disabled(!canGiveUp)
            }
        }
        .padding(20)
        .frame(width: 440)
    }

    private func giveUp() {
        onGiveUp()
        close()
    }

    /// 비교할 때 띄어쓰기·문장부호·유니코드 조합 방식 차이는 무시한다
    static func normalize(_ s: String) -> String {
        s.precomposedStringWithCanonicalMapping.filter { !$0.isWhitespace && !$0.isPunctuation }
    }
}

// MARK: - 기록 (주간 띠 + 하루 상세)

private let weekCal: Calendar = {
    var c = Calendar(identifier: .gregorian)
    c.firstWeekday = 2 // 월요일 시작
    return c
}()

private var weekdaySymbols: [String] {
    Store.shared.settings.language == .en ? ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"] : ["일", "월", "화", "수", "목", "금", "토"]
}

struct DashboardView: View {
    @EnvironmentObject var store: Store
    var openPlan: (String) -> Void
    var openSettings: () -> Void
    var openStats: () -> Void
    var quit: () -> Void
    var openUpdate: () -> Void
    @ObservedObject private var updater = Updater.shared
    @State private var weekOffset = 0
    @State private var selected = dayKey()
    @State private var newLater = ""

    var body: some View {
        let dates = weekDates
        let secs = dates.map { store.studySec(for: dayKey($0)) }
        let sessions = dates.map { store.data.days[dayKey($0)]?.sessions ?? 0 }.reduce(0, +)
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                HStack {
                    Button(L("📋 할 일", "📋 To-dos")) { openPlan(dayKey()) }
                    Button(L("📊 통계", "📊 Stats"), action: openStats)
                    Button(L("⚙︎ 설정", "⚙︎ Settings"), action: openSettings)
                    Spacer()
                    Button(L("종료", "Quit"), action: quit)
                }
                .controlSize(.large)

                HStack {
                    Button { changeWeek(-1) } label: { Image(systemName: "chevron.left") }
                    Text(weekTitle(dates)).font(.title3.bold())
                    Button { changeWeek(1) } label: { Image(systemName: "chevron.right") }
                    Spacer()
                    Text(L("이번 주 공부 \(fmtMinutes(secs.reduce(0, +))) · 🍅 \(sessions)개", "This week: \(fmtMinutes(secs.reduce(0, +))) · 🍅 \(sessions)"))
                        .foregroundStyle(.secondary)
                }
                .buttonStyle(.borderless)

                Card(title: "") {
                    WeekChart(dates: dates, secs: secs, selected: $selected)
                }

                DayDetail(key: selected, openPlan: openPlan)

                laterCard

                Divider()
                HStack {
                    Text("aADHD v\(updater.current) · \(madeBy)").foregroundStyle(.secondary)
                    switch updater.state {
                    case .checking: Text(L("확인 중…", "Checking…")).foregroundStyle(.secondary)
                    case .upToDate: Text(L("최신 버전이에요 ✓", "You're up to date ✓")).foregroundStyle(.secondary)
                    case .failed(let msg): Text(msg).foregroundStyle(.red).lineLimit(2)
                    default: EmptyView()
                    }
                    Spacer()
                    if let v = updater.latest {
                        Button(L("업데이트 \(v)", "Update to \(v)"), action: openUpdate).buttonStyle(.borderedProminent)
                    } else {
                        Button(L("업데이트 확인", "Check for Updates")) { updater.check() }
                    }
                }
                .font(.callout)
            }
            .padding(20)
        }
        .frame(minWidth: 620, minHeight: 700)
    }

    var weekDates: [Date] {
        let start = weekCal.dateInterval(of: .weekOfYear, for: store.now)!.start
        let monday = weekCal.date(byAdding: .day, value: 7 * weekOffset, to: start)!
        return (0..<7).map { weekCal.date(byAdding: .day, value: $0, to: monday)! }
    }

    func weekTitle(_ dates: [Date]) -> String {
        let f = { (d: Date) in "\(weekCal.component(.month, from: d))/\(weekCal.component(.day, from: d))" }
        return (weekOffset == 0 ? L("이번 주 ", "This week ") : "") + "\(f(dates[0])) – \(f(dates[6]))"
    }

    func changeWeek(_ delta: Int) {
        weekOffset += delta
        // 이번 주로 돌아오면 오늘, 지난 주면 그 주 월요일을 고른다
        selected = weekOffset == 0 ? dayKey(store.now) : dayKey(weekDates[0])
    }

    var laterCard: some View {
        Card(title: L("💭 나중에 할 것", "💭 Later")) {
            HStack {
                TextField(L("새로 적기 (Enter)", "Add new (Enter)"), text: $newLater)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit { store.addLater(newLater); newLater = "" }
                Button(L("완료 항목 지우기", "Clear Completed")) { store.clearDoneLater() }
            }
            let items = store.data.later.sorted { ($0.done ? 1 : 0, -$0.created.timeIntervalSince1970) < ($1.done ? 1 : 0, -$1.created.timeIntervalSince1970) }
            if items.isEmpty {
                Text(L("공부 중 떠오른 생각을 ⌥⇧L로 적으면 여기 모여요.", "Thoughts you jot down with ⌥⇧L while studying show up here.")).foregroundStyle(.secondary)
            }
            ForEach(items) { item in
                HStack(alignment: .top) {
                    Toggle("", isOn: Binding(get: { item.done }, set: { _ in store.toggleLater(item.id) }))
                        .labelsHidden()
                    VStack(alignment: .leading, spacing: 2) {
                        Text(item.text).strikethrough(item.done).foregroundStyle(item.done ? .secondary : .primary)
                        Text(item.created.formatted(date: .abbreviated, time: .shortened)
                             + (item.during.isEmpty ? "" : L(" · \"\(item.during)\" 하다가", " · during \"\(item.during)\"")))
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button(L("삭제", "Delete")) { store.deleteLater(item.id) }.buttonStyle(.borderless).foregroundStyle(.secondary)
                }
            }
        }
    }
}

/// 월~일 공부 시간 막대. 세로축은 1시간 단위 눈금, 맨 위는 가장 긴 날을 정시로 올림.
struct WeekChart: View {
    @EnvironmentObject var store: Store
    let dates: [Date]
    let secs: [Double]
    @Binding var selected: String
    private let chartH: CGFloat = 140

    var body: some View {
        let topHours = max(1, Int((secs.max() ?? 0) / 3600 - 0.0001) + 1)
        let scale = chartH / CGFloat(topHours * 3600)
        HStack(alignment: .top, spacing: 6) {
            // 세로축 라벨
            ZStack(alignment: .topTrailing) {
                ForEach(0...topHours, id: \.self) { h in
                    Text(h == 0 ? "0" : "\(h)h")
                        .font(.caption2.monospacedDigit()).foregroundStyle(.secondary)
                        .offset(y: chartH - CGFloat(h * 3600) * scale + 18 - 7)
                }
            }
            .frame(width: 24, height: chartH + 18, alignment: .topTrailing)

            ZStack(alignment: .topLeading) {
                // 1시간 눈금선
                ForEach(0...topHours, id: \.self) { h in
                    Rectangle()
                        .fill(Color.secondary.opacity(h == 0 ? 0.4 : 0.15))
                        .frame(height: 1)
                        .offset(y: chartH - CGFloat(h * 3600) * scale + 18)
                }
                HStack(alignment: .bottom, spacing: 8) {
                    ForEach(dates.indices, id: \.self) { i in
                        dayColumn(i, scale: scale)
                    }
                }
            }
        }
    }

    func dayColumn(_ i: Int, scale: CGFloat) -> some View {
        let key = dayKey(dates[i])
        let isSel = key == selected
        let isFuture = dates[i] > store.now && key != dayKey(store.now)
        let sec = secs[i]
        return Button { selected = key } label: {
            VStack(spacing: 4) {
                Text(sec >= 60 ? shortTime(sec) : " ")
                    .font(.caption2.monospacedDigit()).foregroundStyle(.secondary)
                    .frame(height: 14)
                ZStack(alignment: .bottom) {
                    Color.clear.frame(height: chartH)
                    if sec >= 60 {
                        RoundedRectangle(cornerRadius: 4)
                            .fill(Color.red.opacity(isSel ? 0.9 : 0.55))
                            .frame(height: CGFloat(sec) * scale)
                    }
                }
                Text("\(weekdaySymbols[weekCal.component(.weekday, from: dates[i]) - 1]) \(weekCal.component(.day, from: dates[i]))")
                    .font(.caption.weight(isSel ? .bold : .regular))
                    .foregroundStyle(isFuture ? .tertiary : (isSel ? .primary : .secondary))
            }
            .padding(.horizontal, 4)
            .padding(.bottom, 4)
            .frame(maxWidth: .infinity)
            .background(RoundedRectangle(cornerRadius: 8).fill(isSel ? Color.accentColor.opacity(0.15) : .clear))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    func shortTime(_ sec: Double) -> String {
        let m = Int((sec / 60).rounded())
        return m >= 60 ? "\(m / 60)h \(m % 60)m" : "\(m)m"
    }
}

/// 선택한 하루의 할 일, 한 일(정리 메모), 딴짓
struct DayDetail: View {
    @EnvironmentObject var store: Store
    let key: String
    var openPlan: (String) -> Void

    var body: some View {
        let day = store.data.days[key] ?? DayStats()
        let today = dayKey(store.now)
        let isFuture = key > today
        VStack(alignment: .leading, spacing: 12) {
            Text(dayTitle(key) + (relativeLabel(key, today: today).map { " (\($0))" } ?? "")).font(.title3.bold())
            if !isFuture {
                HStack(spacing: 10) {
                    Kpi(value: fmtMinutes(store.studySec(for: key)), label: L("공부", "Study"))
                    Kpi(value: "\(day.sessions)", label: L("완료한 뽀모도로", "Pomodoros done"))
                    Kpi(value: fmtMinutes(day.distractTotal), label: L("딴짓", "Distractions"))
                }
            }

            Card(title: L("할 일", "To-dos")) {
                HStack {
                    if day.todos.isEmpty {
                        Text(isFuture ? L("아직 적은 할 일이 없어요.", "No to-dos yet.") : L("이 날은 적은 할 일이 없어요.", "No to-dos for this day.")).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button(day.todos.isEmpty ? L("✏️ 할 일 적기", "✏️ Add To-dos") : L("✏️ 편집", "✏️ Edit")) { openPlan(key) }
                }
                ForEach(TodoKind.allCases, id: \.self) { kind in
                    let items = day.todos.filter { $0.kind == kind }
                    if !items.isEmpty {
                        HStack(spacing: 6) {
                            Image(systemName: kind.symbol).foregroundStyle(kind.color)
                            Text(kind.title).font(.subheadline.bold())
                            Text("\(items.filter(\.done).count)/\(items.count)")
                                .font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                        }
                        ForEach(items) { item in
                            TodoRow(item: item, day: key, color: kind.color)
                        }
                    }
                }
            }

            if !isFuture {
                Card(title: L("한 일", "Sessions")) {
                    if day.log.isEmpty {
                        Text(L("이 날은 뽀모도로 기록이 없어요.", "No Pomodoros this day.")).foregroundStyle(.secondary)
                    }
                    ForEach(day.log) { l in
                        VStack(alignment: .leading, spacing: 2) {
                            Text("\(l.done ? "🍅" : "✖️") \(l.end.formatted(date: .omitted, time: .shortened)) — \(l.task.isEmpty ? L("(할 일 없음)", "(no task)") : l.task) \(l.done ? L("(\(l.minutes ?? 0)분)", "(\(l.minutes ?? 0) min)") : L("(포기)", "(gave up)"))")
                            if !l.note.isEmpty {
                                Text("📝 \(l.note)").font(.callout).foregroundStyle(.secondary)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                        }
                        .padding(.vertical, 2)
                    }
                }

                Card(title: L("딴짓 사이트", "Distracting sites")) {
                    let entries = day.distractSec.sorted { $0.value > $1.value }
                    if entries.isEmpty {
                        Text(L("딴짓 기록이 없어요 👏", "No distractions 👏")).foregroundStyle(.secondary)
                    }
                    let maxV = entries.first?.value ?? 1
                    ForEach(entries, id: \.key) { site, sec in
                        HStack {
                            Text(site).frame(width: 130, alignment: .leading)
                            GeometryReader { g in
                                RoundedRectangle(cornerRadius: 3).fill(.secondary.opacity(0.5))
                                    .frame(width: max(2, g.size.width * sec / maxV))
                            }
                            .frame(height: 10)
                            Text(fmtMinutes(sec)).monospacedDigit().foregroundStyle(.secondary).frame(width: 70, alignment: .trailing)
                        }
                    }
                    Text(L("연속 사용 경고 \(day.warnings)회 · 중도 포기 \(day.abandoned)회", "\(day.warnings) distraction alerts · \(day.abandoned) sessions abandoned"))
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }
}

// MARK: - 학습 통계 (일 · 주)

struct StatsView: View {
    @EnvironmentObject var store: Store
    enum Span: CaseIterable {
        case day, week
        var label: String { self == .day ? L("일", "Day") : L("주", "Week") }
    }
    @State private var span: Span = .day
    @State private var offset = 0 // 0 = 오늘 / 이번 주, -1 = 어제 / 지난 주 …

    var body: some View {
        let keys = rangeKeys
        let usage = store.studyUsage(days: keys)
        let days = keys.map { store.data.days[$0] ?? DayStats() }
        let study = keys.map { store.studySec(for: $0) }.reduce(0, +)
        let sessions = days.map(\.sessions).reduce(0, +)
        let abandoned = days.map(\.abandoned).reduce(0, +)
        let tries = sessions + abandoned
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                HStack {
                    Picker("", selection: $span) {
                        ForEach(Span.allCases, id: \.self) { Text($0.label).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .frame(width: 110)
                    .onChange(of: span) { offset = 0 }
                    Spacer()
                    Button { offset -= 1 } label: { Image(systemName: "chevron.left") }
                    Text(rangeTitle(keys)).font(.headline).frame(minWidth: 180)
                    Button { offset += 1 } label: { Image(systemName: "chevron.right") }
                        .disabled(offset >= 0)
                }
                .buttonStyle(.borderless)

                HStack(spacing: 10) {
                    Kpi(value: fmtMinutes(study), label: L("공부 시간", "Study time"))
                    Kpi(value: "\(sessions)", label: L("완료한 뽀모도로", "Pomodoros done"))
                    Kpi(value: tries == 0 ? "–" : "\(Int((Double(sessions) / Double(tries) * 100).rounded()))%", label: L("집중 성공률", "Focus success rate"))
                    if span == .week {
                        let studied = keys.filter { store.studySec(for: $0) >= 60 }.count
                        Kpi(value: L("\(studied)/7일", "\(studied)/7 days"), label: L("공부한 날", "Days studied"))
                    } else {
                        Kpi(value: fmtMinutes(days.map(\.distractTotal).reduce(0, +)), label: L("딴짓", "Distractions"))
                    }
                }

                TopCard(title: L("집중 중 많이 쓴 앱", "Top apps while focusing"), items: usage.apps, color: .orange)
                TopCard(title: L("집중 중 많이 본 사이트", "Top sites while focusing"), items: usage.sites, color: .blue)

                Text(store.settings.trackStudyUsage
                     ? L("집중·정리 중(일시정지·자리 비움 제외) 맨 앞에 있던 앱과 사이트 도메인만 세요. 이 맥에만 저장돼요.", "Counts only the frontmost app and site domain during focus and wrap-up (not while paused or away). Stored only on this Mac.")
                     : L("앱·사이트 기록이 꺼져 있어요. 설정에서 켤 수 있어요.", "App and site tracking is off. You can turn it on in Settings."))
                    .font(.caption).foregroundStyle(.secondary)
            }
            .padding(20)
        }
        .frame(minWidth: 560, minHeight: 600)
    }

    private var rangeKeys: [String] {
        let cal = Calendar.current
        if span == .day {
            return [dayKey(cal.date(byAdding: .day, value: offset, to: store.now)!)]
        }
        var wc = Calendar(identifier: .gregorian)
        wc.firstWeekday = 2 // 월요일 시작
        let monday = wc.date(byAdding: .day, value: 7 * offset, to: wc.dateInterval(of: .weekOfYear, for: store.now)!.start)!
        return (0..<7).map { dayKey(wc.date(byAdding: .day, value: $0, to: monday)!) }
    }

    private func rangeTitle(_ keys: [String]) -> String {
        let today = dayKey(store.now)
        if span == .day {
            return dayTitle(keys[0]) + (relativeLabel(keys[0], today: today).map { " (\($0))" } ?? "")
        }
        let md = { (k: String) -> String in
            guard let d = dayKeyFormatter.date(from: k) else { return k }
            return "\(Calendar.current.component(.month, from: d))/\(Calendar.current.component(.day, from: d))"
        }
        return (offset == 0 ? L("이번 주 ", "This week ") : "") + "\(md(keys[0])) – \(md(keys[6]))"
    }
}

/// 상위 5개를 막대 + 비율 + 시간으로 보여준다
struct TopCard: View {
    let title: String
    let items: [String: Double]
    let color: Color

    var body: some View {
        let total = items.values.reduce(0, +)
        let top = items.sorted { $0.value > $1.value }.prefix(5)
        Card(title: title) {
            if total < 60 {
                Text(L("아직 기록이 없어요. 집중을 시작하면 쌓여요.", "Nothing yet. Start a focus session to see data here.")).foregroundStyle(.secondary)
            } else {
                ForEach(Array(top.enumerated()), id: \.element.key) { i, item in
                    let share = item.value / total
                    HStack(spacing: 10) {
                        Text("\(i + 1)").font(.caption.bold().monospacedDigit()).foregroundStyle(.secondary).frame(width: 14)
                        Text(item.key).lineLimit(1).frame(width: 150, alignment: .leading)
                        GeometryReader { g in
                            ZStack(alignment: .leading) {
                                RoundedRectangle(cornerRadius: 4).fill(Color.secondary.opacity(0.12))
                                RoundedRectangle(cornerRadius: 4).fill(color.opacity(i == 0 ? 0.9 : 0.6))
                                    .frame(width: max(3, g.size.width * share))
                            }
                        }
                        .frame(height: 12)
                        Text("\(Int((share * 100).rounded()))%").font(.callout.bold().monospacedDigit()).frame(width: 44, alignment: .trailing)
                        Text(item.value < 60 ? L("1분 미만", "<1 min") : fmtMinutes(item.value))
                            .font(.callout.monospacedDigit()).foregroundStyle(.secondary).frame(width: 84, alignment: .trailing)
                    }
                }
                if items.count > 5 {
                    let rest = items.sorted { $0.value > $1.value }.dropFirst(5).map(\.value).reduce(0, +)
                    Text(L("기타 \(items.count - 5)개 · \(Int((rest / total * 100).rounded()))%", "\(items.count - 5) more · \(Int((rest / total * 100).rounded()))%")).font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }
}

struct Kpi: View {
    let value: String
    let label: String
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(value).font(.title2.bold().monospacedDigit())
            Text(label).font(.caption).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 10).fill(.background.secondary))
    }
}

struct Card<Content: View>: View {
    let title: String
    @ViewBuilder var content: Content
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            if !title.isEmpty { Text(title).font(.headline) }
            content
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(14)
        .background(RoundedRectangle(cornerRadius: 10).fill(.background.secondary))
    }
}

// MARK: - 설정

struct SettingsView: View {
    @EnvironmentObject var store: Store
    @State private var s = Settings()
    @State private var sitesText = ""
    @State private var allowText = ""
    @State private var saved = false
    @State private var rejected: [String] = []
    var chooseFolder: () -> Void
    var revealFolder: () -> Void
    var toggleLogin: () -> Void
    var uninstall: () -> Void
    @State private var loginOn = SMAppService.mainApp.status == .enabled
    @ObservedObject private var sync = RemindersSync.shared

    var body: some View {
        ScrollView { // 내용이 화면보다 길면 스크롤
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Text("언어 / Language").font(.headline)
                Spacer()
                Picker("", selection: Binding(get: { store.settings.language }, set: { lang in
                    store.setLanguage(lang) // 바로 적용
                    s.language = lang
                    s.stopPhrase = store.settings.stopPhrase
                })) {
                    Text("한국어").tag(Lang.ko)
                    Text("English").tag(Lang.en)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(width: 180)
            }

            Text(L("시간", "Timer")).font(.headline)
            Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 8) {
                NumberRow(label: L("집중 (분)", "Focus (min)"), value: $s.focusMin, range: Settings.focusRange)
                NumberRow(label: L("정리 (분)", "Wrap-up (min)"), value: $s.wrapMin, range: Settings.wrapRange)
                NumberRow(label: L("휴식 (분)", "Break (min)"), value: $s.breakMin, range: Settings.breakRange)
                NumberRow(label: L("연속 사용 경고 (분)", "Distraction alert after (min)"), value: $s.warnAfterMin, range: Settings.warnRange)
            }
            Button(L("기본값으로 (35 · 3 · 7)", "Reset to Defaults (35 · 3 · 7)")) {
                s.focusMin = 35
                s.wrapMin = 3
                s.breakMin = 7
            }
            .buttonStyle(.link)

            Text(L("포기할 때 입력할 문장", "Phrase to type when giving up")).font(.headline)
            TextField("", text: $s.stopPhrase).textFieldStyle(.roundedBorder)


            Text(L("차단할 사이트 (한 줄에 하나, 하위 도메인 포함)", "Blocked sites (one per line, includes subdomains)")).font(.headline)
            TextEditor(text: $sitesText)
                .font(.body.monospaced())
                .frame(height: 120)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(.quaternary))

            Text(L("허용할 사이트 (위 목록의 하위 도메인이어도 막지 않음)", "Allowed sites (never blocked, even under a blocked domain)")).font(.headline)
            TextEditor(text: $allowText)
                .font(.body.monospaced())
                .frame(height: 44)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(.quaternary))

            Toggle(L("집중 중 쓴 앱 · 사이트 기록 (학습 통계용, 이 맥에만 저장)", "Track apps and sites used while focusing (for stats, stored only on this Mac)"), isOn: $s.trackStudyUsage)
            Toggle(L("아침 6시 이후 처음 맥을 쓸 때 할 일 창 띄우기", "Show to-dos the first time I use my Mac after 6 a.m."), isOn: $s.morningPlan)

            HStack {
                Button(L("저장", "Save")) {
                    s.sites = sitesText.split(whereSeparator: \.isNewline).map(String.init)
                    s.allowSites = allowText.split(whereSeparator: \.isNewline).map(String.init)
                    rejected = store.updateSettings(s)
                    load()
                    saved = true
                }
                .keyboardShortcut(.defaultAction)
                if saved { Text(L("저장됨 ✓", "Saved ✓")).foregroundStyle(.secondary) }
            }
            if !rejected.isEmpty {
                Text(L("사이트로 인식하지 못해 뺐어요: ", "Removed entries that aren't valid sites: ") + rejected.joined(separator: ", "))
                    .font(.caption).foregroundStyle(.red)
            }

            Divider()
            Toggle(L("로그인 시 자동 실행 (꺼져 있으면 사이트 차단도 안 돼요)", "Launch at login (blocking only works while the app is running)"), isOn: Binding(
                get: { loginOn },
                set: { _ in
                    toggleLogin()
                    loginOn = SMAppService.mainApp.status == .enabled
                }))
            Toggle(L("아이폰 · 아이패드와 할 일 맞추기 (미리 알림)", "Sync to-dos with iPhone and iPad (Reminders)"), isOn: Binding(
                get: { sync.enabled },
                set: { sync.setEnabled($0) }))
            Text(sync.status.isEmpty
                 ? L("미리 알림에 'aADHD 할 일' · 'aADHD 나중에 할 것' 목록을 만들어 아이클라우드로 맞춰요. 폰에서 체크 · 추가 · 수정한 것도 여기로 와요. (오늘 이후 할 일만)",
                     "Creates 'aADHD To-dos' and 'aADHD Later' lists in Reminders, synced through iCloud. Checks, additions, and edits on your phone come back here. (Today and later only)")
                 : sync.status)
                .font(.caption).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            Divider()
            Text(L("기록 저장 위치", "Data location")).font(.headline)
            Text(store.dataDir.path).font(.caption.monospaced()).foregroundStyle(.secondary).textSelection(.enabled)
            Text(L("매일 자동으로 백업해요. 앱을 다시 빌드하거나 업데이트해도 기록은 그대로예요.", "Backed up automatically every day. Rebuilding or updating the app keeps your data."))
                .font(.caption)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Button(L("다른 폴더 선택…", "Choose Folder…"), action: chooseFolder)
                Button(L("Finder에서 보기", "Show in Finder"), action: revealFolder)
            }

            Divider()
            HStack {
                Button(L("aADHD 삭제…", "Delete aADHD…"), role: .destructive, action: uninstall)
                Spacer()
                Text("v\(Updater.shared.current) · \(madeBy)").font(.caption).foregroundStyle(.secondary)
            }
        }
        .padding(20)
        .frame(width: 480)
        }
        .frame(width: 500)
        .frame(minHeight: 360, idealHeight: 680)
        .onAppear(perform: load)
    }

    func load() {
        s = store.settings
        sitesText = s.sites.joined(separator: "\n")
        allowText = s.allowSites.joined(separator: "\n")
    }
}

struct NumberRow: View {
    let label: String
    @Binding var value: Int
    let range: ClosedRange<Int>
    var body: some View {
        GridRow {
            Text(label)
            HStack(spacing: 4) {
                TextField("", value: Binding(get: { value }, set: { value = min(max($0, range.lowerBound), range.upperBound) }),
                          format: .number)
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 60)
                    .multilineTextAlignment(.trailing)
                Stepper("", value: $value, in: range).labelsHidden()
            }
        }
    }
}
