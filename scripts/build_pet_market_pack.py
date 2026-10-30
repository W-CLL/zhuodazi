"""Build a versioned GIF-only desktop-pet market archive from local assets."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import zipfile

MAX_FILES = 80
MAX_GIF_BYTES = 8 * 1024 * 1024
MAX_TOTAL_BYTES = 64 * 1024 * 1024
ID_PATTERN = re.compile(r"[a-z0-9][a-z0-9-]{0,63}\Z")
VERSION_PATTERN = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+\Z")
RESERVED = re.compile(r"(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?\Z", re.I)


def validate_path(value: str) -> str:
    if not value or len(value) > 200 or "\\" in value or value.startswith("/"):
        raise ValueError("素材路径无效")
    for component in value.split("/"):
        if (not component or component in (".", "..") or component.startswith(".")
                or component.endswith((" ", ".")) or RESERVED.fullmatch(component)
                or any(ord(char) < 32 or char in '<>:"|?*' for char in component)):
            raise ValueError(f"素材路径不适合 Windows：{value}")
    return value


def build_pack(source_files: list[Path], *, product_id: str, version: str,
               title: str, output: Path, source_root: Path | None = None) -> dict:
    """Create a new ZIP without overwriting an existing file or editing inputs."""
    if not ID_PATTERN.fullmatch(product_id):
        raise ValueError("商品标识只能使用小写字母、数字和连字符，最多64字符")
    if not VERSION_PATTERN.fullmatch(version) or len(version) > 40:
        raise ValueError("版本请使用数字形式，例如 1.0.0")
    if not title.strip() or len(title.strip()) > 80:
        raise ValueError("标题不能为空且不能超过80字符")
    if not 1 <= len(source_files) <= MAX_FILES:
        raise ValueError(f"资源包需要1至{MAX_FILES}个GIF")
    if output.exists():
        raise FileExistsError("输出文件已存在，请使用新的版本或输出路径")

    resources: list[tuple[str, bytes]] = []
    manifest_files = []
    seen = set()
    total = 0
    for source in sorted(source_files, key=lambda item: str(item).casefold()):
        if source.is_symlink() or not source.is_file():
            raise ValueError(f"素材必须是普通文件：{source.name}")
        relative = source.relative_to(source_root).as_posix() if source_root else source.name
        archive_path = validate_path("gifs/" + relative)
        if not source.name.lower().endswith(".gif") or archive_path.casefold() in seen:
            raise ValueError("只允许GIF，且素材路径不能重复（不区分大小写）")
        seen.add(archive_path.casefold())
        length = source.stat().st_size
        if not 10 <= length <= MAX_GIF_BYTES:
            raise ValueError(f"GIF 不能为空且不能超过8MB：{source.name}")
        data = source.read_bytes()
        if len(data) != length or data[:6] not in (b"GIF87a", b"GIF89a"):
            raise ValueError(f"GIF内容或文件大小无效：{source.name}")
        width, height = struct.unpack_from("<HH", data, 6)
        if not 1 <= width <= 2048 or not 1 <= height <= 2048:
            raise ValueError(f"GIF宽高必须为1至2048像素：{source.name}")
        total += len(data)
        if total > MAX_TOTAL_BYTES - 256 * 1024:
            raise ValueError("全部素材解压后超过64MB")
        resources.append((archive_path, data))
        manifest_files.append({"path": archive_path, "sizeBytes": len(data),
                               "sha256": hashlib.sha256(data).hexdigest()})

    manifest = {"schemaVersion": 1, "productId": product_id, "version": version,
                "title": title.strip(), "files": manifest_files}
    manifest_bytes = json.dumps(manifest, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    output.parent.mkdir(parents=True, exist_ok=True)
    created = False
    try:
        with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            created = True
            archive.writestr("pet-market.json", manifest_bytes)
            for archive_path, data in resources:
                archive.writestr(archive_path, data)
        if output.stat().st_size > MAX_TOTAL_BYTES:
            raise ValueError("压缩包超过64MB")
    except BaseException:
        if created:
            output.unlink(missing_ok=True)
        raise
    return {"productId": product_id, "version": version, "assetCount": len(resources),
            "sizeBytes": output.stat().st_size, "sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
            "output": str(output.resolve())}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--product-id", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--title", required=True)
    sources = parser.add_mutually_exclusive_group(required=True)
    sources.add_argument("--source-dir", type=Path)
    sources.add_argument("--files", type=Path, nargs="+")
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    source_root = arguments.source_dir.resolve() if arguments.source_dir else None
    if source_root and not source_root.is_dir():
        parser.error("素材目录不存在")
    files = ([file for file in source_root.rglob("*") if file.suffix.lower() == ".gif"]
             if source_root else arguments.files)
    try:
        result = build_pack(files, product_id=arguments.product_id, version=arguments.version,
                            title=arguments.title, output=arguments.output, source_root=source_root)
    except (ValueError, OSError, zipfile.BadZipFile) as error:
        parser.exit(1, f"打包失败：{error}\n")
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
