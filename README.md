# RyzeHub

Pipeline do przetwarzania i transferu danych zgłoszeń (ticketów) między **dashboardem klienta** a **helpcenter**.

Pipeline for processing and transferring ticket data between **client dashboard** and **helpcenter**.

---

## Przenoszone dane / Transferred Data

| Pole / Field | Opis / Description |
|---|---|
| `ticket_id` | ID zgłoszenia |
| `ticket_type` | Rodzaj (bug, feature_request, question, complaint, technical_issue, other) |
| `description` | Opis zgłoszenia |
| `conversation` | Wstępna konwersacja (nadawca, rola, treść, czas) |

---

## Architektura / Architecture

```
┌─────────────────────┐     ┌──────────────────────┐     ┌────────────────────┐
│  Client Dashboard   │     │      Pipeline        │     │     HelpCenter     │
│   (Source REST)     │────▶│  Transform/Enrich    │────▶│  (Destination REST)│
│                     │     │  Validate/Filter     │     │                    │
└─────────────────────┘     └──────────────────────┘     └────────────────────┘
        GET /tickets               processing                  POST /tickets
```

### Przepływ / Flow

1. **Pobierz** tickety z dashboardu klienta (paginacja, retry)
2. **Filtruj** — pomiń rozwiązane, zamknięte, duplikaty
3. **Waliduj** — sprawdź wymagane pola (id, typ, opis, konwersacja)
4. **Wzbogać** — auto-kategoryzacja, auto-priorytet, tagi
5. **Transfer** — utwórz tickety w helpcenter
6. **Oznacz** — oznacz jako przeniesione w dashboardzie klienta

---

## Struktura projektu / Project Structure

```
ticket-pipeline/
├── main.py                # Entry point (CLI)
├── pipeline.py            # Główny pipeline / Main pipeline orchestrator
├── source_client.py       # Klient API dashboardu klienta
├── destination_client.py  # Klient API helpcenter
├── transformer.py         # Walidacja, enrichment, filtrowanie
├── models.py              # Modele danych (Ticket, ConversationMessage, etc.)
├── config.py              # Konfiguracja (env variables)
├── tests.py               # Testy / Tests (15 tests)
├── requirements.txt       # Dependencies
└── README.md
```

---

## Szybki start / Quick Start

### 1. Zainstaluj zależności / Install dependencies

```bash
pip install -r requirements.txt
```

### 2. Ustaw zmienne środowiskowe / Set environment variables

```bash
export CLIENT_DASHBOARD_URL="https://your-client-dashboard.com/api/v1"
export CLIENT_DASHBOARD_API_KEY="your-api-key"
export HELPCENTER_URL="https://your-helpcenter.com/api/v1"
export HELPCENTER_API_KEY="your-api-key"
```

### 3. Uruchom / Run

```bash
# Jednorazowo / One-time run
python main.py

# Tryb ciągły (co 5 min) / Continuous mode (every 5 min)
python main.py --continuous

# Z interwałem 2 min / With 2-minute interval
python main.py --continuous --interval 120

# Debug logging
python main.py --log-level DEBUG
```

---

## Opcje CLI / CLI Options

| Opcja / Option | Opis / Description |
|---|---|
| `--continuous`, `-c` | Tryb ciągły (polling) / Continuous mode |
| `--interval N`, `-i N` | Interwał w sekundach / Interval in seconds |
| `--no-categorize` | Wyłącz auto-kategoryzację / Disable auto-categorization |
| `--no-priority` | Wyłącz auto-priorytet / Disable auto-prioritization |
| `--no-deduplicate` | Wyłącz deduplikację / Disable deduplication |
| `--include-resolved` | Uwzględnij rozwiązane / Include resolved tickets |
| `--include-closed` | Uwzględnij zamknięte / Include closed tickets |
| `--log-level` | Poziom logów: DEBUG/INFO/WARNING/ERROR |

---

## Transformacja danych / Data Transformation

### Auto-kategoryzacja / Auto-categorization
Na podstawie słów kluczowych w opisie i konwersacji:
- `technical` — błąd, error, crash, awaria, 500, 404
- `billing` — faktura, płatność, invoice, payment
- `account` — konto, logowanie, hasło, login
- `feature` — proponuję, sugestia, feature request
- `integration` — api, webhook, integracja
- `performance` — wolno, wydajność, slow

### Auto-priorytet / Auto-priority
- `CRITICAL` — krytyczny, produkcja, outage, emergency
- `HIGH` — ważne, pilne, urgent, blokujące
- `MEDIUM` — domyślny / default
- `LOW` — kiedyś, nie pilne, pytanie

---

## Testy / Tests

```bash
python tests.py
```

15 testów pokrywających: walidację, kategoryzację, priorytetyzację, filtrowanie, serializację.

---

## API Endpoints (oczekiwane / expected)

### Client Dashboard (Source)
- `GET /api/v1/tickets?page=1&per_page=50&status=new,in_progress` — lista ticketów
- `GET /api/v1/tickets/{id}` — pojedynczy ticket
- `PATCH /api/v1/tickets/{id}/status` — aktualizacja statusu

### HelpCenter (Destination)
- `POST /api/v1/tickets` — utwórz ticket
- `PUT /api/v1/tickets/{id}` — aktualizuj ticket
- `GET /api/v1/tickets/lookup?source_ticket_id=X` — sprawdź duplikat

---

## License

MIT
