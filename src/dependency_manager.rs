//! Dependency Manager / Manager zależności
//! Scans repositories for internal dependencies / Skanuje repozytoria pod kątem wewnętrznych zależności

use std::collections::HashSet;
use std::path::{Path, PathBuf};
use tracing::{debug, info, warn};

pub struct DependencyManager {
    org_repos_names: Vec<String>,
}

impl DependencyManager {
    pub fn new(org_repos_names: Vec<String>) -> Self {
        Self { org_repos_names }
    }

    /// Find internal dependencies in a repository / Znajdź wewnętrzne zależności w repozytorium
    pub fn find_internal_dependencies(&self, repo_path: &Path) -> Vec<String> {
        let mut dependencies = HashSet::new();

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
                            // Sprawdź czy nazwa repo pojawia się w pliku zależności
                            if self.contains_dependency(&content, repo_name) {
                                dependencies.insert(repo_name.clone());
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

        let deps: Vec<String> = dependencies.into_iter().collect();
        if !deps.is_empty() {
            info!(
                "Found {} internal dependencies in {}",
                deps.len(),
                repo_path.display()
            );
        }

        deps
    }

    /// Check if content contains dependency reference / Sprawdź czy content zawiera referencję do zależności
    fn contains_dependency(&self, content: &str, repo_name: &str) -> bool {
        // For Rust/Cargo - check for git dependencies
        if content.contains("git = ") && content.contains(repo_name) {
            return true;
        }

        // For Python - check for package names
        if content.contains(repo_name) {
            // Check common patterns
            let patterns = [
                format!("{}==", repo_name),
                format!("{}>=", repo_name),
                format!("{}<=", repo_name),
                format!("{}~=", repo_name),
                format!("{}!=", repo_name),
                repo_name.to_string(),
            ];

            for pattern in &patterns {
                if content.contains(pattern) {
                    return true;
                }
            }
        }

        // For Node.js - check package.json dependencies
        if content.contains(&format!("\"{}\"", repo_name)) {
            return true;
        }

        // For Go - check import paths
        if content.contains(&format!("github.com/{}", repo_name)) {
            return true;
        }

        // For Java/Kotlin - check group IDs
        if content.contains(&format!("com.{}", repo_name)) {
            return true;
        }

        false
    }

    /// Scan all repositories and build dependency graph / Skanuj wszystkie repozytoria i zbuduj graf zależności
    pub fn build_dependency_graph(&self, repos: &[(String, PathBuf)]) -> DependencyGraph {
        let mut graph = DependencyGraph::new();

        for (repo_name, repo_path) in repos {
            let deps = self.find_internal_dependencies(repo_path);
            graph.add_repository(repo_name.clone(), deps);
        }

        info!(
            "Built dependency graph: {} repositories, {} edges",
            graph.repositories.len(),
            graph.edges.len()
        );

        graph
    }
}

/// Dependency graph / Graf zależności
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

    /// Get topological order for build / Pobierz kolejność topologiczną do buildu
    pub fn topological_sort(&self) -> Option<Vec<String>> {
        let mut in_degree = std::collections::HashMap::new();
        let mut result = Vec::new();

        // Initialize in-degrees / Inicjalizuj stopnie wejściowe
        for repo in &self.repositories {
            in_degree.insert(repo.clone(), 0);
        }

        // Calculate in-degrees / Oblicz stopnie wejściowe
        for (_, to) in &self.edges {
            *in_degree.entry(to.clone()).or_insert(0) += 1;
        }

        // Kahn's algorithm / Algorytm Kahna
        let mut queue: Vec<String> = in_degree
            .iter()
            .filter(|(_, &degree)| degree == 0)
            .map(|(repo, _)| repo.clone())
            .collect();

        while let Some(repo) = queue.pop() {
            result.push(repo.clone());

            if let Some(dependents) = self.adjacency.get(&repo) {
                for dependent in dependents {
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
            None // Cycle detected / Wykryto cykl
        }
    }

    /// Get repositories that depend on a given repo / Pobierz repozytoria zależne od danego
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
