"""
Entry point dla pipeline'a / Entry point for the pipeline.

Użycie / Usage:
    # Jednorazowe uruchomienie / One-time run:
    python main.py

    # Tryb ciągły (polling) / Continuous mode:
    python main.py --continuous

    # Z konkretnym interwałem / With specific interval:
    python main.py --continuous --interval 120

    # Zmiennymi środowiskowymi / Environment variables:
    export CLIENT_DASHBOARD_URL="https://..."
    export CLIENT_DASHBOARD_API_KEY="..."
    export HELPCENTER_URL="https://..."
    export HELPCENTER_API_KEY="..."
"""
import argparse
import json
import sys
import os
import logging

# Dodaj ścieżkę do importów / Add path for imports
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from config import PipelineConfig, SourceConfig, DestinationConfig
from pipeline import TicketPipeline

logger = logging.getLogger(__name__)


def parse_args():
    parser = argparse.ArgumentParser(
        description="Ticket Pipeline - transfer between client dashboard and helpcenter"
    )
    parser.add_argument(
        "--continuous", "-c",
        action="store_true",
        help="Run in continuous mode (polling) / Uruchom w trybie ciągłym"
    )
    parser.add_argument(
        "--interval", "-i",
        type=int,
        default=300,
        help="Poll interval in seconds / Interwał w sekundach (default: 300)"
    )
    parser.add_argument(
        "--no-categorize",
        action="store_true",
        help="Disable auto-categorization / Wyłącz auto-kategoryzację"
    )
    parser.add_argument(
        "--no-priority",
        action="store_true",
        help="Disable auto-prioritization / Wyłącz auto-priorytetyzację"
    )
    parser.add_argument(
        "--no-deduplicate",
        action="store_true",
        help="Disable deduplication check / Wyłącz sprawdzanie duplikatów"
    )
    parser.add_argument(
        "--include-resolved",
        action="store_true",
        help="Include resolved tickets / Uwzględnij rozwiązane tickety"
    )
    parser.add_argument(
        "--include-closed",
        action="store_true",
        help="Include closed tickets / Uwzględnij zamknięte tickety"
    )
    parser.add_argument(
        "--log-level",
        choices=["DEBUG", "INFO", "WARNING", "ERROR"],
        default="INFO",
        help="Logging level / Poziom logowania"
    )
    return parser.parse_args()


def main():
    args = parse_args()

    # Build configuration / Konfiguracja
    config = PipelineConfig(
        source=SourceConfig(),
        destination=DestinationConfig(),
        auto_categorize=not args.no_categorize,
        auto_priority=not args.no_priority,
        filter_resolved=not args.include_resolved,
        filter_closed=not args.include_closed,
        deduplicate=not args.no_deduplicate,
        log_level=args.log_level,
        poll_interval=args.interval,
    )

    # Initialize and run pipeline / Inicjalizacja i uruchomienie
    pipeline = TicketPipeline(config)

    print("=" * 60)
    print("  Ticket Pipeline")
    print("  Transfer: Client Dashboard → HelpCenter")
    print("=" * 60)
    print(f"  Mode: {'continuous' if args.continuous else 'one-time'}")
    if args.continuous:
        print(f"  Poll interval: {args.interval}s")
    print(f"  Auto-categorize: {config.auto_categorize}")
    print(f"  Auto-prioritize: {config.auto_priority}")
    print(f"  Deduplicate: {config.deduplicate}")
    print(f"  Filter resolved: {config.filter_resolved}")
    print(f"  Filter closed: {config.filter_closed}")
    print("=" * 60)

    if args.continuous:
        pipeline.run_continuous()
    else:
        metrics = pipeline.run_once()
        summary = metrics.summary()
        print("\n" + "=" * 60)
        print("  PIPELINE SUMMARY / PODSUMOWANIE")
        print("=" * 60)
        print(json.dumps(summary, indent=2, ensure_ascii=False))

        if summary["failed"] > 0:
            print(f"\n  ⚠ {summary['failed']} tickets failed!")
            sys.exit(1)
        elif summary["transferred"] == 0 and summary["fetched"] == 0:
            print("\n  ℹ No tickets to transfer. Brak ticketów do przeniesienia.")
        else:
            print(f"\n  ✓ Successfully transferred {summary['transferred']} tickets")

        sys.exit(0)


if __name__ == "__main__":
    main()
