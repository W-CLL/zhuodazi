import Foundation

enum AppVersion {
    static let current = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "2.2.0"

    static func compare(_ left: String, _ right: String) -> ComparisonResult {
        let leftParts = left.split(separator: "-", maxSplits: 1).first!.split(separator: ".").map { Int($0) ?? 0 }
        let rightParts = right.split(separator: "-", maxSplits: 1).first!.split(separator: ".").map { Int($0) ?? 0 }
        for index in 0..<max(leftParts.count, rightParts.count) {
            let lhs = index < leftParts.count ? leftParts[index] : 0
            let rhs = index < rightParts.count ? rightParts[index] : 0
            if lhs < rhs { return .orderedAscending }
            if lhs > rhs { return .orderedDescending }
        }
        return .orderedSame
    }
}
