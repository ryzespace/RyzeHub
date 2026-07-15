"""
Transformacja i walidacja danych ticketów / Ticket data transformation and validation.
Zaawansowana transformacja: walidacja, enrichment, filtrowanie, deduplikacja.
Advanced transformation: validation, enrichment, filtering, deduplication.
"""
import re
import hashlib
import logging
from datetime import datetime
from typing import List, Optional, Tuple

from models import (
    Ticket, TicketType, TicketPriority, TicketStatus,
    ConversationMessage, TransferResult
)

logger = logging.getLogger(__name__)


class TicketValidator:
    """
    Walidator ticketów / Ticket validator.
    Sprawdza czy ticket ma wszystkie wymagane dane.
    Checks if ticket has all required data.
    """

    REQUIRED_FIELDS = ["ticket_id", "ticket_type", "description"]

    @staticmethod
    def validate(ticket: Ticket) -> Tuple[bool, List[str]]:
        """
        Waliduj ticket / Validate ticket.

        Returns:
            (is_valid, list_of_errors)
        """
        errors = []

        # Sprawdź wymagane pola / Check required fields
        if not ticket.ticket_id:
            errors.append("Brak ticket_id / Missing ticket_id")
        if not ticket.description or not ticket.description.strip():
            errors.append("Brak opisu / Missing description")
        if ticket.ticket_type == TicketType.OTHER:
            logger.warning(f"Ticket {ticket.ticket_id} ma typ 'other' - może wymagać ręcznej kategoryzacji")

        # Walidacja konwersacji / Conversation validation
        if not ticket.conversation:
            errors.append("Brak wstępnej konwersacji / Missing initial conversation")

        for i, msg in enumerate(ticket.conversation):
            if not msg.content.strip():
                errors.append(f"Wiadomość #{i + 1} w konwersacji jest pusta / Message #{i + 1} is empty")
            if not msg.sender:
                errors.append(f"Wiadomość #{i + 1} nie ma nadawcy / Message #{i + 1} has no sender")

        is_valid = len(errors) == 0
        if not is_valid:
            logger.warning(f"Ticket {ticket.ticket_id} validation failed: {errors}")
        return is_valid, errors


class TicketEnricher:
    """
    Wzbogacanie danych ticketów / Ticket data enrichment.
    Automatyczna kategoryzacja i priorytetyzacja.
    Automatic categorization and prioritization.
    """

    # Słowa kluczowe do automatycznej kategoryzacji / Keywords for auto-categorization
    CATEGORY_KEYWORDS = {
        "billing": ["faktura", "płatność", "invoice", "payment", "billing", "rachunek", "cena"],
        "technical": ["błąd", "error", "crash", "awaria", "nie działa", "bug", "timeout", "500", "404"],
        "account": ["konto", "account", "logowanie", "login", "hasło", "password", "rejestracja"],
        "feature": ["proponuję", "sugestia", "feature", "request", "nowa funkcja", "dodaj"],
        "integration": ["api", "integracja", "webhook", "integration", "sdk", "połączenie"],
        "performance": ["wolno", "wydajność", "performance", "slow", "lag", "timeout"],
    }

    # Słowa kluczowe do priorytetyzacji / Keywords for prioritization
    PRIORITY_KEYWORDS = {
        TicketPriority.CRITICAL: [
            "krytyczny", "critical", "produkcja", "production", "awaria",
            "outage", "down", "nie działa", "emergency", "natychmiast"
        ],
        TicketPriority.HIGH: [
            "ważne", "important", "pilne", "urgent", "szybko",
            "asap", "blokujące", "blocking"
        ],
        TicketPriority.LOW: [
            "kiedyś", "someday", "niska", "low priority",
            "nie pilne", "not urgent", "pytanie", "question"
        ],
    }

    @classmethod
    def auto_categorize(cls, ticket: Ticket) -> str:
        """
        Automatycznie przypisz kategorię na podstawie opisu i konwersacji.
        Auto-assign category based on description and conversation.
        """
        text = ticket.description.lower()
        text += " " + " ".join(m.content.lower() for m in ticket.conversation)

        best_category = "general"
        best_score = 0

        for category, keywords in cls.CATEGORY_KEYWORDS.items():
            score = sum(1 for kw in keywords if kw in text)
            if score > best_score:
                best_score = score
                best_category = category

        logger.debug(f"Auto-categorized ticket {ticket.ticket_id}: {best_category} (score={best_score})")
        return best_category

    @classmethod
    def auto_prioritize(cls, ticket: Ticket) -> TicketPriority:
        """
        Automatycznie przypisz priorytet na podstawie treści.
        Auto-assign priority based on content.
        """
        text = ticket.description.lower()
        text += " " + " ".join(m.content.lower() for m in ticket.conversation)

        best_priority = TicketPriority.MEDIUM
        best_score = 0

        for priority, keywords in cls.PRIORITY_KEYWORDS.items():
            score = sum(1 for kw in keywords if kw in text)
            if score > best_score:
                best_score = score
                best_priority = priority

        logger.debug(f"Auto-prioritized ticket {ticket.ticket_id}: {best_priority.value} (score={best_score})")
        return best_priority

    @classmethod
    def generate_tags(cls, ticket: Ticket) -> List[str]:
        """
        Wygeneruj tagi na podstawie typu i kategorii.
        Generate tags based on type and category.
        """
        tags = [ticket.ticket_type.value]
        if ticket.category:
            tags.append(ticket.category)
        if ticket.priority in (TicketPriority.HIGH, TicketPriority.CRITICAL):
            tags.append("needs_attention")
        if len(ticket.conversation) > 3:
            tags.append("multi_message")
        return list(set(tags))

    @classmethod
    def enrich(cls, ticket: Ticket, auto_categorize: bool = True,
               auto_priority: bool = True) -> Ticket:
        """
        Wzbogać ticket o dodatkowe dane.
        Enrich ticket with additional data.
        """
        if auto_categorize and not ticket.category:
            ticket.category = cls.auto_categorize(ticket)

        if auto_priority and ticket.priority == TicketPriority.MEDIUM:
            ticket.priority = cls.auto_prioritize(ticket)

        ticket.tags = cls.generate_tags(ticket)
        ticket.transferred_at = datetime.now().isoformat()

        return ticket


class TicketFilter:
    """
    Filtrowanie ticketów / Ticket filtering.
    """

    @staticmethod
    def filter_tickets(
            tickets: List[Ticket],
            filter_resolved: bool = True,
            filter_closed: bool = True,
            exclude_ids: Optional[set] = None,
    ) -> List[Ticket]:
        """
        Filtruj tickety przed transferem.
        Filter tickets before transfer.
        """
        filtered = []
        excluded_count = 0

        for ticket in tickets:
            # Filtruj po statusie / Filter by status
            if filter_resolved and ticket.status == TicketStatus.RESOLVED:
                excluded_count += 1
                continue
            if filter_closed and ticket.status == TicketStatus.CLOSED:
                excluded_count += 1
                continue

            # Filtruj po ID (już przeniesione) / Filter by ID (already transferred)
            if exclude_ids and ticket.ticket_id in exclude_ids:
                excluded_count += 1
                continue

            filtered.append(ticket)

        logger.info(
            f"Filtered tickets: {len(filtered)} to transfer, "
            f"{excluded_count} excluded (resolved/closed/duplicate)"
        )
        return filtered


class TicketTransformer:
    """
    Główny klasa transformująca / Main transformation class.
    Łączy walidację, enrichment i filtrowanie.
    Combines validation, enrichment and filtering.
    """

    def __init__(self, auto_categorize: bool = True, auto_priority: bool = True):
        self.auto_categorize = auto_categorize
        self.auto_priority = auto_priority
        self.validator = TicketValidator()
        self.enricher = TicketEnricher()
        self.filter = TicketFilter()

    def process_ticket(self, ticket: Ticket) -> Tuple[Optional[Ticket], List[str]]:
        """
        Przetwórz pojedynczy ticket: walidacja + enrichment.
        Process single ticket: validation + enrichment.

        Returns:
            (processed_ticket or None, list_of_errors)
        """
        # 1. Walidacja / Validation
        is_valid, errors = self.validator.validate(ticket)
        if not is_valid:
            return None, errors

        # 2. Enrichment
        ticket = self.enricher.enrich(
            ticket,
            auto_categorize=self.auto_categorize,
            auto_priority=self.auto_priority,
        )

        # 3. Normalizacja danych / Data normalization
        ticket = self._normalize(ticket)

        return ticket, []

    def _normalize(self, ticket: Ticket) -> Ticket:
        """
        Normalizuj dane ticketa / Normalize ticket data.
        """
        # Oczyść opis / Clean description
        if ticket.description:
            ticket.description = ticket.description.strip()

        # Sortuj konwersację po czasie / Sort conversation by time
        ticket.conversation.sort(key=lambda m: m.timestamp)

        # Normalizuj nazwę klienta / Normalize client name
        if ticket.client_name:
            ticket.client_name = ticket.client_name.strip().title()

        return ticket

    def process_batch(
            self,
            tickets: List[Ticket],
            filter_resolved: bool = True,
            filter_closed: bool = True,
            exclude_ids: Optional[set] = None,
    ) -> Tuple[List[Ticket], List[TransferResult]]:
        """
        Przetwórz batch ticketów / Process batch of tickets.

        Returns:
            (list_of_valid_tickets, list_of_failed_results)
        """
        # 1. Filtrowanie / Filtering
        filtered = self.filter.filter_tickets(
            tickets,
            filter_resolved=filter_resolved,
            filter_closed=filter_closed,
            exclude_ids=exclude_ids,
        )

        # 2. Transformacja każdego ticketa / Transform each ticket
        valid_tickets = []
        failed_results = []

        for ticket in filtered:
            processed, errors = self.process_ticket(ticket)
            if processed:
                valid_tickets.append(processed)
            else:
                failed_results.append(TransferResult(
                    ticket_id=ticket.ticket_id,
                    success=False,
                    error_message="; ".join(errors),
                    timestamp=datetime.now().isoformat(),
                ))

        logger.info(
            f"Batch processing: {len(tickets)} input → "
            f"{len(valid_tickets)} valid, {len(failed_results)} failed"
        )
        return valid_tickets, failed_results
