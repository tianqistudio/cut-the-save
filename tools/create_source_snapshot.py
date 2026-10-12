#!/usr/bin/env python3
"""Create a deterministic, privacy-checked source snapshot for one release candidate."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path, PurePosixPath
import sys
import zipfile


ROOT_FILES = (
    ".gitignore",
    ".gitattributes",
    "CutTheSave.sln",
    "Directory.Build.props",
    "README.md",
    "README.en.md",
    "PRIVACY.md",
    "SECURITY.md",
    "THIRD_PARTY_NOTICES.md",
    "UPDATE_CONFIGURATION.md",
    "CHANGELOG.md",
    "RELEASE_CHECKLIST.md",
    "FIELD_ACCEPTANCE.md",
    "CutTheSave.update.ini.example",
)
ROOT_DIRECTORIES = (".github", "docs", "scripts", "src", "tests", "tools")
EXCLUDED_PARTS = {"bin", "obj", "__pycache__", ".git", ".vs"}
EXCLUDED_SUFFIXES = {".user", ".suo", ".pfx", ".p12", ".pem", ".key"}
BLOCKED_NAMES = {".env", "cutthesave.update.ini", "ctr_save.bin"}
FIXED_ZIP_TIME = (1980, 1, 1, 0, 0, 0)


class SnapshotError(RuntimeError):
    pass


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def collect_files(root: Path) -> list[Path]:
    candidates: list[Path] = []
    for relative in ROOT_FILES:
        path = root / relative
        if not path.is_file():
            raise SnapshotError(f"required source file is missing: {relative}")
        candidates.append(path)

    for relative in ROOT_DIRECTORIES:
        directory = root / relative
        if not directory.is_dir():
            raise SnapshotError(f"required source directory is missing: {relative}")
        for path in directory.rglob("*"):
            source_relative = path.relative_to(root)
            if any(part.lower() in EXCLUDED_PARTS for part in source_relative.parts):
                continue
            if path.is_symlink():
                raise SnapshotError(f"source snapshot refuses symlinks: {source_relative.as_posix()}")
            if source_relative.as_posix() == "docs/completion-audit.md":
                continue
            if not path.is_file():
                continue
            if path.name.lower() in BLOCKED_NAMES or path.name.lower().startswith(".env") or path.suffix.lower() in EXCLUDED_SUFFIXES:
                raise SnapshotError(f"source snapshot refuses a sensitive file: {source_relative.as_posix()}")
            allowed = {".github": {".yml"}, "docs": {".md"}, "scripts": {".ps1"},
                       "src": {".cs", ".csproj", ".xaml"}, "tests": {".cs", ".csproj", ".py"}, "tools": {".py"}}
            if path.suffix.lower() not in allowed[relative] and path.name != "packages.lock.json":
                continue
            candidates.append(path)

    unique = {path.resolve(): path for path in candidates}
    return sorted(unique.values(), key=lambda path: path.relative_to(root).as_posix())


def build_manifest(root: Path, files: list[Path]) -> dict[str, object]:
    records: list[dict[str, object]] = []
    tree_digest = hashlib.sha256()
    for path in files:
        relative = path.relative_to(root).as_posix()
        data_hash = sha256_file(path)
        size = path.stat().st_size
        tree_digest.update(relative.encode("utf-8"))
        tree_digest.update(b"\0")
        tree_digest.update(str(size).encode("ascii"))
        tree_digest.update(b"\0")
        tree_digest.update(data_hash.encode("ascii"))
        tree_digest.update(b"\n")
        records.append({"path": relative, "byte_size": size, "sha256": data_hash})

    return {
        "schema_version": 1,
        "kind": "cut_the_save_source_snapshot",
        "project": "DEV_Cut the Save",
        "created_at_utc": datetime.now(timezone.utc).isoformat(),
        "tree_hash_algorithm": "sha256(path_utf8 + NUL + decimal_size + NUL + file_sha256 + LF)",
        "tree_sha256": tree_digest.hexdigest(),
        "file_count": len(records),
        "files": records,
    }


def write_snapshot(root: Path, output: Path) -> dict[str, object]:
    releases_root = (root / "releases" / "sources").resolve()
    output = output.resolve()
    try:
        output.relative_to(releases_root)
    except ValueError as exc:
        raise SnapshotError("output must stay under releases/sources") from exc
    if output.suffix.lower() != ".zip":
        raise SnapshotError("source snapshot output must use the .zip extension")
    if output.exists():
        raise SnapshotError(f"refusing to overwrite source snapshot: {output}")

    files = collect_files(root)
    manifest = build_manifest(root, files)
    manifest_bytes = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in files:
            relative = path.relative_to(root).as_posix()
            info = zipfile.ZipInfo(relative, FIXED_ZIP_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, path.read_bytes())

        manifest_info = zipfile.ZipInfo("_CutTheSave-source-manifest.json", FIXED_ZIP_TIME)
        manifest_info.compress_type = zipfile.ZIP_DEFLATED
        manifest_info.external_attr = 0o100644 << 16
        archive.writestr(manifest_info, manifest_bytes)

    return {
        "path": output.relative_to(root).as_posix(),
        "byte_size": output.stat().st_size,
        "sha256": sha256_file(output),
        "tree_sha256": manifest["tree_sha256"],
        "file_count": manifest["file_count"],
        "manifest_sha256": sha256_bytes(manifest_bytes),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = args.root.resolve()
    output = args.output if args.output.is_absolute() else root / args.output
    try:
        result = write_snapshot(root, output)
    except SnapshotError as exc:
        print(f"FAIL source snapshot: {exc}", file=sys.stderr)
        return 1
    print("PASS source snapshot")
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
