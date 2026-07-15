import os
import logging
import re

logger = logging.getLogger(__name__)

class DependencyManager:
    def __init__(self, org_repos_names):
        self.org_repos_names = org_repos_names

    def find_internal_dependencies(self, repo_path):
        dependencies = []
        files_to_scan = ['requirements.txt', 'package.json', 'go.mod', 'pyproject.toml']
        for filename in files_to_scan:
            filepath = os.path.join(repo_path, filename)
            if os.path.exists(filepath):
                with open(filepath, 'r', encoding='utf-8') as f:
                    content = f.read()
                    for repo_name in self.org_repos_names:
                        if re.search(rf'\b{re.escape(repo_name)}\b', content):
                            dependencies.append(repo_name)
        return list(set(dependencies))
