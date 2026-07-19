//! Hub repository catalog.
//! Defines the repositories that are currently managed by the hub and
//! repositories planned as future dependency targets.

use std::collections::HashSet;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RepositoryStage {
    Active,
    Future,
}

#[derive(Debug, Clone, Copy)]
pub struct RepositoryDefinition {
    pub canonical_name: &'static str,
    pub stage: RepositoryStage,
    pub aliases: &'static [&'static str],
}

pub const HUB_REPOSITORIES: [RepositoryDefinition; 5] = [
    RepositoryDefinition {
        canonical_name: "RyzeSpace.Client",
        stage: RepositoryStage::Active,
        aliases: &[
            "client",
            "ryzespace-client",
            "ryzespace_client",
            "ryzespace/client",
            "@ryzespace/client",
        ],
    },
    RepositoryDefinition {
        canonical_name: "RyzeSpace.HelpCenter",
        stage: RepositoryStage::Active,
        aliases: &[
            "helpcenter",
            "help-center",
            "ryzespace-helpcenter",
            "ryzespace_helpcenter",
            "ryzespace/helpcenter",
            "@ryzespace/helpcenter",
        ],
    },
    RepositoryDefinition {
        canonical_name: "RyzeSpace.AdminPanel",
        stage: RepositoryStage::Future,
        aliases: &[
            "admin",
            "admin-panel",
            "panel-admina",
            "ryzespace-adminpanel",
            "ryzespace/admin-panel",
            "@ryzespace/admin-panel",
        ],
    },
    RepositoryDefinition {
        canonical_name: "RyzeSpace.Mobile",
        stage: RepositoryStage::Future,
        aliases: &[
            "mobile",
            "app-mobile",
            "aplikacja-mobilna",
            "ryzespace-mobile",
            "ryzespace/mobile",
            "@ryzespace/mobile",
        ],
    },
    RepositoryDefinition {
        canonical_name: "RyzeSpace.Desktop",
        stage: RepositoryStage::Future,
        aliases: &[
            "desktop",
            "desktop-app",
            "aplikacja-desktopowa",
            "ryzespace-desktop",
            "ryzespace/desktop",
            "@ryzespace/desktop",
        ],
    },
];

pub fn active_repository_names() -> Vec<String> {
    repository_names_by_stage(RepositoryStage::Active)
}

pub fn future_repository_names() -> Vec<String> {
    repository_names_by_stage(RepositoryStage::Future)
}

pub fn tracked_repository_names() -> Vec<String> {
    HUB_REPOSITORIES
        .iter()
        .map(|repo| repo.canonical_name.to_string())
        .collect()
}

pub fn repository_names_by_stage(stage: RepositoryStage) -> Vec<String> {
    HUB_REPOSITORIES
        .iter()
        .filter(|repo| repo.stage == stage)
        .map(|repo| repo.canonical_name.to_string())
        .collect()
}

pub fn resolve_canonical_repository_name(value: &str) -> Option<&'static str> {
    let normalized_value = normalize_repository_token(value);
    if normalized_value.is_empty() {
        return None;
    }

    HUB_REPOSITORIES
        .iter()
        .find(|repo| repository_tokens(repo).contains(&normalized_value))
        .map(|repo| repo.canonical_name)
}

pub fn dependency_identifiers(value: &str) -> Vec<String> {
    if let Some(repo) = find_repository_definition(value) {
        let mut identifiers = Vec::new();
        identifiers.push(repo.canonical_name.to_string());
        identifiers.extend(repo.aliases.iter().map(|alias| alias.to_string()));
        identifiers.extend(derived_identifiers(repo.canonical_name));
        dedupe_preserve_order(identifiers)
    } else {
        dedupe_preserve_order(derived_identifiers(value))
    }
}

pub fn normalize_repository_token(value: &str) -> String {
    value
        .chars()
        .filter(|c| c.is_ascii_alphanumeric())
        .map(|c| c.to_ascii_lowercase())
        .collect()
}

fn find_repository_definition(value: &str) -> Option<&'static RepositoryDefinition> {
    let normalized_value = normalize_repository_token(value);
    HUB_REPOSITORIES
        .iter()
        .find(|repo| repository_tokens(repo).contains(&normalized_value))
}

fn repository_tokens(repo: &RepositoryDefinition) -> HashSet<String> {
    let mut tokens = HashSet::new();
    tokens.insert(normalize_repository_token(repo.canonical_name));
    for alias in repo.aliases {
        tokens.insert(normalize_repository_token(alias));
    }
    for identifier in derived_identifiers(repo.canonical_name) {
        tokens.insert(normalize_repository_token(&identifier));
    }
    tokens
}

fn derived_identifiers(value: &str) -> Vec<String> {
    let trimmed = value.trim();
    if trimmed.is_empty() {
        return Vec::new();
    }

    let mut identifiers = vec![trimmed.to_string(), trimmed.to_lowercase()];
    let parts: Vec<&str> = trimmed
        .split(|c: char| matches!(c, '.' | '-' | '_' | '/' | '@'))
        .filter(|part| !part.is_empty())
        .collect();

    if let Some(last) = parts.last() {
        identifiers.push((*last).to_string());
        identifiers.push(last.to_lowercase());
    }

    if parts.len() >= 2 {
        let namespace = parts[..parts.len() - 1].join("");
        let namespace_kebab = parts[..parts.len() - 1]
            .iter()
            .map(|part| part.to_ascii_lowercase())
            .collect::<Vec<_>>()
            .join("-");
        let name = parts[parts.len() - 1].to_ascii_lowercase();

        identifiers.push(format!("{}.{}", namespace, name));
        identifiers.push(format!("{}-{}", namespace, name));
        identifiers.push(format!("{}_{}", namespace, name));
        identifiers.push(format!("{}/{}", namespace, name));
        identifiers.push(format!("@{}/{}", namespace_kebab, name));
    }

    dedupe_preserve_order(identifiers)
}

fn dedupe_preserve_order(values: Vec<String>) -> Vec<String> {
    let mut seen = HashSet::new();
    let mut result = Vec::new();

    for value in values {
        if value.trim().is_empty() {
            continue;
        }

        let key = normalize_repository_token(&value);
        if seen.insert(key) {
            result.push(value);
        }
    }

    result
}
