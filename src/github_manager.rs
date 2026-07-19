//! GitHub Manager
//! Manages GitHub organization repositories and submodules

use anyhow::Result;
use reqwest::Client;
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::process::Command;
use tracing::{debug, error, info, warn};

/// GitHub repository info
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GitHubRepo {
    pub name: String,
    pub full_name: String,
    pub clone_url: String,
    pub ssh_url: String,
    pub default_branch: String,
    pub private: bool,
    pub archived: bool,
    pub language: Option<String>,
}

/// GitHub Manager configuration
#[derive(Debug, Clone)]
pub struct GitHubManagerConfig {
    pub org_name: String,
    pub token: Option<String>,
    pub base_url: String,
}

impl Default for GitHubManagerConfig {
    fn default() -> Self {
        Self {
            org_name: String::new(),
            token: None,
            base_url: "https://api.github.com".to_string(),
        }
    }
}

pub struct GitHubManager {
    client: Client,
    config: GitHubManagerConfig,
}

impl GitHubManager {
    pub fn new(config: GitHubManagerConfig) -> Result<Self> {
        let client = Client::builder()
            .user_agent("ticket-pipeline-rust")
            .build()?;

        Ok(Self { client, config })
    }

    /// Get all repositories from organization
    pub async fn get_repositories(&self) -> Result<Vec<GitHubRepo>> {
        let mut repos = Vec::new();
        let mut page = 1;
        let per_page = 100;

        loop {
            let url = format!(
                "{}/orgs/{}/repos?per_page={}&page={}",
                self.config.base_url, self.config.org_name, per_page, page
            );

            debug!("Fetching repos from: {}", url);

            let mut request = self.client.get(&url);
            if let Some(ref token) = self.config.token {
                request = request.header("Authorization", format!("token {}", token));
            }

            let response = request.send().await?;

            if !response.status().is_success() {
                let status = response.status();
                let body = response.text().await.unwrap_or_default();
                error!("GitHub API error: {} - {}", status, body);
                break;
            }

            let page_repos: Vec<GitHubRepo> = response.json().await?;

            if page_repos.is_empty() {
                break;
            }

            repos.extend(page_repos);
            page += 1;
        }

        info!("Fetched {} repositories from {}", repos.len(), self.config.org_name);
        Ok(repos)
    }

    /// Clone repository
    pub fn clone_repository(&self, repo: &GitHubRepo, target_dir: &Path) -> Result<PathBuf> {
        let repo_path = target_dir.join(&repo.name);

        if repo_path.exists() {
            debug!("Repository {} already exists", repo.name);
            return Ok(repo_path);
        }

        info!("Cloning {}...", repo.name);

        let output = Command::new("git")
            .args([
                "clone",
                &repo.clone_url,
                &repo_path.to_string_lossy(),
            ])
            .output()?;

        if !output.status.success() {
            let stderr = String::from_utf8_lossy(&output.stderr);
            anyhow::bail!("Failed to clone {}: {}", repo.name, stderr);
        }

        info!("✓ Cloned {}", repo.name);
        Ok(repo_path)
    }

    /// Add submodule to repository
    pub fn add_submodule(
        &self,
        parent_repo_path: &Path,
        submodule_url: &str,
        submodule_name: &str,
    ) -> bool {
        info!(
            "Adding {} as submodule to {}",
            submodule_name,
            parent_repo_path.display()
        );

        let submodule_path = parent_repo_path.join("libs").join(submodule_name);

        // Create libs directory if needed
        let libs_dir = parent_repo_path.join("libs");
        if !libs_dir.exists() {
            if let Err(e) = std::fs::create_dir_all(&libs_dir) {
                error!("Failed to create libs directory: {}", e);
                return false;
            }
        }

        let output = Command::new("git")
            .args([
                "submodule",
                "add",
                submodule_url,
                &submodule_path.to_string_lossy(),
            ])
            .current_dir(parent_repo_path)
            .output();

        match output {
            Ok(out) if out.status.success() => {
                info!("✓ Added submodule {}", submodule_name);
                true
            }
            Ok(out) => {
                let stderr = String::from_utf8_lossy(&out.stderr);
                error!("Failed to add submodule {}: {}", submodule_name, stderr);
                false
            }
            Err(e) => {
                error!("Failed to execute git submodule: {}", e);
                false
            }
        }
    }

    /// Initialize and update submodules
    pub fn init_submodules(&self, repo_path: &Path) -> bool {
        info!("Initializing submodules in {}", repo_path.display());

        let output = Command::new("git")
            .args(["submodule", "update", "--init", "--recursive"])
            .current_dir(repo_path)
            .output();

        match output {
            Ok(out) if out.status.success() => {
                info!("✓ Initialized submodules");
                true
            }
            Ok(out) => {
                let stderr = String::from_utf8_lossy(&out.stderr);
                error!("Failed to init submodules: {}", stderr);
                false
            }
            Err(e) => {
                error!("Failed to execute git submodule update: {}", e);
                false
            }
        }
    }

    /// Pull latest changes
    pub fn pull_latest(&self, repo_path: &Path) -> bool {
        let output = Command::new("git")
            .args(["pull", "--rebase"])
            .current_dir(repo_path)
            .output();

        match output {
            Ok(out) if out.status.success() => {
                debug!("✓ Pulled latest changes");
                true
            }
            Ok(out) => {
                let stderr = String::from_utf8_lossy(&out.stderr);
                warn!("Failed to pull: {}", stderr);
                false
            }
            Err(e) => {
                error!("Failed to execute git pull: {}", e);
                false
            }
        }
    }

    /// Clone all repositories
    pub async fn clone_all_repositories(&self, target_dir: &Path) -> Result<Vec<(String, PathBuf)>> {
        let repos = self.get_repositories().await?;
        let mut cloned = Vec::new();

        // Create target directory
        if !target_dir.exists() {
            std::fs::create_dir_all(target_dir)?;
        }

        for repo in &repos {
            if repo.archived {
                debug!("Skipping archived repo: {}", repo.name);
                continue;
            }

            match self.clone_repository(repo, target_dir) {
                Ok(path) => {
                    cloned.push((repo.name.clone(), path));
                }
                Err(e) => {
                    error!("Failed to clone {}: {}", repo.name, e);
                }
            }
        }

        info!("Cloned {}/{} repositories", cloned.len(), repos.len());
        Ok(cloned)
    }

    /// Get organization name
    pub fn org_name(&self) -> &str {
        &self.config.org_name
    }

    /// Get all repository names
    pub async fn get_repo_names(&self) -> Result<Vec<String>> {
        let repos = self.get_repositories().await?;
        Ok(repos.into_iter().map(|r| r.name).collect())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_github_manager_config() {
        let config = GitHubManagerConfig {
            org_name: "test-org".to_string(),
            token: Some("test-token".to_string()),
            base_url: "https://api.github.com".to_string(),
        };

        assert_eq!(config.org_name, "test-org");
    }

    #[tokio::test]
    async fn test_github_manager_creation() {
        let config = GitHubManagerConfig::default();
        let manager = GitHubManager::new(config);
        assert!(manager.is_ok());
    }
}
