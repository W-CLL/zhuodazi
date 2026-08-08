import XCTest
@testable import ZhuoDaziCore

final class RandomBagTests: XCTestCase {
    func testEachCycleUsesEveryItemExactlyOnce() {
        let files = (1...262).map { "pet-\($0)" }
        let bag = RandomBag<String>()

        let firstCycle = takeCycle(bag, files: files)
        XCTAssertEqual(Set(firstCycle).count, files.count)

        let secondCycle = takeCycle(bag, files: files, previous: firstCycle.last)
        XCTAssertEqual(Set(secondCycle).count, files.count)
        XCTAssertNotEqual(secondCycle.first, firstCycle.last)
    }

    func testSingleItemLibraryReturnsItsOnlyItem() {
        let bag = RandomBag<String>()
        XCTAssertEqual(bag.next(from: ["only"], excluding: "only"), "only")
    }

    func testChangingLibraryDropsPreviousItems() {
        let bag = RandomBag<String>()
        _ = bag.next(from: ["old-a", "old-b"])

        let newFiles = ["new-a", "new-b", "new-c"]
        let selections = takeCycle(bag, files: newFiles)
        XCTAssertEqual(Set(selections), Set(newFiles))
    }

    private func takeCycle(
        _ bag: RandomBag<String>,
        files: [String],
        previous initialPrevious: String? = nil
    ) -> [String] {
        var previous = initialPrevious
        return files.map { _ in
            let selected = bag.next(from: files, excluding: previous)
            XCTAssertNotNil(selected)
            if files.count > 1 {
                XCTAssertNotEqual(selected, previous)
            }
            previous = selected
            return selected!
        }
    }
}
