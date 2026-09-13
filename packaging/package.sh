#!/usr/bin/env bash
#
# Builds a versioned, checksummed, manifested Pine artefact (TD-49).
#
# WHAT THIS IS, AND WHAT IT IS NOT.
#   It is: a repeatable publish pinned to one runtime identifier, a SHA-256 over every file that
#   ships, and a release manifest in the shape `docs/29-Deployment` §5.3 specifies.
#   It is NOT: an installer (.msi/.dmg/.deb), and it is NOT an updater. Both remain open under
#   TD-49. Producing a .tar.gz and calling it "packaging done" would be the same class of claim this
#   audit exists to remove, so the script says which half it did.
#
# THE SIGNING RULE IS THE POINT OF THIS FILE.
#   No code-signing certificate exists for this project, and none can be conjured by a script. So
#   the signing step is a real seam with two honest outcomes and no third:
#     - a certificate is configured  -> `packaging/sign.sh` signs the artefact AND VERIFIES IT file
#                                       by file; the manifest says `"signed": true` and carries the
#                                       certificate's thumbprint and subject, so a reader can check
#                                       WHO signed it rather than only THAT something did
#     - no certificate is configured -> the artefact is NOT signed, the manifest says
#                                       `"signed": false`, and the script prints a warning that
#                                       names the consequence to a user.
#   `--require-signature` turns the second outcome into a non-zero exit, so a release pipeline can
#   refuse to publish an unsigned build without anyone having to remember to check.
#
#   The signing step used to live here, in twenty lines that had never been executed and that could
#   not have worked (MSYS rewrote `/fd` into a path before signtool saw it). That measurement, and
#   the refusals that replaced those lines, are in `packaging/sign.sh` — TD-108, post-audit plan
#   item 2.2.
#
# USAGE
#   packaging/package.sh [--rid win-x64] [--out artifacts] [--configuration Release]
#                        [--keep-native-pdbs] [--require-signature]
#
# WHY win-x64 IS THE DEFAULT AND THE ONLY TESTED TARGET.
#   Pine's OCR depends on `OpenCvSharp4.runtime.win` and `Sdcb.PaddleInference.runtime.win64.mkl`
#   (both named by `PackageReference` in Pine.Infrastructure.csproj; cited by name rather than by
#   line, per iteration 21 — the previous citation said ":17,22" and those lines have since moved)
#   — both Windows-x64-only. A linux-x64 or osx-arm64 artefact
#   would build and launch and then be unable to do the thing the application is for. That is
#   audit row TD-44, and this script will happily take another --rid; it just will not pretend the
#   result is a supported build.

set -euo pipefail

RID="win-x64"
OUT_ROOT="artifacts"
CONFIGURATION="Release"
KEEP_NATIVE_PDBS=0
REQUIRE_SIGNATURE=0

while [ $# -gt 0 ]; do
  case "$1" in
    --rid) RID="$2"; shift 2 ;;
    --out) OUT_ROOT="$2"; shift 2 ;;
    --configuration) CONFIGURATION="$2"; shift 2 ;;
    --keep-native-pdbs) KEEP_NATIVE_PDBS=1; shift ;;
    --require-signature) REQUIRE_SIGNATURE=1; shift ;;
    -h|--help) sed -n '2,41p' "$0"; exit 0 ;;
    *) echo "package.sh: unknown argument '$1'" >&2; exit 2 ;;
  esac
done

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# --require-signature IS ANSWERED HERE, BEFORE ANYTHING IS BUILT.
# The refusal also stands further down, at the signing step, where it has the whole picture. This
# one is the cheap half: whether a certificate is configured at all is knowable in a millisecond,
# and the signing step is reached only after two self-contained publishes of ~660 MiB. Making a
# caller wait ten minutes to be told something that was true before the first byte was compiled is
# its own small dishonesty, and `61-Manual-Test-Plan` §13D asks a tester to do exactly this.
if [ "$REQUIRE_SIGNATURE" -eq 1 ] && { [ -z "${PINE_SIGNTOOL:-}" ] || [ -z "${PINE_SIGNING_CERT:-}" ]; }; then
  echo "package.sh: --require-signature was given and no certificate is configured." >&2
  echo "            PINE_SIGNTOOL and PINE_SIGNING_CERT must both be set. Refusing before building," >&2
  echo "            because nothing that happens next could change this answer." >&2
  exit 1
fi

APP_PROJECT="src/Pine.Ui.Windows/Pine.Ui.Windows.csproj"
CLI_PROJECT="src/Pine.Cli/Pine.Cli.csproj"

echo "==> Resolving version from MSBuild (Directory.Build.props is the single source)"
# -getProperty is asked of the project rather than parsed out of the props file, so that a
# -p:VersionSuffix= override on the command line is reflected here too.
# A single -getProperty prints the bare value (a CRLF-terminated line on Windows), not JSON; the
# trailing carriage return would otherwise end up inside a filename and inside the manifest.
VERSION="$(dotnet msbuild "$APP_PROJECT" -getProperty:Version -nologo | tr -d '[:space:]')"

if [ -z "$VERSION" ]; then
  echo "package.sh: could not resolve a version. Refusing to produce an artefact that cannot name itself." >&2
  exit 1
fi

PACKAGE_NAME="pine-${VERSION}-${RID}"
STAGE="${OUT_ROOT}/${PACKAGE_NAME}"

echo "    version = ${VERSION}"
echo "    rid     = ${RID}"
echo "    stage   = ${STAGE}"

rm -rf "$STAGE"
mkdir -p "$STAGE"

echo "==> Publishing the desktop head (self-contained, RID-pinned)"
# Self-contained: an installer that also requires the user to find and install a .NET runtime is
# not an installer. RID-pinned: without it, publish emits 30 runtime-identifier folders for a
# single-platform application (measured: 1.78 GiB, 220 files, 30 RIDs).
dotnet publish "$APP_PROJECT" \
  --configuration "$CONFIGURATION" \
  --runtime "$RID" \
  --self-contained true \
  -p:DebugType=none \
  -o "$STAGE" \
  --nologo

echo "==> Publishing the CLI alongside it"
# The CLI is what lets a packaged copy state its own build identifier without opening a vault.
dotnet publish "$CLI_PROJECT" \
  --configuration "$CONFIGURATION" \
  --runtime "$RID" \
  --self-contained true \
  -p:DebugType=none \
  -o "$STAGE" \
  --nologo

if [ "$KEEP_NATIVE_PDBS" -eq 0 ]; then
  echo "==> Removing native debug symbols from the artefact"
  # These .pdb files belong to third-party native libraries (SkiaSharp, HarfBuzz) and arrive from
  # their NuGet packages, so -p:DebugType=none does not touch them. They are not needed at runtime
  # and they are not lost: they remain in the NuGet packages this build restored, which is where a
  # native crash dump would look for them anyway. Measured cost of keeping them: ~105 MB.
  find "$STAGE" -name '*.pdb' -type f -delete
fi

echo "==> Verifying the artefact can state its own identity"
# A packaged build that cannot say which build it is cannot be supported. This runs the published
# binary, not the source tree, so it is evidence about the artefact rather than about the repo.
PUBLISHED_VERSION=""
if [ -x "${STAGE}/Pine.Cli.exe" ]; then
  PUBLISHED_VERSION="$("${STAGE}/Pine.Cli.exe" --version)"
elif [ -x "${STAGE}/Pine.Cli" ]; then
  PUBLISHED_VERSION="$("${STAGE}/Pine.Cli" --version)"
else
  echo "package.sh: the published CLI is missing; the artefact cannot be asked what it is." >&2
  exit 1
fi

echo "    artefact reports: ${PUBLISHED_VERSION}"

case "$PUBLISHED_VERSION" in
  "${VERSION}"|"${VERSION}"+*) ;;
  *)
    echo "package.sh: the artefact reports '${PUBLISHED_VERSION}' but was built as '${VERSION}'." >&2
    echo "            Shipping a build whose own version disagrees with its filename is exactly the" >&2
    echo "            confusion TD-49 is about. Refusing." >&2
    exit 1
    ;;
esac

echo "==> Signing"
# THE WHOLE SIGNING POLICY LIVES IN packaging/sign.sh, and this is the only thing this script says
# about it: whether a certificate was configured, and what an absent one means for the caller.
#
# Until 2026-09-12 the policy was twenty lines here, and those twenty lines had never run -- no
# certificate exists, so the branch was dead. It was also wrong: under Git Bash, which is the shell
# this script runs in, MSYS rewrote `/fd` into a path before signtool saw it, so the command failed
# with "No file digest algorithm specified" while `/fd SHA256` sat on the command line. A seam that
# has never been executed is not a seam; it is an intention. See sign.sh for the measurement.
SIGNED="false"
SIGNING_NOTE="No code-signing certificate was configured, so this artefact is UNSIGNED."
SIGNING_THUMBPRINT=""
SIGNING_SUBJECT=""
SIGNED_FILES=0
TIMESTAMPED_FILES=0
TRUSTED_ROOT="no"

if [ -n "${PINE_SIGNTOOL:-}" ] && [ -n "${PINE_SIGNING_CERT:-}" ]; then
  FACTS="${OUT_ROOT}/${PACKAGE_NAME}.signing-facts"
  rm -f "$FACTS"

  bash "${REPO_ROOT}/packaging/sign.sh" "$STAGE" --facts "$FACTS"

  # sign.sh exits non-zero on every failure it knows about, so reaching here means it believes it
  # succeeded. This reads its facts back rather than assuming them: a facts file that is missing or
  # that does not say `signed=true` is a disagreement between two statements, and the manifest is
  # not allowed to be the optimistic one.
  if [ ! -f "$FACTS" ]; then
    echo "package.sh: sign.sh reported success and wrote no facts file. Refusing to describe an" >&2
    echo "            artefact whose signing step cannot say what it did." >&2
    exit 1
  fi

  # shellcheck disable=SC1090
  SIGNING_THUMBPRINT="$(sed -n 's/^thumbprint=//p' "$FACTS")"
  SIGNING_SUBJECT="$(sed -n 's/^subject=//p' "$FACTS")"
  SIGNED_FILES="$(sed -n 's/^signed_files=//p' "$FACTS")"
  TIMESTAMPED_FILES="$(sed -n 's/^timestamped_files=//p' "$FACTS")"
  TRUSTED_ROOT="$(sed -n 's/^trusted_root=//p' "$FACTS")"
  SIGNED_TOTAL_LOADABLE="$(sed -n 's/^total_loadable_files=//p' "$FACTS")"

  if [ "$(sed -n 's/^signed=//p' "$FACTS")" != "true" ] || [ -z "$SIGNING_THUMBPRINT" ]; then
    echo "package.sh: the signing step did not report a signature. Refusing." >&2
    exit 1
  fi

  rm -f "$FACTS"

  SIGNED="true"
  SIGNING_NOTE="Signed with ${SIGNING_SUBJECT} (SHA-256 thumbprint ${SIGNING_THUMBPRINT}); ${SIGNED_FILES} file(s) signed by this build, ${TIMESTAMPED_FILES} of them timestamped."

  if [ "$TRUSTED_ROOT" != "yes" ]; then
    # Reachable only via PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT=1, which exists for the self-test. The
    # manifest has to carry it, because "signed": true over a certificate no user's machine trusts
    # is the same lie as "signed": true over no certificate at all.
    SIGNING_NOTE="${SIGNING_NOTE} THE SIGNING CERTIFICATE DOES NOT CHAIN TO A TRUSTED ROOT: this build MUST NOT be published."
  fi
else
  echo "    !! UNSIGNED BUILD"
  echo "    !! Windows SmartScreen will warn on first launch, and a user has no way to tell this"
  echo "    !! artefact apart from one someone else built and modified. Set PINE_SIGNTOOL and"
  echo "    !! PINE_SIGNING_CERT to sign it. (TD-108: no certificate exists for this project yet.)"
  if [ "$REQUIRE_SIGNATURE" -eq 1 ]; then
    echo "package.sh: --require-signature was given and no certificate is configured. Refusing." >&2
    exit 1
  fi
fi

echo "==> Writing SHA256SUMS"
# Over every file that ships, not just the archive: the archive checksum tells a user the download
# was not corrupted, while the per-file list is what makes a modified DLL inside a correct-looking
# archive detectable.
(
  cd "$STAGE"
  find . -type f ! -name 'SHA256SUMS' -print0 \
    | sort -z \
    | xargs -0 sha256sum > SHA256SUMS
)

FILE_COUNT="$(find "$STAGE" -type f | wc -l | tr -d ' ')"
TOTAL_BYTES="$(find "$STAGE" -type f -printf '%s\n' | awk '{s+=$1} END {print s+0}')"

echo "==> Creating the archive"
ARCHIVE="${OUT_ROOT}/${PACKAGE_NAME}.tar.gz"
# Everything below runs from inside OUT_ROOT with relative names. GNU tar reads an absolute Windows
# path like `C:/out/x.tar.gz` as `host:path` and tries to open an rmt connection to a host called
# "C"; --force-local would fix that on GNU tar and is not accepted by the bsdtar on macOS runners.
# Staying relative works on all three.
(
  cd "$OUT_ROOT"
  rm -f "${PACKAGE_NAME}.tar.gz"
  tar -czf "${PACKAGE_NAME}.tar.gz" "$PACKAGE_NAME"
)
ARCHIVE_BYTES="$(cd "$OUT_ROOT" && (stat -c %s "${PACKAGE_NAME}.tar.gz" 2>/dev/null || stat -f %z "${PACKAGE_NAME}.tar.gz"))"
ARCHIVE_SHA="$(cd "$OUT_ROOT" && sha256sum "${PACKAGE_NAME}.tar.gz" | cut -d' ' -f1)"

# JSON STRING ESCAPING, BECAUSE THE MANIFEST NOW CARRIES TEXT THIS SCRIPT DID NOT AUTHOR.
# A certificate subject is `CN="Pine ...", O=...` -- it contains double quotes as a matter of course,
# and on a Windows path it can contain backslashes. Interpolating one straight into the heredoc below
# produces a manifest that is not JSON, which a release client would fail to parse on exactly the
# field it needs most. Order matters: backslashes first, then quotes, then control characters.
json_escape() {
  printf '%s' "$1" \
    | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' \
    | tr -d '\r\n\t'
}

if [ "$SIGNED" = "true" ]; then
  SIGNATURE_JSON="$(cat <<SIG
{
      "type": "authenticode",
      "thumbprint": "$(json_escape "$SIGNING_THUMBPRINT")",
      "subject": "$(json_escape "$SIGNING_SUBJECT")",
      "signed_by_this_build": ${SIGNED_FILES},
      "loadable_files": ${SIGNED_TOTAL_LOADABLE:-0},
      "timestamped": ${TIMESTAMPED_FILES},
      "chains_to_a_trusted_root": $( [ "$TRUSTED_ROOT" = "yes" ] && echo true || echo false )
    }
SIG
)"
else
  SIGNATURE_JSON="null"
fi

echo "==> Writing the release manifest"
# The shape is `docs/29-Deployment` §5.3, with two deliberate departures, both recorded in the file
# itself so a reader is not left to infer them:
#   - `signature` is null unless the artefact was actually signed. §5.3 says a client MUST verify
#     the signature before fetching; a manifest that carries a placeholder would defeat that check
#     by being present. When it is not null it is an OBJECT rather than a blob, because the useful
#     question is not "is there a signature" but "whose", and a thumbprint is what answers it. Its
#     `chains_to_a_trusted_root` field can be false: that is the self-test's certificate, and a
#     manifest saying `"signed": true` over a certificate no user's machine trusts would be the same
#     lie as saying it over no certificate at all.
#   - there is no `url`. Nothing hosts these artefacts yet, and inventing a download address that
#     404s is worse than admitting there is none.
MANIFEST="${OUT_ROOT}/${PACKAGE_NAME}.release.json"
cat > "$MANIFEST" <<JSON
{
  "schema": "pine.release.v1",
  "version": "${VERSION}",
  "reported_by_artefact": "${PUBLISHED_VERSION}",
  "channel": "alpha",
  "critical": false,
  "signed": ${SIGNED},
  "signature": ${SIGNATURE_JSON},
  "signing_note": "$(json_escape "$SIGNING_NOTE")",
  "artifacts": [
    {
      "rid": "${RID}",
      "file": "${PACKAGE_NAME}.tar.gz",
      "size": ${ARCHIVE_BYTES},
      "sha256": "${ARCHIVE_SHA}",
      "url": null,
      "unpacked_files": ${FILE_COUNT},
      "unpacked_size": ${TOTAL_BYTES}
    }
  ],
  "not_included": [
    "An installer. This is an archive, not an .msi/.dmg/.deb (TD-49, remaining).",
    "An update mechanism. Pine performs no network I/O at all, by design; an in-app updater would be its first (TD-49, remaining).",
    "Any runtime identifier other than ${RID}. The OCR stack is Windows-x64-only (TD-44)."
  ]
}
JSON

echo
echo "================ ARTEFACT ================"
echo " version        : ${VERSION}"
echo " reported by it : ${PUBLISHED_VERSION}"
echo " rid            : ${RID}"
echo " signed         : ${SIGNED}${SIGNING_THUMBPRINT:+ (${SIGNING_THUMBPRINT})}"
echo " files          : ${FILE_COUNT}"
echo " unpacked bytes : ${TOTAL_BYTES}"
echo " archive        : ${ARCHIVE} (${ARCHIVE_BYTES} bytes)"
echo " archive sha256 : ${ARCHIVE_SHA}"
echo " manifest       : ${MANIFEST}"
echo "=========================================="
