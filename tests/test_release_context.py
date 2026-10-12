from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
import os
import shutil
from contextlib import contextmanager


sys.dont_write_bytecode = True
SCRIPT = Path(__file__).resolve().parents[1] / "tools" / "release_context.py"
SPEC = importlib.util.spec_from_file_location("release_context", SCRIPT)
assert SPEC and SPEC.loader
release_context = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release_context)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


@contextmanager
def temporary_workspace():
    path = tempfile.mkdtemp(prefix="CutTheSave-governance-")
    try:
        yield path
    finally:
        if os.environ.get("CTS_RETAIN_TEST_FILES") != "1":
            shutil.rmtree(path)


class ReleaseContextTests(unittest.TestCase):
    def make_workspace(self, root: Path) -> dict:
        (root / "artifacts" / "release").mkdir(parents=True)
        (root / "scripts").mkdir()
        (root / "src").mkdir()
        (root / "tests").mkdir()
        (root / "docs").mkdir()
        (root / "work").mkdir()
        (root / "releases" / "receipts").mkdir(parents=True)

        payload = b"candidate payload"
        validator = b"validator"
        source = b"source"
        payload_path = root / "artifacts" / "release" / "candidate.zip"
        manifest_path = root / "artifacts" / "release" / "candidate-SHA256.txt"
        validator_path = root / "scripts" / "verify.ps1"
        source_path = root / "source.txt"
        payload_path.write_bytes(payload)
        manifest_path.write_text(f"{digest(payload).upper()}  candidate.zip\n", encoding="utf-8")
        validator_path.write_bytes(validator)
        source_path.write_bytes(source)

        receipt_relative = "releases/receipts/baseline.json"
        cleanup_relative = "releases/receipts/cleanup.json"
        receipt = {
            "schema_version": 1,
            "kind": "baseline_inventory",
            "inventory": {
                "artifacts_top_level": [
                    {
                        "path": "artifacts/release",
                        "kind": "directory",
                        "byte_size": len(payload) + manifest_path.stat().st_size,
                        "hash_status": "pending_directory_not_hashed",
                    }
                ],
                "work_top_level": [],
                "formal_candidate_files": [
                    {
                        "path": "artifacts/release/candidate.zip",
                        "kind": "file",
                        "byte_size": len(payload),
                        "hash_status": "verified",
                        "sha256": digest(payload),
                    }
                ],
            },
        }
        (root / receipt_relative).write_text(json.dumps(receipt), encoding="utf-8")
        cleanup = {
            "schema_version": 1,
            "mode": "dry-run",
            "apply_authorized": False,
            "baseline_receipt": receipt_relative,
            "allow_roots": ["work"],
            "protected_roots": ["artifacts", "src", "tests", "docs", "scripts", "releases"],
            "allowlist": [],
            "delete_candidates": [],
        }
        (root / cleanup_relative).write_text(json.dumps(cleanup), encoding="utf-8")

        index = {
            "schema_version": 1,
            "project": "DEV_Cut the Save",
            "current_release": {
                "status": "unresolved",
                "release_id": None,
                "reason": "No release has been explicitly selected.",
            },
            "source_sets": [
                {
                    "source_set_id": "srcset_test",
                    "status": "unfrozen",
                    "hash_status": "pending_unfrozen_source_set",
                    "sha256": None,
                    "provenance_note": "Test source set is intentionally unfrozen.",
                    "members": [
                        {
                            "path": "source.txt",
                            "kind": "file",
                            "byte_size": len(source),
                            "hash_status": "verified",
                            "sha256": digest(source),
                        }
                    ],
                }
            ],
            "artifacts": [
                {
                    "artifact_id": "zip",
                    "role": "release_zip",
                    "status": "candidate",
                    "path": "artifacts/release/candidate.zip",
                    "kind": "file",
                    "byte_size": len(payload),
                    "hash_status": "verified",
                    "sha256": digest(payload),
                },
                {
                    "artifact_id": "manifest",
                    "role": "release_manifest",
                    "status": "candidate",
                    "path": "artifacts/release/candidate-SHA256.txt",
                    "kind": "file",
                    "byte_size": manifest_path.stat().st_size,
                    "hash_status": "verified",
                    "sha256": release_context.sha256_file(manifest_path),
                },
                {
                    "artifact_id": "validator",
                    "role": "release_validator",
                    "status": "tool",
                    "path": "scripts/verify.ps1",
                    "kind": "file",
                    "byte_size": len(validator),
                    "hash_status": "verified",
                    "sha256": digest(validator),
                },
            ],
            "release_candidates": [
                {
                    "release_id": "candidate-test",
                    "status": "candidate",
                    "is_current": False,
                    "zip_artifact_id": "zip",
                    "manifest_artifact_id": "manifest",
                    "validator_artifact_id": "validator",
                    "source_set_id": None,
                    "provenance_status": "unresolved_no_source_binding",
                    "checksum_binding_status": "verified",
                    "package_validation_status": "not_run_for_governance_baseline",
                    "field_gate_status": "pending",
                    "selection_note": "Test candidate only.",
                }
            ],
            "field_gates": [
                {"gate_id": "field-a", "status": "pending", "description": "Test field gate."}
            ],
            "governance": {
                "baseline_receipt": receipt_relative,
                "cleanup_manifest": cleanup_relative,
                "current_budget": {"max_characters": 4500, "max_nonempty_lines": 90},
            },
        }
        (root / "artifact-index.json").write_text(json.dumps(index), encoding="utf-8")
        (root / "CURRENT.md").write_text(release_context.render_current(index), encoding="utf-8")
        return index

    def test_valid_unresolved_context_passes(self) -> None:
        with temporary_workspace() as temporary:
            root = Path(temporary)
            self.make_workspace(root)
            result = release_context.audit_workspace(root)
            self.assertEqual(result["current_status"], "unresolved")
            self.assertEqual(result["candidate_count"], 1)

    def test_path_traversal_is_rejected(self) -> None:
        with temporary_workspace() as temporary:
            root = Path(temporary)
            index = self.make_workspace(root)
            cleanup_path = root / index["governance"]["cleanup_manifest"]
            cleanup = json.loads(cleanup_path.read_text(encoding="utf-8"))
            cleanup["allowlist"] = ["../outside"]
            cleanup_path.write_text(json.dumps(cleanup), encoding="utf-8")
            with self.assertRaisesRegex(release_context.AuditError, "traversal"):
                release_context.audit_workspace(root)

    def test_hash_mismatch_is_rejected(self) -> None:
        with temporary_workspace() as temporary:
            root = Path(temporary)
            self.make_workspace(root)
            (root / "artifacts" / "release" / "candidate.zip").write_bytes(b"changed")
            with self.assertRaisesRegex(release_context.AuditError, "SHA-256 mismatch"):
                release_context.audit_workspace(root)

    def test_verified_current_must_be_unique(self) -> None:
        with temporary_workspace() as temporary:
            root = Path(temporary)
            index = self.make_workspace(root)
            first = index["release_candidates"][0]
            first.update(
                {
                    "status": "verified",
                    "is_current": True,
                    "package_validation_status": "verified",
                    "field_gate_status": "passed",
                }
            )
            second = dict(first)
            second["release_id"] = "candidate-test-2"
            index["release_candidates"].append(second)
            artifacts = {item["artifact_id"]: item for item in index["artifacts"]}
            for candidate in (first, second):
                relative = f"releases/receipts/{candidate['release_id']}-package.json"
                candidate["validation_receipt"] = relative
                receipt = {
                    "schema_version": 1,
                    "kind": "package_validation",
                    "release_id": candidate["release_id"],
                    "result": "verified",
                    "zip": artifacts["zip"],
                    "checksum_manifest": artifacts["manifest"],
                    "validator": artifacts["validator"],
                }
                (root / relative).write_text(json.dumps(receipt), encoding="utf-8")
            index["field_gates"][0]["status"] = "passed"
            index["current_release"] = {"status": "verified", "release_id": "candidate-test"}
            (root / "artifact-index.json").write_text(json.dumps(index), encoding="utf-8")
            (root / "CURRENT.md").write_text(release_context.render_current(index), encoding="utf-8")
            with self.assertRaisesRegex(release_context.AuditError, "exactly one"):
                release_context.audit_workspace(root)
            # The same otherwise-valid fixture must pass with exactly one selected release.
            second["is_current"] = False
            (root / "artifact-index.json").write_text(json.dumps(index), encoding="utf-8")
            (root / "CURRENT.md").write_text(release_context.render_current(index), encoding="utf-8")
            self.assertEqual(release_context.audit_workspace(root)["current_status"], "verified")

    def test_verified_package_cannot_omit_its_receipt(self) -> None:
        with temporary_workspace() as temporary:
            root = Path(temporary)
            index = self.make_workspace(root)
            index["release_candidates"][0]["package_validation_status"] = "verified"
            (root / "artifact-index.json").write_text(json.dumps(index), encoding="utf-8")
            with self.assertRaisesRegex(release_context.AuditError, "validation_receipt"):
                release_context.audit_workspace(root)

    def test_archived_validator_preserves_exact_receipt_identity(self):
        with temporary_workspace() as temporary:
            root = Path(temporary)
            index = self.make_workspace(root)
            artifacts = {item["artifact_id"]: item for item in index["artifacts"]}
            candidate = index["release_candidates"][0]
            candidate["package_validation_status"] = "verified"
            candidate["validation_receipt"] = "releases/receipts/package.json"
            archived = root / "releases/validators/old.ps1"
            archived.parent.mkdir()
            archived.write_bytes(b"validator")
            original = dict(artifacts["validator"], path="scripts/Verify-CutTheSaveRelease.ps1")
            receipt = dict(schema_version=1, kind="package_validation", release_id=candidate["release_id"], result="verified",
                           zip=artifacts["zip"], checksum_manifest=artifacts["manifest"], validator=original)
            (root / candidate["validation_receipt"]).write_text(json.dumps(receipt), encoding="utf-8")
            artifacts["validator"].update(path="releases/validators/old.ps1", receipt_original_path=original["path"])
            (root / "artifact-index.json").write_text(json.dumps(index), encoding="utf-8")
            (root / "CURRENT.md").write_text(release_context.render_current(index), encoding="utf-8")
            self.assertEqual(release_context.audit_workspace(root)["current_status"], "unresolved")
            archived.write_bytes(b"tampered")
            with self.assertRaises(release_context.AuditError):
                release_context.audit_workspace(root)

    def test_unknown_source_binding_is_rejected(self) -> None:
        with temporary_workspace() as temporary:
            root = Path(temporary)
            index = self.make_workspace(root)
            index["release_candidates"][0]["source_set_id"] = "srcset_missing"
            index["release_candidates"][0]["provenance_status"] = "bound"
            (root / "artifact-index.json").write_text(json.dumps(index), encoding="utf-8")
            with self.assertRaisesRegex(release_context.AuditError, "unknown source_set_id"):
                release_context.audit_workspace(root)


if __name__ == "__main__":
    unittest.main()
