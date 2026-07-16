# Build stage / Etap budowania
FROM rust:1.75-slim-bookworm as builder

WORKDIR /app

# Install build dependencies / Zainstaluj zależności build
RUN apt-get update && apt-get install -y \
    pkg-config \
    libssl-dev \
    && rm -rf /var/lib/apt/lists/*

# Copy manifests / Kopiuj manifesty
COPY Cargo.toml Cargo.lock ./

# Create dummy main for dependency caching / Utwórz dummy main dla cache
RUN mkdir src && echo "fn main() {}" > src/main.rs
RUN cargo build --release
RUN rm -rf src

# Copy source code / Kopiuj kod źródłowy
COPY src ./src

# Build release / Zbuduj release
RUN cargo build --release

# Runtime stage / Etap uruchomieniowy
FROM debian:bookworm-slim

WORKDIR /app

# Install runtime dependencies / Zainstaluj zależności runtime
RUN apt-get update && apt-get install -y \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Copy binary / Kopiuj binarkę
COPY --from=builder /app/target/release/ticket-pipeline /app/ticket-pipeline

# Create non-root user / Utwórz użytkownika non-root
RUN useradd -m -u 1000 pipeline
USER pipeline

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD /app/ticket-pipeline health || exit 1

ENTRYPOINT ["/app/ticket-pipeline"]
