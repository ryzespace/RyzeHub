//! Dependency Manager
//! Scans repositories for internal dependencies

use std::collections::{HashMap, HashSet};
use std::path::{Path, PathBuf};
use tracing::{debug, info, warn};

use crate::hub_catalog;

pub struct DependencyManager {
    org_repos_names: Vec<String>,
}

impl DependencyManager {
    pub fn new(org_repos_names: Vec<String>) -> Self {
        let mut names = Vec::new();

        for repo_name in org_repos_names
            .into_iter()
            .chain(hub_catalog::tracked_repository_names())
        {
            let canonical_name = hub_catalog::resolve_canonical_repository_name(&repo_name)
                .unwrap_or(repo_name.as_str())
                .to_string();

            if !names.contains(&canonical_name) {
                names.push(canonical_name);
            }
        }

        Self {
            org_repos_names: names,
        }
    }

    /// Find internal dependencies in a repository
    pub fn find_internal_dependencies(&self, repo_path: &Path) -> Vec<String> {
        let mut dependencies = HashSet::new();
        let current_repo_name = repo_path
            .file_name()
            .and_then(|name| name.to_str())
            .and_then(hub_catalog::resolve_canonical_repository_name)
            .map(|name| name.to_string());

        let files_to_scan = [
            "requirements.txt",
            "package.json",
            "go.mod",
            "pyproject.toml",
            "Cargo.toml",
            "pom.xml",
            "build.gradle",
        ];

        for filename in &files_to_scan {
            let filepath = repo_path.join(filename);
            if filepath.exists() {
                match std::fs::read_to_string(&filepath) {
                    Ok(content) => {
                        for repo_name in &self.org_repos_names {
                            // Check if repo name appears in dependency file
                            if self.contains_dependency(&content, repo_name) {
                                if current_repo_name.as_ref() != Some(repo_name) {
                                    dependencies.insert(repo_name.clone());
                                }
                                debug!(
                                    "Found dependency {} in {}",
                                    repo_name,
                                    filepath.display()
                                );
                            }
                        }
                    }
                    Err(e) => {
                        warn!("Failed to read {}: {}", filepath.display(), e);
                    }
                }
            }
        }

        let mut deps: Vec<String> = dependencies.into_iter().collect();
        deps.sort();
        if !deps.is_empty() {
            info!(
                "Found {} internal dependencies in {}",
                deps.len(),
                repo_path.display()
            );
        }

        deps
    }

    /// Check if content contains dependency reference
    fn contains_dependency(&self, content: &str, repo_name: &str) -> bool {
        let normalized_content = hub_catalog::normalize_repository_token(content);

        for identifier in hub_catalog::dependency_identifiers(repo_name) {
            if content.contains(&identifier) {
                return true;
            }

            let normalized_identifier = hub_catalog::normalize_repository_token(&identifier);
            if !normalized_identifier.is_empty()
                && normalized_content.contains(&normalized_identifier)
            {
                return true;
            }
        }

        false
    }

    /// Scan all repositories and build dependency graph
    pub fn build_dependency_graph(&self, repos: &[(String, PathBuf)]) -> DependencyGraph {
        let mut graph = DependencyGraph::new();

        for (repo_name, repo_path) in repos {
            let canonical_name = hub_catalog::resolve_canonical_repository_name(repo_name)
                .unwrap_or(repo_name.as_str())
                .to_string();
            let deps = self.find_internal_dependencies(repo_path);
            graph.add_repository(canonical_name, deps);
        }

        info!(
            "Built dependency graph: {} repositories, {} edges",
            graph.repositories.len(),
            graph.edges.len()
        );

        graph
    }
}

/// Dependency graph
#[derive(Debug, Clone)]
pub struct DependencyGraph {
    pub repositories: HashSet<String>,
    pub edges: Vec<(String, String)>, // (from, to)
    adjacency: std::collections::HashMap<String, Vec<String>>,
}

impl DependencyGraph {
    pub fn new() -> Self {
        Self {
            repositories: HashSet::new(),
            edges: Vec::new(),
            adjacency: std::collections::HashMap::new(),
        }
    }

    pub fn add_repository(&mut self, name: String, dependencies: Vec<String>) {
        self.repositories.insert(name.clone());
        self.adjacency.insert(name.clone(), dependencies.clone());

        for dep in dependencies {
            self.edges.push((name.clone(), dep));
        }
    }

    /// Get topological order for build
    pub fn topological_sort(&self) -> Option<Vec<String>> {
        let mut in_degree = HashMap::new();
        let mut dependents = HashMap::<String, Vec<String>>::new();
        let mut result = Vec::new();

        // Initialize in-degrees
        for repo in &self.repositories {
            in_degree.insert(repo.clone(), 0);
        }

        // Calculate in-degrees
        for (repo, dependency) in &self.edges {
            *in_degree.entry(repo.clone()).or_insert(0) += 1;
            dependents
                .entry(dependency.clone())
                .or_default()
                .push(repo.clone());
        }

        // Kahn's algorithm
        let mut queue: Vec<String> = in_degree
            .iter()
            .filter(|(_, &degree)| degree == 0)
            .map(|(repo, _)| repo.clone())
            .collect();

        while let Some(repo) = queue.pop() {
            result.push(repo.clone());

            if let Some(next_repos) = dependents.get(&repo) {
                for dependent in next_repos {
                    if let Some(degree) = in_degree.get_mut(dependent) {
                        *degree -= 1;
                        if *degree == 0 {
                            queue.push(dependent.clone());
                        }
                    }
                }
            }
        }

        if result.len() == self.repositories.len() {
            Some(result)
        } else {
            None // Cycle detected
        }
    }

    /// Get repositories that depend on a given repo
    pub fn get_dependents(&self, repo_name: &str) -> Vec<String> {
        self.edges
            .iter()
            .filter(|(_, to)| to == repo_name)
            .map(|(from, _)| from.clone())
            .collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use tempfile::TempDir;

    #[test]
    fn test_find_python_dependencies() {
        let temp_dir = TempDir::new().unwrap();
        let repo_path = temp_dir.path();

        fs::write(
            repo_path.join("requirements.txt"),
            "flask==2.0.0\nmy-internal-lib>=1.0.0\nrequests\n",
        )
        .unwrap();

        let manager = DependencyManager::new(vec!["my-internal-lib".to_string()]);
        let deps = manager.find_internal_dependencies(repo_path);

        assert_eq!(deps, vec!["my-internal-lib"]);
    }

    #[test]
    fn test_find_rust_dependencies() {
        let temp_dir = TempDir::new().unwrap();
        let repo_path = temp_dir.path();

        fs::write(
            repo_path.join("Cargo.toml"),
            r#"
[dependencies]
serde = "1.0"
my-rust-lib = { git = "https://github.com/org/my-rust-lib" }
"#,
        )
        .unwrap();

        let manager = DependencyManager::new(vec!["my-rust-lib".to_string()]);
        let deps = manager.find_internal_dependencies(repo_path);

        assert_eq!(deps, vec!["my-rust-lib"]);
    }

    #[test]
    fn test_find_hub_dependencies_by_alias() {
        let temp_dir = TempDir::new().unwrap();
        let repo_path = temp_dir.path().join("frontend");
        fs::create_dir_all(&repo_path).unwrap();

        fs::write(
            repo_path.join("package.json"),
            r#"
{
  "dependencies": {
    "@ryzespace/client": "workspace:*",
    "@ryzespace/helpcenter": "^1.0.0"
  }
}
"#,
        )
        .unwrap();

        let manager = DependencyManager::new(vec![
            "RyzeSpace.Client".to_string(),
            "RyzeSpace.HelpCenter".to_string(),
        ]);
        let deps = manager.find_internal_dependencies(&repo_path);

        assert_eq!(
            deps,
            vec![
                "RyzeSpace.Client".to_string(),
                "RyzeSpace.HelpCenter".to_string()
            ]
        );
    }

    #[test]
    fn test_dependency_graph() {
        let mut graph = DependencyGraph::new();
        graph.add_repository("lib-a".to_string(), vec![]);
        graph.add_repository("lib-b".to_string(), vec!["lib-a".to_string()]);
        graph.add_repository("app".to_string(), vec!["lib-b".to_string()]);

        let order = graph.topological_sort().unwrap();
        assert_eq!(order.len(), 3);

        // lib-a should come before lib-b
        let pos_a = order.iter().position(|x| x == "lib-a").unwrap();
        let pos_b = order.iter().position(|x| x == "lib-b").unwrap();
        assert!(pos_a < pos_b);
    }
}
