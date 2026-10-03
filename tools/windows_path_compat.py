from __future__ import annotations

from collections import defaultdict

_RESERVED = {"CON", "PRN", "AUX", "NUL"} | {f"COM{i}" for i in range(1, 10)} | {f"LPT{i}" for i in range(1, 10)}
_FORBIDDEN = set('<>:"\\|?*')


def windows_path_errors(paths):
    normalized = [str(path).replace("\\", "/").strip("/") for path in paths]
    normalized = [path for path in normalized if path]
    errors = []

    file_groups = defaultdict(set)
    dir_groups = defaultdict(set)
    for path in normalized:
        file_groups[path.casefold()].add(path)
        parts = path.split("/")
        for index, segment in enumerate(parts):
            if segment.endswith((".", " ")):
                errors.append(f"trailing dot or space in Windows path segment: {path}")
            if any(char in _FORBIDDEN or ord(char) < 32 for char in segment):
                errors.append(f"forbidden Windows character in path segment: {path}")
            base = segment.split(".", 1)[0].upper()
            if base in _RESERVED:
                errors.append(f"reserved Windows name in path segment: {path}")
            if index < len(parts) - 1:
                directory = "/".join(parts[: index + 1])
                dir_groups[directory.casefold()].add(directory)

    for originals in file_groups.values():
        if len(originals) > 1:
            errors.append("case-insensitive path collision: " + " <> ".join(sorted(originals)))
    for originals in dir_groups.values():
        if len(originals) > 1:
            errors.append("case-insensitive directory collision: " + " <> ".join(sorted(originals)))

    return sorted(set(errors))


def require_windows_paths(paths, context="paths"):
    errors = windows_path_errors(paths)
    if errors:
        raise RuntimeError(f"Windows-incompatible {context}: " + "; ".join(errors))
