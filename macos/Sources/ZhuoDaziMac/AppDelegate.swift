import AppKit

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var petController: PetWindowController!
    private var statusItem: NSStatusItem!
    private var visibilityItem: NSMenuItem!
    private var mouseItem: NSMenuItem!
    private var randomItem: NSMenuItem!

    func applicationDidFinishLaunching(_ notification: Notification) {
        petController = PetWindowController()
        petController.show()
        configureStatusMenu()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    private func configureStatusMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = NSImage(systemSymbolName: "sparkles", accessibilityDescription: "桌搭子")
        statusItem.button?.toolTip = "桌搭子"

        let menu = NSMenu()
        visibilityItem = menu.addItem(withTitle: "隐藏桌搭子", action: #selector(toggleVisibility(_:)), keyEquivalent: "")
        mouseItem = menu.addItem(withTitle: "跟随鼠标", action: #selector(toggleMouseInteraction(_:)), keyEquivalent: "")
        randomItem = menu.addItem(withTitle: "随机移动", action: #selector(toggleRandomMovement(_:)), keyEquivalent: "")
        mouseItem.state = .on
        randomItem.state = .on
        menu.addItem(.separator())
        menu.addItem(withTitle: "退出桌搭子", action: #selector(quit), keyEquivalent: "q")

        for item in menu.items where item.action != nil {
            item.target = self
        }
        statusItem.menu = menu
    }

    @objc private func toggleVisibility(_ sender: NSMenuItem) {
        if petController.isVisible {
            petController.hide()
            visibilityItem.title = "显示桌搭子"
        } else {
            petController.show()
            visibilityItem.title = "隐藏桌搭子"
        }
    }

    @objc private func toggleMouseInteraction(_ sender: NSMenuItem) {
        sender.state = sender.state == .on ? .off : .on
        petController.mouseInteractionEnabled = sender.state == .on
    }

    @objc private func toggleRandomMovement(_ sender: NSMenuItem) {
        sender.state = sender.state == .on ? .off : .on
        petController.randomMovementEnabled = sender.state == .on
    }

    @objc private func quit() {
        NSApplication.shared.terminate(nil)
    }
}
