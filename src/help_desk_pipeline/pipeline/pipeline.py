"""
Główny pipeline transferu ticketów / Main ticket transfer pipeline.
Orkiestruje pobieranie, transformację i wysyłanie.
Orchestrates fetching, transformation and sending.
"""
import time
import logging
from datetime import datetime
from typing import List, Optional

from tickets.models import Ticket, TransferResult, TicketStatus
from config.config import PipelineConfig
from source_client import ClientDashboardClient
from destination_client import HelpCenterClient
from transformer import TicketTransformer

logger = logging.getLogger(__name__)


class PipelineMetrics:
    """Metryki pipeline'a / Pipeline metrics."""

    def __init__(self):
        self.started_at: Optional[datetime] = None
        self.finished_at: Optional[datetime] = None
        self.fetched_count: int = 0
        self.filtered_count: int = 0
        self.valid_count: int = 0
        self.transferred_count: int = 0
        self.failed_count: int = 0
        self.errors: List[str] = []

    def start(self):
        self.started_at = datetime.now()

    def finish(self):
        self.finished_at = datetime.now()

    @property
    def duration_seconds(self) -> float:
        if self.started_at and self.finished_at:
            return (self.finished_at - self.started_at).total_seconds()
        return 0.0

    def summary(self) -> dict:
        return {
            "started_at": self.started_at.isoformat() if self.started_at else None,
            "finished_at": self.finished_at.isoformat() if self.finished_at else None,
            "duration_seconds": round(self.duration_seconds, 2),
            "fetched": self.fetched_count,
            "filtered_out": self.filtered_count,
            "valid_for_transfer": self.valid_count,
            "transferred": self.transferred_count,
            "failed": self.failed_count,
            "errors": self.errors[-10:],  # Ostatnie 10 błędów
        }


class TicketPipeline:
    """
    Pipeline transferu ticketów między dashboardem klienta a helpcenter.
    Pipeline for transferring tickets between client dashboard and helpcenter.

    Przepływ / Flow:
    1. Pobierz tickety z dashboardu klienta / Fetch tickets from client dashboard
    2. Filtruj (pomiń rozwiązane/zamknięte/duplikaty) / Filter (skip resolved/closed/duplicates)
    3. Waliduj wymagane pola / Validate required fields
    4. Wzbogać dane (kategoria, priorytet, tagi) / Enrich data (category, priority, tags)
    5. Transfer do helpcenter / Transfer to helpcenter
    6. Oznacz jako przeniesione / Mark as transferred
    """

    def __init__(self, config: PipelineConfig):
        self.config = config
        self.source = ClientDashboardClient(config.source)
        self.destination = HelpCenterClient(config.destination)
        self.transformer = TicketTransformer(
            auto_categorize=config.auto_categorize,
            auto_priority=config.auto_priority,
        )
        self._setup_logging()

    def _setup_logging(self):
        """Konfiguracja logowania / Logging setup."""
        logging.basicConfig(
            level=getattr(logging, self.config.log_level.upper(), logging.INFO),
            format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
            datefmt="%Y-%m-%d %H:%M:%S",
        )

    def run_once(self) -> PipelineMetrics:
        """
        Uruchom pipeline raz / Run pipeline once.

        Wykonuje pojedynczą iterację: pobierz → transformuj → wyślij.
        Performs single iteration: fetch → transform → send.
        """
        metrics = PipelineMetrics()
        metrics.start()
        logger.info("=" * 60)
        logger.info("Pipeline run started / Rozpoczęto przetwarzanie")
        logger.info("=" * 60)

        try:

            logger.info("Step 1: Fetching tickets from client dashboard...")
            statuses_to_fetch = self._get_statuses_to_fetch()
            tickets = self._fetch_all_pages(statuses_to_fetch)
            metrics.fetched_count = len(tickets)

            if not tickets:
                logger.info("No tickets to process. Brak ticketów do przetworzenia.")
                metrics.finish()
                return metrics


            already_transferred = set()
            if self.config.deduplicate:
                logger.info("Step 2: Checking for duplicates...")
                already_transferred = self._check_duplicates(tickets)
                logger.info(f"Found {len(already_transferred)} already transferred tickets")

            logger.info("Step 3: Transforming tickets (validate + enrich)...")
            valid_tickets, failed_results = self.transformer.process_batch(
                tickets,
                filter_resolved=self.config.filter_resolved,
                filter_closed=self.config.filter_closed,
                exclude_ids=already_transferred if self.config.deduplicate else None,
            )
            metrics.valid_count = len(valid_tickets)
            metrics.filtered_count = metrics.fetched_count - len(valid_tickets) - len(failed_results)
            metrics.failed_count += len(failed_results)

            if not valid_tickets:
                logger.info("No valid tickets after transformation.")
                metrics.finish()
                return metrics


            logger.info(f"Step 4: Transferring {len(valid_tickets)} tickets to helpcenter...")
            transfer_results = self._transfer_tickets(valid_tickets)

            for result in transfer_results:
                if result.success:
                    metrics.transferred_count += 1
                    # Oznacz jako przeniesiony w dashboardzie klienta
                    # Mark as transferred in client dashboard
                    self.source.mark_as_transferred(
                        result.ticket_id,
                        result.helpcenter_ticket_id or ""
                    )
                else:
                    metrics.failed_count += 1
                    metrics.errors.append(
                        f"Ticket {result.ticket_id}: {result.error_message}"
                    )

        except Exception as e:
            logger.error(f"Pipeline error: {e}", exc_info=True)
            metrics.errors.append(f"Pipeline error: {str(e)}")

        metrics.finish()
        logger.info("=" * 60)
        logger.info(f"Pipeline finished. Summary / Podsumowanie: {metrics.summary()}")
        logger.info("=" * 60)
        return metrics

    def run_continuous(self):
        """
        Uruchom pipeline w trybie ciągłym (polling).
        Run pipeline in continuous mode (polling).
        """
        logger.info(f"Starting continuous pipeline (poll every {self.config.poll_interval}s)")
        while True:
            try:
                self.run_once()
            except KeyboardInterrupt:
                logger.info("Pipeline stopped by user. Pipeline zatrzymany przez użytkownika.")
                break
            except Exception as e:
                logger.error(f"Pipeline run error: {e}", exc_info=True)

            logger.info(f"Waiting {self.config.poll_interval}s until next run...")
            time.sleep(self.config.poll_interval)

    def _get_statuses_to_fetch(self) -> List[TicketStatus]:
        """
        Określ jakie statusy pobrać / Determine which statuses to fetch.
        """
        all_statuses = list(TicketStatus)
        if self.config.filter_resolved:
            all_statuses = [s for s in all_statuses if s != TicketStatus.RESOLVED]
        if self.config.filter_closed:
            all_statuses = [s for s in all_statuses if s != TicketStatus.CLOSED]
        return all_statuses

    def _fetch_all_pages(self, status_filter: List[TicketStatus]) -> List[Ticket]:
        """
        Pobierz wszystkie strony z paginacją / Fetch all pages with pagination.
        """
        all_tickets = []
        page = 1

        while True:
            batch = self.source.fetch_tickets(
                status_filter=status_filter,
                page=page,
            )
            if not batch:
                break
            all_tickets.extend(batch)

            if len(batch) < self.config.source.batch_size:
                break  # Ostatnia strona / Last page
            page += 1

        return all_tickets

    def _check_duplicates(self, tickets: List[Ticket]) -> set:
        """
        Sprawdź które tickety już istnieją w helpcenter.
        Check which tickets already exist in helpcenter.
        """
        already_exists = set()
        for ticket in tickets:
            existing_id = self.destination.check_ticket_exists(ticket.ticket_id)
            if existing_id:
                already_exists.add(ticket.ticket_id)
                logger.debug(f"Ticket {ticket.ticket_id} already in helpcenter as {existing_id}")
        return already_exists

    def _transfer_tickets(self, tickets: List[Ticket]) -> List[TransferResult]:
        """
        Przenieś tickety do helpcenter.
        Transfer tickets to helpcenter.
        """
        results = []
        now = datetime.now().isoformat()

        for ticket in tickets:
            try:
                helpcenter_id = self.destination.create_ticket(ticket)
                results.append(TransferResult(
                    ticket_id=ticket.ticket_id,
                    success=True,
                    helpcenter_ticket_id=helpcenter_id,
                    timestamp=now,
                ))
                logger.info(
                    f"✓ Transferred ticket {ticket.ticket_id} → {helpcenter_id}"
                )
            except Exception as e:
                results.append(TransferResult(
                    ticket_id=ticket.ticket_id,
                    success=False,
                    error_message=str(e),
                    timestamp=now,
                ))
                logger.error(f"✗ Failed to transfer ticket {ticket.ticket_id}: {e}")

        return results
