import Foundation

/// A delayed event may finish only the presentation that created it.
public struct PresentationLifetime {
    public struct Token: Equatable, Sendable {
        fileprivate let id = UUID()
    }

    private var current: Token?

    public init() {}

    public mutating func begin() -> Token {
        let token = Token()
        current = token
        return token
    }

    public func isCurrent(_ token: Token) -> Bool { current == token }

    @discardableResult
    public mutating func finish(_ token: Token) -> Bool {
        guard isCurrent(token) else { return false }
        current = nil
        return true
    }

    public mutating func invalidate() { current = nil }
}
