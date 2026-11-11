import Foundation
import XCTest
@testable import ZhuoDaziCore

final class UninstallCompletionTests: XCTestCase {
    private let eventId = "31df6ca1-05ba-4ea5-bf89-6d1da19d5124"

    private func response(ok: Bool = true, id: String? = nil, state: String = "uninstall_reported",
                          stale: Bool = false, duplicate: Bool = false) throws -> Data {
        try JSONSerialization.data(withJSONObject: [
            "ok": ok, "eventId": id ?? eventId, "deviceState": state,
            "ignoredAsStale": stale, "duplicate": duplicate
        ])
    }

    func testConfirmsCompletedAndIdempotentlyRetriedEvents() throws {
        XCTAssertTrue(ControlledUninstallContract.acknowledgesCompletion(
            try response(), eventId: eventId
        ))
        XCTAssertTrue(ControlledUninstallContract.acknowledgesCompletion(
            try response(id: eventId.uppercased(), duplicate: true), eventId: eventId
        ))
    }

    func testDoesNotTreatStoredStaleEventsOrAnotherEventAsCompletion() throws {
        XCTAssertFalse(ControlledUninstallContract.acknowledgesCompletion(
            try response(state: "installed", stale: true), eventId: eventId
        ))
        XCTAssertFalse(ControlledUninstallContract.acknowledgesCompletion(
            try response(stale: true), eventId: eventId
        ))
        XCTAssertFalse(ControlledUninstallContract.acknowledgesCompletion(
            try response(id: UUID().uuidString), eventId: eventId
        ))
        XCTAssertFalse(ControlledUninstallContract.acknowledgesCompletion(
            try response(ok: false), eventId: eventId
        ))
    }

    func testRejectsNonAcknowledgementsAndOversizedResponses() {
        for data in [Data("{}".utf8), Data("<html>error</html>".utf8), Data(repeating: 0x20, count: 32 * 1024 + 1)] {
            XCTAssertFalse(ControlledUninstallContract.acknowledgesCompletion(data, eventId: eventId))
        }
    }
}
