#!/usr/bin/env python3
"""Verify the restored W.DamengProvider package against the pinned manifest.

The script only reads files: it never loads or executes the package assembly.
It exits non-zero when any identity check fails or a required file is missing.

Usage:
  verify-package.py [--manifest PATH] [--nupkg PATH] [--packages-dir DIR]
                    [--lock-file PATH ...] [--assembly PATH]

Without --nupkg the package is located in NUGET_PACKAGES (or ~/.nuget/packages).
Without --lock-file every packages.lock.json under the repository is scanned and
each one that references the package must carry the manifest's NuGet contentHash.
--assembly additionally verifies a DLL that a test process reported as loaded.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import struct
import sys
import uuid
import zipfile
import xml.etree.ElementTree as ElementTree
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_MANIFEST = Path(__file__).resolve().with_name("w-damengprovider.manifest.json")


class VerificationError(Exception):
    pass


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_mvid(image: bytes) -> str:
    """Read the module MVID from ECMA-335 metadata without loading the assembly."""
    try:
        pe = struct.unpack_from("<I", image, 0x3C)[0]
        section_count = struct.unpack_from("<H", image, pe + 6)[0]
        optional_size = struct.unpack_from("<H", image, pe + 20)[0]
        magic = struct.unpack_from("<H", image, pe + 24)[0]
        directories = pe + 24 + (96 if magic == 0x10B else 112)
        cli_rva = struct.unpack_from("<I", image, directories + 14 * 8)[0]
        sections = [
            struct.unpack_from("<8sIIII", image, pe + 24 + optional_size + index * 40)
            for index in range(section_count)
        ]

        def to_offset(rva: int) -> int:
            for _, virtual_size, virtual_address, raw_size, raw_pointer in sections:
                if virtual_address <= rva < virtual_address + max(virtual_size, raw_size):
                    return rva - virtual_address + raw_pointer
            raise VerificationError("RVA outside of all sections")

        metadata_rva = struct.unpack_from("<I", image, to_offset(cli_rva) + 8)[0]
        metadata = to_offset(metadata_rva)
        version_length = struct.unpack_from("<I", image, metadata + 12)[0]
        position = metadata + 16 + version_length
        stream_count = struct.unpack_from("<H", image, position + 2)[0]
        position += 4
        streams: dict[str, int] = {}
        for _ in range(stream_count):
            offset = struct.unpack_from("<I", image, position)[0]
            position += 8
            end = image.index(b"\0", position)
            streams[image[position:end].decode("ascii")] = metadata + offset
            position = (end + 4) & ~3

        tables = streams.get("#~") or streams["#-"]
        heap_sizes = image[tables + 6]
        valid = struct.unpack_from("<Q", image, tables + 8)[0]
        string_index_size = 4 if heap_sizes & 1 else 2
        guid_index_size = 4 if heap_sizes & 2 else 2
        module_row = tables + 24 + 4 * bin(valid).count("1")
        guid_offset = module_row + 2 + string_index_size
        guid_index = struct.unpack_from("<I" if guid_index_size == 4 else "<H", image, guid_offset)[0]
        guid_position = streams["#GUID"] + 16 * (guid_index - 1)
        return str(uuid.UUID(bytes_le=image[guid_position:guid_position + 16]))
    except (struct.error, KeyError, ValueError, IndexError) as error:
        raise VerificationError(f"cannot read assembly MVID: {error}") from error


def find_lock_files() -> list[Path]:
    ignored = {"bin", "obj", "artifacts", ".git", ".local"}
    result = []
    for directory, subdirectories, files in os.walk(REPOSITORY_ROOT):
        subdirectories[:] = [name for name in subdirectories if name not in ignored]
        if "packages.lock.json" in files:
            result.append(Path(directory) / "packages.lock.json")
    return sorted(result)


def check(condition: bool, message: str, failures: list[str]) -> None:
    print(("ok   " if condition else "FAIL ") + message)
    if not condition:
        failures.append(message)


def verify(arguments: argparse.Namespace) -> list[str]:
    manifest = json.loads(Path(arguments.manifest).read_text(encoding="utf-8"))
    package_id = manifest["packageId"]
    version = manifest["version"]
    failures: list[str] = []

    if arguments.nupkg:
        package_path = Path(arguments.nupkg)
        metadata_path = None
    else:
        packages_dir = Path(
            arguments.packages_dir
            or os.environ.get("NUGET_PACKAGES")
            or Path.home() / ".nuget" / "packages")
        package_directory = packages_dir / package_id.lower() / version.lower()
        package_path = package_directory / f"{package_id.lower()}.{version.lower()}.nupkg"
        metadata_path = package_directory / ".nupkg.metadata"

    if not package_path.is_file():
        raise VerificationError(f"package not found: {package_path.name} (restore first)")

    package = package_path.read_bytes()
    check(sha256(package) == manifest["packageSha256"], "package SHA-256 matches manifest", failures)
    # NuGet hashes a signed package without its signature entry, so the expected
    # contentHash is pinned in the manifest; byte identity is the SHA-256 above.
    content_hash = manifest["nugetContentHash"]

    if metadata_path is not None:
        if metadata_path.is_file():
            metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
            check(metadata.get("contentHash") == content_hash,
                  ".nupkg.metadata contentHash matches manifest", failures)
        else:
            check(False, ".nupkg.metadata present in restore directory", failures)

    with zipfile.ZipFile(package_path) as archive:
        nuspec_name = next((name for name in archive.namelist() if name.endswith(".nuspec")), None)
        if nuspec_name is None:
            raise VerificationError("nuspec missing from package")
        nuspec = ElementTree.fromstring(archive.read(nuspec_name))
        namespace = {"n": nuspec.tag[1:].split("}")[0]} if nuspec.tag.startswith("{") else {}
        prefix = "n:" if namespace else ""
        metadata_node = nuspec.find(f"{prefix}metadata", namespace)
        check(metadata_node is not None and metadata_node.findtext(f"{prefix}id", None, namespace) == package_id,
              "nuspec id matches", failures)
        check(metadata_node is not None and metadata_node.findtext(f"{prefix}version", None, namespace) == version,
              "nuspec version matches", failures)
        repository = metadata_node.find(f"{prefix}repository", namespace) if metadata_node is not None else None
        check(repository is not None and repository.get("commit") == manifest["source"]["repositoryCommit"],
              "nuspec repository commit matches", failures)
        dependencies = metadata_node.find(f"{prefix}dependencies", namespace) if metadata_node is not None else None
        dependency_ids = [] if dependencies is None else [
            node.get("id") for node in dependencies.iter() if node.get("id")]
        check(not dependency_ids, "package declares no runtime NuGet dependencies", failures)

        assembly = archive.read(manifest["assemblyPath"])
        check(sha256(assembly) == manifest["assemblySha256"], "packaged DLL SHA-256 matches", failures)
        check(read_mvid(assembly) == manifest["assemblyMvid"], "packaged DLL MVID matches", failures)

    lock_files = [Path(path) for path in arguments.lock_file] if arguments.lock_file else find_lock_files()
    referencing = 0
    for lock_file in lock_files:
        lock = json.loads(lock_file.read_text(encoding="utf-8"))
        relative = lock_file.relative_to(REPOSITORY_ROOT) if lock_file.is_relative_to(REPOSITORY_ROOT) else lock_file
        for framework, entries in lock.get("dependencies", {}).items():
            for forbidden in manifest.get("forbiddenPackageIds", []):
                check(forbidden not in entries, f"{relative} [{framework}] does not reference {forbidden}", failures)
            entry = entries.get(package_id)
            if entry is None:
                continue
            referencing += 1
            check(entry.get("resolved") == version, f"{relative} [{framework}] resolves {version}", failures)
            check(entry.get("contentHash") == content_hash,
                  f"{relative} [{framework}] contentHash matches manifest", failures)
    if arguments.require_lock_reference:
        check(referencing > 0, "at least one lock file references the package", failures)

    for loaded in arguments.assembly or []:
        image = Path(loaded).read_bytes()
        check(sha256(image) == manifest["assemblySha256"], f"loaded DLL {Path(loaded).name} SHA-256 matches", failures)
        check(read_mvid(image) == manifest["assemblyMvid"], f"loaded DLL {Path(loaded).name} MVID matches", failures)

    return failures


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--manifest", default=str(DEFAULT_MANIFEST))
    parser.add_argument("--nupkg")
    parser.add_argument("--packages-dir")
    parser.add_argument("--lock-file", action="append")
    parser.add_argument("--assembly", action="append")
    parser.add_argument("--require-lock-reference", action="store_true")
    arguments = parser.parse_args()
    try:
        failures = verify(arguments)
    except (VerificationError, OSError, json.JSONDecodeError, zipfile.BadZipFile, KeyError) as error:
        print(f"FAIL {error}")
        return 2
    if failures:
        print(f"{len(failures)} check(s) failed")
        return 1
    print("all checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
