import os
import yaml
import logging
from .config import DOCKER_TEMPLATES

logger = logging.getLogger(__name__)

class DockerManager:
    def __init__(self, hub_dir):
        self.hub_dir = hub_dir

    def detect_language(self, repo_path):
        files = os.listdir(repo_path)
        if 'requirements.txt' in files or 'pyproject.toml' in files:
            return 'python'
        elif 'package.json' in files:
            return 'nodejs'
        elif 'go.mod' in files:
            return 'golang'
        return 'generic'

    def generate_dockerfile(self, repo_path, lang):
        dockerfile_path = os.path.join(repo_path, "Dockerfile")
        if os.path.exists(dockerfile_path):
            return True
        template = DOCKER_TEMPLATES.get(lang, DOCKER_TEMPLATES['generic'])
        content = f"FROM {template['image']}\nWORKDIR /app\nCOPY . .\n{template['cmd']}"
        with open(dockerfile_path, "w") as f:
            f.write(content)
        return True

    def create_docker_compose(self, cloned_repos):
        compose_data = {
            'version': '3.8',
            'services': {
                name: {
                    'build': path,
                    'container_name': f"hub_{name}",
                    'restart': 'always'
                } for name, path in cloned_repos.items()
            }
        }
        compose_path = os.path.join(self.hub_dir, "docker-compose.yml")
        with open(compose_path, "w") as f:
            yaml.dump(compose_data, f, default_flow_style=False)
