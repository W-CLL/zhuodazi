public final class RandomBag<Element: Hashable> {
    private var pool: [Element] = []
    private var remaining: [Element] = []

    public init() {}

    public func next(from elements: [Element], excluding excluded: Element? = nil) -> Element? {
        guard !elements.isEmpty else {
            pool = []
            remaining = []
            return nil
        }

        ensurePool(elements)
        if let selected = takeRandom(excluding: excluded) {
            return selected
        }

        remaining = pool
        if let selected = takeRandom(excluding: excluded) {
            return selected
        }

        return remaining.removeFirst()
    }

    private func ensurePool(_ elements: [Element]) {
        var seen = Set<Element>()
        let nextPool = elements.filter { seen.insert($0).inserted }
        guard pool != nextPool else { return }
        pool = nextPool
        remaining = nextPool
    }

    private func takeRandom(excluding excluded: Element?) -> Element? {
        let candidateIndices = remaining.indices.filter { remaining[$0] != excluded }
        var generator = SystemRandomNumberGenerator()
        guard let selectedIndex = candidateIndices.randomElement(using: &generator) else { return nil }
        return remaining.remove(at: selectedIndex)
    }
}
