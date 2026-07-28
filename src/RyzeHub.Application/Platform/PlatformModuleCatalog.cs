using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform;

/// <summary>
/// Describes every capability offered by the hub platform layer.
/// </summary>
public static class PlatformModuleCatalog
{
    private static readonly PlatformModuleDefinition[] Modules =
    [
        new(
            "real_time_event_system",
            "Real Time Event System",
            "Dostarcza zdarzenia w czasie rzeczywistym do web, mobile i desktop.",
            ["status zgłoszeń", "płatności", "aktywacja serwera", "ostrzeżenia bezpieczeństwa", "wiadomości"],
            ["Natychmiastowa informacja zwrotna dla użytkownika", "Łatwiejsza synchronizacja dashboardów i paneli"]),
        new(
            "notification_center",
            "Notification Center",
            "Centralizuje wysyłkę i zarządzanie powiadomieniami dla wszystkich kanałów.",
            ["mobile push", "desktop notifications", "email", "sms", "discord webhooks", "slack webhooks"],
            ["Użytkownik zarządza wszystkimi powiadomieniami z jednego miejsca", "Łatwiejsze reguły routingu i priorytetów"]),
        new(
            "audit_log_engine",
            "Audit Log Engine",
            "Rejestruje krytyczne działania użytkowników, systemów i administratorów.",
            ["logowania", "zmiany ustawień", "operacje administracyjne", "operacje finansowe", "zmiany uprawnień"],
            ["Większe zaufanie do platformy", "Szybsze diagnozowanie incydentów i regresji"]),
        new(
            "permission_role_hub",
            "Permission & Role Hub",
            "Łączy role biznesowe z granularnymi uprawnieniami operacyjnymi, synchronizowanymi z RyzeAuth.",
            ["User", "Seller", "Moderator", "Support", "Admin", "SuperAdmin", "server:create", "server:delete", "billing:view", "billing:manage"],
            ["Skalowalny model dostępu dla nowych produktów", "Precyzyjna kontrola operacji administracyjnych i finansowych"]),
        new(
            "presence_system",
            "Presence System",
            "Śledzi stan użytkownika, aktywność i aktywne urządzenia.",
            ["online/offline", "ostatnia aktywność", "aktywne urządzenia"],
            ["Lepsza obsługa zgłoszeń i komunikacji", "Dodatkowy sygnał dla bezpieczeństwa konta"]),
        new(
            "device_management",
            "Device Management",
            "Zapewnia pełny panel zarządzania urządzeniami w stylu Google lub Steam.",
            ["lista urządzeń", "wylogowanie urządzenia", "zaufane urządzenia", "wykrywanie nowych urządzeń"],
            ["Jedno miejsce kontroli urządzeń użytkownika", "Szybsza reakcja na podejrzane aktywności"]),
        new(
            "session_manager",
            "Session Manager",
            "Centralnie zarządza sesjami web, mobile i desktop w oparciu o sesje Keycloak z RyzeAuth.",
            ["sesje web", "sesje mobile", "sesje desktop"],
            ["Jedno miejsce kontroli dostępu", "Spójne wymuszanie polityk bezpieczeństwa"]),
        new(
            "api_gateway",
            "API Gateway",
            "Udostępnia hub jako pojedynczy punkt wejścia do mikroserwisów.",
            ["rate limiting", "auth", "monitoring", "caching"],
            ["Uproszczony routing ruchu klienta", "Jednolite egzekwowanie autoryzacji i limitów"]),
        new(
            "distributed_cache",
            "Distributed Cache",
            "Przyspiesza platformę przez cache współdzielony między usługami.",
            ["redis", "sesje", "ustawienia", "często używane dane"],
            ["Szybsze dashboardy i API", "Mniejsze obciążenie usług źródłowych"]),
        new(
            "activity_feed",
            "Activity Feed",
            "Buduje oś czasu działań użytkownika i systemu.",
            ["utworzono VPS", "dodano metodę płatności", "wysłano zgłoszenie"],
            ["Pełna przejrzystość działań na koncie", "Lepszy kontekst dla supportu i adminów"]),
        new(
            "internal_messaging",
            "Internal Messaging",
            "Udostępnia komunikację wewnętrzną bez opuszczania platformy.",
            ["Client ↔ Support", "Client ↔ Admin", "Admin ↔ Moderator"],
            ["Komunikacja pozostaje w jednym ekosystemie", "Łatwiejsze powiązanie rozmów ze zgłoszeniami i audytem"]),
        new(
            "feature_flags",
            "Feature Flags",
            "Pozwala włączać i wyłączać funkcje bez nowego deployu.",
            ["betaBilling=true", "newDashboard=false"],
            ["Bezpieczne rollouty nowych funkcji", "Lepsze eksperymenty i kontrola beta testów"]),
        new(
            "health_monitoring",
            "Health Monitoring",
            "Monitoruje stan API, baz danych, mikroserwisów i kolejek.",
            ["API", "bazy danych", "mikroserwisy", "kolejki"],
            ["Szybsze wykrywanie awarii", "Lepsza podstawa do automatycznych alertów"]),
        new(
            "telemetry_analytics",
            "Telemetry & Analytics",
            "Zbiera metryki użycia produktu i wydajności systemu.",
            ["liczba aktywnych użytkowników", "obciążenie systemu", "czas odpowiedzi API", "błędy"],
            ["Lepsza optymalizacja produktu", "Decyzje oparte na danych zamiast intuicji"]),
        new(
            "security_center",
            "Security Center",
            "Daje użytkownikowi pełny panel bezpieczeństwa konta, zasilany zdarzeniami RyzeAuth.",
            ["historia logowań", "aktywne sesje", "alerty bezpieczeństwa", "zaufane urządzenia"],
            ["Użytkownik sam kontroluje bezpieczeństwo konta", "Mniej zgłoszeń do supportu"]),
        new(
            "event_bus",
            "Event Bus",
            "Rozluźnia powiązania między mikroserwisami dzięki publikacji zdarzeń.",
            ["payment.completed", "support.status_updated", "security.warning", "server.activated"],
            ["Luźne powiązanie usług", "Łatwiejsze dodawanie nowych konsumentów"]),
        new(
            "file_transfer_service",
            "File Transfer Service",
            "Obsługuje bezpieczny przepływ plików wewnątrz platformy.",
            ["skanowanie antywirusowe", "szyfrowanie", "wersjonowanie"],
            ["Bezpieczna wymiana załączników", "Kontrola nad historią plików"])
    ];

    public static IReadOnlyList<PlatformModuleDefinition> All => Modules;

    public static IReadOnlyList<string> EnabledModules { get; } = [.. Modules.Select(module => module.Key)];

    public static IReadOnlyList<RoleDefinition> DefaultRoles { get; } =
    [
        new("User", ["server:create", "billing:view", "messages:send"]),
        new("Seller", ["billing:view", "billing:manage", "offers:manage"]),
        new("Moderator", ["messages:moderate", "support:view", "activity:view"]),
        new("Support", ["support:view", "support:manage", "messages:send", "security:alerts:view"]),
        new("Admin", ["server:create", "server:delete", "billing:view", "billing:manage", "settings:manage", "users:manage"]),
        new("SuperAdmin",
        [
            "server:create",
            "server:delete",
            "billing:view",
            "billing:manage",
            "permissions:manage",
            "gateway:manage",
            "feature_flags:manage",
            "security:manage"
        ])
    ];
}
