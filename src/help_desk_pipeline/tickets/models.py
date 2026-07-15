"""
Modele danych dla ticketów / Data models for tickets.
"""
from dataclasses import dataclass, field, asdict
from datetime import datetime
from typing import List, Optional
from enum import Enum


class TicketType(Enum):
    """Rodzaj zgłoszenia / Ticket type."""
    BUG = "bug"
    FEATURE_REQUEST = "feature_request"
    QUESTION = "question"
    COMPLAINT = "complaint"
    TECHNICAL_ISSUE = "technical_issue"
    OTHER = "other"


class TicketPriority(Enum):
    """Priorytet zgłoszenia / Ticket priority."""
    LOW = "low"
    MEDIUM = "medium"
    HIGH = "high"
    CRITICAL = "critical"


class TicketStatus(Enum):
    """Status zgłoszenia / Ticket status."""
    NEW = "new"
    IN_PROGRESS = "in_progress"
    WAITING = "waiting"
    RESOLVED = "resolved"
    CLOSED = "closed"


@dataclass
class ConversationMessage:
    """Wiadomość w konwersacji / Message in conversation."""
    sender: str
    role: str  # 'client' | 'agent' | 'system'
    content: str
    timestamp: str
    message_id: Optional[str] = None

    def to_dict(self) -> dict:
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict) -> "ConversationMessage":
        return cls(
            sender=data.get("sender", ""),
            role=data.get("role", "client"),
            content=data.get("content", ""),
            timestamp=data.get("timestamp", ""),
            message_id=data.get("message_id"),
        )


@dataclass
class Ticket:
    """
    Model zgłoszenia / Ticket model.
    Przenoszone dane: id, rodzaj, opis, wstępna konwersacja.
    Transferred data: id, type, description, initial conversation.
    """
    ticket_id: str
    ticket_type: TicketType
    description: str
    conversation: List[ConversationMessage] = field(default_factory=list)

    # Metadane / Metadata
    priority: TicketPriority = TicketPriority.MEDIUM
    status: TicketStatus = TicketStatus.NEW
    created_at: str = ""
    updated_at: str = ""
    client_id: str = ""
    client_name: str = ""
    category: str = ""
    tags: List[str] = field(default_factory=list)

    # Pole do śledzenia transferu / Transfer tracking field
    transferred_at: Optional[str] = None
    helpcenter_ticket_id: Optional[str] = None

    def to_dict(self) -> dict:
        """Serializacja do dict (dla API) / Serialize to dict (for API)."""
        return {
            "ticket_id": self.ticket_id,
            "ticket_type": self.ticket_type.value,
            "description": self.description,
            "conversation": [msg.to_dict() for msg in self.conversation],
            "priority": self.priority.value,
            "status": self.status.value,
            "created_at": self.created_at,
            "updated_at": self.updated_at,
            "client_id": self.client_id,
            "client_name": self.client_name,
            "category": self.category,
            "tags": self.tags,
            "transferred_at": self.transferred_at,
            "helpcenter_ticket_id": self.helpcenter_ticket_id,
        }

    @classmethod
    def from_dict(cls, data: dict) -> "Ticket":
        """Deserializacja z dict / Deserialize from dict."""
        conversation = [
            ConversationMessage.from_dict(msg)
            for msg in data.get("conversation", [])
        ]
        return cls(
            ticket_id=data.get("ticket_id", ""),
            ticket_type=TicketType(data.get("ticket_type", "other")),
            description=data.get("description", ""),
            conversation=conversation,
            priority=TicketPriority(data.get("priority", "medium")),
            status=TicketStatus(data.get("status", "new")),
            created_at=data.get("created_at", ""),
            updated_at=data.get("updated_at", ""),
            client_id=data.get("client_id", ""),
            client_name=data.get("client_name", ""),
            category=data.get("category", ""),
            tags=data.get("tags", []),
            transferred_at=data.get("transferred_at"),
            helpcenter_ticket_id=data.get("helpcenter_ticket_id"),
        )


@dataclass
class TransferResult:
    """Wynik transferu pojedynczego ticketa / Result of single ticket transfer."""
    ticket_id: str
    success: bool
    helpcenter_ticket_id: Optional[str] = None
    error_message: Optional[str] = None
    timestamp: str = ""
