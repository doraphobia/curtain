#!/usr/bin/env python3
"""
Font Family Sync

Synchronizes user-installed font files between Windows and macOS devices through a
shared vault folder. The vault can live in a cloud-synced folder, SMB share, NAS,
external drive, or any directory all devices can see.

The tool intentionally syncs additions only. Removing fonts from another machine
is a destructive operation, so deletion sync is left out of the default workflow.
"""

from __future__ import annotations

import argparse
import contextlib
import ctypes
import fnmatch
import hashlib
import json
import os
import platform
import shutil
import socket
import struct
import subprocess
import sys
import tempfile
import time
import uuid
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable

FONT_EXTENSIONS = {".otf", ".ttf", ".ttc", ".otc"}
INDEX_VERSION = 1
DEFAULT_INTERVAL_SECONDS = 30


class SyncError(RuntimeError):
    pass


@dataclass(frozen=True)
class FontName:
    family: str = ""
    subfamily: str = ""
    full_name: str = ""
    postscript_name: str = ""
    typographic_family: str = ""
    typographic_subfamily: str = ""

    @property
    def sync_family(self) -> str:
        return self.typographic_family or self.family or self.full_name

    @property
    def sync_style(self) -> str:
        return self.typographic_subfamily or self.subfamily


@dataclass
class FontFile:
    path: Path
    sha256: str
    size: int
    names: list[FontName] = field(default_factory=list)

    @property
    def family_names(self) -> list[str]:
        values = {name.sync_family.strip() for name in self.names if name.sync_family.strip()}
        if not values:
            values.add(self.path.stem)
        return sorted(values, key=str.lower)

    @property
    def full_names(self) -> list[str]:
        values = {name.full_name.strip() for name in self.names if name.full_name.strip()}
        if not values:
            values.add(self.path.stem)
        return sorted(values, key=str.lower)

    @property
    def postscript_names(self) -> list[str]:
        values = {name.postscript_name.strip() for name in self.names if name.postscript_name.strip()}
        return sorted(values, key=str.lower)


@dataclass
class SyncConfig:
    vault: Path
    device_id: str
    device_name: str
    publish_roots: list[Path]
    installed_roots: list[Path]
    install_root: Path
    ignore_patterns: list[str]
    interval_seconds: int = DEFAULT_INTERVAL_SECONDS
    dry_run: bool = False
    refresh_font_cache: bool = False


def expand_path(value: str | Path) -> Path:
    return Path(os.path.expandvars(os.path.expanduser(str(value)))).resolve()


def normalize_key(value: str) -> str:
    return " ".join(value.casefold().split())


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def atomic_write_json(path: Path, data: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=path.parent, delete=False) as tmp:
        json.dump(data, tmp, ensure_ascii=False, indent=2, sort_keys=True)
        tmp.write("\n")
        tmp_path = Path(tmp.name)
    os.replace(tmp_path, path)


@contextlib.contextmanager
def vault_lock(vault: Path, timeout_seconds: int = 30) -> Iterable[None]:
    lock_dir = vault / ".font_family_sync.lock"
    deadline = time.time() + timeout_seconds
    while True:
        try:
            lock_dir.mkdir(parents=True)
            (lock_dir / "owner.txt").write_text(
                f"{socket.gethostname()} {os.getpid()} {time.time()}\n",
                encoding="utf-8",
            )
            break
        except FileExistsError:
            if time.time() > deadline:
                raise SyncError(f"Timed out waiting for vault lock: {lock_dir}")
            time.sleep(0.25)
    try:
        yield
    finally:
        with contextlib.suppress(FileNotFoundError):
            (lock_dir / "owner.txt").unlink()
        with contextlib.suppress(OSError):
            lock_dir.rmdir()


def platform_font_dirs() -> tuple[list[Path], list[Path], Path]:
    system = platform.system().lower()
    home = Path.home()
    if system == "darwin":
        user = home / "Library" / "Fonts"
        installed = [user, Path("/Library/Fonts"), Path("/System/Library/Fonts")]
        return [user], installed, user
    if system == "windows":
        local_app_data = Path(os.environ.get("LOCALAPPDATA", home / "AppData" / "Local"))
        windir = Path(os.environ.get("WINDIR", r"C:\Windows"))
        user = local_app_data / "Microsoft" / "Windows" / "Fonts"
        installed = [user, windir / "Fonts"]
        return [user], installed, user
    user = home / ".local" / "share" / "fonts"
    installed = [user, Path("/usr/local/share/fonts"), Path("/usr/share/fonts")]
    return [user], installed, user


def default_device_id() -> str:
    seed = f"{platform.node()}|{platform.system()}|{Path.home()}"
    return hashlib.sha1(seed.encode("utf-8", "replace")).hexdigest()[:12]


def load_config(args: argparse.Namespace) -> SyncConfig:
    publish_default, installed_default, install_default = platform_font_dirs()
    data: dict[str, Any] = {}
    if args.config:
        config_path = expand_path(args.config)
        if config_path.exists():
            data = json.loads(config_path.read_text(encoding="utf-8"))
        else:
            raise SyncError(f"Config file does not exist: {config_path}")

    vault_value = args.vault or data.get("vault")
    if not vault_value:
        raise SyncError("A vault path is required. Use --vault or a config file.")

    publish_roots_raw = args.publish_root or data.get("publish_roots") or [str(path) for path in publish_default]
    installed_roots_raw = (
        args.installed_root
        or data.get("installed_roots")
        or [str(path) for path in installed_default]
    )
    install_root_raw = args.install_root or data.get("install_root") or str(install_default)
    ignore_patterns = data.get("ignore_patterns") or [
        ".*",
        "__MACOSX",
        "*.download",
        "*.tmp",
        "*.bak",
    ]
    interval = int(args.interval or data.get("interval_seconds") or DEFAULT_INTERVAL_SECONDS)
    device_id = args.device_id or data.get("device_id") or default_device_id()
    device_name = args.device_name or data.get("device_name") or f"{platform.node()} ({platform.system()})"

    return SyncConfig(
        vault=expand_path(vault_value),
        device_id=str(device_id),
        device_name=str(device_name),
        publish_roots=[expand_path(path) for path in publish_roots_raw],
        installed_roots=[expand_path(path) for path in installed_roots_raw],
        install_root=expand_path(install_root_raw),
        ignore_patterns=[str(pattern) for pattern in ignore_patterns],
        interval_seconds=interval,
        dry_run=bool(args.dry_run),
        refresh_font_cache=bool(args.refresh_font_cache or data.get("refresh_font_cache", False)),
    )


def should_ignore(path: Path, patterns: list[str]) -> bool:
    parts = path.parts
    for part in parts:
        for pattern in patterns:
            if fnmatch.fnmatch(part, pattern):
                return True
    return False


def iter_font_paths(roots: Iterable[Path], ignore_patterns: list[str]) -> Iterable[Path]:
    seen: set[Path] = set()
    for root in roots:
        if not root.exists():
            continue
        if root.is_file():
            candidates = [root]
        else:
            candidates = root.rglob("*")
        for path in candidates:
            if path in seen:
                continue
            seen.add(path)
            if not path.is_file():
                continue
            if path.suffix.lower() not in FONT_EXTENSIONS:
                continue
            if should_ignore(path, ignore_patterns):
                continue
            yield path


def unique_strings(values: Iterable[str]) -> list[str]:
    result: dict[str, str] = {}
    for value in values:
        clean = " ".join(value.replace("\x00", "").split())
        if clean:
            result.setdefault(normalize_key(clean), clean)
    return sorted(result.values(), key=str.lower)


def decode_name_bytes(platform_id: int, raw: bytes) -> str:
    candidates: list[str]
    if platform_id in (0, 3):
        candidates = ["utf-16-be", "utf-8", "latin-1"]
    elif platform_id == 1:
        candidates = ["mac_roman", "utf-8", "latin-1"]
    else:
        candidates = ["utf-16-be", "utf-8", "latin-1"]
    for codec in candidates:
        try:
            return raw.decode(codec).strip("\x00 \t\r\n")
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1", errors="replace").strip("\x00 \t\r\n")


def choose_name(values: list[tuple[int, int, int, str]]) -> str:
    if not values:
        return ""

    def score(item: tuple[int, int, int, str]) -> tuple[int, int, int]:
        platform_id, _encoding_id, language_id, _value = item
        if platform_id == 3 and language_id in (0x0409, 0x0000):
            return (0, 0, language_id)
        if platform_id == 0:
            return (1, 0, language_id)
        if platform_id == 3:
            return (2, 0, language_id)
        return (3, platform_id, language_id)

    return sorted(values, key=score)[0][3]


def parse_name_table(data: bytes, table_offset: int, table_length: int) -> FontName:
    if table_offset + table_length > len(data) or table_length < 6:
        return FontName()
    table = data[table_offset : table_offset + table_length]
    _format_id, count, string_offset = struct.unpack_from(">HHH", table, 0)
    names: dict[int, list[tuple[int, int, int, str]]] = {}
    for index in range(count):
        record_offset = 6 + index * 12
        if record_offset + 12 > len(table):
            break
        platform_id, encoding_id, language_id, name_id, length, offset = struct.unpack_from(
            ">HHHHHH", table, record_offset
        )
        start = string_offset + offset
        end = start + length
        if start < 0 or end > len(table):
            continue
        value = decode_name_bytes(platform_id, table[start:end])
        if value:
            names.setdefault(name_id, []).append((platform_id, encoding_id, language_id, value))
    return FontName(
        family=choose_name(names.get(1, [])),
        subfamily=choose_name(names.get(2, [])),
        full_name=choose_name(names.get(4, [])),
        postscript_name=choose_name(names.get(6, [])),
        typographic_family=choose_name(names.get(16, [])),
        typographic_subfamily=choose_name(names.get(17, [])),
    )


def parse_sfnt_name(data: bytes, offset: int = 0) -> FontName:
    if offset + 12 > len(data):
        return FontName()
    num_tables = struct.unpack_from(">H", data, offset + 4)[0]
    records_offset = offset + 12
    for index in range(num_tables):
        record_offset = records_offset + index * 16
        if record_offset + 16 > len(data):
            break
        tag, _checksum, table_offset, table_length = struct.unpack_from(">4sIII", data, record_offset)
        if tag == b"name":
            return parse_name_table(data, table_offset, table_length)
    return FontName()


def parse_font_names(path: Path) -> list[FontName]:
    data = path.read_bytes()
    if len(data) < 4:
        return [FontName(full_name=path.stem)]
    if data[:4] == b"ttcf":
        if len(data) < 12:
            return [FontName(full_name=path.stem)]
        count = struct.unpack_from(">I", data, 8)[0]
        names: list[FontName] = []
        for index in range(count):
            entry_offset = 12 + index * 4
            if entry_offset + 4 > len(data):
                break
            sfnt_offset = struct.unpack_from(">I", data, entry_offset)[0]
            names.append(parse_sfnt_name(data, sfnt_offset))
        return names or [FontName(full_name=path.stem)]
    return [parse_sfnt_name(data)]


def read_font_file(path: Path) -> FontFile | None:
    try:
        return FontFile(
            path=path,
            sha256=sha256_file(path),
            size=path.stat().st_size,
            names=parse_font_names(path),
        )
    except Exception as exc:
        print(f"Warning: skipped unreadable font {path}: {exc}", file=sys.stderr)
        return None


def scan_fonts(roots: Iterable[Path], ignore_patterns: list[str]) -> dict[str, FontFile]:
    fonts: dict[str, FontFile] = {}
    for path in iter_font_paths(roots, ignore_patterns):
        font = read_font_file(path)
        if font:
            fonts.setdefault(font.sha256, font)
    return fonts


def ensure_vault(vault: Path) -> None:
    (vault / "blobs").mkdir(parents=True, exist_ok=True)
    index = vault / "index.json"
    if not index.exists():
        atomic_write_json(
            index,
            {
                "version": INDEX_VERSION,
                "created_at": time.time(),
                "updated_at": time.time(),
                "fonts": {},
            },
        )


def load_index(vault: Path) -> dict[str, Any]:
    ensure_vault(vault)
    path = vault / "index.json"
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise SyncError(f"Invalid vault index: {path}: {exc}") from exc
    if data.get("version") != INDEX_VERSION:
        raise SyncError(f"Unsupported vault index version: {data.get('version')}")
    data.setdefault("fonts", {})
    return data


def blob_path_for(vault: Path, digest: str, suffix: str) -> Path:
    clean_suffix = suffix.lower() if suffix.lower() in FONT_EXTENSIONS else ".font"
    return vault / "blobs" / f"{digest}{clean_suffix}"


def font_to_record(font: FontFile, config: SyncConfig) -> dict[str, Any]:
    now = time.time()
    return {
        "sha256": font.sha256,
        "size": font.size,
        "file_name": font.path.name,
        "extension": font.path.suffix.lower(),
        "families": font.family_names,
        "full_names": font.full_names,
        "postscript_names": font.postscript_names,
        "source_devices": {
            config.device_id: {
                "device_name": config.device_name,
                "first_seen_at": now,
                "last_seen_at": now,
                "source_path": str(font.path),
            }
        },
        "installed_devices": {},
    }


def merge_font_record(existing: dict[str, Any], font: FontFile, config: SyncConfig) -> dict[str, Any]:
    now = time.time()
    existing["size"] = font.size
    existing["extension"] = existing.get("extension") or font.path.suffix.lower()
    existing["file_name"] = existing.get("file_name") or font.path.name
    existing["families"] = unique_strings([*existing.get("families", []), *font.family_names])
    existing["full_names"] = unique_strings([*existing.get("full_names", []), *font.full_names])
    existing["postscript_names"] = unique_strings(
        [*existing.get("postscript_names", []), *font.postscript_names]
    )
    devices = existing.setdefault("source_devices", {})
    previous = devices.get(config.device_id, {})
    devices[config.device_id] = {
        "device_name": config.device_name,
        "first_seen_at": previous.get("first_seen_at", now),
        "last_seen_at": now,
        "source_path": str(font.path),
    }
    return existing


def safe_install_name(record: dict[str, Any]) -> str:
    original = record.get("file_name") or f"{record['sha256']}{record.get('extension', '.otf')}"
    keep = []
    for char in original:
        if char.isalnum() or char in "._- +()[]":
            keep.append(char)
        else:
            keep.append("_")
    cleaned = "".join(keep).strip(" .")
    return cleaned or f"{record['sha256']}{record.get('extension', '.otf')}"


def installed_hashes(config: SyncConfig) -> set[str]:
    return set(scan_fonts(config.installed_roots, config.ignore_patterns).keys())


def unique_install_path(install_root: Path, record: dict[str, Any]) -> Path:
    name = safe_install_name(record)
    target = install_root / name
    digest = record["sha256"]
    if not target.exists():
        return target
    with contextlib.suppress(Exception):
        if sha256_file(target) == digest:
            return target
    stem = Path(name).stem
    suffix = Path(name).suffix or record.get("extension", ".otf")
    target = install_root / f"{stem}_FontFamilySync_{digest[:8]}{suffix}"
    counter = 2
    while target.exists():
        with contextlib.suppress(Exception):
            if sha256_file(target) == digest:
                return target
        target = install_root / f"{stem}_FontFamilySync_{digest[:8]}_{counter}{suffix}"
        counter += 1
    return target


def mark_installed(record: dict[str, Any], config: SyncConfig, path: Path, status: str) -> None:
    now = time.time()
    devices = record.setdefault("installed_devices", {})
    previous = devices.get(config.device_id, {})
    devices[config.device_id] = {
        "device_name": config.device_name,
        "first_installed_at": previous.get("first_installed_at", now),
        "last_checked_at": now,
        "installed_path": str(path),
        "status": status,
    }


def refresh_windows_font_cache(target: Path) -> None:
    if platform.system().lower() != "windows":
        return
    try:
        import winreg  # type: ignore

        font_name = target.stem
        suffix = target.suffix.lower()
        kind = "TrueType" if suffix in {".ttf", ".ttc"} else "OpenType"
        registry_name = f"{font_name} ({kind})"
        key_path = r"Software\Microsoft\Windows NT\CurrentVersion\Fonts"
        with winreg.CreateKey(winreg.HKEY_CURRENT_USER, key_path) as key:
            winreg.SetValueEx(key, registry_name, 0, winreg.REG_SZ, str(target))
    except Exception as exc:
        print(f"Warning: could not update Windows font registry for {target}: {exc}", file=sys.stderr)

    try:
        gdi32 = ctypes.windll.gdi32
        user32 = ctypes.windll.user32
        gdi32.AddFontResourceExW(str(target), 0, 0)
        HWND_BROADCAST = 0xFFFF
        WM_FONTCHANGE = 0x001D
        SMTO_ABORTIFHUNG = 0x0002
        result = ctypes.c_ulong()
        user32.SendMessageTimeoutW(
            HWND_BROADCAST,
            WM_FONTCHANGE,
            0,
            0,
            SMTO_ABORTIFHUNG,
            1000,
            ctypes.byref(result),
        )
    except Exception as exc:
        print(f"Warning: could not broadcast Windows font refresh for {target}: {exc}", file=sys.stderr)


def refresh_macos_font_cache() -> None:
    if platform.system().lower() != "darwin":
        return
    # Copying into ~/Library/Fonts is enough for new app launches. This gentle
    # ping avoids aggressive cache deletion and keeps already-running apps stable.
    with contextlib.suppress(Exception):
        subprocess.run(["atsutil", "server", "-ping"], check=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def refresh_linux_font_cache() -> None:
    if platform.system().lower() in {"darwin", "windows"}:
        return
    if shutil.which("fc-cache"):
        with contextlib.suppress(Exception):
            subprocess.run(["fc-cache", "-f"], check=False)


def install_blob(record: dict[str, Any], config: SyncConfig) -> tuple[bool, Path | None, str]:
    blob = blob_path_for(config.vault, record["sha256"], record.get("extension", ".otf"))
    if not blob.exists():
        return False, None, f"missing blob {blob}"
    config.install_root.mkdir(parents=True, exist_ok=True)
    target = unique_install_path(config.install_root, record)
    if target.exists():
        with contextlib.suppress(Exception):
            if sha256_file(target) == record["sha256"]:
                mark_installed(record, config, target, "already_installed")
                return False, target, "already installed"
    if config.dry_run:
        return True, target, "would install"
    shutil.copy2(blob, target)
    with contextlib.suppress(Exception):
        if sha256_file(target) != record["sha256"]:
            target.unlink(missing_ok=True)
            raise SyncError(f"Install verification failed for {target}")
    if platform.system().lower() == "windows":
        refresh_windows_font_cache(target)
    mark_installed(record, config, target, "installed")
    return True, target, "installed"


def publish_local_fonts(index: dict[str, Any], config: SyncConfig) -> tuple[int, int]:
    local = scan_fonts(config.publish_roots, config.ignore_patterns)
    published = 0
    already = 0
    fonts = index.setdefault("fonts", {})
    for digest, font in sorted(local.items(), key=lambda item: item[1].path.name.lower()):
        blob = blob_path_for(config.vault, digest, font.path.suffix)
        if digest not in fonts:
            fonts[digest] = font_to_record(font, config)
            published += 1
        else:
            fonts[digest] = merge_font_record(fonts[digest], font, config)
            already += 1
        if not blob.exists() and not config.dry_run:
            blob.parent.mkdir(parents=True, exist_ok=True)
            tmp = blob.with_suffix(blob.suffix + ".tmp")
            shutil.copy2(font.path, tmp)
            if sha256_file(tmp) != digest:
                tmp.unlink(missing_ok=True)
                raise SyncError(f"Vault copy verification failed for {font.path}")
            os.replace(tmp, blob)
    return published, already


def install_missing_fonts(index: dict[str, Any], config: SyncConfig) -> tuple[int, int, list[str]]:
    hashes = installed_hashes(config)
    installed = 0
    skipped = 0
    errors: list[str] = []
    for digest, record in sorted(index.get("fonts", {}).items(), key=lambda item: safe_install_name(item[1]).lower()):
        if digest in hashes:
            mark_installed(record, config, Path(record.get("file_name", digest)), "already_installed_by_hash")
            skipped += 1
            continue
        did_install, target, message = install_blob(record, config)
        if did_install:
            installed += 1
            hashes.add(digest)
        elif message.startswith("missing blob"):
            errors.append(message)
        else:
            skipped += 1
            if target and target.exists():
                hashes.add(digest)
    return installed, skipped, errors


def sync_once(config: SyncConfig) -> dict[str, Any]:
    ensure_vault(config.vault)
    with vault_lock(config.vault):
        index = load_index(config.vault)
        published, seen_local = publish_local_fonts(index, config)
        installed, skipped, errors = install_missing_fonts(index, config)
        index["updated_at"] = time.time()
        index["last_sync"] = {
            "device_id": config.device_id,
            "device_name": config.device_name,
            "at": time.time(),
        }
        if not config.dry_run:
            atomic_write_json(config.vault / "index.json", index)
    if config.refresh_font_cache and not config.dry_run:
        refresh_macos_font_cache()
        refresh_linux_font_cache()
    return {
        "published_new_files": published,
        "published_existing_files": seen_local,
        "installed_new_files": installed,
        "already_present_or_skipped": skipped,
        "errors": errors,
    }


def family_map_from_records(records: Iterable[dict[str, Any]]) -> dict[str, dict[str, Any]]:
    families: dict[str, dict[str, Any]] = {}
    for record in records:
        family_names = record.get("families") or [Path(record.get("file_name", "Unknown")).stem]
        for family in family_names:
            key = normalize_key(family)
            entry = families.setdefault(key, {"name": family, "files": 0, "sha256": set(), "full_names": set()})
            entry["files"] += 1
            entry["sha256"].add(record.get("sha256"))
            for full_name in record.get("full_names", []):
                entry["full_names"].add(full_name)
    return families


def status(config: SyncConfig) -> dict[str, Any]:
    index = load_index(config.vault)
    local_hashes = installed_hashes(config)
    vault_records = list(index.get("fonts", {}).values())
    for digest, record in index.get("fonts", {}).items():
        record["sha256"] = digest
    vault_families = family_map_from_records(vault_records)
    installed_records = [record for digest, record in index.get("fonts", {}).items() if digest in local_hashes]
    missing_records = [record for digest, record in index.get("fonts", {}).items() if digest not in local_hashes]
    installed_families = family_map_from_records(installed_records)
    missing_families = {
        key: value for key, value in vault_families.items() if key not in installed_families
    }
    partial_families: dict[str, dict[str, Any]] = {}
    for key, family in vault_families.items():
        local = installed_families.get(key)
        if local and local["sha256"] != family["sha256"]:
            partial_families[key] = {
                "name": family["name"],
                "installed_files": len(local["sha256"]),
                "vault_files": len(family["sha256"]),
            }
    return {
        "vault": str(config.vault),
        "device_id": config.device_id,
        "device_name": config.device_name,
        "vault_files": len(index.get("fonts", {})),
        "vault_families": len(vault_families),
        "installed_vault_files_here": len(installed_records),
        "missing_vault_files_here": len(missing_records),
        "missing_families_here": sorted([entry["name"] for entry in missing_families.values()], key=str.lower),
        "partial_families_here": sorted(partial_families.values(), key=lambda item: item["name"].lower()),
    }


def print_json(data: Any) -> None:
    print(json.dumps(data, ensure_ascii=False, indent=2, sort_keys=True))


def write_config_template(path: Path, vault: str | None = None) -> None:
    publish_default, installed_default, install_default = platform_font_dirs()
    template = {
        "vault": vault or str(Path.home() / "FontFamilyVault"),
        "device_id": default_device_id(),
        "device_name": f"{platform.node()} ({platform.system()})",
        "interval_seconds": DEFAULT_INTERVAL_SECONDS,
        "publish_roots": [str(path) for path in publish_default],
        "installed_roots": [str(path) for path in installed_default],
        "install_root": str(install_default),
        "ignore_patterns": [".*", "__MACOSX", "*.download", "*.tmp", "*.bak"],
        "refresh_font_cache": False,
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    atomic_write_json(path, template)


def command_init(args: argparse.Namespace) -> int:
    vault = expand_path(args.vault)
    ensure_vault(vault)
    if args.config_out:
        write_config_template(expand_path(args.config_out), str(vault))
    print_json({"initialized_vault": str(vault), "config_written": args.config_out or None})
    return 0


def command_once(args: argparse.Namespace) -> int:
    config = load_config(args)
    print_json(sync_once(config))
    return 0


def command_watch(args: argparse.Namespace) -> int:
    config = load_config(args)
    print(f"Watching font sync vault: {config.vault}")
    print(f"Device: {config.device_name} [{config.device_id}]")
    print("Press Ctrl+C to stop.")
    while True:
        started = time.time()
        try:
            result = sync_once(config)
            stamp = time.strftime("%Y-%m-%d %H:%M:%S")
            print(f"[{stamp}] {json.dumps(result, ensure_ascii=False, sort_keys=True)}", flush=True)
        except KeyboardInterrupt:
            print("\nStopped.")
            return 0
        except Exception as exc:
            print(f"Sync failed: {exc}", file=sys.stderr, flush=True)
        elapsed = time.time() - started
        time.sleep(max(1, config.interval_seconds - elapsed))


def command_status(args: argparse.Namespace) -> int:
    config = load_config(args)
    print_json(status(config))
    return 0


def command_doctor(args: argparse.Namespace) -> int:
    publish, installed, install_root = platform_font_dirs()
    print_json(
        {
            "platform": platform.platform(),
            "python": sys.version,
            "default_device_id": default_device_id(),
            "default_publish_roots": [str(path) for path in publish],
            "default_installed_roots": [str(path) for path in installed],
            "default_install_root": str(install_root),
            "supported_extensions": sorted(FONT_EXTENSIONS),
        }
    )
    return 0


def add_common_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--config", help="Path to a JSON config file.")
    parser.add_argument("--vault", help="Shared font vault directory.")
    parser.add_argument("--device-id", help="Stable device id for this machine.")
    parser.add_argument("--device-name", help="Human-readable device name.")
    parser.add_argument("--publish-root", action="append", help="Font directory to publish from. Repeatable.")
    parser.add_argument("--installed-root", action="append", help="Font directory used for installed-font checks.")
    parser.add_argument("--install-root", help="Directory where missing fonts should be installed.")
    parser.add_argument("--interval", type=int, help="Watch interval in seconds.")
    parser.add_argument("--dry-run", action="store_true", help="Show what would happen without copying fonts.")
    parser.add_argument(
        "--refresh-font-cache",
        action="store_true",
        help="Ask the platform font cache to refresh after installing fonts.",
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Cross-platform font family sync tool.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    init_parser = subparsers.add_parser("init", help="Create a vault and optional config file.")
    init_parser.add_argument("--vault", required=True, help="Shared font vault directory.")
    init_parser.add_argument("--config-out", help="Write a config JSON file.")
    init_parser.set_defaults(func=command_init)

    once_parser = subparsers.add_parser("once", help="Run one publish/install sync cycle.")
    add_common_arguments(once_parser)
    once_parser.set_defaults(func=command_once)

    watch_parser = subparsers.add_parser("watch", help="Continuously sync fonts.")
    add_common_arguments(watch_parser)
    watch_parser.set_defaults(func=command_watch)

    status_parser = subparsers.add_parser("status", help="Compare this device with the vault.")
    add_common_arguments(status_parser)
    status_parser.set_defaults(func=command_status)

    doctor_parser = subparsers.add_parser("doctor", help="Show platform defaults.")
    doctor_parser.set_defaults(func=command_doctor)
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return int(args.func(args))
    except SyncError as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
