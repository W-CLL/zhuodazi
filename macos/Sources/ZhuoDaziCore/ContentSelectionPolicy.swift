import Foundation

public enum ContentSelectionPolicy {
    public static let maximumEnabledContent = 10
    public static let maximumEnabledPetSources = 3
    public static let minimumPetSize = 60
    public static let maximumPetSize = 300

    public static func canEnable(
        currentEnabledCount: Int,
        additionalCount: Int,
        maximum: Int
    ) -> Bool {
        additionalCount >= 0 && currentEnabledCount >= 0
            && currentEnabledCount + additionalCount <= maximum
    }

    public static func normalizedIdentifiers(
        _ identifiers: [String],
        validIdentifiers: Set<String>,
        limit: Int
    ) -> [String] {
        var seen = Set<String>()
        return identifiers.filter { identifier in
            !identifier.isEmpty
                && validIdentifiers.contains(identifier)
                && seen.insert(identifier).inserted
        }.prefix(limit).map { $0 }
    }

    public static func mergedWords(
        from packs: [[String: [String]]],
        action: String
    ) -> [String] {
        var seen = Set<String>()
        return packs.flatMap { $0[action] ?? [] }.filter { line in
            !line.isEmpty && seen.insert(line).inserted
        }
    }
}

public enum ConversationPresentationTiming {
    public static func theaterLine(_ text: String?) -> TimeInterval {
        scaledDuration(
            text,
            baseSeconds: 5,
            includedScalars: 6,
            secondsPerScalar: 0.14,
            maximumSeconds: 12
        )
    }

    public static func hallVisit(_ text: String?) -> TimeInterval {
        scaledDuration(
            text,
            baseSeconds: 12,
            includedScalars: 10,
            secondsPerScalar: 0.12,
            maximumSeconds: 20
        )
    }

    private static func scaledDuration(
        _ text: String?,
        baseSeconds: TimeInterval,
        includedScalars: Int,
        secondsPerScalar: TimeInterval,
        maximumSeconds: TimeInterval
    ) -> TimeInterval {
        let count = text?.trimmingCharacters(in: .whitespacesAndNewlines).unicodeScalars.count ?? 0
        let seconds = baseSeconds + Double(max(0, count - includedScalars)) * secondsPerScalar
        return min(maximumSeconds, seconds)
    }
}
