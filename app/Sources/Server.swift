import Foundation
import Network

// 차단 페이지를 보여주는 아주 작은 HTTP 서버. 127.0.0.1 에서만 열린다.
//   GET  /blocked         → 차단 페이지 (브라우저 감시가 분산 사이트 탭을 여기로 보낸다)
//   GET  /status          → 현재 상태 (차단 여부, 남은 시간, 할 일) — 차단 페이지가 읽는다
//   POST /later {text}    → 나중에 할 것 추가

let SERVER_PORT: UInt16 = 47321
private let maxBodyBytes = 64 * 1024

final class StateServer {
    private var listener: NWListener?
    private let store = Store.shared

    func start() {
        let params = NWParameters.tcp
        params.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: NWEndpoint.Port(rawValue: SERVER_PORT)!)
        params.allowLocalEndpointReuse = true
        do {
            listener = try NWListener(using: params)
        } catch {
            NSLog("FocusGuard: listen failed: \(error)")
            return
        }
        listener?.stateUpdateHandler = { state in
            if case .failed(let error) = state { NSLog("FocusGuard: server failed: \(error)") }
        }
        listener?.newConnectionHandler = { [weak self] conn in
            conn.start(queue: .main)
            // 요청을 끝내지 않고 붙잡고 있는 연결은 5초 뒤 끊는다
            DispatchQueue.main.asyncAfter(deadline: .now() + 5) { conn.cancel() }
            self?.receive(conn, buffer: Data())
        }
        listener?.start(queue: .main)
    }

    private func receive(_ conn: NWConnection, buffer: Data) {
        conn.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self] data, _, isComplete, error in
            guard let self else { return }
            var buf = buffer
            if let data { buf.append(data) }
            if let (headers, body) = Self.split(buf) {
                let lines = headers.split(separator: "\r\n", omittingEmptySubsequences: false)
                guard let length = Self.contentLength(lines) else {
                    return self.send(conn, status: "400 Bad Request", type: "text/plain", body: Data())
                }
                guard length <= maxBodyBytes else {
                    return self.send(conn, status: "413 Payload Too Large", type: "text/plain", body: Data())
                }
                if body.count >= length {
                    return self.handle(conn, lines: lines, body: body.prefix(length))
                }
            }
            if isComplete || error != nil || buf.count > maxBodyBytes + 16 * 1024 {
                conn.cancel()
            } else {
                self.receive(conn, buffer: buf)
            }
        }
    }

    private static func split(_ data: Data) -> (String, Data)? {
        guard let range = data.range(of: Data("\r\n\r\n".utf8)) else { return nil }
        return (String(decoding: data[..<range.lowerBound], as: UTF8.self), Data(data[range.upperBound...]))
    }

    /// 헤더 값들 (이름은 대소문자 무시). 값이 비어 있어도 안전하게 "" 를 돌려준다.
    private static func headers(_ name: String, in lines: [Substring]) -> [String] {
        lines.dropFirst().compactMap { line in
            guard let colon = line.firstIndex(of: ":"), line[..<colon].lowercased() == name else { return nil }
            return line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces)
        }
    }

    private static func header(_ name: String, in lines: [Substring]) -> String? { headers(name, in: lines).first }

    /// 본문 길이. 없으면 0, 비었거나·숫자가 아니거나·음수거나·여러 개면 nil (잘못된 요청).
    private static func contentLength(_ lines: [Substring]) -> Int? {
        let values = headers("content-length", in: lines)
        if values.isEmpty { return 0 }
        guard values.count == 1, let n = Int(values[0]), n >= 0 else { return nil }
        return n
    }

    private func handle(_ conn: NWConnection, lines: [Substring], body: Data) {
        let requestLine = String(lines.first ?? "")
        // 다른 웹사이트가 이 서버를 부르지 못하게 한다:
        // Host 가 정확히 우리 주소가 아니면 거부(DNS rebinding 방지), Origin 이 있으면 우리 주소만 허용(CSRF 방지).
        let host = Self.header("host", in: lines)
        let origin = Self.header("origin", in: lines)
        guard host == "127.0.0.1:\(SERVER_PORT)", origin == nil || origin == "http://127.0.0.1:\(SERVER_PORT)" else {
            send(conn, status: "403 Forbidden", type: "text/plain", body: Data())
            return
        }
        let json = (try? JSONSerialization.jsonObject(with: body)) as? [String: Any] ?? [:]
        var status = "200 OK"
        var out: [String: Any] = [:]

        if requestLine.hasPrefix("GET /blocked") {
            send(conn, status: status, type: "text/html", body: Data(blockedPageHTML().utf8))
            return
        } else if requestLine.hasPrefix("GET /status") {
            out = statusJSON()
        } else if requestLine.hasPrefix("POST /later") {
            let text = (json["text"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            if text.isEmpty {
                status = "400 Bad Request"
            } else if !store.canPersist {
                // 저장이 막혀 있으면 받았다고 하지 않는다
                status = "503 Service Unavailable"
                out = ["error": store.saveError ?? L("기록을 저장할 수 없는 상태예요.", "Saving is unavailable right now.")]
            } else {
                store.addLater(text)
                out = statusJSON()
            }
        } else {
            status = "404 Not Found"
        }

        let payload = (try? JSONSerialization.data(withJSONObject: out)) ?? Data("{}".utf8)
        send(conn, status: status, type: "application/json", body: payload)
    }

    private func send(_ conn: NWConnection, status: String, type: String, body: Data) {
        var resp = Data(("HTTP/1.1 \(status)\r\nContent-Type: \(type); charset=utf-8\r\n"
            + "Content-Length: \(body.count)\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n").utf8)
        resp.append(body)
        conn.send(content: resp, completion: .contentProcessed { _ in conn.cancel() })
    }

    private func statusJSON() -> [String: Any] {
        let t = store.timer
        return [
            "phase": t.phase.rawValue,
            "block": store.isBlocking,
            "paused": store.isPaused,
            "remainingSec": store.phase == .idle ? 0 : Int(store.remaining.rounded()),
            "task": t.task,
            "limited": store.limitedToday, // 오늘 제한 시간을 다 쓴 사이트
            "limitMin": store.settings.limitMin,
        ]
    }
}
