import AppKit
import Darwin

/// Run with scripts/test-display-lifecycle.sh in a logged-in macOS desktop session.
/// Compiles the actual canvas and interaction controller without starting the app,
/// connecting to services, or reading/writing account settings.
@main
struct DisplayLifecycleSmoke {
    enum Failure: Error { case expectation(String) }

    @MainActor
    static func require(_ condition: @autoclosure () -> Bool, _ message: String) throws {
        if !condition() { throw Failure.expectation(message) }
    }

    @MainActor
    static func buttons(in view: NSView) -> [NSButton] {
        (view as? NSButton).map { [$0] } ?? view.subviews.flatMap { buttons(in: $0) }
    }

    @MainActor
    static func click(_ button: NSButton) {
        if let action = button.action { NSApp.sendAction(action, to: button.target, from: button) }
    }

    @MainActor
    static func runChecks() async throws {
        let canvas = PetCanvasView(frame: NSRect(x: 0, y: 0, width: 180, height: 210))
        guard let bubble = canvas.subviews.compactMap({ $0 as? NSTextField }).first else {
            throw Failure.expectation("Canvas did not create its bubble")
        }
        let oldBubble = canvas.showBubble("old scene", duration: 0.04)
        canvas.showBubble("new scene", duration: 0.25)
        canvas.dismissBubble(ifCurrent: oldBubble)
        try require(!bubble.isHidden && bubble.stringValue == "new scene", "Old scene cleanup hid the replacement bubble")
        try await Task.sleep(for: .milliseconds(100))
        try require(!bubble.isHidden, "Old timer hid the replacement bubble")
        try await Task.sleep(for: .milliseconds(220))
        try require(bubble.isHidden, "The current bubble did not expire")

        let parent = NSPanel(contentRect: NSRect(x: 100, y: 100, width: 180, height: 210),
                             styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        parent.contentView = canvas
        parent.orderFrontRegardless()
        let panel = PetInteractionPanelController()
        defer { panel.dismiss(notifying: false); parent.orderOut(nil) }
        var firstResponses = 0
        var secondResponses = 0
        panel.present(title: "Question", message: "Show the next card", choices: [PetInteractionChoice("Next", "next")], relativeTo: parent) { _ in
            firstResponses += 1
            panel.present(title: "Answer", message: "This must remain visible", choices: [PetInteractionChoice("Done", "done")], relativeTo: parent) { _ in
                secondResponses += 1
            }
        }
        guard let firstContent = panel.window?.contentView,
              let oldChoice = buttons(in: firstContent).first(where: { $0.title == "Next" }),
              let oldClose = buttons(in: firstContent).first(where: { $0.toolTip == "稍后再说" }) else {
            throw Failure.expectation("Question controls were not created")
        }
        click(oldChoice)
        try require(firstResponses == 1 && panel.window?.isVisible == true, "Answer card did not open")
        click(oldChoice)
        click(oldClose)
        try require(secondResponses == 0 && panel.window?.isVisible == true, "An old button acted on the new card")
        panel.updateLevel(.floating)
        parent.setFrameOrigin(NSPoint(x: 220, y: 180))
        panel.reposition(relativeTo: parent)
        try require(panel.window?.isVisible == true && secondResponses == 0, "Appearance refresh dismissed the interaction")

        var replacementResponses = 0
        panel.present(title: "Replacement", message: "Replacement must not count as a response", choices: [], relativeTo: parent) { _ in
            replacementResponses += 1
        }
        try require(secondResponses == 0, "Programmatic replacement invoked the previous card's response")
        guard let replacementContent = panel.window?.contentView,
              let currentClose = buttons(in: replacementContent).first(where: { $0.toolTip == "稍后再说" }) else {
            throw Failure.expectation("Replacement close control was not created")
        }
        click(currentClose)
        click(currentClose)
        try require(replacementResponses == 1 && panel.window?.isVisible == false, "Current close must complete exactly once")
    }

    @MainActor
    static func main() {
        let app = NSApplication.shared
        app.setActivationPolicy(.accessory)
        Task { @MainActor in
            do {
                try await runChecks()
                print("PASS: stale bubble timers, scene cleanup, reentrant answer cards, stale buttons, appearance refresh, and close-once")
                exit(EXIT_SUCCESS)
            } catch {
                print("FAIL: \(error)")
                exit(EXIT_FAILURE)
            }
        }
        app.run()
    }
}
