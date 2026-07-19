//! Hub Manager
//! Orchestrates hub update process

use anyhow::Result;
use std::collections::{HashMap, HashSet};
use std::path::PathBuf;
use tracing::{error, info, warn};

use crate::dependency_manager::DependencyManager;
use crate::docker_manager::DockerManager;
use crate::github_manager::{GitHubManager, GitHubManagerConfig};
use crate::hub_catalog;

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

    /// Run hub update
    pub async fn update_hub(&self, dockerize: bool) -> Result<HubUpdateResult> {
        info!("═══════════════════════════════════════════════");
        info!("Hub Update Started / Rozpoczęto aktualizację huba");
        info!("═══════════════════════════════════════════════");

        let mut result = HubUpdateResult::new();

        // Step 1: Clone all repositories
        info!("Step 1: Cloning repositories...");
        let hub_path = PathBuf::from(&self.hub_dir);
        let cloned_repos = self.github.clone_all_repositories(&hub_path).await?;
        result.cloned_count = cloned_repos.len();
        info!("✓ Cloned {} repositories", cloned_repos.len());

        let cloned_repo_names: HashSet<String> = cloned_repos
            .iter()
            .map(|(repo_name, _)| {
                hub_catalog::resolve_canonical_repository_name(repo_name)
                    .unwrap_or(repo_name.as_str())
                    .to_string()
            })
            .collect();

        result.active_repositories = hub_catalog::active_repository_names()
            .into_iter()
            .filter(|repo_name| cloned_repo_names.contains(repo_name))
            .collect();
        result.missing_active_repositories = hub_catalog::active_repository_names()
            .into_iter()
            .filter(|repo_name| !cloned_repo_names.contains(repo_name))
            .collect();
        result.future_dependency_targets = hub_catalog::future_repository_names();

        if !result.active_repositories.is_empty() {
            info!(
                "✓ Active hub repositories available: {}",
                result.active_repositories.join(", ")
            );
        }

        if !result.missing_active_repositories.is_empty() {
            warn!(
                "Missing active hub repositories: {}",
                result.missing_active_repositories.join(", ")
            );
        }

        info!(
            "Future dependency targets configured: {}",
            result.future_dependency_targets.join(", ")
        );

        // Step 2: Detect dependencies
        info!("Step 2: Analyzing dependencies...");
        let repo_names: Vec<String> = self.github.get_repo_names().await?;
        let dep_manager = DependencyManager::new(repo_names);
        let dep_graph = dep_manager.build_dependency_graph(&cloned_repos);
        result.dependency_edges = dep_graph.edges.len();
        info!(
            "✓ Found {} dependency edges",
            dep_graph.edges.len()
        );

        // Step 3: Generate Dockerfiles
        if dockerize {
            info!("Step 3: Generating Dockerfiles...");
            let processed = self.docker.process_repositories(&cloned_repos);
            result.dockerized_count = processed.len();
            info!("✓ Generated {} Dockerfiles", processed.len());
        }

        // Step 4: Get build order
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
        info!("  Active repos: {}", result.active_repositories.join(", "));
        info!(
            "  Future targets: {}",
            result.future_dependency_targets.join(", ")
        );
        info!("═══════════════════════════════════════════════");

        Ok(result)
    }

    /// Get cloned repository paths
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

/// Hub update result
#[derive(Debug, Clone, serde::Serialize)]
pub struct HubUpdateResult {
    pub cloned_count: usize,
    pub dependency_edges: usize,
    pub dockerized_count: usize,
    pub build_order: Vec<String>,
    pub active_repositories: Vec<String>,
    pub missing_active_repositories: Vec<String>,
    pub future_dependency_targets: Vec<String>,
    pub errors: Vec<String>,
}

impl HubUpdateResult {
    pub fn new() -> Self {
        Self {
            cloned_count: 0,
            dependency_edges: 0,
            dockerized_count: 0,
            build_order: Vec::new(),
            active_repositories: Vec::new(),
            missing_active_repositories: Vec::new(),
            future_dependency_targets: Vec::new(),
            errors: Vec::new(),
        }
    }

    pub fn is_success(&self) -> bool {
        self.errors.is_empty()
    }
}
