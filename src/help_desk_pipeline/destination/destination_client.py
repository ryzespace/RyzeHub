"""
Klient API helpcenter (destination) / Helpcenter API client (destination).
Przesyła przetworzone tickety do helpcenter.
Sends processed tickets to helpcenter.
"""
import time
import logging
from typing import Optional, List

import requests

from models import Ticket
from config import DestinationConfig

logger = logging.getLogger(__name__)


class HelpCenterClient:
    """
    Klient łączący się z helpcenter.
    Client connecting to the helpcenter.
    """

    def __init__(self, config: DestinationConfig):
        self.config = config
        self.session = requests.Session()
        self.session.headers.update({
            "Authorization": f"Bearer {config.api_key}",
            "Content-Type": "application/json",
            "Accept": "application/json",
        })

    def _request_with_retry(self, method: str, url: str, **kwargs) -> requests.Response:
        """
        Wykonaj request z retry.
        Execute request with retry.
        """
        last_exception = None
        for attempt in range(1, self.config.max_retries + 1):
            try:
                response = self.session.request(
                    method, url,
                    timeout=self.config.timeout,
                    **kwargs
                )
                response.raise_for_status()
                return response
            except requests.RequestException as e:
                last_exception = e
                logger.warning(
                    f"HelpCenter request failed (attempt {attempt}/{self.config.max_retries}): {e}"
                )
                if attempt < self.config.max_retries:
                    time.sleep(self.config.retry_delay * attempt)

        raise ConnectionError(
            f"HelpCenter request failed after {self.config.max_retries} retries: {last_exception}"
        )

    def create_ticket(self, ticket: Ticket) -> str:
        """
        Utwórz ticket w helpcenter i zwróć nowe ID.
        Create ticket in helpcenter and return new ID.

        Args:
            ticket: Przetworzony ticket / Processed ticket

        Returns:
            ID ticketa w helpcenter / Ticket ID in helpcenter
        """
        url = f"{self.config.base_url}/tickets"
        payload = {
            "ticket_id": ticket.ticket_id,
            "ticket_type": ticket.ticket_type.value,
            "description": ticket.description,
            "conversation": [msg.to_dict() for msg in ticket.conversation],
            "priority": ticket.priority.value,
            "category": ticket.category,
            "tags": ticket.tags,
            "client_id": ticket.client_id,
            "client_name": ticket.client_name,
            "source": "client_dashboard",
            "source_ticket_id": ticket.ticket_id,
        }

        logger.info(f"Creating ticket in helpcenter (source_id={ticket.ticket_id})")
        response = self._request_with_retry("POST", url, json=payload)
        result = response.json()

        new_id = result.get("ticket_id", result.get("id", ""))
        logger.info(f"Created helpcenter ticket: {new_id} (from source: {ticket.ticket_id})")
        return new_id

    def update_ticket(self, helpcenter_ticket_id: str, ticket: Ticket) -> bool:
        """
        Zaktualizuj istniejący ticket w helpcenter.
        Update existing ticket in helpcenter.
        """
        url = f"{self.config.base_url}/tickets/{helpcenter_ticket_id}"
        payload = {
            "description": ticket.description,
            "conversation": [msg.to_dict() for msg in ticket.conversation],
            "priority": ticket.priority.value,
            "category": ticket.category,
            "tags": ticket.tags,
        }

        logger.info(f"Updating helpcenter ticket: {helpcenter_ticket_id}")
        self._request_with_retry("PUT", url, json=payload)
        return True

    def check_ticket_exists(self, source_ticket_id: str) -> Optional[str]:
        """
        Sprawdź czy ticket ze źródłowym ID już istnieje w helpcenter.
        Check if ticket with source ID already exists in helpcenter.

        Returns:
            Helpcenter ticket ID jeśli istnieje, None w przeciwnym razie.
            Helpcenter ticket ID if exists, None otherwise.
        """
        url = f"{self.config.base_url}/tickets/lookup"
        params = {"source_ticket_id": source_ticket_id}

        try:
            response = self._request_with_retry("GET", url, params=params)
            result = response.json()
            if result.get("found"):
                return result.get("ticket_id")
        except Exception:
            pass  # Nie znaleziono lub błąd / Not found or error
        return None

    def batch_create_tickets(self, tickets: List[Ticket]) -> List[dict]:
        """
        Utwórz wiele ticketów w jednej operacji batch.
        Create multiple tickets in one batch operation.
        """
        url = f"{self.config.base_url}/tickets/batch"
        payload = {
            "tickets": [
                {
                    "ticket_id": t.ticket_id,
                    "ticket_type": t.ticket_type.value,
                    "description": t.description,
                    "conversation": [m.to_dict() for m in t.conversation],
                    "priority": t.priority.value,
                    "category": t.category,
                    "tags": t.tags,
                    "client_id": t.client_id,
                    "client_name": t.client_name,
                    "source": "client_dashboard",
                    "source_ticket_id": t.ticket_id,
                }
                for t in tickets
            ]
        }

        logger.info(f"Batch creating {len(tickets)} tickets in helpcenter")
        response = self._request_with_retry("POST", url, json=payload)
        result = response.json()
        return result.get("results", [])
