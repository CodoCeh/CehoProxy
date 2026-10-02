import AppKit

enum Look {
    case guarded, starting, off, trouble, stopped, locked
}

enum Badge {
    case disc, half, ring, slash, square, lock
}

enum Outcome {
    case ok, needPassword, waiting, unreachable
}

struct Snapshot {
    let running: Bool
    let starting: Bool
    var recovering: Bool = false
    let daemon: Bool
    var exitCountry: String?
    var exitIp: String?
    var exitProbed: Bool
    var trayControls: Bool
}

struct Reading {
    let outcome: Outcome
    let snapshot: Snapshot?
    let waitSeconds: Int
}

struct Phrase {
    let ru: String
    let en: String
}

let phrases: [String: Phrase] = [
    "btn_on": Phrase(ru: "Включить", en: "Turn on"),
    "btn_off": Phrase(ru: "Выключить", en: "Turn off"),
    "tray_panel": Phrase(ru: "Открыть панель", en: "Open the panel"),
    "tray_quit": Phrase(ru: "Выход", en: "Quit"),
    "state_on": Phrase(ru: "Защита включена", en: "Protection is on"),
    "state_starting": Phrase(
        ru: "Защита запускается — туннель ещё поднимается",
        en: "Protection is starting — the tunnel is still coming up"),
    "state_recovering": Phrase(
        ru: "Восстанавливаю защиту — программы из списка пока без интернета",
        en: "Restoring protection — the listed apps have no internet until then"),
    "state_off": Phrase(ru: "Защита выключена", en: "Protection is off"),
    "hero_no_exit": Phrase(ru: "Нет связи с VPN", en: "No connection to the VPN"),
    "state_no_exit": Phrase(
        ru: "ноды не отвечают, соединения программ рвутся",
        en: "no node responds, app connections are dropped"),
    "tray_no_service": Phrase(ru: "Служба не запущена", en: "The service is not running"),
    "tray_no_service_hint": Phrase(
        ru: "Включить защиту может только администратор: sudo chp daemon",
        en: "Only an administrator can turn protection on: sudo chp daemon"),
    "tray_need_password": Phrase(ru: "Нужен пароль", en: "Password required"),
    "tray_sign_in": Phrase(ru: "Ввести пароль…", en: "Enter the password…"),
    "tray_password_hint": Phrase(
        ru: "Пока пароль не введён, значок ничего не показывает и не переключает",
        en: "Until the password is entered the icon shows nothing and switches nothing"),
    "tray_password_prompt": Phrase(
        ru: "Введите пароль панели CehoProxy. Он остаётся только в памяти этого сеанса.",
        en: "Enter the CehoProxy panel password. It stays only in this session's memory."),
    "tray_wait": Phrase(ru: "Панель закрыта ещё {0} с", en: "The panel stays closed for another {0} s"),
    "tray_wait_hint": Phrase(
        ru: "Столько просит подождать сама панель после неверных попыток",
        en: "The panel itself asks to wait this long after failed attempts"),
    "tray_failed": Phrase(ru: "Панель не приняла команду", en: "The panel did not accept the command"),
    "exit_is": Phrase(ru: "выход: {0} · {1}", en: "exit: {0} · {1}"),
    "notice_off": Phrase(
        ru: "Защита выключилась. Программы из списка идут без VPN или без интернета.",
        en: "Protection turned off. The listed apps go without the VPN or without internet."),
    "notice_trouble": Phrase(
        ru: "Нет связи с VPN: ноды не отвечают. Программы из списка пока без интернета.",
        en: "No connection to the VPN: nodes do not answer. The listed apps have no internet for now."),
    "notice_service_gone": Phrase(ru: "Служба CehoProxy остановилась.", en: "The CehoProxy service stopped."),
    "notice_exit_changed": Phrase(ru: "Выход сменился: {0} → {1}", en: "The exit changed: {0} → {1}"),
    "auth_enter": Phrase(ru: "Войти", en: "Sign in"),
    "auth_wrong": Phrase(ru: "Неверный пароль.", en: "Wrong password."),
    "btn_cancel": Phrase(ru: "Отмена", en: "Cancel"),
]

func say(_ lang: String, _ key: String, _ args: String...) -> String {
    guard let phrase = phrases[key] else { return key }
    var text = lang == "en" ? phrase.en : phrase.ru
    for (index, value) in args.enumerated() {
        text = text.replacingOccurrences(of: "{\(index)}", with: value)
    }
    return text
}

let pollSeconds = 5.0
let maxWaitSeconds = 300

final class NoRedirect: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask,
                    willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest,
                    completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }
}

final class PanelLink {
    private let root: String
    private let session: URLSession
    private let noRedirect = NoRedirect()

    var password: String?

    init(root: String) {
        self.root = root
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 30
        config.httpShouldSetCookies = true
        config.httpCookieAcceptPolicy = .always
        session = URLSession(configuration: config, delegate: noRedirect, delegateQueue: nil)
    }

    var port: Int? {
        guard let text = try? String(contentsOfFile: root + "/panel.port", encoding: .utf8) else {
            return nil
        }
        return Int(text.split(whereSeparator: { $0 == " " || $0 == "\n" }).first.map(String.init) ?? "")
    }

    var url: String? { port.map { "http://127.0.0.1:\($0)" } }

    private func post(_ path: String, _ body: String, _ type: String, _ password: String?,
                      _ done: @escaping (Int, Data?, String?) -> Void) {
        guard let base = url, let target = URL(string: base + path) else {
            done(0, nil, nil)
            return
        }
        var request = URLRequest(url: target)
        request.httpMethod = "POST"
        request.setValue(type, forHTTPHeaderField: "Content-Type")
        if let password = password {
            if password.allSatisfy({ $0.isASCII }) {
                request.setValue(password, forHTTPHeaderField: "X-Ceho-Password")
            }
            request.setValue(Data(password.utf8).base64EncodedString(), forHTTPHeaderField: "X-Ceho-Password-B64")
        }
        request.httpBody = body.data(using: .utf8)
        session.dataTask(with: request) { data, response, _ in
            let http = response as? HTTPURLResponse
            done(http?.statusCode ?? 0, data, http?.value(forHTTPHeaderField: "Retry-After"))
        }.resume()
    }

    func status(quick: Bool, _ done: @escaping (Reading) -> Void) {
        let argv = quick ? "status\n--json\n--quick" : "status\n--json"
        post("/api", argv, "text/plain; charset=utf-8", password ?? "") { code, data, retryAfter in
            switch code {
            case 200:
                done(Reading(outcome: .ok, snapshot: PanelLink.parse(data, probed: !quick),
                             waitSeconds: 0))
            case 401:
                done(Reading(outcome: .needPassword, snapshot: nil, waitSeconds: 0))
            case 429:
                done(Reading(outcome: .waiting, snapshot: nil,
                             waitSeconds: PanelLink.retryAfter(retryAfter)))
            default:
                done(Reading(outcome: .unreachable, snapshot: nil, waitSeconds: 0))
            }
        }
    }

    static func retryAfter(_ header: String?) -> Int {
        guard let value = Int(header?.trimmingCharacters(in: .whitespaces) ?? "") else {
            return Int(pollSeconds)
        }
        return min(max(value, 1), maxWaitSeconds)
    }

    func language(_ done: @escaping (String?) -> Void) {
        post("/api", "lang", "text/plain; charset=utf-8", password ?? "") { code, data, _ in
            guard code == 200, let data = data,
                  let wrapper = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let text = wrapper["text"] as? String else {
                done(nil)
                return
            }
            done(text.trimmingCharacters(in: .whitespacesAndNewlines) == "en" ? "en" : "ru")
        }
    }

    func control(_ path: String, _ done: @escaping (Bool) -> Void) {
        let act = { [weak self] in
            self?.post(path, "tab=state", "application/x-www-form-urlencoded", nil) { code, _, _ in
                done(code == 303)
            }
        }
        guard let password = password, !password.isEmpty else {
            act()
            return
        }
        let escaped = password.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? password
        post("/login", "password=" + escaped, "application/x-www-form-urlencoded", nil) { code, _, _ in
            if code == 303 { act() } else { done(false) }
        }
    }

    private static func parse(_ data: Data?, probed: Bool) -> Snapshot? {
        guard let data = data,
              let wrapper = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let text = wrapper["text"] as? String,
              let inner = text.data(using: .utf8),
              let state = try? JSONSerialization.jsonObject(with: inner) as? [String: Any],
              let daemon = state["daemon"] as? Bool else {
            return nil
        }
        return Snapshot(
            running: state["running"] as? Bool ?? false,
            starting: (state["starting"] as? Bool ?? false) || (state["recovering"] as? Bool ?? false),
            recovering: state["recovering"] as? Bool ?? false,
            daemon: daemon,
            exitCountry: state["exitCountry"] as? String,
            exitIp: state["exitIp"] as? String,
            exitProbed: probed,
            trayControls: state["trayControls"] as? Bool ?? false)
    }
}

final class Tray: NSObject, NSMenuDelegate {
    let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    let link: PanelLink
    private let menu = NSMenu()
    private var lang = "ru"
    private var langKnown = false
    private var passwordProved = false
    private var look = Look.stopped
    private var noticeStarted = false
    private var noticeLost = false
    private var noticeLast = Look.stopped
    private var noticeCountry: String?
    private var snapshot: Snapshot?
    private var resumeAt = Date.distantPast
    private var lastRead = Date.distantPast
    private var failure: String?
    private var working = false
    private var timer: Timer?
    private let root: String
    private var goneChecks = 0

    init(root: String) {
        self.root = root
        link = PanelLink(root: root)
        super.init()
        menu.delegate = self
        menu.autoenablesItems = false
        item.menu = menu
        paint()
        read()
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            self?.tick()
        }
    }

    private var waitSeconds: Int {
        let left = resumeAt.timeIntervalSinceNow
        return left > 0 ? Int(left.rounded(.up)) : 0
    }

    private func tick() {
        if waitSeconds > 0 {
            paint()
            return
        }
        if resumeAt > Date.distantPast {
            resumeAt = Date.distantPast
            paint()
        }
        if Date().timeIntervalSince(lastRead) >= pollSeconds { read() }
    }

    func read() {
        lastRead = Date()
        link.status(quick: true) { [weak self] reading in
            guard let self = self else { return }
            guard reading.outcome == .ok, let fresh = reading.snapshot else {
                self.accept(reading)
                return
            }
            let merged = Tray.remember(fresh, self.snapshot)
            guard merged.running, !merged.exitProbed else {
                self.accept(Reading(outcome: .ok, snapshot: merged, waitSeconds: 0))
                return
            }
            self.link.status(quick: false) { full in
                let seen = full.outcome == .ok ? (full.snapshot ?? merged) : merged
                self.accept(Reading(outcome: .ok, snapshot: seen, waitSeconds: 0))
            }
        }
    }

    private static func remember(_ fresh: Snapshot, _ previous: Snapshot?) -> Snapshot {
        guard fresh.running, !fresh.exitProbed,
              let previous = previous, previous.running, previous.exitProbed else {
            return fresh
        }
        var merged = fresh
        merged.exitCountry = previous.exitCountry
        merged.exitIp = previous.exitIp
        merged.exitProbed = true
        return merged
    }

    private func accept(_ reading: Reading) {
        DispatchQueue.main.async {
            switch reading.outcome {
            case .waiting:
                self.resumeAt = Date().addingTimeInterval(Double(reading.waitSeconds))
                if !self.passwordProved { self.link.password = nil }
                self.snapshot = nil
                self.look = .locked
            case .needPassword:
                self.link.password = nil
                self.passwordProved = false
                self.snapshot = nil
                self.look = .locked
            case .ok:
                if self.link.password != nil { self.passwordProved = true }
                self.snapshot = reading.snapshot
                self.look = Tray.lookOf(reading.snapshot)
                if !self.langKnown {
                    self.langKnown = true
                    self.link.language { value in
                        guard let value = value else { return }
                        DispatchQueue.main.async {
                            self.lang = value
                            self.paint()
                        }
                    }
                }
            case .unreachable:
                self.langKnown = false
                self.snapshot = nil
                self.look = .stopped
                if !FileManager.default.fileExists(atPath: self.root + "/cehoproxy") {
                    self.goneChecks += 1
                    if self.goneChecks >= 3 { DispatchQueue.main.async { NSApp.terminate(nil) } }
                }
            }
            if case .unreachable = reading.outcome {} else { self.goneChecks = 0 }
            if let notice = self.nextNotice() { Tray.notify(notice) }
            self.paint()
        }
    }

    private func nextNotice() -> String? {
        let country = look == .guarded ? snapshot?.exitCountry : nil
        var notice: String?
        if !noticeStarted || look == .locked || noticeLast == .locked {
            noticeStarted = true
        } else if look == .guarded {
            if noticeLost {
                notice = say(lang, "state_on")
                if let ip = snapshot?.exitIp, !ip.isEmpty {
                    notice! += " · " + say(lang, "exit_is", snapshot?.exitCountry ?? "?", ip)
                }
            } else if noticeLast == .guarded, let was = noticeCountry, let now = country, was != now {
                notice = say(lang, "notice_exit_changed", was, now)
            }
            noticeLost = false
        } else if !noticeLost && (noticeLast == .guarded || noticeLast == .starting) {
            switch look {
            case .off where noticeLast == .guarded: notice = say(lang, "notice_off")
            case .trouble: notice = say(lang, "notice_trouble")
            case .stopped: notice = say(lang, "notice_service_gone")
            case .starting where snapshot?.recovering == true: notice = say(lang, "state_recovering")
            default: break
            }
            if notice != nil { noticeLost = true }
        }
        noticeLast = look
        if let country = country { noticeCountry = country }
        return notice
    }

    private static func notify(_ text: String) {
        let quoted = text.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        task.arguments = ["-e", "display notification \"\(quoted)\" with title \"CehoProxy\""]
        try? task.run()
    }

    private static func lookOf(_ snapshot: Snapshot?) -> Look {
        guard let snapshot = snapshot, snapshot.daemon else { return .stopped }
        if snapshot.running && snapshot.exitProbed && snapshot.exitIp == nil { return .trouble }
        if snapshot.running { return .guarded }
        if snapshot.starting { return .starting }
        return .off
    }

    private func stateText() -> String {
        switch look {
        case .guarded: return say(lang, "state_on")
        case .starting: return say(lang, snapshot?.recovering == true ? "state_recovering" : "state_starting")
        case .off: return say(lang, "state_off")
        case .trouble: return say(lang, "hero_no_exit")
        case .stopped: return say(lang, "tray_no_service")
        case .locked:
            let wait = waitSeconds
            return wait > 0 ? say(lang, "tray_wait", String(wait)) : say(lang, "tray_need_password")
        }
    }

    private func hint() -> String? {
        switch look {
        case .stopped: return say(lang, "tray_no_service_hint")
        case .trouble: return say(lang, "state_no_exit")
        case .locked: return say(lang, waitSeconds > 0 ? "tray_wait_hint" : "tray_password_hint")
        default: return nil
        }
    }

    private func badge() -> Badge {
        switch look {
        case .guarded: return .disc
        case .starting: return .half
        case .off: return .ring
        case .trouble: return .slash
        case .stopped: return .square
        case .locked: return .lock
        }
    }

    private func colour() -> NSColor {
        switch look {
        case .guarded: return NSColor(srgbRed: 0.18, green: 0.77, blue: 0.42, alpha: 1)
        case .starting: return NSColor(srgbRed: 0.96, green: 0.65, blue: 0.14, alpha: 1)
        case .off, .trouble: return NSColor(srgbRed: 0.88, green: 0.31, blue: 0.24, alpha: 1)
        default: return NSColor(srgbRed: 0.54, green: 0.56, blue: 0.60, alpha: 1)
        }
    }

    private func paint() {
        guard let button = item.button else { return }
        button.image = mark()
        var tip = "CehoProxy — " + stateText()
        if look == .guarded, let ip = snapshot?.exitIp {
            tip += " · " + say(lang, "exit_is", snapshot?.exitCountry ?? "?", ip)
        }
        button.toolTip = tip
    }

    private func mark() -> NSImage {
        let side: CGFloat = 18
        let image = NSImage(size: NSSize(width: side, height: side))
        image.lockFocus()
        Tray.brand()?.draw(in: NSRect(x: 0, y: 0, width: side, height: side),
                           from: .zero, operation: .sourceOver, fraction: 1)
        draw(NSRect(x: side - 9, y: 0, width: 9, height: 9))
        image.unlockFocus()
        return image
    }

    private func draw(_ box: NSRect) {
        let dark = NSColor(srgbRed: 0.06, green: 0.08, blue: 0.09, alpha: 0.94)
        let tint = colour()
        switch badge() {
        case .disc:
            tint.setFill()
            NSBezierPath(ovalIn: box).fill()
            stroke(NSBezierPath(ovalIn: box.insetBy(dx: 0.5, dy: 0.5)), dark, 1)
        case .half:
            dark.setFill()
            NSBezierPath(ovalIn: box).fill()
            let half = NSBezierPath()
            half.appendArc(withCenter: NSPoint(x: box.midX, y: box.midY),
                           radius: box.width / 2 - 0.5, startAngle: 180, endAngle: 360)
            half.close()
            tint.setFill()
            half.fill()
            stroke(NSBezierPath(ovalIn: box.insetBy(dx: 0.5, dy: 0.5)), dark, 1)
        case .ring:
            dark.setFill()
            NSBezierPath(ovalIn: box).fill()
            stroke(NSBezierPath(ovalIn: box.insetBy(dx: 1.5, dy: 1.5)), tint, 2)
        case .slash:
            tint.setFill()
            NSBezierPath(ovalIn: box).fill()
            stroke(NSBezierPath(ovalIn: box.insetBy(dx: 0.5, dy: 0.5)), dark, 1)
            let line = NSBezierPath()
            line.move(to: NSPoint(x: box.minX + box.width * 0.25, y: box.minY + box.height * 0.25))
            line.line(to: NSPoint(x: box.maxX - box.width * 0.25, y: box.maxY - box.height * 0.25))
            stroke(line, NSColor.white, 1.6)
        case .square:
            tint.setFill()
            NSBezierPath(rect: box).fill()
            stroke(NSBezierPath(rect: box.insetBy(dx: 0.5, dy: 0.5)), dark, 1)
        case .lock:
            tint.setFill()
            NSBezierPath(ovalIn: box).fill()
            stroke(NSBezierPath(ovalIn: box.insetBy(dx: 0.5, dy: 0.5)), dark, 1)
            NSColor.white.setFill()
            NSBezierPath(rect: NSRect(x: box.minX + box.width * 0.28, y: box.minY + box.height * 0.22,
                                      width: box.width * 0.44, height: box.height * 0.32)).fill()
            let shackle = NSBezierPath()
            shackle.appendArc(withCenter: NSPoint(x: box.midX, y: box.minY + box.height * 0.54),
                              radius: box.width * 0.17, startAngle: 0, endAngle: 180)
            stroke(shackle, NSColor.white, 1.4)
        }
    }

    private func stroke(_ path: NSBezierPath, _ colour: NSColor, _ width: CGFloat) {
        colour.setStroke()
        path.lineWidth = width
        path.stroke()
    }

    private static var cachedBrand: NSImage?

    private static func brand() -> NSImage? {
        if let ready = cachedBrand { return ready }
        var places: [String] = []
        if let resource = Bundle.main.resourcePath { places.append(resource + "/cehoproxy.png") }
        places.append(
            URL(fileURLWithPath: CommandLine.arguments[0])
                .deletingLastPathComponent().path + "/cehoproxy.png")
        for place in places where FileManager.default.fileExists(atPath: place) {
            cachedBrand = NSImage(contentsOfFile: place)
            return cachedBrand
        }
        return nil
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()

        let state = NSMenuItem(title: stateText(), action: nil, keyEquivalent: "")
        state.isEnabled = false
        menu.addItem(state)

        if let note = failure ?? hint() {
            let line = NSMenuItem(title: note, action: nil, keyEquivalent: "")
            line.isEnabled = false
            menu.addItem(line)
        }

        menu.addItem(NSMenuItem.separator())

        if look == .locked {
            let sign = NSMenuItem(
                title: say(lang, "tray_sign_in"), action: #selector(signIn), keyEquivalent: "")
            sign.target = self
            sign.isEnabled = waitSeconds == 0
            menu.addItem(sign)
        } else if snapshot?.trayControls == true {
            let on = NSMenuItem(title: say(lang, "btn_on"), action: #selector(turnOn), keyEquivalent: "")
            on.target = self
            on.isEnabled = look == .off && !working
            menu.addItem(on)

            let off = NSMenuItem(title: say(lang, "btn_off"), action: #selector(turnOff), keyEquivalent: "")
            off.target = self
            off.isEnabled = (look == .guarded || look == .starting || look == .trouble) && !working
            menu.addItem(off)
        }

        if look == .locked || snapshot?.trayControls == true {
            menu.addItem(NSMenuItem.separator())
        }

        let panel = NSMenuItem(
            title: say(lang, "tray_panel"), action: #selector(openPanel), keyEquivalent: "")
        panel.target = self
        panel.isEnabled = link.url != nil
        menu.addItem(panel)

        menu.addItem(NSMenuItem.separator())

        let quit = NSMenuItem(title: say(lang, "tray_quit"), action: #selector(quit), keyEquivalent: "")
        quit.target = self
        menu.addItem(quit)
    }

    @objc func signIn() {
        guard let entered = ask() else { return }
        link.password = entered
        failure = nil
        link.status(quick: true) { [weak self] reading in
            guard let self = self else { return }
            if reading.outcome == .needPassword {
                DispatchQueue.main.async { self.failure = say(self.lang, "auth_wrong") }
            }
            self.accept(reading)
        }
    }

    private func ask() -> String? {
        let alert = NSAlert()
        alert.messageText = "CehoProxy"
        alert.informativeText = say(lang, "tray_password_prompt")
        alert.addButton(withTitle: say(lang, "auth_enter"))
        alert.addButton(withTitle: say(lang, "btn_cancel"))
        let field = NSSecureTextField(frame: NSRect(x: 0, y: 0, width: 280, height: 24))
        alert.accessoryView = field
        alert.window.initialFirstResponder = field
        NSApp.activate(ignoringOtherApps: true)
        guard alert.runModal() == .alertFirstButtonReturn else { return nil }
        return field.stringValue.isEmpty ? nil : field.stringValue
    }

    @objc func turnOn() { command("/control/start") }

    @objc func turnOff() { command("/control/stop") }

    private func command(_ path: String) {
        if working { return }
        working = true
        failure = nil
        link.control(path) { [weak self] done in
            guard let self = self else { return }
            DispatchQueue.main.async {
                self.working = false
                if !done { self.failure = say(self.lang, "tray_failed") }
                self.read()
            }
        }
    }

    @objc func openPanel() {
        guard let url = link.url, let target = URL(string: url) else { return }
        NSWorkspace.shared.open(target)
    }

    @objc func quit() {
        NSApp.terminate(nil)
    }
}

let root = ProcessInfo.processInfo.environment["CEHOPROXY_HOME"]
    ?? "/Library/Application Support/CehoProxy"
let application = NSApplication.shared
application.setActivationPolicy(.accessory)
let tray = Tray(root: root)
application.run()
