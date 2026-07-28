import AppKit
import ImageIO

final class PetCanvasView: NSView {
    var dragBegan: ((NSPoint) -> Void)?
    var dragMoved: ((NSPoint) -> Void)?
    var dragEnded: ((NSPoint) -> Void)?
    var clicked: (() -> Void)?

    private let imageView = NSImageView()
    private let bubble = NSTextField(labelWithString: "")
    private var didDrag = false
    private var animationTimer: Timer?
    private var currentPetImage: NSImage?

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true

        imageView.imageAlignment = .alignCenter
        imageView.imageScaling = .scaleProportionallyUpOrDown
        imageView.animates = true
        imageView.wantsLayer = true
        addSubview(imageView)

        bubble.alignment = .center
        bubble.font = .systemFont(ofSize: 12, weight: .medium)
        bubble.textColor = NSColor(calibratedWhite: 0.15, alpha: 1)
        bubble.backgroundColor = NSColor(calibratedWhite: 1, alpha: 0.94)
        bubble.isBezeled = false
        bubble.drawsBackground = true
        bubble.wantsLayer = true
        bubble.layer?.cornerRadius = 6
        bubble.maximumNumberOfLines = 2
        bubble.lineBreakMode = .byWordWrapping
        bubble.isHidden = true
        addSubview(bubble)

        imageView.image = NSImage(systemSymbolName: "sparkles", accessibilityDescription: "桌搭子")
        layoutContent()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    deinit {
        animationTimer?.invalidate()
    }

    override func mouseDown(with event: NSEvent) {
        didDrag = false
        dragBegan?(NSEvent.mouseLocation)
    }

    override func mouseDragged(with event: NSEvent) {
        didDrag = true
        dragMoved?(NSEvent.mouseLocation)
    }

    override func mouseUp(with event: NSEvent) {
        dragEnded?(NSEvent.mouseLocation)
        if !didDrag { clicked?() }
    }

    override func layout() {
        super.layout()
        layoutContent()
    }

    func showBubble(_ text: String, duration: TimeInterval = 3.2) {
        bubble.stringValue = text
        bubble.isHidden = false
        NSObject.cancelPreviousPerformRequests(withTarget: self, selector: #selector(hideBubble), object: nil)
        perform(#selector(hideBubble), with: nil, afterDelay: duration)
    }

    func showPet(at url: URL) {
        animationTimer?.invalidate()
        animationTimer = nil
        currentPetImage = NSImage(contentsOf: url)
        restartAnimation()

        guard let duration = Self.animationDuration(at: url) else { return }
        let timer = Timer(timeInterval: duration, repeats: true) { [weak self] _ in
            self?.restartAnimation()
        }
        timer.tolerance = min(0.05, duration * 0.02)
        RunLoop.main.add(timer, forMode: .common)
        animationTimer = timer
    }

    func setMirrored(_ mirrored: Bool) {
        imageView.layer?.setAffineTransform(mirrored ? CGAffineTransform(scaleX: -1, y: 1) : .identity)
    }

    private func layoutContent() {
        let bubbleHeight: CGFloat = 48
        imageView.frame = NSRect(x: 8, y: 0, width: max(1, bounds.width - 16), height: max(1, bounds.height - bubbleHeight + 4))
        bubble.frame = NSRect(x: 8, y: max(0, bounds.height - bubbleHeight), width: max(1, bounds.width - 16), height: 42)
    }

    private func restartAnimation() {
        guard let image = currentPetImage else {
            imageView.image = NSImage(systemSymbolName: "sparkles", accessibilityDescription: "桌搭子")
            return
        }
        imageView.animates = false
        for case let representation as NSBitmapImageRep in image.representations {
            representation.setProperty(.loopCount, withValue: NSNumber(value: 0))
            representation.setProperty(.currentFrame, withValue: NSNumber(value: 0))
        }
        imageView.image = image
        imageView.animates = true
    }

    private static func animationDuration(at url: URL) -> TimeInterval? {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        let frameCount = CGImageSourceGetCount(source)
        guard frameCount > 1 else { return nil }

        var duration: TimeInterval = 0
        for index in 0..<frameCount {
            guard let properties = CGImageSourceCopyPropertiesAtIndex(source, index, nil) as? [CFString: Any],
                  let gif = properties[kCGImagePropertyGIFDictionary] as? [CFString: Any] else {
                duration += 0.1
                continue
            }
            let unclamped = (gif[kCGImagePropertyGIFUnclampedDelayTime] as? NSNumber)?.doubleValue
            let clamped = (gif[kCGImagePropertyGIFDelayTime] as? NSNumber)?.doubleValue
            let delay = unclamped ?? clamped ?? 0.1
            duration += delay >= 0.02 ? delay : 0.1
        }
        return max(duration, 0.1)
    }

    @objc private func hideBubble() {
        bubble.isHidden = true
    }
}
