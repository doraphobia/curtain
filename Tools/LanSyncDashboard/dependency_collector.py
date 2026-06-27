#!/usr/bin/env python3
"""
Build a project dependency manifest for a normalized project copy.

The collector is intentionally conservative. It reports what can be resolved
without launching host applications, and marks Adobe binary formats as
host-application tasks instead of pretending they are safely rewriteable.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import time


UNITY_REFERENCE_EXTENSIONS = {
    ".anim",
    ".asset",
    ".controller",
    ".mat",
    ".playable",
    ".prefab",
    ".scenetemplate",
    ".shadergraph",
    ".shadersubgraph",
    ".unity",
}

TEXT_REFERENCE_EXTENSIONS = {
    ".aepx",
    ".asset",
    ".controller",
    ".css",
    ".json",
    ".mat",
    ".prefab",
    ".scenetemplate",
    ".shadergraph",
    ".shadersubgraph",
    ".svg",
    ".txt",
    ".unity",
    ".xml",
    ".yaml",
    ".yml",
}

ADOBE_KIND_BY_SUFFIX = {
    ".aep": "after_effects_binary",
    ".aepx": "after_effects_xml",
    ".ai": "illustrator",
    ".psd": "photoshop",
    ".psb": "photoshop_large_document",
}

GUID_RE = re.compile(r"\bguid:\s*([0-9a-fA-F]{32})\b")
WINDOWS_PATH_RE = re.compile(r"(?<![\w])(?:[A-Za-z]:\\|\\\\)[^\"'\r\n<>|*?]+")
POSIX_PATH_RE = re.compile(r"(?<![\w])(?:/Users|/Volumes|/Applications|/private|/tmp)/[^\"'\r\n<>]+")

MAX_TEXT_SCAN_BYTES = 8 * 1024 * 1024
MAX_HASH_FILE_BYTES = 64 * 1024 * 1024
MAX_TOTAL_HASH_BYTES = 512 * 1024 * 1024


def now_stamp() -> str:
    return time.strftime("%Y-%m-%d %H:%M:%S")


def rel_posix(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def read_text(path: Path) -> str | None:
    try:
        if path.stat().st_size > MAX_TEXT_SCAN_BYTES:
            return None
        return path.read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return None


def strip_reference_path(value: str) -> str:
    return value.strip().rstrip(".,;:)]}'\"")


def is_relative_to(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def classify_reference(path_text: str, source_root: Path | None, dest_root: Path | None, source_to_dest: dict[str, str]) -> dict:
    normalized = path_text.replace("\\", "/")
    result = {
        "path": path_text,
        "classification": "unknown",
        "suggested_path": "",
        "note": "",
    }

    if source_root:
        source_norm = str(source_root).replace("\\", "/")
        if normalized == source_norm or normalized.startswith(source_norm.rstrip("/") + "/"):
            result["classification"] = "inside_source"
            result["note"] = "Reference points at the original source tree"
            result["suggested_path"] = source_to_dest.get(path_text, "")
            return result

    if dest_root:
        dest_norm = str(dest_root).replace("\\", "/")
        if normalized == dest_norm or normalized.startswith(dest_norm.rstrip("/") + "/"):
            result["classification"] = "inside_normalized_copy"
            return result

    if path_text.startswith("/") or re.match(r"^[A-Za-z]:\\", path_text) or path_text.startswith("\\\\"):
        result["classification"] = "external_absolute"
        result["note"] = "Requires collection, relink, or manual review"
    return result


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def entries_from_root(root: Path) -> list[dict]:
    entries = []
    for current, dirs, files in os.walk(root):
        current_path = Path(current)
        for name in sorted(dirs + files, key=str.casefold):
            path = current_path / name
            entries.append(
                {
                    "type": "dir" if path.is_dir() else "file",
                    "source_rel": rel_posix(path, root),
                    "dest_rel": rel_posix(path, root),
                    "source_abs": str(path),
                    "dest_abs": str(path),
                    "renamed": False,
                    "reasons": [],
                    "is_symlink": path.is_symlink(),
                }
            )
    return entries


def selected_path(entry: dict, executed: bool) -> Path:
    return Path(entry["dest_abs"] if executed else entry["source_abs"])


def selected_rel(entry: dict, executed: bool) -> str:
    return entry["dest_rel"] if executed else entry["source_rel"]


def build_source_to_dest(entries: list[dict]) -> dict[str, str]:
    mapping = {}
    for entry in entries:
        mapping[str(Path(entry["source_abs"]))] = str(Path(entry["dest_abs"]))
    return mapping


def collect_adobe(entries: list[dict], executed: bool, helper_script: str) -> dict:
    buckets = {kind: [] for kind in set(ADOBE_KIND_BY_SUFFIX.values())}
    tasks = []
    for entry in entries:
        if entry["type"] != "file":
            continue
        suffix = Path(selected_rel(entry, executed)).suffix.lower()
        kind = ADOBE_KIND_BY_SUFFIX.get(suffix)
        if not kind:
            continue
        item = {
            "path": selected_rel(entry, executed),
            "automation": "report_only",
            "status": "detected",
        }
        if kind == "after_effects_binary":
            item.update(
                {
                    "automation": "host_script_required",
                    "helper_script": helper_script,
                    "status": "requires_after_effects_to_collect_and_relink",
                }
            )
        elif kind == "after_effects_xml":
            item["automation"] = "text_reference_scan"
        elif kind in {"illustrator", "photoshop", "photoshop_large_document"}:
            item.update(
                {
                    "automation": "host_script_or_manual_relink_required",
                    "status": "binary_or_container_format_not_rewritten_by_collector",
                }
            )
        buckets[kind].append(item)
    if buckets.get("after_effects_binary"):
        tasks.append(
            {
                "target": "after_effects",
                "action": "run_helper_script",
                "script": helper_script,
                "reason": "AEP binary dependency paths must be resolved inside After Effects",
            }
        )
    if buckets.get("illustrator") or buckets.get("photoshop") or buckets.get("photoshop_large_document"):
        tasks.append(
            {
                "target": "adobe_links",
                "action": "host_application_dependency_collection",
                "status": "planned",
                "reason": "Illustrator and Photoshop linked assets require host-app link APIs for safe relink/save-as",
            }
        )
    return {"files": buckets, "tasks": tasks}


def collect_unity(entries: list[dict], root: Path, executed: bool) -> dict:
    guid_to_asset: dict[str, str] = {}
    meta_without_asset = []

    for entry in entries:
        if entry["type"] != "file":
            continue
        rel = selected_rel(entry, executed)
        path = selected_path(entry, executed)
        if not rel.endswith(".meta") or not path.is_file():
            continue
        text = read_text(path)
        if not text:
            continue
        match = GUID_RE.search(text)
        if not match:
            continue
        asset_rel = rel[:-5]
        asset_path = root / asset_rel
        if asset_path.exists():
            guid_to_asset[match.group(1).lower()] = asset_rel
        else:
            meta_without_asset.append({"meta": rel, "guid": match.group(1).lower()})

    references = []
    missing = {}
    files_scanned = 0
    for entry in entries:
        if entry["type"] != "file":
            continue
        rel = selected_rel(entry, executed)
        path = selected_path(entry, executed)
        if path.suffix.lower() not in UNITY_REFERENCE_EXTENSIONS:
            continue
        text = read_text(path)
        if text is None:
            continue
        files_scanned += 1
        for guid in sorted(set(match.lower() for match in GUID_RE.findall(text))):
            asset = guid_to_asset.get(guid, "")
            item = {
                "file": rel,
                "guid": guid,
                "asset": asset,
                "status": "resolved_internal" if asset else "missing_from_project_tree",
            }
            references.append(item)
            if not asset:
                missing.setdefault(guid, []).append(rel)

    return {
        "meta_assets": len(guid_to_asset),
        "files_scanned": files_scanned,
        "references": references[:10000],
        "reference_count": len(references),
        "missing_guid_count": len(missing),
        "missing_guids": [
            {"guid": guid, "referenced_by": files[:20], "reference_count": len(files)}
            for guid, files in sorted(missing.items())
        ],
        "meta_without_asset": meta_without_asset,
        "notes": [
            "Unity references are GUID-based; renaming files is safe when each asset keeps its matching .meta file.",
            "Missing GUIDs usually mean the copied folder is only a subset of a Unity project or references a package/outside asset.",
        ],
    }


def collect_text_references(entries: list[dict], executed: bool, source_root: Path | None, dest_root: Path | None) -> list[dict]:
    source_to_dest = build_source_to_dest(entries)
    references = []
    for entry in entries:
        if entry["type"] != "file":
            continue
        rel = selected_rel(entry, executed)
        path = selected_path(entry, executed)
        if path.suffix.lower() not in TEXT_REFERENCE_EXTENSIONS:
            continue
        text = read_text(path)
        if text is None:
            continue
        found = set()
        for regex in (WINDOWS_PATH_RE, POSIX_PATH_RE):
            for match in regex.findall(text):
                found.add(strip_reference_path(match))
        for value in sorted(found):
            if not value:
                continue
            references.append(
                {
                    "file": rel,
                    **classify_reference(value, source_root, dest_root, source_to_dest),
                }
            )
    return references[:10000]


def collect_duplicate_candidates(entries: list[dict], executed: bool) -> dict:
    by_size: dict[int, list[Path]] = {}
    for entry in entries:
        if entry["type"] != "file":
            continue
        path = selected_path(entry, executed)
        try:
            stat = path.stat()
        except OSError:
            continue
        if stat.st_size == 0 or stat.st_size > MAX_HASH_FILE_BYTES:
            continue
        by_size.setdefault(stat.st_size, []).append(path)

    hashed_bytes = 0
    hash_groups: dict[str, list[Path]] = {}
    skipped_large_groups = 0
    for size, paths in by_size.items():
        if len(paths) < 2:
            continue
        for path in paths:
            if hashed_bytes + size > MAX_TOTAL_HASH_BYTES:
                skipped_large_groups += 1
                continue
            try:
                digest = sha256_file(path)
            except OSError:
                continue
            hashed_bytes += size
            hash_groups.setdefault(digest, []).append(path)

    groups = []
    for digest, paths in sorted(hash_groups.items()):
        if len(paths) < 2:
            continue
        groups.append(
            {
                "sha256": digest,
                "size": paths[0].stat().st_size,
                "paths": [str(path) for path in paths],
                "policy": "report_only_do_not_delete",
            }
        )
    return {
        "groups": groups[:1000],
        "group_count": len(groups),
        "hashed_bytes": hashed_bytes,
        "skipped_after_hash_budget": skipped_large_groups,
        "policy": "Duplicates are reported only. The collector never deletes or deduplicates project files automatically.",
    }


def collect_dependencies(
    entries: list[dict],
    *,
    source_root: Path | None = None,
    dest_root: Path | None = None,
    executed: bool = True,
    helper_script: str = "ae_relink_collect.jsx",
) -> dict:
    root = dest_root if executed and dest_root else source_root
    if root is None:
        raise ValueError("source_root or dest_root is required")
    root = root.resolve()
    adobe = collect_adobe(entries, executed, helper_script)
    unity = collect_unity(entries, root, executed)
    text_refs = collect_text_references(entries, executed, source_root, dest_root)
    duplicates = collect_duplicate_candidates(entries, executed)
    return {
        "generated_at": now_stamp(),
        "mode": "normalized_copy" if executed else "source_dry_run",
        "root": str(root),
        "source_root": str(source_root) if source_root else "",
        "dest_root": str(dest_root) if dest_root else "",
        "adobe": adobe,
        "unity": unity,
        "text_path_references": text_refs,
        "text_path_reference_count": len(text_refs),
        "duplicate_content_candidates": duplicates,
        "automation_policy": {
            "rename_policy": "safe_copy_only",
            "source_tree_mutation": "never",
            "delete_policy": "never_delete_automatically",
            "sync_policy": "register_normalized_copy_as_the_sync_folder_after_review",
        },
    }


def collect_dependencies_for_root(root: Path) -> dict:
    root = root.expanduser().resolve()
    if not root.is_dir():
        raise ValueError(f"Root is not a directory: {root}")
    return collect_dependencies(entries_from_root(root), source_root=root, dest_root=root, executed=True)


def write_dependency_manifest(manifest: dict, report_dir: Path) -> Path:
    report_dir.mkdir(parents=True, exist_ok=True)
    path = report_dir / "dependency_manifest.json"
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return path


def manifest_summary(manifest: dict) -> dict:
    adobe_files = manifest.get("adobe", {}).get("files", {})
    return {
        "adobe_files": {key: len(value) for key, value in adobe_files.items()},
        "adobe_tasks": manifest.get("adobe", {}).get("tasks", []),
        "unity_reference_count": manifest.get("unity", {}).get("reference_count", 0),
        "unity_missing_guid_count": manifest.get("unity", {}).get("missing_guid_count", 0),
        "text_path_reference_count": manifest.get("text_path_reference_count", 0),
        "duplicate_content_group_count": manifest.get("duplicate_content_candidates", {}).get("group_count", 0),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Collect project dependency information without mutating source files.")
    parser.add_argument("--root", required=True, help="Project root to scan.")
    parser.add_argument("--report-dir", help="Directory for dependency_manifest.json. Defaults to ROOT/_CrossPlatformReport.")
    args = parser.parse_args()

    root = Path(args.root).expanduser().resolve()
    report_dir = Path(args.report_dir).expanduser().resolve() if args.report_dir else root / "_CrossPlatformReport"
    manifest = collect_dependencies_for_root(root)
    path = write_dependency_manifest(manifest, report_dir)
    print(json.dumps({"manifest": str(path), **manifest_summary(manifest)}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
