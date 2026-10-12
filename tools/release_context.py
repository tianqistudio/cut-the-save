#!/usr/bin/env python3
"""Audit and rebuild the small, explicit Cut the Save release context.

This tool is deliberately separate from the product build, signing, publishing,
and field-acceptance scripts.  It never selects a release by file name or mtime.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import sys
import tempfile
from typing import Any, Iterable
import zipfile


SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
REPARSE_POINT = 0x400


class AuditError(RuntimeError):
    """Raised when release governance data is unsafe or inconsistent."""


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise AuditError(f"missing JSON file: {path}") from exc
    except json.JSONDecodeError as exc:
        raise AuditError(f"invalid JSON in {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise AuditError(f"JSON root must be an object: {path}")
    return value


def normalize_relative(raw: Any, *, label: str) -> str:
    if not isinstance(raw, str) or not raw.strip():
        raise AuditError(f"{label} must be a non-empty relative POSIX path")
    if "\\" in raw:
        raise AuditError(f"{label} must use POSIX separators: {raw!r}")
    pure = PurePosixPath(raw)
    if pure.is_absolute() or raw.startswith("/"):
        raise AuditError(f"{label} must not be absolute: {raw!r}")
    if any(part in {"", ".", ".."} for part in pure.parts):
        raise AuditError(f"{label} contains traversal or ambiguous segments: {raw!r}")
    normalized = pure.as_posix()
    if normalized != raw.rstrip("/"):
        raise AuditError(f"{label} is not normalized: {raw!r}")
    return normalized


def _is_reparse_or_link(path: Path) -> bool:
    try:
        stat = path.lstat()
    except FileNotFoundError:
        return False
    attributes = getattr(stat, "st_file_attributes", 0)
    return path.is_symlink() or bool(attributes & REPARSE_POINT)


def safe_path(root: Path, raw: Any, *, label: str, must_exist: bool = True) -> Path:
    relative = normalize_relative(raw, label=label)
    root_resolved = root.resolve()
    candidate = root_resolved.joinpath(*PurePosixPath(relative).parts)
    try:
        candidate.resolve(strict=False).relative_to(root_resolved)
    except ValueError as exc:
        raise AuditError(f"{label} escapes the workspace: {relative}") from exc

    cursor = root_resolved
    for part in PurePosixPath(relative).parts:
        cursor = cursor / part
        if cursor.exists() and _is_reparse_or_link(cursor):
            raise AuditError(f"{label} crosses a symlink or reparse point: {relative}")

    if must_exist and not candidate.exists():
        raise AuditError(f"{label} does not exist: {relative}")
    return candidate


def _require_sha256(value: Any, *, label: str) -> str:
    if not isinstance(value, str) or not SHA256_RE.fullmatch(value):
        raise AuditError(f"{label} must be a lowercase SHA-256 value")
    return value


def _path_is_under(path: str, root: str) -> bool:
    path_parts = PurePosixPath(path)
    root_parts = PurePosixPath(root)
    return path_parts == root_parts or root_parts in path_parts.parents


def _audit_hashed_file(root: Path, record: dict[str, Any], *, label: str) -> Path:
    path = safe_path(root, record.get("path"), label=f"{label}.path")
    kind = record.get("kind")
    if kind == "file" and not path.is_file():
        raise AuditError(f"{label} expected a file: {record.get('path')}")
    if kind == "directory" and not path.is_dir():
        raise AuditError(f"{label} expected a directory: {record.get('path')}")
    if kind not in {"file", "directory"}:
        raise AuditError(f"{label}.kind must be file or directory")

    hash_status = record.get("hash_status")
    if hash_status == "verified":
        if kind != "file":
            raise AuditError(f"{label} cannot claim a verified whole-directory hash")
        expected = _require_sha256(record.get("sha256"), label=f"{label}.sha256")
        actual = sha256_file(path)
        if actual != expected:
            raise AuditError(
                f"SHA-256 mismatch for {record.get('path')}: expected {expected}, got {actual}"
            )
        expected_size = record.get("byte_size")
        if not isinstance(expected_size, int) or expected_size < 0:
            raise AuditError(f"{label}.byte_size must be a non-negative integer")
        if path.stat().st_size != expected_size:
            raise AuditError(
                f"size mismatch for {record.get('path')}: "
                f"expected {expected_size}, got {path.stat().st_size}"
            )
    elif hash_status not in {
        "pending_directory_not_hashed",
        "pending_unfrozen_source_set",
        "pending_not_selected",
    }:
        raise AuditError(f"unsupported {label}.hash_status: {hash_status!r}")
    return path


def _audit_source_snapshot(root: Path, source_set: dict[str, Any], *, label: str) -> None:
    if source_set.get("hash_status") != "verified":
        return
    if source_set.get("status") != "frozen":
        raise AuditError(f"{label} must be frozen when its hash is verified")
    tree_hash = _require_sha256(source_set.get("sha256"), label=f"{label}.sha256")
    snapshot_path = safe_path(root, source_set.get("snapshot_path"), label=f"{label}.snapshot_path")
    if not snapshot_path.is_file() or snapshot_path.suffix.lower() != ".zip":
        raise AuditError(f"{label}.snapshot_path must be a ZIP file")

    try:
        with zipfile.ZipFile(snapshot_path, "r") as archive:
            names = archive.namelist()
            if len(names) != len(set(names)):
                raise AuditError(f"{label} source snapshot contains duplicate ZIP entries")
            manifest_name = "_CutTheSave-source-manifest.json"
            if manifest_name not in names:
                raise AuditError(f"{label} source snapshot is missing its manifest")
            manifest = json.loads(archive.read(manifest_name).decode("utf-8"))
            if manifest.get("schema_version") != 1 or manifest.get("kind") != "cut_the_save_source_snapshot":
                raise AuditError(f"{label} source snapshot manifest has an unexpected schema or kind")
            if manifest.get("project") != "DEV_Cut the Save" or manifest.get("tree_sha256") != tree_hash:
                raise AuditError(f"{label} source snapshot project or tree hash does not match the index")
            records = manifest.get("files")
            if not isinstance(records, list) or manifest.get("file_count") != len(records):
                raise AuditError(f"{label} source snapshot file list is invalid")

            expected_names = {manifest_name}
            recomputed_tree = hashlib.sha256()
            previous_path = ""
            for position, record in enumerate(records):
                if not isinstance(record, dict):
                    raise AuditError(f"{label} source snapshot files[{position}] must be an object")
                relative = normalize_relative(record.get("path"), label=f"{label}.files[{position}].path")
                if relative <= previous_path:
                    raise AuditError(f"{label} source snapshot paths must be unique and sorted")
                previous_path = relative
                expected_names.add(relative)
                expected_size = record.get("byte_size")
                if not isinstance(expected_size, int) or expected_size < 0:
                    raise AuditError(f"{label} source snapshot has an invalid byte size")
                expected_hash = _require_sha256(record.get("sha256"), label=f"{label}.files[{position}].sha256")
                try:
                    data = archive.read(relative)
                except KeyError as exc:
                    raise AuditError(f"{label} source snapshot is missing {relative}") from exc
                if len(data) != expected_size or hashlib.sha256(data).hexdigest() != expected_hash:
                    raise AuditError(f"{label} source snapshot content does not match its manifest: {relative}")
                recomputed_tree.update(relative.encode("utf-8"))
                recomputed_tree.update(b"\0")
                recomputed_tree.update(str(expected_size).encode("ascii"))
                recomputed_tree.update(b"\0")
                recomputed_tree.update(expected_hash.encode("ascii"))
                recomputed_tree.update(b"\n")
            if set(names) != expected_names:
                raise AuditError(f"{label} source snapshot contains unmanifested entries")
            if recomputed_tree.hexdigest() != tree_hash:
                raise AuditError(f"{label} source snapshot tree hash verification failed")
    except (OSError, zipfile.BadZipFile, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AuditError(f"{label} source snapshot could not be verified: {exc}") from exc


def _audit_package_validation_receipt(
    root: Path,
    candidate: dict[str, Any],
    artifacts: dict[str, dict[str, Any]],
    *,
    label: str,
) -> None:
    if candidate.get("package_validation_status") != "verified":
        return
    receipt_path = safe_path(root, candidate.get("validation_receipt"), label=f"{label}.validation_receipt")
    receipt = load_json(receipt_path)
    if receipt.get("schema_version") != 1 or receipt.get("kind") != "package_validation":
        raise AuditError(f"{label} validation receipt has an unexpected schema or kind")
    if receipt.get("release_id") != candidate.get("release_id") or receipt.get("result") != "verified":
        raise AuditError(f"{label} validation receipt does not verify this release")

    expected = {
        "zip": artifacts[candidate["zip_artifact_id"]],
        "checksum_manifest": artifacts[candidate["manifest_artifact_id"]],
        "validator": artifacts[candidate["validator_artifact_id"]],
    }
    for receipt_key, artifact in expected.items():
        record = receipt.get(receipt_key)
        if not isinstance(record, dict):
            raise AuditError(f"{label} validation receipt is missing {receipt_key}")
        expected_path = artifact.get("receipt_original_path", artifact.get("path")) if receipt_key == "validator" else artifact.get("path")
        if receipt_key == "validator" and artifact.get("receipt_original_path") is not None:
            if not artifact["path"].startswith("releases/validators/") or expected_path != "scripts/Verify-CutTheSaveRelease.ps1":
                raise AuditError("invalid archived validator provenance")
        if record.get("path") != expected_path or record.get("sha256") != artifact.get("sha256"):
            raise AuditError(f"{label} validation receipt does not match indexed {receipt_key}")


def _audit_checksum_binding(
    root: Path,
    candidate: dict[str, Any],
    artifacts: dict[str, dict[str, Any]],
    *,
    label: str,
) -> None:
    if candidate.get("checksum_binding_status") != "verified":
        return
    zip_record = artifacts[candidate["zip_artifact_id"]]
    manifest_record = artifacts[candidate["manifest_artifact_id"]]
    zip_hash = _require_sha256(zip_record.get("sha256"), label=f"{label}.zip.sha256")
    zip_name = PurePosixPath(zip_record["path"]).name
    manifest_path = safe_path(root, manifest_record["path"], label=f"{label}.manifest")
    bindings: dict[str, str] = {}
    for line in manifest_path.read_text(encoding="utf-8-sig").splitlines():
        match = re.fullmatch(r"([0-9A-Fa-f]{64})\s+(.+)", line.strip())
        if not match:
            raise AuditError(f"invalid checksum line in {manifest_record['path']}: {line!r}")
        bindings[match.group(2).strip()] = match.group(1).lower()
    if bindings.get(zip_name) != zip_hash:
        raise AuditError(f"manifest does not bind {zip_name} to its indexed SHA-256")


def _audit_receipt(root: Path, path: Path) -> dict[str, Any]:
    receipt = load_json(path)
    if receipt.get("schema_version") != 1 or receipt.get("kind") != "baseline_inventory":
        raise AuditError("baseline receipt must be schema 1 and kind baseline_inventory")
    for group in ("artifacts_top_level", "work_top_level", "formal_candidate_files"):
        records = receipt.get("inventory", {}).get(group)
        if not isinstance(records, list):
            raise AuditError(f"baseline receipt inventory.{group} must be an array")
        for position, record in enumerate(records):
            if not isinstance(record, dict):
                raise AuditError(f"baseline receipt {group}[{position}] must be an object")
            safe_path(root, record.get("path"), label=f"receipt.{group}[{position}].path")
            byte_size = record.get("byte_size")
            if not isinstance(byte_size, int) or byte_size < 0:
                raise AuditError(f"receipt.{group}[{position}].byte_size is invalid")
            if record.get("hash_status") == "verified":
                _require_sha256(record.get("sha256"), label=f"receipt.{group}[{position}].sha256")
    return receipt


def _audit_cleanup(root: Path, path: Path, expected_receipt: str) -> dict[str, Any]:
    cleanup = load_json(path)
    if cleanup.get("schema_version") != 1 or cleanup.get("mode") != "dry-run":
        raise AuditError("cleanup manifest must be schema 1 and mode dry-run")
    if cleanup.get("apply_authorized") is not False:
        raise AuditError("cleanup manifest must keep apply_authorized=false")
    if cleanup.get("baseline_receipt") != expected_receipt:
        raise AuditError("cleanup manifest points to an unexpected baseline receipt")

    allow_roots = cleanup.get("allow_roots")
    protected_roots = cleanup.get("protected_roots")
    allowlist = cleanup.get("allowlist")
    candidates = cleanup.get("delete_candidates")
    if not all(isinstance(value, list) for value in (allow_roots, protected_roots, allowlist, candidates)):
        raise AuditError("cleanup roots, allowlist, and delete_candidates must be arrays")
    normalized_allow_roots = [normalize_relative(p, label="cleanup.allow_roots") for p in allow_roots]
    normalized_protected = [normalize_relative(p, label="cleanup.protected_roots") for p in protected_roots]
    normalized_allowlist = [normalize_relative(p, label="cleanup.allowlist") for p in allowlist]

    if len(set(normalized_allowlist)) != len(normalized_allowlist):
        raise AuditError("cleanup allowlist contains duplicates")
    for item in normalized_allowlist:
        safe_path(root, item, label="cleanup.allowlist")
        if not any(_path_is_under(item, allowed) for allowed in normalized_allow_roots):
            raise AuditError(f"cleanup allowlist path is outside allowed roots: {item}")
        if any(_path_is_under(item, protected) for protected in normalized_protected):
            raise AuditError(f"cleanup allowlist path is protected: {item}")

    candidate_paths: list[str] = []
    for position, record in enumerate(candidates):
        if not isinstance(record, dict):
            raise AuditError(f"cleanup.delete_candidates[{position}] must be an object")
        candidate_path = normalize_relative(
            record.get("path"), label=f"cleanup.delete_candidates[{position}].path"
        )
        safe_path(root, candidate_path, label=f"cleanup.delete_candidates[{position}].path")
        if candidate_path not in normalized_allowlist:
            raise AuditError(f"cleanup candidate is not explicitly allowlisted: {candidate_path}")
        candidate_paths.append(candidate_path)
    if len(set(candidate_paths)) != len(candidate_paths):
        raise AuditError("cleanup delete_candidates contains duplicates")
    return cleanup


def render_current(index: dict[str, Any]) -> str:
    current = index["current_release"]
    candidates = index.get("release_candidates", [])
    candidate = candidates[0] if candidates else None
    artifacts = {entry["artifact_id"]: entry for entry in index.get("artifacts", [])}
    source_set = index.get("source_sets", [{}])[0]
    pending_gates = [gate for gate in index.get("field_gates", []) if gate.get("status") == "pending"]

    lines = [
        "# Cut the Save 当前发布上下文",
        "",
        "> 本页由 `artifact-index.json` 生成。它是发布状态入口，不是构建记录；不得按文件名日期或修改时间猜测正式版本。",
        "",
        "## 当前结论",
        "",
    ]
    if current.get("status") == "verified":
        lines.append(f"- 正式 current：`{current['release_id']}`（已由索引显式确认）。")
    else:
        lines.append(f"- 正式 current：**未确认**。{current['reason']}")
    lines.extend(
        [
            f"- 源码集：`{source_set.get('source_set_id')}`，状态 `{source_set.get('status')}`；{source_set.get('provenance_note')}",
            "- 权威规则：候选包、校验通过和现场验收是三层不同状态；任一层都不能自动推出正式 current。",
            "",
            "## Candidate / Verified / Field gates",
            "",
            "| 层级 | 状态 | 当前含义 |",
            "| --- | --- | --- |",
        ]
    )
    if candidate:
        lines.extend(
            [
                f"| Candidate | `{candidate['status']}` | 索引明确登记的待审候选 `{candidate['release_id']}`；不是正式版。 |",
                f"| Verified | `{candidate['package_validation_status']}` | ZIP 与相邻清单的绑定为 `{candidate['checksum_binding_status']}`；未运行的验证不得写成通过。 |",
                f"| Field gates | `{candidate['field_gate_status']}` | 仍有 {len(pending_gates)} 项候选相关现场门槛未形成通过回执。 |",
                "",
                "## 显式候选",
                "",
            ]
        )
        zip_artifact = artifacts[candidate["zip_artifact_id"]]
        manifest_artifact = artifacts[candidate["manifest_artifact_id"]]
        validator_artifact = artifacts[candidate["validator_artifact_id"]]
        lines.extend(
            [
                f"- ZIP：`{zip_artifact['path']}`，SHA-256 `{zip_artifact['sha256']}`。",
                f"- 校验清单：`{manifest_artifact['path']}`，SHA-256 `{manifest_artifact['sha256']}`。",
                f"- 包验证器：`{validator_artifact['path']}`，SHA-256 `{validator_artifact['sha256']}`。",
                f"- 来源绑定：`{candidate['provenance_status']}`。{candidate['selection_note']}",
            ]
        )
    lines.extend(["", "## 未关闭的现场门槛", ""])
    if pending_gates:
        for gate in pending_gates:
            lines.append(f"- `{gate['gate_id']}`：{gate['description']}")
    else:
        lines.append("- 无待处理现场门槛；通过或豁免仍必须有显式回执。")
    governance = index["governance"]
    lines.extend(
        [
            "",
            "## 安全与维护边界",
            "",
            "- 本治理层不构建、不签名、不发布，不读取真实用户存档或凭据，也不修改 RTSS。",
            f"- 清理清单 `{governance['cleanup_manifest']}` 仅为 dry-run，当前没有获准删除项；`artifacts/` 始终受保护。",
            "- 只有显式选择 release ID、绑定源码与完整验证回执、关闭或明确豁免现场门槛后，才能把 `current_release.status` 改为 `verified`。",
            "- 运行 `python tools/release_context.py audit` 检查路径、哈希、唯一 current、预算和清理白名单。",
            "- 仅在修改索引后运行 `python tools/release_context.py build-current` 重建本页；不要手工编辑本页。",
            "",
        ]
    )
    return "\n".join(lines)


def audit_workspace(root: Path, *, require_current_match: bool = True) -> dict[str, Any]:
    root = root.resolve()
    index_path = root / "artifact-index.json"
    index = load_json(index_path)
    if index.get("schema_version") != 1:
        raise AuditError("artifact-index.json must use schema_version 1")
    if index.get("project") != "DEV_Cut the Save":
        raise AuditError("artifact index project name is unexpected")

    current = index.get("current_release")
    if not isinstance(current, dict) or current.get("status") not in {"unresolved", "verified"}:
        raise AuditError("current_release.status must be unresolved or verified")

    source_sets = index.get("source_sets")
    if not isinstance(source_sets, list) or not source_sets:
        raise AuditError("artifact index must contain at least one source set")
    source_ids: set[str] = set()
    for source_position, source_set in enumerate(source_sets):
        source_id = source_set.get("source_set_id")
        if not isinstance(source_id, str) or not source_id or source_id in source_ids:
            raise AuditError("source_set_id values must be unique non-empty strings")
        source_ids.add(source_id)
        if source_set.get("hash_status") == "verified":
            _require_sha256(source_set.get("sha256"), label=f"source_sets[{source_position}].sha256")
        elif source_set.get("hash_status") != "pending_unfrozen_source_set":
            raise AuditError("source set must be verified or explicitly pending/unfrozen")
        members = source_set.get("members")
        if not isinstance(members, list) or not members:
            raise AuditError("each source set must list its members")
        for member_position, member in enumerate(members):
            _audit_hashed_file(
                root,
                member,
                label=f"source_sets[{source_position}].members[{member_position}]",
            )
        _audit_source_snapshot(root, source_set, label=f"source_sets[{source_position}]")

    artifact_records = index.get("artifacts")
    if not isinstance(artifact_records, list) or not artifact_records:
        raise AuditError("artifact index must contain artifacts")
    artifacts: dict[str, dict[str, Any]] = {}
    allowed_roles = {"release_zip", "release_manifest", "release_validator"}
    allowed_statuses = {"candidate", "tool", "verified", "historical"}
    for position, record in enumerate(artifact_records):
        artifact_id = record.get("artifact_id")
        if not isinstance(artifact_id, str) or not artifact_id or artifact_id in artifacts:
            raise AuditError("artifact_id values must be unique non-empty strings")
        if record.get("role") not in allowed_roles or record.get("status") not in allowed_statuses:
            raise AuditError(f"artifact {artifact_id} has an unsupported role or status")
        _audit_hashed_file(root, record, label=f"artifacts[{position}]")
        artifacts[artifact_id] = record

    candidates = index.get("release_candidates")
    if not isinstance(candidates, list) or not candidates:
        raise AuditError("artifact index must explicitly list release candidates")
    candidate_ids: set[str] = set()
    current_candidates: list[dict[str, Any]] = []
    for position, candidate in enumerate(candidates):
        candidate_id = candidate.get("release_id")
        if not isinstance(candidate_id, str) or not candidate_id or candidate_id in candidate_ids:
            raise AuditError("release_id values must be unique non-empty strings")
        candidate_ids.add(candidate_id)
        if candidate.get("status") not in {"candidate", "verified", "rejected"}:
            raise AuditError(f"release candidate {candidate_id} has an unsupported status")
        source_set_id = candidate.get("source_set_id")
        if source_set_id is not None and source_set_id not in source_ids:
            raise AuditError(f"release candidate {candidate_id} has an unknown source_set_id")
        if candidate.get("provenance_status") not in {
            "unresolved_no_source_binding",
            "bound",
            "mismatch",
        }:
            raise AuditError(f"release candidate {candidate_id} has an unsupported provenance status")
        if candidate.get("provenance_status") == "bound" and source_set_id is None:
            raise AuditError(f"release candidate {candidate_id} claims provenance without a source set")
        if candidate.get("checksum_binding_status") not in {"pending", "verified", "failed"}:
            raise AuditError(f"release candidate {candidate_id} has an unsupported checksum status")
        if candidate.get("package_validation_status") not in {
            "not_run_for_governance_baseline",
            "pending",
            "verified",
            "failed",
        }:
            raise AuditError(f"release candidate {candidate_id} has an unsupported package status")
        if candidate.get("field_gate_status") not in {"pending", "passed", "failed", "waived"}:
            raise AuditError(f"release candidate {candidate_id} has an unsupported field-gate status")
        for key in ("zip_artifact_id", "manifest_artifact_id", "validator_artifact_id"):
            if candidate.get(key) not in artifacts:
                raise AuditError(f"release candidate {candidate_id} has an unknown {key}")
        _audit_checksum_binding(root, candidate, artifacts, label=f"release_candidates[{position}]")
        _audit_package_validation_receipt(
            root,
            candidate,
            artifacts,
            label=f"release_candidates[{position}]",
        )
        if candidate.get("is_current") is True:
            current_candidates.append(candidate)

    if current.get("status") == "unresolved":
        if current.get("release_id") is not None or current_candidates:
            raise AuditError("unresolved current must have no release_id and no current candidate")
    else:
        if len(current_candidates) != 1:
            raise AuditError("verified current requires exactly one current candidate")
        selected = current_candidates[0]
        if current.get("release_id") != selected.get("release_id"):
            raise AuditError("current_release.release_id does not match the current candidate")
        if selected.get("status") != "verified":
            raise AuditError("current candidate must have status verified")
        if selected.get("package_validation_status") != "verified":
            raise AuditError("current candidate must have a verified package validation receipt")
        if selected.get("field_gate_status") != "passed":
            raise AuditError("current candidate must have passed field gates")

    field_gates = index.get("field_gates")
    if not isinstance(field_gates, list):
        raise AuditError("field_gates must be an array")
    gate_ids: set[str] = set()
    for gate in field_gates:
        gate_id = gate.get("gate_id")
        if not isinstance(gate_id, str) or not gate_id or gate_id in gate_ids:
            raise AuditError("field gate IDs must be unique non-empty strings")
        gate_ids.add(gate_id)
        if gate.get("status") not in {"pending", "passed", "waived", "not_applicable"}:
            raise AuditError(f"field gate {gate_id} has an unsupported status")
    if current.get("status") == "verified" and any(
        gate.get("status") == "pending" for gate in field_gates
    ):
        raise AuditError("verified current cannot retain pending field gates")

    governance = index.get("governance")
    if not isinstance(governance, dict):
        raise AuditError("governance settings are missing")
    receipt_relative = normalize_relative(
        governance.get("baseline_receipt"), label="governance.baseline_receipt"
    )
    cleanup_relative = normalize_relative(
        governance.get("cleanup_manifest"), label="governance.cleanup_manifest"
    )
    receipt_path = safe_path(root, receipt_relative, label="governance.baseline_receipt")
    cleanup_path = safe_path(root, cleanup_relative, label="governance.cleanup_manifest")
    _audit_receipt(root, receipt_path)
    _audit_cleanup(root, cleanup_path, receipt_relative)

    rendered = render_current(index)
    max_characters = governance.get("current_budget", {}).get("max_characters")
    max_nonempty_lines = governance.get("current_budget", {}).get("max_nonempty_lines")
    if not isinstance(max_characters, int) or not isinstance(max_nonempty_lines, int):
        raise AuditError("CURRENT budget values must be integers")
    nonempty_lines = sum(1 for line in rendered.splitlines() if line.strip())
    if len(rendered) > max_characters:
        raise AuditError(f"rendered CURRENT exceeds {max_characters} characters")
    if nonempty_lines > max_nonempty_lines:
        raise AuditError(f"rendered CURRENT exceeds {max_nonempty_lines} non-empty lines")
    if require_current_match:
        current_path = root / "CURRENT.md"
        try:
            existing = current_path.read_text(encoding="utf-8")
        except FileNotFoundError as exc:
            raise AuditError("CURRENT.md is missing; run build-current") from exc
        if existing != rendered:
            raise AuditError("CURRENT.md is stale or hand-edited; run build-current")

    return {
        "current_status": current.get("status"),
        "candidate_count": len(candidates),
        "pending_field_gates": sum(1 for gate in field_gates if gate.get("status") == "pending"),
        "current_characters": len(rendered),
        "current_nonempty_lines": nonempty_lines,
    }


def build_current(root: Path) -> dict[str, Any]:
    audit_workspace(root, require_current_match=False)
    index = load_json(root / "artifact-index.json")
    rendered = render_current(index)
    current_path = root / "CURRENT.md"
    with tempfile.NamedTemporaryFile(
        "w", encoding="utf-8", newline="\n", dir=root, delete=False, prefix="CURRENT.", suffix=".tmp"
    ) as stream:
        stream.write(rendered)
        temporary = Path(stream.name)
    try:
        os.replace(temporary, current_path)
    finally:
        if temporary.exists():
            temporary.unlink()
    return audit_workspace(root)


def main(argv: Iterable[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", nargs="?", choices=("audit", "build-current"), default="audit")
    parser.add_argument(
        "--root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
        help="workspace root (defaults to the parent of tools/)",
    )
    args = parser.parse_args(list(argv) if argv is not None else None)
    try:
        result = build_current(args.root) if args.command == "build-current" else audit_workspace(args.root)
    except AuditError as exc:
        print(f"FAIL release context audit: {exc}", file=sys.stderr)
        return 1
    print("PASS release context audit")
    print(json.dumps(result, ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
