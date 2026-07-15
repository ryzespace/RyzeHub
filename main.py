import argparse
import json
import logging
import os
import sys
import time

from src.Ci_conf.config import (
    LOG_FORMAT,
    LOG_LEVEL,
    DEFAULT_HUB_DIR,
)

from src.Ci_conf.github_manager import GitHubManager
from src.Ci_conf.docker_manager import DockerManager
from src.Ci_conf.dependency_manager import DependencyManager

logging.basicConfig(
    level=LOG_LEVEL,
    format=LOG_FORMAT
)

logger = logging.getLogger(__name__)


def parse_args():
    parser = argparse.ArgumentParser(
        description="GitHub Organization Hub"
    )

    parser.add_argument(
        "--dockerize",
        action="store_true",
        help="Generate Dockerfiles and docker-compose"
    )

    parser.add_argument(
        "--submodules",
        action="store_true",
        help="Detect and create internal submodules"
    )

    parser.add_argument(
        "--dir",
        default=DEFAULT_HUB_DIR,
        help=f"Hub directory (default: {DEFAULT_HUB_DIR})"
    )

    parser.add_argument(
        "--continuous",
        "-c",
        action="store_true",
        help="Run continuously"
    )

    parser.add_argument(
        "--interval",
        "-i",
        type=int,
        default=300,
        help="Interval between executions in seconds"
    )

    parser.add_argument(
        "--org",
        help="GitHub organization name"
    )

    parser.add_argument(
        "--token",
        help="GitHub token"
    )

    parser.add_argument(
        "--log-level",
        choices=["DEBUG", "INFO", "WARNING", "ERROR"],
        default=LOG_LEVEL,
        help="Logging level"
    )

    return parser.parse_args()


def run_once(args):
    stats = {
        "repositories_found": 0,
        "repositories_cloned": 0,
        "dockerfiles_generated": 0,
        "submodules_added": 0,
        "failed": 0
    }

    org = args.org or input(
        "Podaj nazwę organizacji GitHub: "
    ).strip()

    token = args.token

    if token is None:
        token = (
            input(
                "Podaj GitHub Token (opcjonalnie, Enter aby pominąć): "
            ).strip()
            or None
        )

    if not org:
        raise ValueError("Organizacja GitHub jest wymagana")

    os.makedirs(args.dir, exist_ok=True)

    gh = GitHubManager(org, token)
    dm = DockerManager(args.dir)

    repos_data = gh.get_repositories()

    stats["repositories_found"] = len(repos_data)

    repo_names = [repo["name"] for repo in repos_data]
    repo_urls = {
        repo["name"]: repo["clone_url"]
        for repo in repos_data
    }

    cloned_paths = {}

    logger.info(
        "Znaleziono %s repozytoriów",
        len(repos_data)
    )

    for repo in repos_data:
        try:
            path = gh.clone_repository(
                repo,
                args.dir
            )

            if path:
                cloned_paths[repo["name"]] = path
                stats["repositories_cloned"] += 1

        except Exception:
            logger.exception(
                "Błąd podczas klonowania %s",
                repo["name"]
            )
            stats["failed"] += 1

    if args.submodules:
        dep_manager = DependencyManager(repo_names)

        for name, path in cloned_paths.items():
            try:
                gh.init_submodules(path)

                internal_deps = (
                    dep_manager.find_internal_dependencies(path)
                )

                for dep_name in internal_deps:
                    if dep_name == name:
                        continue

                    if dep_name not in repo_urls:
                        continue

                    url = repo_urls[dep_name]

                    if token:
                        url = url.replace(
                            "https://",
                            f"https://{token}@"
                        )

                    gh.add_submodule(
                        path,
                        url,
                        dep_name
                    )

                    stats["submodules_added"] += 1

            except Exception:
                logger.exception(
                    "Błąd podczas konfiguracji submodules dla %s",
                    name
                )
                stats["failed"] += 1

    if args.dockerize:
        for name, path in cloned_paths.items():
            try:
                lang = dm.detect_language(path)

                dm.generate_dockerfile(
                    path,
                    lang
                )

                stats["dockerfiles_generated"] += 1

            except Exception:
                logger.exception(
                    "Błąd podczas dockerizacji %s",
                    name
                )
                stats["failed"] += 1

        try:
            dm.create_docker_compose(
                cloned_paths
            )
        except Exception:
            logger.exception(
                "Błąd podczas generowania docker-compose"
            )
            stats["failed"] += 1

    logger.info(
        "Zakończono. Zasoby zapisano w: %s",
        args.dir
    )

    return stats


def run_continuous(args):
    logger.info(
        "Uruchomiono tryb ciągły. Interwał: %ss",
        args.interval
    )

    while True:
        try:
            run_once(args)

        except KeyboardInterrupt:
            logger.info("Zatrzymano przez użytkownika.")
            break

        except Exception:
            logger.exception(
                "Błąd podczas wykonania zadania"
            )

        logger.info(
            "Oczekiwanie %s sekund...",
            args.interval
        )

        time.sleep(args.interval)


def main():
    args = parse_args()

    logging.getLogger().setLevel(
        getattr(logging, args.log_level)
    )

    print("=" * 60)
    print(" GitHub Organization Hub")
    print("=" * 60)
    print(f" Dockerize: {args.dockerize}")
    print(f" Submodules: {args.submodules}")
    print(f" Directory: {args.dir}")
    print(
        f" Mode: {'continuous' if args.continuous else 'one-time'}"
    )

    if args.continuous:
        print(f" Interval: {args.interval}s")

    print("=" * 60)

    if args.continuous:
        run_continuous(args)
        return

    try:
        summary = run_once(args)

        print("\n" + "=" * 60)
        print(" SUMMARY")
        print("=" * 60)
        print(
            json.dumps(
                summary,
                indent=2,
                ensure_ascii=False
            )
        )

        if summary["failed"] > 0:
            sys.exit(1)

        sys.exit(0)

    except Exception as ex:
        logger.exception(ex)
        sys.exit(1)


if __name__ == "__main__":
    main()