mod models;
mod config;
mod transformer;
mod source_client;
mod destination_client;
mod pipeline;
mod security;
mod metrics;
mod errors;
mod dependency_manager;
mod docker_manager;
mod github_manager;
mod hub_catalog;
mod hub_manager;
mod crypto;
mod error_detection;

use anyhow::Result;
use clap::{Parser, Subcommand};
use tracing::{info, error};
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};

use crate::config::PipelineConfig;
use crate::pipeline::TicketPipeline;

#[derive(Parser)]
#[command(name = "ticket-pipeline")]
#[command(about = "High-performance ticket transfer pipeline")]
#[command(version)]
struct Cli {
    #[command(subcommand)]
    command: Option<Commands>,

    #[arg(short, long)]
    continuous: bool,

    #[arg(short, long, default_value = "300")]
    interval: u64,

    #[arg(long, default_value = "info")]
    log_level: String,

    #[arg(long)]
    no_categorize: bool,

    #[arg(long)]
    no_priority: bool,

    #[arg(long)]
    no_deduplicate: bool,

    #[arg(long)]
    include_resolved: bool,

    #[arg(long)]
    include_closed: bool,
}

#[derive(Subcommand)]
enum Commands {
    Health,
    Metrics,
    Validate,
    Hub {
        #[arg(short, long)]
        org: String,
        #[arg(short, long)]
        token: Option<String>,
        #[arg(short, long, default_value = "github_hub")]
        dir: String,
        #[arg(long)]
        dockerize: bool,
    },
    Deps {
        #[arg(short, long, default_value = ".")]
        path: String,
        #[arg(short, long)]
        repos: String,
    },
    Docker {
        #[arg(short, long, default_value = ".")]
        path: String,
    },
    Encrypt {
        #[arg(short, long)]
        data: String,
        #[arg(short, long, env = "ENCRYPTION_KEY")]
        key: String,
    },
    Decrypt {
        #[arg(short, long)]
        data: String,
        #[arg(short, long, env = "ENCRYPTION_KEY")]
        key: String,
    },
    Vault {
        #[command(subcommand)]
        action: VaultCommands,
    },
    Sign {
        #[arg(short, long)]
        data: String,
        #[arg(short, long, env = "SIGNING_KEY")]
        key: String,
    },
    Verify {
        #[arg(short, long)]
        data: String,
        #[arg(short, long)]
        signature: String,
        #[arg(short, long, env = "SIGNING_KEY")]
        key: String,
    },
    Errors {
        #[command(subcommand)]
        action: ErrorCommands,
    },
}

#[derive(Subcommand)]
enum VaultCommands {
    Store {
        #[arg(short, long)]
        key_id: String,
        #[arg(short, long)]
        data: String,
        #[arg(short, long)]
        name: String,
    },
    Retrieve {
        #[arg(short, long)]
        key_id: String,
    },
    List,
    Integrity,
}

#[derive(Subcommand)]
enum ErrorCommands {
    Analyze {
        #[arg(short, long)]
        message: String,
        #[arg(short, long, default_value = "unknown")]
        source: String,
    },
    Stats,
    Anomalies,
    TestPatterns,
}

#[tokio::main]
async fn main() -> Result<()> {
    dotenv::dotenv().ok();

    let cli = Cli::parse();

    tracing_subscriber::registry()
        .with(tracing_subscriber::EnvFilter::new(&cli.log_level))
        .with(tracing_subscriber::fmt::layer().json())
        .init();

    info!("═══════════════════════════════════════════════");
    info!("  Ticket Pipeline v{}", env!("CARGO_PKG_VERSION"));
    info!("  Transfer: RyzeSpace.Client -> RyzeSpace.HelpCenter");
    info!("═══════════════════════════════════════════════");

    if let Some(command) = &cli.command {
        match command {
            Commands::Health => {
                info!("Running health check...");
                let config = PipelineConfig::from_env()?;
                let pipeline = TicketPipeline::new(config).await?;
                let health = pipeline.health_check().await?;
                println!("{}", serde_json::to_string_pretty(&health)?);
                return Ok(());
            }
            Commands::Metrics => {
                info!("Fetching metrics...");
                let metrics = metrics::get_metrics();
                println!("{}", metrics);
                return Ok(());
            }
            Commands::Validate => {
                info!("Validating configuration...");
                let config = PipelineConfig::from_env()?;
                info!("Configuration valid");
                info!("  Auto-categorize: {}", config.auto_categorize);
                info!("  Auto-priority: {}", config.auto_priority);
                info!("  Deduplicate: {}", config.deduplicate);
                return Ok(());
            }
            Commands::Hub { org, token, dir, dockerize } => {
                info!("Starting hub update...");
                info!("  Organization: {}", org);
                info!("  Directory: {}", dir);
                info!("  Dockerize: {}", dockerize);

                let hub = hub_manager::HubManager::new(
                    org.clone(),
                    token.clone(),
                    dir.clone(),
                ).await?;

                let result = hub.update_hub(*dockerize).await?;

                println!("\n═══════════════════════════════════════════════");
                println!("  HUB UPDATE SUMMARY");
                println!("═══════════════════════════════════════════════");
                println!("{}", serde_json::to_string_pretty(&result)?);

                if !result.is_success() {
                    error!("Hub update completed with errors!");
                    std::process::exit(1);
                }
                return Ok(());
            }
            Commands::Deps { path, repos } => {
                info!("Analyzing dependencies...");
                let repo_names: Vec<String> = repos.split(',')
                    .map(|s| s.trim().to_string())
                    .collect();

                let dep_manager = dependency_manager::DependencyManager::new(repo_names);
                let repo_path = std::path::Path::new(path);

                let deps = dep_manager.find_internal_dependencies(repo_path);

                println!("\n═══════════════════════════════════════════════");
                println!("  DEPENDENCY ANALYSIS");
                println!("═══════════════════════════════════════════════");
                println!("Path: {}", path);
                println!("Found {} internal dependencies:", deps.len());
                for dep in &deps {
                    println!("  - {}", dep);
                }
                return Ok(());
            }
            Commands::Docker { path } => {
                info!("Generating Dockerfile...");
                let repo_path = std::path::Path::new(path);
                let docker_manager = docker_manager::DockerManager::new(path.clone());

                let lang = docker_manager.detect_language(repo_path);
                info!("Detected language: {}", lang);

                let success = docker_manager.generate_dockerfile(repo_path, &lang);

                if success {
                    println!("Generated Dockerfile for {} at {}", lang, path);
                } else {
                    error!("Failed to generate Dockerfile");
                    std::process::exit(1);
                }
                return Ok(());
            }
            Commands::Encrypt { data, key } => {
                info!("Encrypting data...");
                let engine = crypto::CryptoEngine::new(key)?;
                let packet = engine.encrypt(data.as_bytes())?;

                let encrypted_str = format!(
                    "ENC:{}:{}:{}",
                    packet.context.key_id,
                    base64::encode(&packet.context.nonce),
                    base64::encode(&packet.ciphertext)
                );

                println!("\n═══════════════════════════════════════════════");
                println!("  ENCRYPTION RESULT");
                println!("═══════════════════════════════════════════════");
                println!("Original: {} bytes", data.len());
                println!("Encrypted: {} bytes", encrypted_str.len());
                println!("Key ID: {}", packet.context.key_id);
                println!("Algorithm: {}", packet.context.algorithm);
                println!("Checksum: {}", packet.checksum);
                println!("\nEncrypted data:");
                println!("{}", encrypted_str);
                return Ok(());
            }
            Commands::Decrypt { data, key } => {
                info!("Decrypting data...");
                let engine = crypto::CryptoEngine::new(key)?;

                let parts: Vec<&str> = data.split(':').collect();
                if parts.len() != 4 || parts[0] != "ENC" {
                    anyhow::bail!("Invalid encrypted data format");
                }

                let nonce = base64::decode(parts[2])?;
                let ciphertext = base64::decode(parts[3])?;

                let packet = crypto::engine::EncryptedPacket {
                    context: crypto::engine::EncryptionContext {
                        version: 1,
                        algorithm: "AES-256-GCM".to_string(),
                        key_id: parts[1].to_string(),
                        nonce,
                        created_at: chrono::Utc::now(),
                        expires_at: None,
                        metadata: std::collections::HashMap::new(),
                    },
                    ciphertext,
                    auth_tag: Vec::new(),
                    checksum: String::new(),
                };

                let decrypted = engine.decrypt(&packet)?;
                let plaintext = String::from_utf8(decrypted)?;

                println!("\n═══════════════════════════════════════════════");
                println!("  DECRYPTION RESULT");
                println!("═══════════════════════════════════════════════");
                println!("Decrypted: {} bytes", plaintext.len());
                println!("\nDecrypted data:");
                println!("{}", plaintext);
                return Ok(());
            }
            Commands::Vault { action } => {
                let vault_path = std::env::var("VAULT_PATH").unwrap_or_else(|_| "vault.json".to_string());
                let current_user = std::env::var("VAULT_USER").unwrap_or_else(|_| "system".to_string());
                let vault_key = std::env::var("ENCRYPTION_KEY")
                    .map_err(|_| anyhow::anyhow!("ENCRYPTION_KEY not set"))?;

                let mut vault = crypto::SecureVault::new(&vault_key, &vault_path, &current_user)?;

                match action {
                    VaultCommands::Store { key_id, data, name } => {
                        vault.store_key(&key_id, data.as_bytes(), &name, "Stored via CLI", vec![])?;
                        println!("Key {} stored in vault", key_id);
                    }
                    VaultCommands::Retrieve { key_id } => {
                        let data = vault.retrieve_key(&key_id)?;
                        println!("Key ID: {}", key_id);
                        println!("Data: {} bytes", data.len());
                        println!("Data (hex): {}", hex::encode(&data));
                    }
                    VaultCommands::List => {
                        let keys = vault.list_keys();
                        println!("\n═══════════════════════════════════════════════");
                        println!("  VAULT KEYS");
                        println!("═══════════════════════════════════════════════");
                        println!("Total keys: {}", keys.len());
                        for key in keys {
                            if let Some(meta) = vault.get_key_metadata(&key) {
                                println!("  - {} - {} ({})", key, meta.name, meta.description);
                            }
                        }
                    }
                    VaultCommands::Integrity => {
                        let hash = vault.integrity_hash();
                        println!("Vault integrity hash: {}", hash);
                    }
                }
                return Ok(());
            }
            Commands::Sign { data, key } => {
                info!("Signing data...");
                let engine = crypto::SignatureEngine::new(key)?;
                let signature = engine.sign(data.as_bytes())?;

                println!("\n═══════════════════════════════════════════════");
                println!("  SIGNATURE RESULT");
                println!("═══════════════════════════════════════════════");
                println!("Data: {} bytes", data.len());
                println!("Algorithm: {}", signature.algorithm);
                println!("Key ID: {}", signature.key_id);
                println!("Timestamp: {}", signature.timestamp);
                println!("\nSignature:");
                println!("{}", signature.signature);
                return Ok(());
            }
            Commands::Verify { data, signature, key } => {
                info!("Verifying signature...");
                let engine = crypto::SignatureEngine::new(key)?;
                let valid = engine.verify(data.as_bytes(), &signature)?;

                println!("\n═══════════════════════════════════════════════");
                println!("  VERIFICATION RESULT");
                println!("═══════════════════════════════════════════════");
                if valid {
                    println!("Signature is VALID");
                } else {
                    println!("Signature is INVALID");
                    std::process::exit(1);
                }
                return Ok(());
            }
            Commands::Errors { action } => {
                let mut engine = error_detection::ErrorDetectionEngine::new();

                match action {
                    ErrorCommands::Analyze { message, source } => {
                        if let Some(error) = engine.detect_error(&message, &source) {
                            println!("\n═══════════════════════════════════════════════");
                            println!("  ERROR ANALYSIS");
                            println!("═══════════════════════════════════════════════");
                            println!("Error ID: {}", error.id);
                            println!("Category: {:?}", error.category);
                            println!("Severity: {:?}", error.severity);
                            println!("Source: {}", error.source);
                            println!("Pattern: {:?}", error.pattern_id);
                            println!("Message: {}", error.message);
                        } else {
                            println!("No known error pattern detected");
                        }
                    }
                    ErrorCommands::Stats => {
                        let stats = engine.get_statistics();
                        println!("\n═══════════════════════════════════════════════");
                        println!("  ERROR STATISTICS");
                        println!("═══════════════════════════════════════════════");
                        for (category, count) in stats {
                            println!("  {:?}: {}", category, count);
                        }
                    }
                    ErrorCommands::Anomalies => {
                        let mut detector = error_detection::anomaly::AnomalyDetector::new();
                        detector.track_metric("error_rate", 1000);

                        for i in 0..20 {
                            detector.record("error_rate", 5.0 + (i as f64 * 0.1));
                        }
                        detector.record("error_rate", 50.0);

                        let anomalies = detector.detect();
                        println!("\n═══════════════════════════════════════════════");
                        println!("  ANOMALY DETECTION");
                        println!("═══════════════════════════════════════════════");
                        println!("Detected {} anomalies", anomalies.len());
                        for anomaly in anomalies {
                            println!("\n  - {}", anomaly.description);
                            println!("    Severity: {:.2}", anomaly.severity);
                            println!("    Confidence: {:.2}", anomaly.confidence);
                        }
                    }
                    ErrorCommands::TestPatterns => {
                        println!("\n═══════════════════════════════════════════════");
                        println!("  ERROR PATTERNS");
                        println!("═══════════════════════════════════════════════");
                        let patterns = engine.get_pattern_stats();
                        for pattern in patterns {
                            println!("\n  Pattern: {}", pattern.name);
                            println!("  ID: {}", pattern.id);
                            println!("  Category: {:?}", pattern.category);
                            println!("  Severity: {:?}", pattern.severity);
                            println!("  Description: {}", pattern.description);
                            println!("  Occurrences: {}", pattern.occurrence_count);
                        }
                    }
                }
                return Ok(());
            }
        }
    }

    let mut config = PipelineConfig::from_env()?;
    config.auto_categorize = !cli.no_categorize;
    config.auto_priority = !cli.no_priority;
    config.filter_resolved = !cli.include_resolved;
    config.filter_closed = !cli.include_closed;
    config.deduplicate = !cli.no_deduplicate;
    config.poll_interval = cli.interval;

    info!("Mode: {}", if cli.continuous { "continuous" } else { "one-time" });
    if cli.continuous {
        info!("Poll interval: {}s", cli.interval);
    }
    info!("Auto-categorize: {}", config.auto_categorize);
    info!("Auto-prioritize: {}", config.auto_priority);
    info!("Deduplicate: {}", config.deduplicate);
    info!("Filter resolved: {}", config.filter_resolved);
    info!("Filter closed: {}", config.filter_closed);
    info!("═══════════════════════════════════════════════");

    let pipeline = TicketPipeline::new(config).await?;

    if cli.continuous {
        info!("Starting continuous pipeline...");
        pipeline.run_continuous().await?;
    } else {
        info!("Running pipeline once...");
        let metrics = pipeline.run_once().await?;

        info!("═══════════════════════════════════════════════");
        info!("  PIPELINE SUMMARY");
        info!("═══════════════════════════════════════════════");
        println!("{}", serde_json::to_string_pretty(&metrics.summary())?);

        if metrics.failed_count > 0 {
            error!("{} tickets failed!", metrics.failed_count);
            std::process::exit(1);
        } else if metrics.transferred_count == 0 && metrics.fetched_count == 0 {
            info!("No tickets to transfer.");
        } else {
            info!("Successfully transferred {} tickets", metrics.transferred_count);
        }
    }

    Ok(())
}
