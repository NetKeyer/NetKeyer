#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import zipfile
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

from service_version import DEFAULT_PROTOCOL_VERSION, load_services_version


@dataclass
class BuildInfo:
    services_version: str
    protocol_version: int
    build_tag: str
    commit: str
    built_at_utc: str


SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_OUTPUT_DIR = SCRIPT_DIR / "dist"

# Deployment bundle contents. These are sufficient to run rendezvous/relay with Docker Compose.
INCLUDE_PATHS = [
    "docker-compose.yml",
    "README.md",
    ".python-version",
    "pyproject.toml",
    "uv.lock",
    "service_version.py",
    "server",
    "relay",
]

EXCLUDE_NAMES = {
    "__pycache__",
    ".venv",
    ".pytest_cache",
    ".mypy_cache",
}

EXCLUDE_SUFFIXES = {".pyc", ".pyo"}

EXCLUDE_RELATIVE_DIRS = {
    Path("server/tests"),
    Path("relay/tests"),
}

COMPOSE_STAMP_KEYS = {
    "RENDEZVOUS_SERVICES_VERSION",
    "RENDEZVOUS_SERVICES_PROTOCOL_VERSION",
    "RENDEZVOUS_SERVICES_BUILD_TAG",
    "RENDEZVOUS_SERVICES_BUILD_COMMIT",
    "RENDEZVOUS_SERVICES_BUILD_DATE",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Create a stamped, cross-platform rendezvous/relay deployment artifact (.zip) "
            "with compose metadata for release traceability."
        )
    )
    parser.add_argument("--version", default="", help="Services version override. Defaults to pyproject project.version.")
    parser.add_argument("--protocol-version", type=int, default=DEFAULT_PROTOCOL_VERSION, help="Protocol version stamp.")
    parser.add_argument("--tag", default="", help="Build tag override (example: rs-v0.2.0).")
    parser.add_argument("--commit", default="", help="Build commit override (example: git short SHA).")
    parser.add_argument("--build-date", default="", help="Build date override in UTC ISO-8601 format.")
    parser.add_argument("--output-dir", default=str(DEFAULT_OUTPUT_DIR), help="Output directory for artifact.")
    parser.add_argument("--keep-staging", action="store_true", help="Keep the staging folder after zip is created.")
    parser.add_argument(
        "--include-path",
        action="append",
        default=[],
        help=(
            "Additional file or directory (relative to rendezvous_services/) to include in the bundle. "
            "Can be provided multiple times."
        ),
    )
    return parser.parse_args()


def run_git(args: list[str]) -> str:
    try:
        result = subprocess.run(
            ["git", *args],
            cwd=str(SCRIPT_DIR.parent),
            check=True,
            capture_output=True,
            text=True,
        )
        return result.stdout.strip()
    except Exception:
        return ""


def detect_build_info(args: argparse.Namespace) -> BuildInfo:
    version = (args.version or "").strip() or load_services_version()
    protocol_version = max(1, int(args.protocol_version))

    git_tag = run_git(["describe", "--tags", "--exact-match"])
    git_commit = run_git(["rev-parse", "--short", "HEAD"])

    build_tag = (args.tag or "").strip() or git_tag or f"rs-v{version}"
    commit = (args.commit or "").strip() or git_commit
    built_at_utc = (args.build_date or "").strip() or datetime.now(timezone.utc).isoformat()

    return BuildInfo(
        services_version=version,
        protocol_version=protocol_version,
        build_tag=build_tag,
        commit=commit,
        built_at_utc=built_at_utc,
    )


def should_exclude(path: Path, rel_path: Path) -> bool:
    if path.name in EXCLUDE_NAMES:
        return True
    if path.suffix.lower() in EXCLUDE_SUFFIXES:
        return True
    for excluded in EXCLUDE_RELATIVE_DIRS:
        try:
            rel_path.relative_to(excluded)
            return True
        except ValueError:
            continue
    return False


def copy_with_filters(src: Path, dst: Path) -> None:
    if src.is_file():
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
        return

    for root, dirs, files in os.walk(src):
        root_path = Path(root)
        root_rel = root_path.relative_to(src)

        kept_dirs: list[str] = []
        for d in dirs:
            path = root_path / d
            rel_from_script = (src.relative_to(SCRIPT_DIR) / root_rel / d) if src != SCRIPT_DIR else (root_rel / d)
            if should_exclude(path, rel_from_script):
                continue
            kept_dirs.append(d)
        dirs[:] = kept_dirs

        for f in files:
            file_path = root_path / f
            rel_from_script = (src.relative_to(SCRIPT_DIR) / root_rel / f) if src != SCRIPT_DIR else (root_rel / f)
            if should_exclude(file_path, rel_from_script):
                continue

            target = dst / root_rel / f
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(file_path, target)


def stamp_compose(compose_path: Path, build_info: BuildInfo) -> None:
    values = {
        "RENDEZVOUS_SERVICES_VERSION": build_info.services_version,
        "RENDEZVOUS_SERVICES_PROTOCOL_VERSION": str(build_info.protocol_version),
        "RENDEZVOUS_SERVICES_BUILD_TAG": build_info.build_tag,
        "RENDEZVOUS_SERVICES_BUILD_COMMIT": build_info.commit,
        "RENDEZVOUS_SERVICES_BUILD_DATE": build_info.built_at_utc,
    }

    lines = compose_path.read_text(encoding="utf-8").splitlines()
    out: list[str] = []

    for line in lines:
        replaced = False
        for key in COMPOSE_STAMP_KEYS:
            pattern = rf"^(\s*){re.escape(key)}:\s*.*$"
            match = re.match(pattern, line)
            if match:
                indent = match.group(1)
                out.append(f'{indent}{key}: "{values[key]}"')
                replaced = True
                break
        if not replaced:
            out.append(line)

    compose_path.write_text("\n".join(out) + "\n", encoding="utf-8")


def write_metadata(bundle_root: Path, build_info: BuildInfo) -> None:
    metadata = {
        "services_version": build_info.services_version,
        "protocol_version": build_info.protocol_version,
        "build_tag": build_info.build_tag,
        "commit": build_info.commit,
        "built_at_utc": build_info.built_at_utc,
        "artifact_name": f"netkeyer-rendezvous-services-{build_info.services_version}.zip",
    }

    (bundle_root / "RELEASE_METADATA.json").write_text(
        json.dumps(metadata, indent=2) + "\n",
        encoding="utf-8",
    )

    (bundle_root / "DEPLOYMENT.md").write_text(
        "\n".join(
            [
                "# NetKeyer Rendezvous Services Deployment",
                "",
                "This bundle is release-stamped and ready for Docker Compose deployment.",
                "",
                "## Start relay + rendezvous",
                "",
                "```bash",
                "docker compose -f docker-compose.yml up -d",
                "```",
                "",
                "Version/build metadata is embedded in docker-compose environment values and exposed in rendezvous /health.",
            ]
        )
        + "\n",
        encoding="utf-8",
    )


def zip_directory(source_dir: Path, zip_path: Path) -> None:
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for path in sorted(source_dir.rglob("*")):
            if path.is_file():
                arcname = path.relative_to(source_dir)
                zf.write(path, arcname)


def build_artifact(
    build_info: BuildInfo,
    output_dir: Path,
    keep_staging: bool,
    extra_include_paths: list[str] | None = None,
) -> Path:
    output_dir.mkdir(parents=True, exist_ok=True)

    bundle_name = f"netkeyer-rendezvous-services-{build_info.services_version}"
    staging_root = output_dir / bundle_name

    if staging_root.exists():
        shutil.rmtree(staging_root)
    staging_root.mkdir(parents=True, exist_ok=True)

    include_paths = list(INCLUDE_PATHS)
    for rel in extra_include_paths or []:
        value = (rel or "").strip().replace("\\", "/")
        if value and value not in include_paths:
            include_paths.append(value)

    for rel in include_paths:
        src = SCRIPT_DIR / rel
        if not src.exists():
            continue
        dst = staging_root / rel
        copy_with_filters(src, dst)

    compose_path = staging_root / "docker-compose.yml"
    if compose_path.exists():
        stamp_compose(compose_path, build_info)

    write_metadata(staging_root, build_info)

    zip_path = output_dir / f"{bundle_name}.zip"
    if zip_path.exists():
        zip_path.unlink()
    zip_directory(staging_root, zip_path)

    if not keep_staging:
        shutil.rmtree(staging_root)

    return zip_path


def main() -> int:
    args = parse_args()
    build_info = detect_build_info(args)
    output_dir = Path(args.output_dir).resolve()

    artifact = build_artifact(
        build_info,
        output_dir,
        keep_staging=args.keep_staging,
        extra_include_paths=args.include_path,
    )

    print(f"Created artifact: {artifact}")
    print(f"Services version: {build_info.services_version}")
    print(f"Protocol version: {build_info.protocol_version}")
    print(f"Build tag: {build_info.build_tag}")
    print(f"Build commit: {build_info.commit}")
    print(f"Build date UTC: {build_info.built_at_utc}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
