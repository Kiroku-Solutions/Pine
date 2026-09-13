#!/usr/bin/env bash
#
# Prints the path of the newest x64 signtool.exe on this machine, or fails saying why (TD-108).
#
# WHY THIS IS NOT A HARD-CODED PATH IN THE WORKFLOW.
#   signtool.exe ships inside the Windows SDK, under a directory named after the SDK version:
#   `C:/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/signtool.exe` on the machine this
#   was written on. GitHub's `windows-latest` image installs whatever SDK is current when the image
#   is rebuilt, so a literal path in the workflow is a path that works until the image rolls and then
#   fails on a release day with "no such file". Version directories sort correctly by version, not by
#   string (10.0.9 vs 10.0.26100), so this sorts numerically field by field.
#
# IT REFUSES RATHER THAN GUESSING. `App Certification Kit/signtool.exe` also exists and is a
# different, older build; it is excluded rather than accepted as a fallback, because a signing tool
# chosen by accident is exactly the kind of detail that is invisible until a signature is rejected.

set -euo pipefail

roots=(
  "/c/Program Files (x86)/Windows Kits/10/bin"
  "/c/Program Files/Windows Kits/10/bin"
  "${PROGRAMFILES_X86:-}/Windows Kits/10/bin"
)

best=""
best_version=""

for root in "${roots[@]}"; do
  [ -n "$root" ] || continue
  [ -d "$root" ] || continue

  while IFS= read -r candidate; do
    [ -n "$candidate" ] || continue

    # .../bin/<version>/x64/signtool.exe -- the version is two directories up.
    version="$(basename "$(dirname "$(dirname "$candidate")")")"

    # `sort -V` would do this in one line and is not present on every Git Bash install; `sort -t. -k`
    # with numeric keys is POSIX enough to rely on.
    if [ -z "$best" ] || [ "$(printf '%s\n%s\n' "$best_version" "$version" | sort -t. -k1,1n -k2,2n -k3,3n -k4,4n | tail -1)" = "$version" ]; then
      best="$candidate"
      best_version="$version"
    fi
  done <<< "$(find "$root" -mindepth 3 -maxdepth 3 -path '*/x64/signtool.exe' -type f 2>/dev/null || true)"
done

if [ -z "$best" ]; then
  echo "find-signtool.sh: no x64 signtool.exe under any Windows SDK on this machine." >&2
  echo "                  Looked in:" >&2
  printf '                    %s\n' "${roots[@]}" >&2
  echo "                  Install the Windows SDK, or set PINE_SIGNTOOL explicitly." >&2
  exit 1
fi

echo "$best"
