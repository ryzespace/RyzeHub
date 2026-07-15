"""
Testy pipeline'a / Pipeline tests.
Uruchom / Run: python -m pytest tests.py -v
"""
import sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from models import (
    Ticket, TicketType, TicketPriority, TicketStatus,
    ConversationMessage
)
from transformer import TicketValidator, TicketEnricher, TicketFilter, TicketTransformer


def make_sample_ticket(ticket_id="T-001", status=TicketStatus.NEW) -> Ticket:
    """Utwórz przykładowy ticket / Create sample ticket."""
    return Ticket(
        ticket_id=ticket_id,
        ticket_type=TicketType.BUG,
        description="Aplikacja crashuje się przy logowaniu / App crashes on login",
        conversation=[
            ConversationMessage(
                sender="Jan Kowalski",
                role="client",
                content="Nie mogę się zalogować, aplikacja się crashuje. Critical error.",
                timestamp="2026-07-15T10:00:00",
                message_id="msg-1",
            ),
            ConversationMessage(
                sender="System",
                role="system",
                content="Ticket utworzony automatycznie / Ticket created automatically",
                timestamp="2026-07-15T10:01:00",
                message_id="msg-2",
            ),
        ],
        status=status,
        client_id="client-42",
        client_name="jan kowalski",
        created_at="2026-07-15T10:00:00",
    )


# ─── Testy walidatora / Validator tests ─────────────────────

def test_valid_ticket():
    ticket = make_sample_ticket()
    is_valid, errors = TicketValidator.validate(ticket)
    assert is_valid, f"Ticket should be valid, errors: {errors}"
    assert len(errors) == 0
    print("✓ test_valid_ticket passed")


def test_invalid_ticket_no_id():
    ticket = make_sample_ticket()
    ticket.ticket_id = ""
    is_valid, errors = TicketValidator.validate(ticket)
    assert not is_valid
    assert any("ticket_id" in e for e in errors)
    print("✓ test_invalid_ticket_no_id passed")


def test_invalid_ticket_no_description():
    ticket = make_sample_ticket()
    ticket.description = ""
    is_valid, errors = TicketValidator.validate(ticket)
    assert not is_valid
    assert any("opisu" in e or "description" in e for e in errors)
    print("✓ test_invalid_ticket_no_description passed")


def test_invalid_ticket_no_conversation():
    ticket = make_sample_ticket()
    ticket.conversation = []
    is_valid, errors = TicketValidator.validate(ticket)
    assert not is_valid
    assert any("konwersacji" in e or "conversation" in e for e in errors)
    print("✓ test_invalid_ticket_no_conversation passed")


# ─── Testy enrichera / Enricher tests ───────────────────────

def test_auto_categorize_technical():
    ticket = make_sample_ticket()
    ticket.description = "Błąd 500 przy logowaniu, aplikacja się crashuje"
    category = TicketEnricher.auto_categorize(ticket)
    assert category == "technical", f"Expected 'technical', got '{category}'"
    print("✓ test_auto_categorize_technical passed")


def test_auto_categorize_billing():
    ticket = make_sample_ticket()
    ticket.description = "Mam problem z fakturą, płatność nie przeszła"
    ticket.conversation = [
        ConversationMessage(
            sender="Klient", role="client",
            content="Proszę o pomoc z fakturą, invoice nie została opłacona",
            timestamp="2026-07-15T10:00:00", message_id="msg-1",
        ),
    ]
    category = TicketEnricher.auto_categorize(ticket)
    assert category == "billing", f"Expected 'billing', got '{category}'"
    print("✓ test_auto_categorize_billing passed")


def test_auto_prioritize_critical():
    ticket = make_sample_ticket()
    ticket.description = "Krytyczny błąd na produkcji, wszystko nie działa!"
    priority = TicketEnricher.auto_prioritize(ticket)
    assert priority == TicketPriority.CRITICAL
    print("✓ test_auto_prioritize_critical passed")


def test_auto_prioritize_high():
    ticket = make_sample_ticket()
    ticket.description = "To jest ważne i pilne, potrzebuję szybkiej pomocy"
    priority = TicketEnricher.auto_prioritize(ticket)
    assert priority == TicketPriority.HIGH
    print("✓ test_auto_prioritize_high passed")


def test_enrich_sets_tags():
    ticket = make_sample_ticket()
    ticket = TicketEnricher.enrich(ticket)
    assert len(ticket.tags) > 0
    assert ticket.category != ""
    assert ticket.transferred_at is not None
    print("✓ test_enrich_sets_tags passed")


# ─── Testy filtra / Filter tests ────────────────────────────

def test_filter_resolved():
    tickets = [
        make_sample_ticket("T-001", TicketStatus.NEW),
        make_sample_ticket("T-002", TicketStatus.RESOLVED),
        make_sample_ticket("T-003", TicketStatus.IN_PROGRESS),
    ]
    filtered = TicketFilter.filter_tickets(tickets, filter_resolved=True)
    assert len(filtered) == 2
    assert all(t.status != TicketStatus.RESOLVED for t in filtered)
    print("✓ test_filter_resolved passed")


def test_filter_closed():
    tickets = [
        make_sample_ticket("T-001", TicketStatus.NEW),
        make_sample_ticket("T-002", TicketStatus.CLOSED),
    ]
    filtered = TicketFilter.filter_tickets(tickets, filter_closed=True)
    assert len(filtered) == 1
    print("✓ test_filter_closed passed")


def test_filter_exclude_ids():
    tickets = [
        make_sample_ticket("T-001"),
        make_sample_ticket("T-002"),
        make_sample_ticket("T-003"),
    ]
    filtered = TicketFilter.filter_tickets(tickets, exclude_ids={"T-001", "T-003"})
    assert len(filtered) == 1
    assert filtered[0].ticket_id == "T-002"
    print("✓ test_filter_exclude_ids passed")


# ─── Testy transformera / Transformer tests ─────────────────

def test_transformer_full_pipeline():
    tickets = [
        make_sample_ticket("T-001", TicketStatus.NEW),
        make_sample_ticket("T-002", TicketStatus.RESOLVED),
        make_sample_ticket("T-003", TicketStatus.IN_PROGRESS),
    ]
    transformer = TicketTransformer()
    valid, failed = transformer.process_batch(tickets)
    assert len(valid) == 2, f"Expected 2 valid tickets, got {len(valid)}"
    assert len(failed) == 0
    print("✓ test_transformer_full_pipeline passed")


def test_transformer_with_invalid():
    tickets = [
        make_sample_ticket("T-001"),
        make_sample_ticket("T-002"),
    ]
    tickets[1].description = ""  # Invalid
    transformer = TicketTransformer()
    valid, failed = transformer.process_batch(tickets)
    assert len(valid) == 1
    assert len(failed) == 1
    assert failed[0].ticket_id == "T-002"
    print("✓ test_transformer_with_invalid passed")


# ─── Testy serializacji / Serialization tests ───────────────

def test_ticket_serialization_roundtrip():
    ticket = make_sample_ticket()
    ticket.category = "technical"
    ticket.tags = ["bug", "technical"]

    # Serialize
    data = ticket.to_dict()
    assert isinstance(data, dict)
    assert data["ticket_id"] == "T-001"
    assert data["ticket_type"] == "bug"
    assert len(data["conversation"]) == 2

    # Deserialize
    restored = Ticket.from_dict(data)
    assert restored.ticket_id == ticket.ticket_id
    assert restored.ticket_type == ticket.ticket_type
    assert restored.description == ticket.description
    assert len(restored.conversation) == len(ticket.conversation)
    print("✓ test_ticket_serialization_roundtrip passed")


# ─── Runner ──────────────────────────────────────────────────

if __name__ == "__main__":
    tests = [
        test_valid_ticket,
        test_invalid_ticket_no_id,
        test_invalid_ticket_no_description,
        test_invalid_ticket_no_conversation,
        test_auto_categorize_technical,
        test_auto_categorize_billing,
        test_auto_prioritize_critical,
        test_auto_prioritize_high,
        test_enrich_sets_tags,
        test_filter_resolved,
        test_filter_closed,
        test_filter_exclude_ids,
        test_transformer_full_pipeline,
        test_transformer_with_invalid,
        test_ticket_serialization_roundtrip,
    ]

    print(f"\nRunning {len(tests)} tests...\n")
    passed = 0
    failed = 0

    for test in tests:
        try:
            test()
            passed += 1
        except Exception as e:
            print(f"✗ {test.__name__} FAILED: {e}")
            failed += 1

    print(f"\n{'='*40}")
    print(f"Results: {passed} passed, {failed} failed, {len(tests)} total")
    if failed:
        sys.exit(1)
    print("All tests passed! ✓ Wszystkie testy przeszły!")
