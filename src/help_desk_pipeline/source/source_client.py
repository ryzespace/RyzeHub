"""
Klient API dashboardu klienta (source) / Client dashboard API client (source).
Pobiera tickety do przetworzenia / Fetches tickets for processing.
"""
import time
import logging
from typing import List, Optional

import requests

from models import Ticket, TicketStatus
from config import SourceConfig

logger = logging.getLogger(__name__)


class ClientDashboardClient:
    """
    Klient łączący się z dashboardem klienta.
    Client connecting to the client dashboard.
    """

    def __init__(self, config: SourceConfig):
        self.config = config
        self.session = requests.Session()
        self.session.headers.update({
            "Authorization": f"Bearer {config.api_key}",
            "Content-Type": "application/json",
            "Accept": "application/json",
        })

    def _request_with_retry(self, method: str, url: str, **kwargs) -> requests.Response:
        """
        Wykonaj request z retry w razie błędu.
        Execute request with retry on failure.
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
                    f"Request failed (attempt {attempt}/{self.config.max_retries}): {e}"
                )
                if attempt < self.config.max_retries:
                    time.sleep(self.config.retry_delay * attempt)

        raise ConnectionError(
            f"Failed after {self.config.max_retries} retries: {last_exception}"
        )

    def fetch_tickets(
        self,
        status_filter: Optional[List[TicketStatus]] = None,
        page: int = 1,
        per_page: Optional[int] = None
    ) -> List[Ticket]:
        """
        Pobierz tickety z dashboardu klienta.
        Fetch tickets from the client dashboard.

        Args:
            status_filter: Lista statusów do pobrania / List of statuses to fetch
            page: Numer strony / Page number
            per_page: Liczba wyników na stronę / Results per page

        Returns:
            Lista obiektów Ticket / List of Ticket objects
        """
        per_page = per_page or self.config.batch_size
        params = {
            "page": page,
            "per_page": per_page,
        }
        if status_filter:
            params["status"] = ",".join(s.value for s in status_filter)

        url = f"{self.config.base_url}/tickets"
        logger.info(f"Fetching tickets from {url} (page={page}, per_page={per_page})")

        response = self._request_with_retry("GET", url, params=params)
        data = response.json()

        tickets_raw = data.get("tickets", data.get("data", []))
        tickets = [Ticket.from_dict(raw) for raw in tickets_raw]
        logger.info(f"Fetched {len(tickets)} tickets from client dashboard")
        return tickets

    def fetch_ticket_by_id(self, ticket_id: str) -> Ticket:
        """
        Pobierz pojedynczy ticket po ID.
        Fetch single ticket by ID.
        """
        url = f"{self.config.base_url}/tickets/{ticket_id}"
        logger.info(f"Fetching ticket {ticket_id}")

        response = self._request_with_retry("GET", url)
        ticket_data = response.json()
        return Ticket.from_dict(ticket_data)

    def mark_as_transferred(self, ticket_id: str, helpcenter_id: str) -> bool:
        """
        Oznacz ticket jako przeniesiony do helpcenter.
        Mark ticket as transferred to helpcenter.
        """
        url = f"{self.config.base_url}/tickets/{ticket_id}/status"
        payload = {
            "status": "transferred",
            "helpcenter_ticket_id": helpcenter_id,
        }
        try:
            self._request_with_retry("PATCH", url, json=payload)
            logger.info(f"Marked ticket {ticket_id} as transferred (helpcenter: {helpcenter_id})")
            return True
        except Exception as e:
            logger.error(f"Failed to mark ticket {ticket_id} as transferred: {e}")
            return False

    def get_total_count(self, status_filter: Optional[List[TicketStatus]] = None) -> int:
        """
        Pobierz całkowitą liczbę ticketów do przetworzenia.
        Get total number of tickets to process.
        """
        params = {"count_only": "true"}
        if status_filter:
            params["status"] = ",".join(s.value for s in status_filter)

        url = f"{self.config.base_url}/tickets"
        response = self._request_with_retry("GET", url, params=params)
        data = response.json()
        return data.get("total", 0)
