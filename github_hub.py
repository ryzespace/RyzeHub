import requests
import subprocess
import os
import logging
import argparse
import yaml  # Wymaga: pip install PyYAML

# Konfiguracja logowania
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)


class GitHubManager:
    def __init__(self, org_name, token=None):
        self.org_name = org_name
        self.token = token
        self.headers = {}
        if token:
            self.headers["Authorization"] = f"token {token}"

    def get_repositories(self):
        """Pobiera listę wszystkich repozytoriów organizacji z obsługą paginacji."""
        repos = []
        page = 1
        url = f"https://api.github.com/orgs/{self.org_name}/repos?per_page=100"

        while True:
            response = requests.get(f"{url}&page={page}", headers=self.headers)
            if response.status_code != 200:
                logger.error(f"Błąd API GitHub: {response.status_code}")
                break

            data = response.json()
            if not data:
                break

            repos.extend(data)
            page += 1

        return repos

    def clone_repository(self, repo, target_dir):
        """Klonuje pojedyncze repozytorium."""
        repo_name = repo['name']
        clone_url = repo['clone_url']

        if self.token:
            clone_url = clone_url.replace("https://", f"https://{self.token}@")

        repo_path = os.path.join(target_dir, repo_name)

        if os.path.exists(repo_path):
            logger.info(f"Repozytorium {repo_name} już istnieje. Aktualizuję...")
            subprocess.run(["git", "-C", repo_path, "pull"], capture_output=True)
        else:
            logger.info(f"Klonuję {repo_name}...")
            try:
                subprocess.run(["git", "clone", clone_url, repo_path], check=True, capture_output=True)
            except subprocess.CalledProcessError as e:
                logger.error(f"Błąd podczas klonowania {repo_name}: {e}")
                return None

        return repo_path


class DockerManager:
    def __init__(self, hub_dir):
        self.hub_dir = hub_dir

    def detect_language(self, repo_path):
        """Próbuje zgadnąć język programowania na podstawie plików."""
        files = os.listdir(repo_path)
        if 'requirements.txt' in files or 'pyproject.toml' in files:
            return 'python'
        elif 'package.json' in files:
            return 'nodejs'
        elif 'go.mod' in files:
            return 'golang'
        elif 'pom.xml' in files or 'build.gradle' in files:
            return 'java'
        return 'generic'

    def generate_dockerfile(self, repo_path, lang):
        """Generuje prosty Dockerfile, jeśli nie istnieje."""
        dockerfile_path = os.path.join(repo_path, "Dockerfile")
        if os.path.exists(dockerfile_path):
            logger.info(f"Dockerfile już istnieje w {repo_path}. Pomijam generowanie.")
            return True

        logger.info(f"Generuję Dockerfile dla {lang} w {repo_path}...")

        templates = {
            'python': "FROM python:3.9-slim\nWORKDIR /app\nCOPY . .\nRUN pip install -r requirements.txt || echo 'No requirements.txt found'\nCMD [\"python\", \"main.py\"]",
            'nodejs': "FROM node:16-slim\nWORKDIR /app\nCOPY . .\nRUN npm install || echo 'No package.json found'\nCMD [\"npm\", \"start\"]",
            'golang': "FROM golang:1.18-slim\nWORKDIR /app\nCOPY . .\nRUN go build -o app .\nCMD [\"./app\"]",
            'generic': "FROM ubuntu:latest\nWORKDIR /app\nCOPY . .\nCMD [\"sh\"]"
        }

        content = templates.get(lang, templates['generic'])
        with open(dockerfile_path, "w") as f:
            f.write(content)
        return True

    def create_docker_compose(self, cloned_repos):
        """Tworzy główny plik docker-compose.yml dla wszystkich repozytoriów."""
        compose_data = {
            'version': '3.8',
            'services': {}
        }

        for repo_name, repo_path in cloned_repos.items():
            compose_data['services'][repo_name] = {
                'build': repo_path,
                'container_name': f"hub_{repo_name}",
                'restart': 'always'
            }

        compose_path = os.path.join(self.hub_dir, "docker-compose.yml")
        with open(compose_path, "w") as f:
            yaml.dump(compose_data, f, default_flow_style=False)

        logger.info(f"Utworzono plik orkiestracji: {compose_path}")


def main():
    parser = argparse.ArgumentParser(description="GitHub Org Hub - Clone & Containerize")
    parser.add_argument("--org", required=True, help="Nazwa organizacji na GitHubie")
    parser.add_argument("--token", help="GitHub Personal Access Token (opcjonalnie)")
    parser.add_argument("--dockerize", action="store_true", help="Włącz automatyczną konteneryzację")
    parser.add_argument("--dir", default="github_hub", help="Folder docelowy")

    args = parser.parse_args()

    # Inicjalizacja
    if not os.path.exists(args.dir):
        os.makedirs(args.dir)

    gh = GitHubManager(args.org, args.token)
    dm = DockerManager(args.dir)

    # 1. Pobieranie listy i klonowanie
    repos = gh.get_repositories()
    logger.info(f"Znaleziono {len(repos)} repozytoriów.")

    cloned_paths = {}
    for repo in repos:
        path = gh.clone_repository(repo, args.dir)
        if path:
            cloned_paths[repo['name']] = path

    # 2. Konteneryzacja (opcjonalnie)
    if args.dockerize:
        logger.info("Rozpoczynam proces konteneryzacji...")
        for repo_name, path in cloned_paths.items():
            lang = dm.detect_language(path)
            dm.generate_dockerfile(path, lang)

        dm.create_docker_compose(cloned_paths)
        logger.info("Konteneryzacja zakończona sukcesem.")

    logger.info(f"Wszystko gotowe! Repozytoria znajdują się w: {args.dir}")
    if args.dockerize:
        logger.info("Możesz teraz uruchomić wszystkie usługi komendą: cd github_hub && docker-compose up -d")


if __name__ == "__main__":
    main()
