import requests
import subprocess
import os
import logging
from .config import GITHUB_API_URL

logger = logging.getLogger(__name__)


class GitHubManager:
    def __init__(self, org_name, token=None):
        self.org_name = org_name
        self.token = token
        self.headers = {}
        if token:
            self.headers["Authorization"] = f"token {token}"

    def get_repositories(self):

        repos = []
        page = 1
        url = f"{GITHUB_API_URL}/orgs/{self.org_name}/repos?per_page=100"

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

    def add_submodule(self, parent_repo_path, submodule_url, submodule_name):

        logger.info(f"Dodawanie {submodule_name} jako submoduł do {parent_repo_path}...")
        try:

            submodule_path = os.path.join("libs", submodule_name)

            subprocess.run(
                ["git", "submodule", "add", submodule_url, submodule_path],
                cwd=parent_repo_path,
                check=True,
                capture_output=True
            )
            return True
        except subprocess.CalledProcessError as e:
            logger.error(f"Błąd przy dodawaniu submodułu {submodule_name}: {e.stderr.decode()}")
            return False

    def init_submodules(self, repo_path):

        try:
            subprocess.run(["git", "submodule", "update", "--init", "--recursive"], cwd=repo_path, check=True)
            return True
        except subprocess.CalledProcessError as e:
            logger.error(f"Błąd inicjalizacji submodułów w {repo_path}: {e}")
            return False
