//! Docker Manager / Manager Docker
//! Generates Dockerfiles and docker-compose.yml / Generuje Dockerfile i docker-compose.yml

use std::collections::HashMap;
use std::path::Path;
use tracing::{debug, info, warn};

/// Docker template for different languages / Szablon Docker dla różnych języków
#[derive(Debug, Clone)]
pub struct DockerTemplate {
    pub image: &'static str,
    pub build_cmd: &'static str,
    pub run_cmd: &'static str,
}

pub struct DockerManager {
    hub_dir: String,
    templates: HashMap<String, DockerTemplate>,
}

impl DockerManager {
    pub fn new(hub_dir: String) -> Self {
        let mut templates = HashMap::new();

        templates.insert(
            "python".to_string(),
            DockerTemplate {
                image: "python:3.11-slim",
                build_cmd: "RUN pip install --no-cache-dir -r requirements.txt || echo 'No requirements.txt found'",
                run_cmd: r#"CMD ["python", "main.py"]"#,
            },
        );

        templates.insert(
            "nodejs".to_string(),
            DockerTemplate {
                image: "node:20-slim",
                build_cmd: "RUN npm install || echo 'No package.json found'",
                run_cmd: r#"CMD ["npm", "start"]"#,
            },
        );

        templates.insert(
            "rust".to_string(),
            DockerTemplate {
                image: "rust:1.75-slim",
                build_cmd: r#"RUN cargo build --release"#,
                run_cmd: r#"CMD ["./target/release/app"]"#,
            },
        );

        templates.insert(
            "golang".to_string(),
            DockerTemplate {
                image: "golang:1.21-slim",
                build_cmd: r#"RUN go build -o app ."#,
                run_cmd: r#"CMD ["./app"]"#,
            },
        );

        templates.insert(
            "generic".to_string(),
            DockerTemplate {
                image: "ubuntu:latest",
                build_cmd: "",
                run_cmd: r#"CMD ["sh"]"#,
            },
        );

        Self { hub_dir, templates }
    }

    /// Detect programming language / Wykryj język programowania
    pub fn detect_language(&self, repo_path: &Path) -> String {
        let files: Vec<String> = match std::fs::read_dir(repo_path) {
            Ok(entries) => entries
                .filter_map(|e| e.ok())
                .filter_map(|e| e.file_name().into_string().ok())
                .collect(),
            Err(_) => return "generic".to_string(),
        };

        if files.contains(&"requirements.txt".to_string())
            || files.contains(&"pyproject.toml".to_string())
            || files.contains(&"setup.py".to_string())
        {
            "python"
        } else if files.contains(&"package.json".to_string()) {
            "nodejs"
        } else if files.contains(&"Cargo.toml".to_string()) {
            "rust"
        } else if files.contains(&"go.mod".to_string()) {
            "golang"
        } else {
            "generic"
        }
        .to_string()
    }

    /// Generate Dockerfile / Generuj Dockerfile
    pub fn generate_dockerfile(&self, repo_path: &Path, lang: &str) -> bool {
        let dockerfile_path = repo_path.join("Dockerfile");

        // Skip if Dockerfile already exists / Pomiń jeśli Dockerfile już istnieje
        if dockerfile_path.exists() {
            debug!("Dockerfile already exists at {}", dockerfile_path.display());
            return true;
        }

        let template = match self.templates.get(lang) {
            Some(t) => t,
            None => self.templates.get("generic").unwrap(),
        };

        let content = format!(
            r#"FROM {}
WORKDIR /app
COPY . .
{}
{}
"#,
            template.image, template.build_cmd, template.run_cmd
        );

        match std::fs::write(&dockerfile_path, content) {
            Ok(_) => {
                info!("Generated Dockerfile at {}", dockerfile_path.display());
                true
            }
            Err(e) => {
                warn!("Failed to write Dockerfile: {}", e);
                false
            }
        }
    }

    /// Generate multi-stage Dockerfile for Rust / Generuj multi-stage Dockerfile dla Rust
    pub fn generate_rust_dockerfile(&self, repo_path: &Path) -> bool {
        let dockerfile_path = repo_path.join("Dockerfile");

        if dockerfile_path.exists() {
            return true;
        }

        let content = r#"# Build stage
FROM rust:1.75-slim-bookworm as builder
WORKDIR /app
RUN apt-get update && apt-get install -y pkg-config libssl-dev && rm -rf /var/lib/apt/lists/*
COPY Cargo.toml Cargo.lock ./
RUN mkdir src && echo "fn main() {}" > src/main.rs
RUN cargo build --release
RUN rm -rf src
COPY src ./src
RUN cargo build --release

# Runtime stage
FROM debian:bookworm-slim
WORKDIR /app
RUN apt-get update && apt-get install -y ca-certificates && rm -rf /var/lib/apt/lists/*
COPY --from=builder /app/target/release/app /app/app
RUN useradd -m -u 1000 appuser
USER appuser
ENTRYPOINT ["/app/app"]
"#;

        match std::fs::write(&dockerfile_path, content) {
            Ok(_) => {
                info!("Generated Rust multi-stage Dockerfile");
                true
            }
            Err(e) => {
                warn!("Failed to write Dockerfile: {}", e);
                false
            }
        }
    }

    /// Generate docker-compose.yml / Generuj docker-compose.yml
    pub fn create_docker_compose(&self, cloned_repos: &HashMap<String, String>) -> bool {
        let mut services = String::new();

        for (name, path) in cloned_repos {
            services.push_str(&format!(
                r#"  {}:
    build: {}
    container_name: hub_{}
    restart: always
"#,
                name, path, name
            ));
        }

        let compose_content = format!(
            r#"version: '3.8'

services:
{}"#,
            services
        );

        let compose_path = Path::new(&self.hub_dir).join("docker-compose.yml");

        match std::fs::write(&compose_path, compose_content) {
            Ok(_) => {
                info!("Generated docker-compose.yml at {}", compose_path.display());
                true
            }
            Err(e) => {
                warn!("Failed to write docker-compose.yml: {}", e);
                false
            }
        }
    }

    /// Process all repositories / Przetwórz wszystkie repozytoria
    pub fn process_repositories(&self, repos: &[(String, std::path::PathBuf)]) -> HashMap<String, String> {
        let mut processed = HashMap::new();

        for (name, path) in repos {
            let lang = self.detect_language(path);
            info!("Detected language for {}: {}", name, lang);

            let success = if lang == "rust" {
                self.generate_rust_dockerfile(path)
            } else {
                self.generate_dockerfile(path, &lang)
            };

            if success {
                processed.insert(name.clone(), path.to_string_lossy().to_string());
            }
        }

        // Generate docker-compose if we have multiple repos
        // Generuj docker-compose jeśli mamy wiele repo
        if processed.len() > 1 {
            self.create_docker_compose(&processed);
        }

        processed
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use tempfile::TempDir;

    #[test]
    fn test_detect_python() {
        let temp_dir = TempDir::new().unwrap();
        fs::write(temp_dir.path().join("requirements.txt"), "flask==2.0").unwrap();

        let manager = DockerManager::new("/tmp".to_string());
        let lang = manager.detect_language(temp_dir.path());
        assert_eq!(lang, "python");
    }

    #[test]
    fn test_detect_rust() {
        let temp_dir = TempDir::new().unwrap();
        fs::write(temp_dir.path().join("Cargo.toml"), "[package]").unwrap();

        let manager = DockerManager::new("/tmp".to_string());
        let lang = manager.detect_language(temp_dir.path());
        assert_eq!(lang, "rust");
    }

    #[test]
    fn test_generate_dockerfile() {
        let temp_dir = TempDir::new().unwrap();
        let manager = DockerManager::new("/tmp".to_string());

        let result = manager.generate_dockerfile(temp_dir.path(), "python");
        assert!(result);
        assert!(temp_dir.path().join("Dockerfile").exists());

        let content = fs::read_to_string(temp_dir.path().join("Dockerfile")).unwrap();
        assert!(content.contains("python:3.11-slim"));
    }
}
