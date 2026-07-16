//! Hub Manager / Manager Hub
//! Orchestrates hub update process / Orkiestruje proces aktualizacji huba

use anyhow::Result;
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use tracing::{error, info};

use crate::dependency_manager::DependencyManager;
use crate::docker_manager::DockerManager;
use crate::github_manager::{GitHubManager, GitHubManagerConfig};

pub struct HubManager {
    github: GitHubManager,
    docker: DockerManager,
    hub_dir: String,
}

impl HubManager {
    pub async fn new(
        org_name: String,
        token: Option<String>,
        hub_dir: String,
    ) -> Result<Self> {
        let github_config = GitHubManagerConfig {
            org_name,
            token,
            base_url: "https://api.github.com".to_string(),
        };

        let github = GitHubManager::new(github_config)?;
        let docker = DockerManager::new(hub_dir.clone());

        Ok(Self {
            github,
            docker,
            hub_dir,
        })
    }

    /// Run hub update / Uruchom aktualizację huba
    pub async fn update_hub(&self, dockerize: bool) -> Result<HubUpdateResult> {
        info!("═══════════════════════════════════════════════");
        info!("Hub Update Started / Rozpoczęto aktualizację huba");
        info!("═══════════════════════════════════════════════");

        let mut result = HubUpdateResult::new();

        // Step 1: Clone all repositories / Krok 1: Klonuj wszystkie repozytoria
        info!("Step 1: Cloning repositories...");
        let hub_path = PathBuf::from(&self.hub_dir);
        let cloned_repos = self.github.clone_all_repositories(&hub_path).await?;
        result.cloned_count = cloned_repos.len();
        info!("✓ Cloned {} repositories", cloned_repos.len());

        // Step 2: Detect dependencies / Krok 2: Wykryj zależności
        info!("Step 2: Analyzing dependencies...");
        let repo_names: Vec<String> = self.github.get_repo_names().await?;
        let dep_manager = DependencyManager::new(repo_names);
        let dep_graph = dep_manager.build_dependency_graph(&cloned_repos);
        result.dependency_edges = dep_graph.edges.len();
        info!(
            "✓ Found {} dependency edges",
            dep_graph.edges.len()
        );

        // Step 3: Generate Dockerfiles / Krok 3: Generuj Dockerfile
        if dockerize {
            info!("Step 3: Generating Dockerfiles...");
            let processed = self.docker.process_repositories(&cloned_repos);
            result.dockerized_count = processed.len();
            info!("✓ Generated {} Dockerfiles", processed.len());
        }

        // Step 4: Get build order / Krok 4: Pobierz kolejność buildu
        info!("Step 4: Calculating build order...");
        match dep_graph.topological_sort() {
            Some(order) => {
                info!("✓ Build order: {}", order.join(" → "));
                result.build_order = order;
            }
            None => {
                error!("✗ Dependency cycle detected!");
                result.errors.push("Dependency cycle detected".to_string());
            }
        }

        info!("═══════════════════════════════════════════════");
        info!("Hub Update Complete / Zakończono aktualizację huba");
        info!("  Cloned: {} repos", result.cloned_count);
        info!("  Dependencies: {} edges", result.dependency_edges);
        info!("  Dockerized: {} repos", result.dockerized_count);
        info!("═══════════════════════════════════════════════");

        Ok(result)
    }

    /// Get cloned repository paths / Pobierz ścieżki sklonowanych repozytoriów
    pub fn get_repo_paths(&self) -> Result<HashMap<String, PathBuf>> {
        let mut paths = HashMap::new();
        let hub_path = PathBuf::from(&self.hub_dir);

        if !hub_path.exists() {
            return Ok(paths);
        }

        for entry in std::fs::read_dir(&hub_path)? {
            let entry = entry?;
            let path = entry.path();
            if path.is_dir() {
                let name = entry.file_name().to_string_lossy().to_string();
                paths.insert(name, path);
            }
        }

        Ok(paths)
    }
}

/// Hub update result / Wynik aktualizacji huba
#[derive(Debug, Clone, serde::Serialize)]
pub struct HubUpdateResult {
    pub cloned_count: usize,
    pub dependency_edges: usize,
    pub dockerized_count: usize,
    pub build_order: Vec<String>,
    pub errors: Vec<String>,
}

impl HubUpdateResult {
    pub fn new() -> Self {
        Self {
            cloned_count: 0,
            dependency_edges: 0,
            dockerized_count: 0,
            build_order: Vec::new(),
            errors: Vec::new(),
        }
    }

    pub fn is_success(&self) -> bool {
        self.errors.is_empty()
    }
}
