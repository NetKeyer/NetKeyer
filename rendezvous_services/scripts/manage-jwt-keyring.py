#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import secrets
import shutil
import sys
from pathlib import Path
from typing import Dict


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Manage rendezvous JWT keyring JSON file.")
    parser.add_argument(
        "--file",
        default=str(Path(__file__).resolve().parent.parent / "jwt_keys.json"),
        help="Path to keyring JSON file (default: ../jwt_keys.json).",
    )
    return parser.parse_args()


def load_keyring(path: Path) -> Dict[str, str]:
    if not path.exists():
        return {}

    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as ex:
        raise ValueError(f"Invalid JSON in {path}: {ex}") from ex

    if not isinstance(data, dict):
        raise ValueError(f"Keyring file must contain a JSON object of {{kid: secret}} entries: {path}")

    keyring: Dict[str, str] = {}
    for raw_kid, raw_secret in data.items():
        kid = str(raw_kid).strip()
        secret = str(raw_secret).strip() if raw_secret is not None else ""
        if kid and secret:
            keyring[kid] = secret

    return keyring


def rotate_backups(path: Path) -> None:
    bak1 = path.with_name(path.name + ".bak1")
    bak2 = path.with_name(path.name + ".bak2")
    bak3 = path.with_name(path.name + ".bak3")

    if bak3.exists():
        bak3.unlink()
    if bak2.exists():
        shutil.move(str(bak2), str(bak3))
    if bak1.exists():
        shutil.move(str(bak1), str(bak2))
    if path.exists():
        shutil.move(str(path), str(bak1))


def save_keyring(path: Path, keyring: Dict[str, str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    rotate_backups(path)
    serialized = json.dumps(dict(sorted(keyring.items())), indent=2) + "\n"
    path.write_text(serialized, encoding="utf-8")


def generate_secret() -> str:
    return secrets.token_hex(32)


def print_menu() -> None:
    print("\nJWT Keyring Manager")
    print("1) List entries")
    print("2) Add entry")
    print("3) Change entry secret")
    print("4) Delete entry")
    print("5) Quit")


def list_entries(keyring: Dict[str, str]) -> None:
    if not keyring:
        print("No entries found.")
        return

    print(f"{len(keyring)} entr{'y' if len(keyring) == 1 else 'ies'}:")
    for kid in sorted(keyring):
        print(f"- {kid}: {keyring[kid]}")


def prompt_kid(action: str) -> str:
    kid = input(f"Enter Key ID to {action}: ").strip()
    if not kid:
        print("Key ID cannot be empty.")
        return ""
    return kid


def add_entry(path: Path, keyring: Dict[str, str]) -> Dict[str, str]:
    kid = prompt_kid("add")
    if not kid:
        return keyring

    if kid in keyring:
        print(f"Entry '{kid}' already exists. Use change instead.")
        return keyring

    secret = generate_secret()
    updated = dict(keyring)
    updated[kid] = secret
    save_keyring(path, updated)

    print(f"Added entry: {kid}")
    print(f"Secret: {secret}")
    return updated


def change_entry(path: Path, keyring: Dict[str, str]) -> Dict[str, str]:
    kid = prompt_kid("change")
    if not kid:
        return keyring

    if kid not in keyring:
        print(f"Entry '{kid}' was not found.")
        return keyring

    secret = generate_secret()
    updated = dict(keyring)
    updated[kid] = secret
    save_keyring(path, updated)

    print(f"Updated entry: {kid}")
    print(f"Secret: {secret}")
    return updated


def delete_entry(path: Path, keyring: Dict[str, str]) -> Dict[str, str]:
    kid = prompt_kid("delete")
    if not kid:
        return keyring

    if kid not in keyring:
        print(f"Entry '{kid}' was not found.")
        return keyring

    confirm = input(f"Delete '{kid}'? (y/N): ").strip().lower()
    if confirm not in {"y", "yes"}:
        print("Delete cancelled.")
        return keyring

    updated = dict(keyring)
    del updated[kid]
    save_keyring(path, updated)
    print(f"Deleted entry: {kid}")
    return updated


def main() -> int:
    args = parse_args()
    keyring_path = Path(args.file).resolve()

    try:
        keyring = load_keyring(keyring_path)
    except ValueError as ex:
        print(str(ex))
        return 1

    print(f"Using keyring file: {keyring_path}")

    while True:
        print_menu()
        choice = input("Select option [1-5]: ").strip()

        if choice == "1":
            list_entries(keyring)
        elif choice == "2":
            keyring = add_entry(keyring_path, keyring)
            list_entries(keyring)
        elif choice == "3":
            keyring = change_entry(keyring_path, keyring)
            list_entries(keyring)
        elif choice == "4":
            keyring = delete_entry(keyring_path, keyring)
            list_entries(keyring)
        elif choice == "5":
            print("Exiting keyring manager.")
            return 0
        else:
            print("Invalid option. Select 1, 2, 3, 4, or 5.")


if __name__ == "__main__":
    sys.exit(main())
