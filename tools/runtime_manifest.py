"""Explicit maintainer tool; builds verify the manifest and never silently update it."""
import hashlib
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "third_party" / "kokoro-runtime"
rows = ["# PointCursor offline runtime: bytes<TAB>sha256<TAB>relative path"]
for file in sorted(root.rglob("*")):
    if not file.is_file() or file.name == "assets.tsv":
        continue
    digest = hashlib.sha256()
    with file.open("rb") as stream:
        first = stream.read(1024)
        if first.startswith(b"version https://git-lfs.github.com/spec/v1"):
            raise SystemExit(f"Unhydrated LFS asset: {file.relative_to(root)}; run git lfs pull first.")
        digest.update(first)
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    rows.append(f"{file.stat().st_size}\t{digest.hexdigest()}\t{file.relative_to(root).as_posix()}")
(root / "assets.tsv").write_text("\n".join(rows) + "\n", encoding="utf-8", newline="\n")
print(f"Manifest written: {len(rows) - 1} files")
