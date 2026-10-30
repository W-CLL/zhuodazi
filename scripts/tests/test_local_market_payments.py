from __future__ import annotations

from contextlib import closing
from decimal import Decimal
import importlib.util
import io
import json
from pathlib import Path
import sqlite3
from types import SimpleNamespace
import sys
import tempfile
import unittest
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location(
    "local_market_payments",
    Path(__file__).resolve().parents[1] / "local_market_payments.py",
)
payments = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = payments
SPEC.loader.exec_module(payments)


class FakeClock:
    def __init__(self, value=1_800_000_000.0):
        self.value = float(value)

    def __call__(self):
        return self.value


class FakeReader:
    def __init__(self, notices=(), transfers=(), *, notice_results=None, transfer_results=None):
        self.items = list(notices)
        self.transfer_items = list(transfers)
        self.notice_results = None if notice_results is None else list(notice_results)
        self.transfer_results = None if transfer_results is None else list(transfer_results)
        self.notice_limits = []
        self.transfer_limits = []

    @staticmethod
    def _result(sequence, fallback, limit):
        result = sequence.pop(0) if sequence else fallback
        if isinstance(result, BaseException):
            raise result
        return list(result)[:limit]

    def notices(self, *, limit):
        self.notice_limits.append(limit)
        return self._result(self.notice_results, self.items, limit)

    def transfers(self, *, limit):
        self.transfer_limits.append(limit)
        return self._result(self.transfer_results, self.transfer_items, limit)


class BusyError(Exception):
    pass


class FakeSender:
    def __init__(self, results=()):
        self.results = list(results)
        self.payloads = []

    def send(self, payload):
        self.payloads.append(dict(payload))
        return self.results.pop(0) if self.results else (True, "")


def notice(
    key,
    *,
    account="wxid_collection",
    title="你有一笔收款入账",
    detail="金额：￥2.80，备注：123AB",
    direction="incoming",
    category="receipt",
    status="completed",
    amount=Decimal("2.80"),
    currency="CNY",
    transaction="tx_123",
    created=1_700_000_000,
    official=False,
    conversation="",
):
    event = SimpleNamespace(
        account_id=account,
        create_time=created,
        is_official_message=official,
        conversation=conversation,
    )
    return SimpleNamespace(
        event=event,
        event_key=key,
        title=title,
        detail=detail,
        direction=direction,
        category=category,
        status=status,
        amount=amount,
        currency=currency,
        transaction_id=transaction,
    )


def transfer(
    key,
    *,
    account="wxid_collection",
    conversation="wxid_payer",
    kind="transfer",
    amount=Decimal("0.01"),
    memo="123ab",
    payer="wxid_payer",
    receiver="wxid_collection",
    direction="incoming",
    transaction="tx_transfer_123",
    transfer_id="transfer_123",
    pay_subtype="1",
    created=1_700_000_000,
    sender="",
    msg_type=49,
    source_kind="chat",
):
    event = SimpleNamespace(
        account_id=account,
        create_time=created,
        conversation=conversation,
        direction="incoming",
        is_group=conversation.endswith("@chatroom"),
        sender=sender,
        msg_type=msg_type,
        source_kind=source_kind,
    )
    return SimpleNamespace(
        event=event,
        event_key=key,
        kind=kind,
        amount=amount,
        memo=memo,
        payer=payer,
        receiver=receiver,
        direction=direction,
        transfer_id=transfer_id,
        transaction_id=transaction,
        pay_subtype=pay_subtype,
    )


class LocalMarketPaymentTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.root = Path(self.folder.name)
        self.state_path = self.root / "bridge.sqlite3"
        self.binding = "a" * 64

    def store(self):
        store = payments.StateStore(self.state_path, self.binding)
        self.addCleanup(store.close)
        return store

    def test_reference_requires_explicit_label_exact_shape_and_is_case_insensitive(self):
        self.assertEqual(payments.extract_explicit_reference("金额 2.8 元；备注：123ab"), "123AB")
        self.assertEqual(payments.extract_explicit_reference("备注: 007Zx\n"), "007ZX")
        self.assertEqual(payments.extract_explicit_reference("金额￥0.01\n付款方备注123ab\n汇总"), "123AB")
        self.assertEqual(payments.extract_explicit_reference("付款方备注：007zX"), "007ZX")
        self.assertEqual(payments.extract_explicit_reference("金额￥0.01\r\n付款方备注123ab\r\n汇总"), "123AB")
        self.assertEqual(payments.extract_explicit_reference("订单 123AB"), "")
        self.assertEqual(payments.extract_explicit_reference("付款码：123AB"), "")
        self.assertEqual(payments.extract_explicit_reference("说明付款方备注123AB"), "")
        self.assertEqual(payments.extract_explicit_reference("伪付款方备注123AB"), "")
        self.assertEqual(payments.extract_explicit_reference("伪付款方备注：123AB"), "")
        self.assertEqual(payments.extract_explicit_reference("付款方备注订单备注：123AB"), "")
        self.assertEqual(payments.extract_explicit_reference("付款方备注123ABC"), "")
        self.assertEqual(payments.extract_explicit_reference("备注：123ABC"), "")
        self.assertEqual(payments.extract_explicit_reference("备注：12ABC"), "")
        self.assertEqual(
            payments.extract_explicit_reference("备注：123AB", "备注：456CD"),
            "",
            "conflicting explicit references must not be guessed",
        )
        self.assertEqual(
            payments.extract_explicit_reference("付款方备注123AB\n付款方备注456CD"),
            "",
            "conflicting payer references must not be guessed",
        )

    def test_personal_transfer_becomes_observed_unverified_candidate(self):
        observed_at = "2026-10-28T00:00:00.000Z"
        item = transfer(
            "wxid_collection:message_0.db/Msg_payer:42",
            memo=" 123ab\n",
            pay_subtype="already_received_must_not_change_status",
        )

        record = payments.transfer_to_ingest_record(item, observed_at)
        payload = record.payload

        self.assertIsNotNone(payload)
        self.assertEqual(record.event_key, "transfer:wxid_collection:message_0.db/Msg_payer:42")
        self.assertEqual(record.source_created_at, 1_700_000_000)
        self.assertEqual(set(payload), {
            "eventKey", "accountId", "category", "direction", "status",
            "amountFen", "currency", "reference", "transactionId",
            "occurredAt", "observedAt", "verified",
        })
        self.assertEqual(payload["eventKey"], record.event_key)
        self.assertEqual(payload["accountId"], "wxid_collection")
        self.assertEqual(payload["category"], "personal_transfer")
        self.assertEqual(payload["direction"], "incoming")
        self.assertEqual(payload["status"], "observed")
        self.assertEqual(payload["amountFen"], 1)
        self.assertEqual(payload["currency"], "CNY")
        self.assertEqual(payload["reference"], "123AB")
        self.assertEqual(payload["transactionId"], "tx_transfer_123")
        self.assertEqual(payload["occurredAt"], payments.source_time_iso(1_700_000_000))
        self.assertEqual(payload["observedAt"], observed_at)
        self.assertIs(payload["verified"], False)
        serialized = json.dumps(payload, ensure_ascii=False)
        for private_value in (item.memo, item.payer, item.pay_subtype):
            self.assertNotIn(private_value, serialized)

    def test_transfer_reference_labels_and_identifier_fallback_are_strict(self):
        cases = (
            ("entire", "123ab", "tx_primary", "fallback_1", "123AB", "tx_primary"),
            ("general_label", "备注：007zX", "", "fallback_2", "007ZX",
             "transfer_id:fallback_2"),
            ("payer_label", "付款方备注123ab", "tx_payer", "fallback_3", "123AB",
             "tx_payer"),
        )
        for name, memo, transaction, transfer_id, reference, outgoing_id in cases:
            with self.subTest(name=name):
                record = payments.transfer_to_ingest_record(
                    transfer(
                        f"wxid_collection:message_0.db/Msg_payer:{name}",
                        memo=memo,
                        transaction=transaction,
                        transfer_id=transfer_id,
                    ),
                    "2026-10-28T00:00:00.000Z",
                )
                self.assertIsNotNone(record.payload)
                self.assertEqual(record.payload["reference"], reference)
                self.assertEqual(record.payload["transactionId"], outgoing_id)
                self.assertTrue(record.payload["eventKey"].startswith("transfer:"))

    def test_nonqualifying_personal_transfers_are_not_candidates(self):
        cases = {
            "red_packet": {"kind": "red_packet"},
            "group": {"conversation": "12345@chatroom"},
            "outgoing": {"direction": "outgoing"},
            "unknown_direction": {"direction": "unknown"},
            "wrong_receiver": {"receiver": "wxid_someone_else"},
            "missing_payer": {"payer": ""},
            "payer_is_receiver": {"payer": "wxid_collection"},
            "missing_amount": {"amount": None},
            "zero_amount": {"amount": Decimal("0")},
            "negative_amount": {"amount": Decimal("-0.01")},
            "bare_extra_text": {"memo": "订单 123AB"},
            "invalid_short_code": {"memo": "12ABC"},
            "conflicting_references": {"memo": "备注：123AB；备注：456CD"},
            "missing_identifiers": {"transaction": "", "transfer_id": ""},
            "unsafe_identifiers": {
                "transaction": "not allowed spaces", "transfer_id": "also invalid",
            },
        }
        for name, changes in cases.items():
            values = {
                "account": "wxid_collection",
                "conversation": "wxid_payer",
                "kind": "transfer",
                "amount": Decimal("0.01"),
                "memo": "123ab",
                "payer": "wxid_payer",
                "receiver": "wxid_collection",
                "direction": "incoming",
                "transaction": "tx_transfer_123",
                "transfer_id": "transfer_123",
            }
            values.update(changes)
            with self.subTest(name=name):
                record = payments.transfer_to_ingest_record(
                    transfer(f"wxid_collection:message_0.db/Msg_payer:{name}", **values),
                    "2026-10-28T00:00:00.000Z",
                )
                self.assertIsNone(record.payload)

    def test_transfer_with_empty_payer_uses_direct_private_card_source(self):
        observed_at = "2026-10-28T09:10:00.000Z"
        item = transfer(
            "wxid_collection:message_0.db/Msg_payer:empty-payer",
            payer="",
            sender="wxid_payer",
            memo="116pf",
            pay_subtype="1",
        )

        record = payments.transfer_to_ingest_record(item, observed_at)

        self.assertIsNotNone(record.payload)
        self.assertEqual(record.payload["category"], "personal_transfer")
        self.assertEqual(record.payload["direction"], "incoming")
        self.assertEqual(record.payload["status"], "observed")
        self.assertEqual(record.payload["reference"], "116PF")
        self.assertEqual(record.payload["amountFen"], 1)
        self.assertEqual(record.payload["transactionId"], item.transaction_id)
        self.assertIs(record.payload["verified"], False)
        self.assertNotIn("wxid_payer", json.dumps(record.payload))

    def test_empty_payer_fallback_rejects_untrusted_card_sources(self):
        cases = {
            "missing_sender": {"sender": ""},
            "null_sender": {"sender": None},
            "self_sender": {
                "sender": "wxid_collection", "conversation": "wxid_collection",
            },
            "mismatched_sender": {"sender": "wxid_unrelated"},
            "control_character_sender": {
                "sender": "wxid_payer\n", "conversation": "wxid_payer\n",
            },
            "overlong_sender": {"sender": "x" * 201, "conversation": "x" * 201},
            "official_source": {"source_kind": "official"},
            "unknown_source": {"source_kind": ""},
            "non_card_message": {"msg_type": 1},
            "null_payer": {"payer": None},
            "control_character_payer": {"payer": "wxid_payer\n"},
            "overlong_payer": {"payer": "x" * 201},
            "payer_is_receiver": {"payer": "wxid_collection"},
        }
        for name, changes in cases.items():
            values = {"payer": "", "sender": "wxid_payer"}
            values.update(changes)
            with self.subTest(name=name):
                record = payments.transfer_to_ingest_record(
                    transfer(f"wxid_collection:message_0.db/Msg_payer:{name}", **values),
                    "2026-10-28T09:10:00.000Z",
                )
                self.assertIsNone(record.payload)

    def test_empty_payer_fallback_preserves_other_transfer_requirements(self):
        cases = {
            "red_packet": {"kind": "red_packet"},
            "group": {"conversation": "12345@chatroom", "sender": "12345@chatroom"},
            "outgoing": {"direction": "outgoing"},
            "unknown_direction": {"direction": "unknown"},
            "wrong_receiver": {"receiver": "wxid_someone_else"},
            "missing_amount": {"amount": None},
            "zero_amount": {"amount": Decimal("0")},
            "negative_amount": {"amount": Decimal("-0.01")},
            "missing_memo": {"memo": ""},
            "malformed_memo": {"memo": "116PF extra"},
            "missing_identifiers": {"transaction": "", "transfer_id": ""},
        }
        for name, changes in cases.items():
            values = {"payer": "", "sender": "wxid_payer"}
            values.update(changes)
            with self.subTest(name=name):
                record = payments.transfer_to_ingest_record(
                    transfer(f"wxid_collection:message_0.db/Msg_payer:{name}", **values),
                    "2026-10-28T09:10:00.000Z",
                )
                self.assertIsNone(record.payload)

    def test_real_personal_collection_template_gets_conservative_receipt_semantics(self):
        item = notice(
            "wxid_collection:biz_message_0.db/Msg_pay:real-template",
            title="个人收款码到账¥0.01",
            detail=(
                "收款金额￥0.01\n"
                "付款方备注510pw\n"
                "汇总今日第2笔收款，共计￥0.02\n"
                "备注收款成功，已存入零钱。点击可查看详情"
            ),
            direction="unknown",
            category="other",
            status="unknown",
            amount=Decimal("0.01"),
            transaction="synthetic_transaction_1",
            official=True,
            conversation=payments.WECHAT_PAY_OFFICIAL_ID,
        )
        record = payments.notice_to_ingest_record(item, "2026-10-28T00:00:00.000Z")
        payload = record.payload
        self.assertIsNotNone(payload)
        self.assertEqual(payload["category"], "receipt")
        self.assertEqual(payload["direction"], "incoming")
        self.assertEqual(payload["status"], "completed")
        self.assertEqual(payload["amountFen"], 1, "daily aggregate amount must not replace SDK title amount")
        self.assertEqual(payload["reference"], "510PW")
        self.assertEqual(payload["transactionId"], "synthetic_transaction_1")
        self.assertIs(payload["verified"], False)

    def test_personal_collection_lookalikes_are_not_candidates(self):
        good_detail = (
            "收款金额￥0.01\n付款方备注123ab\n"
            "备注收款成功，已存入零钱。点击可查看详情"
        )
        cases = {
            "failed_title": {"title": "个人收款码到账失败¥0.01", "detail": good_detail},
            "fake_prefix": {"title": "伪个人收款码到账¥0.01", "detail": good_detail},
            "ambiguous_title": {"title": "个人收款码到账¥0.01和¥0.02", "detail": good_detail},
            "pending_detail": {
                "title": "个人收款码到账¥0.01",
                "detail": "付款方备注123ab\n待到账\n备注收款成功，已存入零钱。点击可查看详情",
            },
            "failed_detail": {
                "title": "个人收款码到账¥0.01",
                "detail": "付款方备注123ab\n收款失败\n备注收款成功，已存入零钱。点击可查看详情",
            },
            "missing_success_sentence": {
                "title": "个人收款码到账¥0.01",
                "detail": "收款金额￥0.01\n付款方备注123ab",
            },
            "non_official_source": {"title": "个人收款码到账¥0.01", "detail": good_detail,
                                    "official": False},
            "wrong_official_conversation": {
                "title": "个人收款码到账¥0.01", "detail": good_detail,
                "conversation": "gh_unrelated",
            },
            "sdk_pending_semantics": {
                "title": "个人收款码到账¥0.01", "detail": good_detail,
                "status": "pending",
            },
            "sdk_amount_mismatch": {"title": "个人收款码到账¥0.01", "detail": good_detail,
                                    "amount": Decimal("0.02")},
        }
        for name, changes in cases.items():
            values = {
                "title": "个人收款码到账¥0.01",
                "detail": good_detail,
                "official": True,
                "conversation": payments.WECHAT_PAY_OFFICIAL_ID,
                "direction": "unknown",
                "category": "other",
                "status": "unknown",
                "amount": Decimal("0.01"),
            }
            values.update(changes)
            with self.subTest(name=name):
                record = payments.notice_to_ingest_record(
                    notice(f"wxid_collection:biz_message_0.db/Msg_pay:{name}", **values),
                    "2026-10-28T00:00:00.000Z",
                )
                self.assertIsNone(record.payload)

    def test_candidate_rules_keep_receipts_and_reviewable_unknowns_only(self):
        keep = payments.is_receipt_candidate
        self.assertTrue(keep(
            category="receipt", direction="incoming", status="completed",
            amount_fen=280, reference="123AB",
        ))
        self.assertTrue(keep(
            category="receipt", direction="unknown", status="unknown",
            amount_fen=None, reference="123AB",
        ))
        self.assertTrue(keep(
            category="other", direction="unknown", status="unknown",
            amount_fen=580, reference="",
        ))
        self.assertFalse(keep(
            category="other", direction="unknown", status="unknown",
            amount_fen=None, reference="",
        ))
        for category, direction in (
            ("payment", "outgoing"),
            ("refund", "incoming"),
            ("transfer_refund", "incoming"),
            ("daily_summary", "unknown"),
        ):
            self.assertFalse(keep(
                category=category, direction=direction, status="completed",
                amount_fen=280, reference="123AB",
            ))

    def test_first_successful_poll_baselines_both_sources_without_history_replay(self):
        store = self.store()
        old = notice("wxid_collection:biz_message_0.db/Msg_pay:1")
        old_transfer = transfer("wxid_collection:message_0.db/Msg_payer:1")
        reader = FakeReader([old], [old_transfer])
        sender = FakeSender()
        clock = FakeClock()
        bridge = payments.PaymentBridge(reader, store, sender, limit=20, clock=clock)

        baseline = bridge.poll_once()
        self.assertTrue(baseline.success)
        self.assertTrue(baseline.baseline_established)
        self.assertEqual(baseline.captured_count, 0)
        self.assertTrue(baseline.notice.baseline_established)
        self.assertTrue(baseline.transfer.baseline_established)
        self.assertTrue(store.baseline_ready("notice"))
        self.assertTrue(store.baseline_ready("transfer"))
        self.assertEqual(store.counts()["seenCount"], 2)
        self.assertEqual(store.counts()["candidateCount"], 0)

        new = notice(
            "wxid_collection:biz_message_0.db/Msg_pay:2",
            detail="金额：￥2.80；备注：321xy",
            transaction="transaction_2",
            created=1_700_000_100,
        )
        reader.items = [new, old]
        second = bridge.poll_once()
        self.assertTrue(second.success)
        self.assertFalse(second.baseline_established)
        self.assertEqual(second.captured_count, 1)
        self.assertEqual(second.notice.captured_count, 1)
        self.assertEqual(second.transfer.captured_count, 0)
        self.assertEqual(store.counts()["pendingCount"], 1)

        delivery = bridge.deliver_due()
        self.assertEqual((delivery.delivered_count, delivery.failed_count), (1, 0))
        self.assertEqual(reader.notice_limits, [20, 20])
        self.assertEqual(reader.transfer_limits, [20, 20])
        self.assertEqual(len(sender.payloads), 1)
        payload = sender.payloads[0]
        self.assertEqual(set(payload), {
            "eventKey", "accountId", "category", "direction", "status",
            "amountFen", "currency", "reference", "transactionId",
            "occurredAt", "observedAt", "verified",
        })
        self.assertEqual(payload["accountId"], "wxid_collection")
        self.assertEqual(payload["amountFen"], 280)
        self.assertEqual(payload["reference"], "321XY")
        self.assertEqual(payload["transactionId"], "transaction_2")
        self.assertIs(payload["verified"], False)
        serialized = json.dumps(payload, ensure_ascii=False)
        self.assertNotIn("你有一笔收款入账", serialized)
        self.assertNotIn("金额：", serialized)

    def test_full_history_window_stays_healthy_and_never_replays_baseline(self):
        store = self.store()
        reader = FakeReader([notice("old-" + str(i)) for i in range(3)])
        bridge = payments.PaymentBridge(reader, store, FakeSender(), limit=3)
        first = bridge.poll_once()
        self.assertTrue(first.success)
        self.assertFalse(first.saturated)
        self.assertEqual(store.counts()["candidateCount"], 0)
        reader.items.insert(0, notice("new"))
        second = bridge.poll_once()
        self.assertTrue(second.success)
        self.assertFalse(second.saturated)
        self.assertEqual(second.captured_count, 1)
        self.assertEqual(reader.notice_limits, [3, 3])

    def test_reconnect_expands_window_until_previously_seen_boundary(self):
        store = self.store()
        reader = FakeReader([notice("old")])
        bridge = payments.PaymentBridge(reader, store, FakeSender(), limit=2)
        bridge.poll_once()
        reader.items = [notice("new-" + str(i)) for i in range(5)] + [notice("old")]
        result = bridge.poll_once()
        self.assertTrue(result.success)
        self.assertEqual(result.captured_count, 5)
        self.assertEqual(reader.notice_limits, [2, 2, 4, 8])

    def test_unresolved_gap_does_not_advance_cursor_or_claim_healthy(self):
        store = self.store()
        reader = FakeReader([notice("old")])
        bridge = payments.PaymentBridge(reader, store, FakeSender(), limit=5000)
        bridge.poll_once()
        reader.items = [notice("new-" + str(i)) for i in range(5000)]
        result = bridge.poll_once()
        self.assertFalse(result.success)
        self.assertTrue(result.saturated)
        self.assertEqual(result.error_code, "source_window_gap")
        self.assertEqual(store.counts()["seenCount"], 1)
        self.assertEqual(store.counts()["candidateCount"], 0)

    def test_delivery_stop_interrupts_backlog_before_next_claim(self):
        store = self.store()
        store.ingest([])
        store.ingest([payments.notice_to_ingest_record(notice("stop-" + str(i)), payments.utc_iso()) for i in range(5)])
        sender = FakeSender()
        bridge = payments.PaymentBridge(FakeReader(), store, sender)
        result = bridge.deliver_due(should_stop=lambda: len(sender.payloads) >= 1)
        self.assertEqual(result.delivered_count, 1)
        self.assertEqual(store.counts()["pendingCount"], 4)

    def test_delivery_time_budget_leaves_remaining_events_for_later(self):
        store = self.store()
        store.ingest([])
        store.ingest([payments.notice_to_ingest_record(notice("budget-" + str(i)), payments.utc_iso()) for i in range(3)])
        bridge = payments.PaymentBridge(FakeReader(), store, FakeSender())
        with patch.object(payments.time, "monotonic", side_effect=[0, 0, 5]):
            result = bridge.deliver_due(time_budget=4)
        self.assertEqual(result.delivered_count, 1)
        self.assertEqual(store.counts()["pendingCount"], 2)

    def test_new_transfer_after_transfer_baseline_is_enqueued(self):
        store = self.store()
        store.ingest([], source="notice")
        store.ingest([], source="transfer")
        item = transfer(
            "wxid_collection:message_0.db/Msg_payer:new-transfer",
            memo="付款方备注007zx",
            transaction="transaction_new_transfer",
            created=1_700_000_300,
        )
        sender = FakeSender()
        bridge = payments.PaymentBridge(FakeReader(transfers=[item]), store, sender)

        result = bridge.poll_once()

        self.assertTrue(result.notice.success)
        self.assertTrue(result.transfer.success)
        self.assertEqual(result.notice.captured_count, 0)
        self.assertEqual(result.transfer.captured_count, 1)
        self.assertEqual(store.counts()["pendingCount"], 1)
        delivery = bridge.deliver_due()
        self.assertEqual((delivery.delivered_count, delivery.failed_count), (1, 0))
        self.assertEqual(len(sender.payloads), 1)
        self.assertEqual(sender.payloads[0]["category"], "personal_transfer")
        self.assertEqual(sender.payloads[0]["status"], "observed")
        self.assertEqual(sender.payloads[0]["reference"], "007ZX")

    def test_observation_time_is_captured_after_transfer_query_completes(self):
        store = self.store()
        store.ingest([], source="notice")
        store.ingest([], source="transfer")
        clock = FakeClock()
        item = transfer("wxid_collection:message_0.db/Msg_payer:during-query",
                        created=int(clock.value + 4))

        class ReaderWithConcurrentCard(FakeReader):
            def transfers(self, *, limit):
                clock.value += 5
                return super().transfers(limit=limit)

        sender = FakeSender()
        bridge = payments.PaymentBridge(ReaderWithConcurrentCard(transfers=[item]),
                                        store, sender, clock=clock)
        self.assertTrue(bridge.poll_once().success)
        self.assertEqual(bridge.deliver_due().delivered_count, 1)
        self.assertEqual(sender.payloads[0]["observedAt"], payments.utc_iso(clock.value))
        self.assertGreater(sender.payloads[0]["observedAt"], sender.payloads[0]["occurredAt"])

    def test_legacy_baseline_migrates_to_notice_only_and_transfer_skips_history(self):
        with closing(sqlite3.connect(self.state_path)) as legacy:
            legacy.executescript(
                """
                CREATE TABLE bridge_state (
                  key TEXT PRIMARY KEY,
                  value TEXT NOT NULL
                );
                CREATE TABLE seen_events (
                  event_key TEXT PRIMARY KEY,
                  source_created_at INTEGER NOT NULL,
                  first_observed_at TEXT NOT NULL
                );
                INSERT INTO bridge_state(key,value) VALUES ('baseline_ready','1');
                INSERT INTO seen_events(event_key,source_created_at,first_observed_at)
                VALUES ('legacy:seen',1690000000,'2026-10-27T00:00:00.000Z');
                """
            )
        store = self.store()

        self.assertTrue(store.baseline_ready("notice"))
        self.assertFalse(store.baseline_ready("transfer"))
        old_transfer = transfer("wxid_collection:message_0.db/Msg_payer:legacy-transfer")
        result = payments.PaymentBridge(
            FakeReader(transfers=[old_transfer]), store, FakeSender(),
        ).poll_once()

        self.assertTrue(result.notice.success)
        self.assertTrue(result.transfer.success)
        self.assertFalse(result.notice.baseline_established)
        self.assertTrue(result.transfer.baseline_established)
        self.assertEqual(result.captured_count, 0)
        self.assertTrue(store.baseline_ready("notice"))
        self.assertTrue(store.baseline_ready("transfer"))
        self.assertEqual(store.counts()["seenCount"], 2)
        self.assertEqual(store.counts()["candidateCount"], 0)

    def test_transfer_busy_error_retries_twice_then_succeeds(self):
        store = self.store()
        store.ingest([], source="notice")
        sleeps = []
        item = transfer("wxid_collection:message_0.db/Msg_payer:busy-success")
        reader = FakeReader(
            transfer_results=[BusyError("locked"), BusyError("locked"), [item]],
        )
        bridge = payments.PaymentBridge(
            reader, store, FakeSender(), sleeper=sleeps.append,
        )

        result = bridge.poll_once()

        self.assertTrue(result.success)
        self.assertEqual(result.transfer.attempts, 3)
        self.assertEqual(reader.transfer_limits, [200, 200, 200])
        self.assertEqual(len(sleeps), 2)
        self.assertTrue(result.transfer.baseline_established)
        self.assertEqual(result.transfer.captured_count, 0)
        self.assertTrue(store.baseline_ready("transfer"))

    def test_transfer_busy_failure_does_not_prevent_notice_ingestion(self):
        store = self.store()
        store.ingest([], source="notice")
        incoming_notice = notice(
            "wxid_collection:biz_message_0.db/Msg_pay:busy-transfer",
            transaction="notice_while_transfer_busy",
        )
        sleeps = []
        reader = FakeReader(
            [incoming_notice],
            transfer_results=[BusyError("locked"), BusyError("locked"), BusyError("locked")],
        )
        result = payments.PaymentBridge(
            reader, store, FakeSender(), sleeper=sleeps.append,
        ).poll_once()

        self.assertFalse(result.success)
        self.assertTrue(result.notice.success)
        self.assertEqual(result.notice.captured_count, 1)
        self.assertFalse(result.transfer.success)
        self.assertEqual(result.transfer.attempts, 3)
        self.assertEqual(result.transfer.error_code, "source_busy")
        self.assertEqual(len(sleeps), 2)
        self.assertTrue(store.baseline_ready("notice"))
        self.assertFalse(store.baseline_ready("transfer"))
        self.assertEqual(store.counts()["pendingCount"], 1)

    def test_notice_failure_does_not_prevent_transfer_ingestion(self):
        store = self.store()
        store.ingest([], source="transfer")
        incoming_transfer = transfer(
            "wxid_collection:message_0.db/Msg_payer:notice-failed",
            transaction="transfer_while_notice_failed",
        )
        reader = FakeReader(
            transfers=[incoming_transfer], notice_results=[RuntimeError("unavailable")],
        )
        result = payments.PaymentBridge(reader, store, FakeSender(), sleeper=lambda _: None).poll_once()

        self.assertFalse(result.success)
        self.assertFalse(result.notice.success)
        self.assertEqual(result.notice.attempts, 1)
        self.assertEqual(result.notice.error_code, "source_read_failed")
        self.assertTrue(result.transfer.success)
        self.assertEqual(result.transfer.captured_count, 1)
        self.assertEqual(reader.notice_limits, [200])
        self.assertEqual(store.counts()["pendingCount"], 1)

    def test_non_candidate_notice_advances_cursor_without_storing_chat_content(self):
        store = self.store()
        store.ingest([])  # establish an empty baseline
        outgoing = notice(
            "wxid_collection:biz_message_0.db/Msg_pay:3",
            title="已支付￥2.80",
            detail="使用零钱支付；备注：123AB",
            direction="outgoing",
            category="payment",
        )
        bridge = payments.PaymentBridge(FakeReader([outgoing]), store, FakeSender())
        result = bridge.poll_once()
        self.assertTrue(result.success)
        self.assertEqual(result.captured_count, 0)
        counts = store.counts()
        self.assertEqual(counts["seenCount"], 1)
        self.assertEqual(counts["candidateCount"], 0)
        columns = [row[1] for row in store.database.execute("PRAGMA table_info(seen_events)")]
        self.assertEqual(columns, ["event_key", "source_created_at", "first_observed_at"])

    def test_missing_account_id_is_not_sent_and_reports_health_failure(self):
        store = self.store()
        store.ingest([])
        reader = FakeReader([notice("missing:1", account="")])
        sender = FakeSender()
        result = payments.PaymentBridge(reader, store, sender).poll_once()
        self.assertFalse(result.success)
        self.assertEqual(result.error_code, "missing_account_id")
        self.assertEqual(store.counts()["candidateCount"], 0)
        self.assertEqual(store.counts()["seenCount"], 0)
        self.assertEqual(sender.payloads, [])

    def test_ambiguous_amount_and_unlabelled_code_are_never_inferred(self):
        item = notice(
            "wxid_collection:biz_message_0.db/Msg_pay:4",
            detail="金额不明确，订单 123AB",
            amount=None,
            transaction="not allowed spaces",
        )
        record = payments.notice_to_ingest_record(item, "2026-10-28T00:00:00.000Z")
        self.assertIsNotNone(record.payload)
        self.assertIsNone(record.payload["amountFen"])
        self.assertEqual(record.payload["currency"], "")
        self.assertEqual(record.payload["reference"], "")
        self.assertEqual(record.payload["transactionId"], "")

    def test_failed_delivery_is_durable_and_retries_same_idempotency_key(self):
        clock = FakeClock()
        store = payments.StateStore(self.state_path, self.binding)
        store.ingest([])
        record = payments.notice_to_ingest_record(
            notice("wxid_collection:biz_message_1.db/Msg_pay:9"),
            payments.utc_iso(clock()),
        )
        store.ingest([record])
        first_sender = FakeSender([(False, "network_error")])
        first = payments.PaymentBridge(FakeReader(), store, first_sender, clock=clock).deliver_due()
        self.assertEqual((first.delivered_count, first.failed_count), (0, 1))
        self.assertEqual(store.counts()["retryCount"], 1)
        store.close()

        clock.value += 3
        reopened = payments.StateStore(self.state_path, self.binding)
        self.addCleanup(reopened.close)
        second_sender = FakeSender([(True, "")])
        second = payments.PaymentBridge(FakeReader(), reopened, second_sender, clock=clock).deliver_due()
        self.assertEqual((second.delivered_count, second.failed_count), (1, 0))
        self.assertEqual(first_sender.payloads[0]["eventKey"], second_sender.payloads[0]["eventKey"])
        self.assertEqual(reopened.counts()["deliveredCount"], 1)

    def test_state_database_refuses_a_different_account_binding(self):
        store = payments.StateStore(self.state_path, self.binding)
        store.close()
        with self.assertRaises(payments.ConfigError):
            payments.StateStore(self.state_path, "b" * 64)

    def test_status_json_contains_health_and_counts_but_no_payment_details(self):
        store = self.store()
        store.ingest([], source="notice")
        store.ingest([], source="transfer")
        store.ingest([payments.notice_to_ingest_record(
            notice("wxid_secret:message:10", detail="金额：￥5.80；备注：999ZZ"),
            "2026-10-28T00:00:00.000Z",
        )], source="notice")
        status_path = self.root / "status.json"
        status = payments.StatusFile(status_path, store)
        status.record(
            payments.PollResult(
                notice=payments.SourcePollResult(
                    source="notice",
                    success=True,
                    item_count=1,
                    captured_count=1,
                    baseline_established=False,
                    saturated=False,
                    attempts=1,
                ),
                transfer=payments.SourcePollResult(
                    source="transfer",
                    success=True,
                    item_count=2,
                    captured_count=0,
                    baseline_established=False,
                    saturated=False,
                    attempts=1,
                ),
            ),
            payments.DeliveryResult(0, 0),
        )
        status.write(running=True)
        raw = status_path.read_text(encoding="utf-8")
        document = json.loads(raw)
        self.assertTrue(document["baselineReady"])
        self.assertTrue(document["lastPollSuccess"])
        self.assertEqual(document["lastCapturedCount"], 1)
        self.assertTrue(document["noticeBaselineReady"])
        self.assertTrue(document["transferBaselineReady"])
        self.assertTrue(document["noticeLastPollSuccess"])
        self.assertTrue(document["transferLastPollSuccess"])
        self.assertEqual(document["noticeLastCount"], 1)
        self.assertEqual(document["transferLastCount"], 2)
        self.assertEqual(document["noticeLastCapturedCount"], 1)
        self.assertEqual(document["transferLastCapturedCount"], 0)
        self.assertIs(document["noticePollLimitSaturated"], False)
        self.assertIs(document["transferPollLimitSaturated"], False)
        self.assertEqual(document["noticeLastErrorCode"], "")
        self.assertEqual(document["transferLastErrorCode"], "")
        self.assertEqual(document["pendingCount"], 1)
        for secret in ("wxid_secret", "999ZZ", "580", "message:10"):
            self.assertNotIn(secret, raw)

    def test_status_aggregate_health_requires_both_sources(self):
        store = self.store()
        store.ingest([], source="notice")
        status_path = self.root / "partial-status.json"
        status = payments.StatusFile(status_path, store)
        status.record(
            payments.PollResult(
                notice=payments.SourcePollResult(
                    source="notice", success=True, item_count=0, captured_count=0,
                    baseline_established=False, saturated=False, attempts=1,
                ),
                transfer=payments.SourcePollResult(
                    source="transfer", success=False, item_count=0, captured_count=0,
                    baseline_established=False, saturated=False, attempts=3,
                    error_code="source_read_failed",
                ),
            ),
            payments.DeliveryResult(0, 0),
        )
        status.write(running=True)
        document = json.loads(status_path.read_text(encoding="utf-8"))

        self.assertFalse(document["baselineReady"])
        self.assertFalse(document["lastPollSuccess"])
        self.assertTrue(document["noticeBaselineReady"])
        self.assertFalse(document["transferBaselineReady"])
        self.assertTrue(document["noticeLastPollSuccess"])
        self.assertFalse(document["transferLastPollSuccess"])
        self.assertEqual(document["transferLastErrorCode"], "source_read_failed")
        self.assertEqual(document["health"], "degraded")

    def test_market_sender_accepts_only_the_two_local_payment_routes(self):
        original = payments.LocalEventSender("http://127.0.0.1:8765", "synthetic-token")
        self.assertTrue(original.endpoint.endswith(payments.API_PATH))
        market = payments.LocalEventSender("http://127.0.0.1:8765", "synthetic-token",
                                           event_path=payments.MARKET_API_PATH)
        self.assertEqual(market.endpoint, "http://127.0.0.1:8765/api/pet-market/payment-events")
        for bad_path in ("https://example.com", "//example.com", "/api/other", "../payment-events",
                         payments.MARKET_API_PATH + "?redirect=1", None, []):
            with self.subTest(path=bad_path), self.assertRaises(payments.ConfigError):
                payments.LocalEventSender("http://127.0.0.1:8765", "synthetic-token", event_path=bad_path)

    def test_outbox_cannot_be_retargeted_to_another_market_or_port(self):
        store = self.store()
        store.bind_destination("http://127.0.0.1:8765", payments.API_PATH)
        store.bind_destination("http://127.0.0.1:8765", payments.API_PATH)
        for base_url, event_path in (("http://127.0.0.1:8765", payments.MARKET_API_PATH),
                                     ("http://127.0.0.1:8766", payments.API_PATH)):
            with self.subTest(base_url=base_url, path=event_path), self.assertRaises(payments.ConfigError):
                store.bind_destination(base_url, event_path)
        self.assertEqual(store._state("delivery_destination"), "http://127.0.0.1:8765" + payments.API_PATH)

    def test_legacy_payment_history_is_not_reused_for_new_market(self):
        store = self.store()
        store.ingest([payments.notice_to_ingest_record(notice("old-notice"), "2026-10-29T00:00:00Z")])
        with self.assertRaises(payments.ConfigError):
            store.bind_destination("http://127.0.0.1:8765", payments.MARKET_API_PATH)
        store.bind_destination("http://127.0.0.1:8765", payments.API_PATH)

    def test_only_literal_ipv4_loopback_with_explicit_port_is_allowed(self):
        self.assertEqual(
            payments.normalize_base_url("http://127.0.0.1:8765/"),
            "http://127.0.0.1:8765",
        )
        for invalid in (
            "http://localhost:8765",
            "http://127.0.0.2:8765",
            "https://127.0.0.1:8765",
            "http://127.0.0.1",
            "http://127.0.0.1:8765/base",
            "http://user@127.0.0.1:8765",
            "http://example.com:8765",
        ):
            with self.subTest(invalid=invalid), self.assertRaises(payments.ConfigError):
                payments.normalize_base_url(invalid)

    def test_runtime_outputs_cannot_replace_config_keys_or_enter_account_directory(self):
        config_path = self.root / "bridge.json"
        keys_path = self.root / "keys.json"
        account = self.root / "account"
        account.mkdir()
        config_path.write_text("{}", encoding="utf-8")
        keys_path.write_text("{}", encoding="utf-8")
        config = payments.BridgeConfig(
            self.root,
            account,
            keys_path,
            "http://127.0.0.1:8765",
            "synthetic-token-1234567890",
        )
        common = {"state_db": None, "status_json": None, "stop_file": None}
        for override in (
            {"status_json": str(config_path)},
            {"state_db": str(keys_path)},
            {"status_json": str(account / "health.json")},
        ):
            with self.subTest(override=override), self.assertRaises(payments.ConfigError):
                payments.resolve_runtime_paths(
                    config_path,
                    SimpleNamespace(**{**common, **override}),
                    config,
                )

    def test_check_config_does_not_construct_reader_or_create_state_database(self):
        wxlocal_root = self.root / "wxlocal-root"
        account = self.root / "wxid_collection_abcd"
        wxlocal_root.mkdir()
        account.mkdir()
        keys = self.root / "known-keys.json"
        official = account / "db_storage" / "message" / "biz_message_0.db"
        normal = account / "db_storage" / "message" / "message_0.db"
        official.parent.mkdir(parents=True)
        official.touch()
        normal.touch()
        keys.write_text(json.dumps({
            str(official): "ab" * 32,
            str(normal): "cd" * 32,
        }), encoding="utf-8")
        config = self.root / "bridge.json"
        config.write_text(json.dumps({
            "wxlocalRoot": str(wxlocal_root),
            "accountDir": str(account),
            "keysFile": str(keys),
            "baseUrl": "http://127.0.0.1:8765",
            "bridgeToken": "synthetic-token-1234567890",
        }), encoding="utf-8")

        class MustNotConstruct:
            def __init__(self, *args, **kwargs):
                raise AssertionError("PaymentReader must not be constructed by --check-config")

        with patch.object(payments, "load_payment_reader", return_value=MustNotConstruct):
            self.assertEqual(payments.main(["--config", str(config), "--check-config"]), 0)
        self.assertFalse(config.with_suffix(".sqlite3").exists())
        value = json.loads(config.read_text(encoding="utf-8"))
        value["eventPath"] = payments.MARKET_API_PATH
        config.write_text(json.dumps(value), encoding="utf-8")
        self.assertEqual(payments.load_config(config).event_path, payments.MARKET_API_PATH)
        value["eventPath"] = "/api/unrelated"
        config.write_text(json.dumps(value), encoding="utf-8")
        with self.assertRaises(payments.ConfigError):
            payments.load_config(config)

    def test_keys_file_requires_official_and_normal_account_bindings(self):
        account = self.root / "wxid_collection_abcd"
        official = account / "db_storage" / "message" / "biz_message_0.db"
        normal = account / "db_storage" / "message" / "message_0.db"
        official.parent.mkdir(parents=True)
        official.touch()
        normal.touch()
        keys = self.root / "known-keys.json"

        cases = {
            "official_only": {str(official): "ab" * 32},
            "normal_only": {str(normal): "cd" * 32},
        }
        for name, mapping in cases.items():
            with self.subTest(name=name):
                keys.write_text(json.dumps(mapping), encoding="utf-8")
                with self.assertRaises(payments.ConfigError):
                    payments._validate_keys_file(keys, account)

    def test_keys_file_covers_every_existing_payment_source_shard_with_safe_key(self):
        account = self.root / "wxid_collection_abcd"
        sources = (
            account / "db_storage" / "message" / "biz_message_0.db",
            account / "db_storage" / "message" / "biz_message_3.db",
            account / "db_storage" / "message" / "message_0.db",
            account / "db_storage" / "message" / "message_7.db",
        )
        for source in sources:
            source.parent.mkdir(parents=True, exist_ok=True)
            source.touch()
        keys = self.root / "known-keys.json"
        complete_mapping = {
            str(source): f"{index + 1:02x}" * 32
            for index, source in enumerate(sources)
        }

        keys.write_text(json.dumps(complete_mapping), encoding="utf-8")
        payments._validate_keys_file(keys, account)

        for missing in sources:
            with self.subTest(missing=missing.name):
                incomplete_mapping = dict(complete_mapping)
                incomplete_mapping.pop(str(missing))
                keys.write_text(json.dumps(incomplete_mapping), encoding="utf-8")
                with self.assertRaises(payments.ConfigError):
                    payments._validate_keys_file(keys, account)

        unsafe_mapping = dict(complete_mapping)
        unsafe_mapping[str(sources[-1])] = "not-a-safe-key"
        keys.write_text(json.dumps(unsafe_mapping), encoding="utf-8")
        with self.assertRaises(payments.ConfigError):
            payments._validate_keys_file(keys, account)


class ProductionBridgeTests(unittest.TestCase):
    def test_production_is_explicit_and_official_https_only(self):
        url = "https://in.desktoppet.online"
        self.assertEqual(payments.normalize_base_url(url + ":443/", "production"), url)
        with self.assertRaises(payments.ConfigError):
            payments.normalize_base_url(url)
        for invalid in ("http://in.desktoppet.online", "https://evil.example", url + ".evil.example",
                        url + ":8443", url + "/path", url + "?key=test", url + "#x",
                        "https://user:secret@in.desktoppet.online", "http://127.0.0.1:18767"):
            with self.subTest(url=invalid), self.assertRaises(payments.ConfigError):
                payments.normalize_base_url(invalid, "production")
        with self.assertRaises(payments.ConfigError):
            payments.LocalEventSender(url, "fixture", mode="production")

    def test_local_queue_cannot_be_retargeted_to_production(self):
        with tempfile.TemporaryDirectory() as folder:
            store = payments.StateStore(Path(folder) / "bridge.sqlite3", "a" * 64)
            try:
                store.bind_destination("http://127.0.0.1:18767", payments.MARKET_API_PATH)
                with self.assertRaises(payments.ConfigError):
                    store.bind_destination("https://in.desktoppet.online", payments.MARKET_API_PATH, "production")
            finally:
                store.close()

    def test_delivery_outcome_is_distinct_from_network_success(self):
        sender = payments.LocalEventSender("https://in.desktoppet.online", "fixture",
                                          event_path=payments.MARKET_API_PATH, mode="production")
        for body, expected in ((b'{"paymentEvent":{"reviewStatus":"approved"}}', "approved"),
                               (b'{"paymentEvent":{"reviewStatus":"candidate_review"}}', "candidate_review"),
                               (b'{}', "unknown"), (b'invalid', "unknown")):
            response = io.BytesIO(body)
            response.status = 200
            with patch.object(sender.opener, "open", return_value=response):
                self.assertEqual(sender.send({"fixture": True}), (True, ""))
                self.assertEqual(sender.last_outcome, expected)

    def test_default_tls_validation_and_no_redirect(self):
        import ssl
        from urllib.request import HTTPSHandler, ProxyHandler
        sender = payments.LocalEventSender("https://in.desktoppet.online", "fixture",
                                          event_path=payments.MARKET_API_PATH, mode="production")
        https = next(h for h in sender.opener.handlers if isinstance(h, HTTPSHandler))
        context = https._context or ssl.create_default_context()
        self.assertEqual(context.verify_mode, ssl.CERT_REQUIRED)
        self.assertTrue(context.check_hostname)
        self.assertIsNone(payments._NoRedirect().redirect_request(None, None, 302, "", {}, "https://evil.example"))
        proxies = [h for h in sender.opener.handlers if isinstance(h, ProxyHandler)]
        self.assertTrue(all(not h.proxies for h in proxies))

    def test_result_counts_survive_restart_and_unknown_is_not_approved(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "bridge.sqlite3"
            store = payments.StateStore(path, "b" * 64)
            store.ingest([])
            for index, outcome in enumerate(("approved", "candidate_review", "unknown")):
                record = payments.notice_to_ingest_record(notice("test-" + str(index)), payments.utc_iso())
                store.ingest([record])
                key, token, _, _ = store.claim_due(2_000_000_000)
                store.mark_delivered(key, token, payments.utc_iso(), outcome)
            store.close()
            store = payments.StateStore(path, "b" * 64)
            try:
                counts = store.counts()
                self.assertEqual((counts["deliveredCount"], counts["approvedCount"], counts["reviewCount"]), (3, 1, 1))
            finally:
                store.close()

    def test_source_lock_rejects_duplicate_and_releases(self):
        import uuid
        binding = __import__("hashlib").sha256(uuid.uuid4().bytes).hexdigest()
        first = payments.SourceLock(binding)
        try:
            with self.assertRaises(payments.ConfigError):
                payments.SourceLock(binding)
        finally:
            first.close()
        payments.SourceLock(binding).close()


class MarketHeartbeatTests(unittest.TestCase):
    @staticmethod
    def poll(*, transfer_success=True, saturated=False):
        return payments.PollResult(
            payments.SourcePollResult("notice", True, 2, 0, False, False, 1),
            payments.SourcePollResult("transfer", transfer_success, 1, 0, False, saturated, 1),
        )

    def test_market_heartbeat_reports_source_readiness_without_payment_data(self):
        sender = payments.LocalEventSender(
            "http://127.0.0.1:18767", "synthetic-token", event_path=payments.MARKET_API_PATH)
        store = SimpleNamespace(baseline_ready=lambda source: source == "transfer")
        with patch.object(sender, "_post", return_value=(True, "")) as post:
            self.assertEqual(sender.send_heartbeat("wxid_fixture", store, self.poll()), (True, ""))
        endpoint, payload = post.call_args.args
        self.assertEqual(endpoint, "http://127.0.0.1:18767/api/pet-market/payment-heartbeat")
        self.assertEqual(payload, {
            "accountId": "wxid_fixture", "noticeReady": False, "transferReady": True,
            "noticePollSuccess": True, "transferPollSuccess": True, "pollLimitSaturated": False,
        })

    def test_failed_source_and_shutdown_cannot_report_online(self):
        sender = payments.LocalEventSender(
            "http://127.0.0.1:18767", "synthetic-token", event_path=payments.MARKET_API_PATH)
        store = SimpleNamespace(baseline_ready=lambda source: True)
        with patch.object(sender, "_post", return_value=(True, "")) as post:
            sender.send_heartbeat("wxid_fixture", store, self.poll(transfer_success=False, saturated=True))
            failed = post.call_args.args[1]
            self.assertFalse(failed["transferPollSuccess"])
            self.assertTrue(failed["pollLimitSaturated"])
            sender.send_heartbeat("wxid_fixture", store, self.poll(), running=False)
            stopped = post.call_args.args[1]
            self.assertFalse(any(stopped[key] for key in (
                "noticeReady", "transferReady", "noticePollSuccess", "transferPollSuccess")))

    def test_old_payment_listener_does_not_call_new_api(self):
        sender = payments.LocalEventSender("http://127.0.0.1:18765", "synthetic-token")
        with patch.object(sender, "_post") as post:
            self.assertEqual(sender.send_heartbeat("", None, self.poll()), (True, ""))
            post.assert_not_called()

    def test_transient_contention_does_not_refresh_successful_heartbeat_lease(self):
        sender = payments.LocalEventSender(
            "http://127.0.0.1:18767", "synthetic-token", event_path=payments.MARKET_API_PATH)
        store = SimpleNamespace(baseline_ready=lambda source: True)
        busy = payments.PollResult(self.poll().notice, payments.SourcePollResult(
            "transfer", False, 0, 0, False, False, 3, "source_busy"))
        with patch.object(sender, "_post", return_value=(True, "")) as post:
            sender.send_heartbeat("wxid_fixture", store, self.poll())
            for _ in range(20):
                self.assertEqual(sender.send_heartbeat("wxid_fixture", store, busy),
                                 (None, "source_busy_deferred"))
            # Even repeated successful notice reads cannot renew the last
            # transfer lease. The server expires it without another request.
            self.assertEqual(post.call_count, 1)
            sender.send_heartbeat("wxid_fixture", store, self.poll())
            self.assertEqual(post.call_count, 2)
            self.assertTrue(post.call_args.args[1]["transferPollSuccess"])
            sender.send_heartbeat("wxid_fixture", store, busy, running=False)
            self.assertEqual(post.call_count, 3)
            self.assertFalse(post.call_args.args[1]["transferPollSuccess"])

    def test_contention_does_not_hide_hard_failures_or_saturation(self):
        sender = payments.LocalEventSender(
            "http://127.0.0.1:18767", "synthetic-token", event_path=payments.MARKET_API_PATH)
        store = SimpleNamespace(baseline_ready=lambda source: True)
        busy = payments.SourcePollResult("transfer", False, 0, 0, False, False, 3, "source_busy")
        hard = payments.SourcePollResult("notice", False, 0, 0, False, False, 1, "source_read_failed")
        saturated = payments.SourcePollResult("notice", True, 200, 0, False, True, 1)
        with patch.object(sender, "_post", return_value=(True, "")) as post:
            sender.send_heartbeat("wxid_fixture", store, payments.PollResult(hard, busy))
            self.assertEqual(post.call_count, 1)
            self.assertFalse(post.call_args.args[1]["transferPollSuccess"])
            sender.send_heartbeat("wxid_fixture", store, payments.PollResult(saturated, busy))
            self.assertEqual(post.call_count, 2)
            self.assertTrue(post.call_args.args[1]["pollLimitSaturated"])


if __name__ == "__main__":
    unittest.main()
