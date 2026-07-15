import argparse
import os
import logging
from src.Ci_conf.config import LOG_FORMAT, LOG_LEVEL, DEFAULT_HUB_DIR
from src.Ci_conf.github_manager import GitHubManager
from src.Ci_conf.docker_manager import DockerManager
from src.Ci_conf.dependency_manager import DependencyManager

logging.basicConfig(level=LOG_LEVEL, format=LOG_FORMAT)
logger = logging.getLogger(__name__)


def main():
    parser = argparse.ArgumentParser(description="GitHub Org Hub")
    parser.add_argument("--dockerize", action="store_true")
    parser.add_argument("--submodules", action="store_true")
    parser.add_argument("--dir", default=DEFAULT_HUB_DIR)
    args = parser.parse_args()

    org = input("Podaj nazwę organizacji GitHub: ").strip()
    token = input("Podaj GitHub Token (opcjonalnie, Enter aby pominąć): ").strip() or None

    if not org:
        print("Błąd: Organizacja jest wymagana.")
        return

    if not os.path.exists(args.dir):
        os.makedirs(args.dir)

    gh = GitHubManager(org, token)
    dm = DockerManager(args.dir)

    repos_data = gh.get_repositories()
    repo_names = [r['name'] for r in repos_data]
    repo_urls = {r['name']: r['clone_url'] for r in repos_data}

    cloned_paths = {}
    for repo in repos_data:
        path = gh.clone_repository(repo, args.dir)
        if path:
            cloned_paths[repo['name']] = path

    if args.submodules:
        dep_manager = DependencyManager(repo_names)
        for name, path in cloned_paths.items():
            gh.init_submodules(path)
            internal_deps = dep_manager.find_internal_dependencies(path)
            for dep_name in internal_deps:
                if dep_name != name:
                    url = repo_urls[dep_name]
                    if token:
                        url = url.replace("https://", f"https://{token}@")
                    gh.add_submodule(path, url, dep_name)

    if args.dockerize:
        for name, path in cloned_paths.items():
            lang = dm.detect_language(path)
            dm.generate_dockerfile(path, lang)
        dm.create_docker_compose(cloned_paths)

    logger.info(f"Zakończono. Zasoby w: {args.dir}")


if __name__ == "__main__":
    main()
