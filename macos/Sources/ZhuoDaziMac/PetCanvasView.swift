import AppKit

final class PetCanvasView: NSView {
    var dragBegan: ((NSPoint) -> Void)?
    var dragMoved: ((NSPoint) -> Void)?
    var dragEnded: ((NSPoint) -> Void)?
    var clicked: (() -> Void)?

    private let imageView = NSImageView()
    private let bubble = NSTextField(labelWithString: "")

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true

        imageView.frame = NSRect(x: 10, y: 0, width: frameRect.width - 20, height: frameRect.height - 34)
        imageView.imageAlignment = .alignCenter
        imageView.imageScaling = .scaleProportionallyUpOrDown
        imageView.animates = true
        addSubview(imageView)

        bubble.frame = NSRect(x: 8, y: frameRect.height - 34, width: frameRect.width - 16, height: 30)
        bubble.alignment = .center
        bubble.font = .systemFont(ofSize: 12, weight: .medium)
        bubble.textColor = NSColor(calibratedWhite: 0.15, alpha: 1)
        bubble.backgroundColor = NSColor(calibratedWhite: 1, alpha: 0.94)
        bubble.isBezeled = false
        bubble.drawsBackground = true
        bubble.wantsLayer = true
        bubble.layer?.cornerRadius = 6
        bubble.isHidden = true
        addSubview(bubble)

        if let url = Bundle.main.url(forResource: "default-pet", withExtension: "gif", subdirectory: "Pets") {
            imageView.image = NSImage(contentsOf: url)
        } else {
            imageView.image = NSImage(systemSymbolName: "sparkles", accessibilityDescription: "桌搭子")
        }
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func mouseDown(with event: NSEvent) {
        if event.clickCount == 2 {
            clicked?()
            return
        }
        dragBegan?(NSEvent.mouseLocation)
    }

    override func mouseDragged(with event: NSEvent) {
        dragMoved?(NSEvent.mouseLocation)
    }

    override func mouseUp(with event: NSEvent) {
        dragEnded?(NSEvent.mouseLocation)
    }

    func showBubble(_ text: String) {
        bubble.stringValue = text
        bubble.isHidden = false
        NSObject.cancelPreviousPerformRequests(withTarget: self, selector: #selector(hideBubble), object: nil)
        perform(#selector(hideBubble), with: nil, afterDelay: 2.4)
    }

    @objc private func hideBubble() {
        bubble.isHidden = true
    }
}
