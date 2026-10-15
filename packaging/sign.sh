#!/usr/bin/env bash
#
# Signs a staged Pine artefact, and then PROVES it signed it (TD-108, post-audit plan item 2.2).
#
# WHY THIS IS A FILE OF ITS OWN, AND NOT TWENTY LINES INSIDE package.sh.
#   Those twenty lines existed, from iteration 20 until this one, and they had never been executed:
#   no certificate exists for this project, so the branch that used them was dead. It was dead AND
#   WRONG. Under Git Bash on Windows -- which is the shell `packaging/package.sh` runs in, and the
#   shell the CI `package` job uses on `windows-latest` -- MSYS argument conversion rewrites an
#   argument that looks like an absolute POSIX path, and `/fd` looks like one. Measured on
#   2026-09-12: the exact command those lines built fails with
#
#       SignTool Error: No file digest algorithm specified. Please specify the digest algorithm
#       with the /fd flag.
#
#   with `/fd SHA256` present on the command line, and succeeds with the identical command when
#   MSYS2_ARG_CONV_EXCL is set. So the honest summary of the old seam is not "it works, it just has
#   no certificate": on the day a certificate arrived it would have failed, and the failure message
#   would have accused the caller of an omission it had not made.
#
#   Splitting it out is what makes that testable at all. `packaging/selftest-signing.sh` runs this
#   script end to end against a throwaway self-signed certificate and a fixture directory, and
#   falsifies each of its refusals. Publishing a 680 MB artefact is not a prerequisite for finding
#   out whether signing works.
#
# WHAT IT REFUSES, AND WHY EACH REFUSAL IS THERE.
#   - No certificate configured           -> exit 2. The caller decides what an unsigned build means;
#                                            this script never produces one silently.
#   - No timestamp URL                    -> exit 2, unless PINE_SIGNING_ALLOW_UNTIMESTAMPED=1. An
#                                            untimestamped Authenticode signature stops validating
#                                            the day the certificate expires, which turns a shipped
#                                            release into a SmartScreen warning on a schedule.
#   - A file left unsigned after signing  -> exit 1. This is the property worth having: not "the
#                                            entry point is signed", but "nothing in this artefact
#                                            that Windows will load as code is unsigned".
#   - A file whose signature does not
#     match its bytes (HashMismatch)      -> exit 1. Tampering, or a file written after it was signed.
#   - A signature that does not chain to
#     a trusted root                      -> exit 1, unless PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT=1,
#                                            which exists for the self-test and is refused in CI by
#                                            `ReleaseSigningTests`.
#   - Nothing signed at all               -> exit 1. A run that signs zero files and reports success
#                                            is the shape of failure this whole repository is about.
#
# WHAT IT DELIBERATELY DOES NOT DO.
#   It does not re-sign a file that already carries a VALID signature. About nine tenths of the
#   artefact is .NET runtime and third-party native code that Microsoft, Google and the SkiaSharp
#   maintainers have already signed; replacing their signatures with Pine's would destroy the
#   provenance a reader could otherwise check, cost one timestamp-authority round trip per file, and
#   assert authorship of code this project did not write. Pine's own binaries arrive unsigned and are
#   therefore signed. The property asserted afterwards is the same either way: every loadable file
#   has a valid signature by someone, and every file THIS script signed carries one certificate.
#
# USAGE
#   packaging/sign.sh <stage-dir> [--facts <path>]     sign, then verify
#   packaging/sign.sh <stage-dir> --verify-only        verify only; no certificate needed
#
#   `--verify-only` exists so that the refusals above can be falsified without a certificate and
#   without a 680 MB publish -- `packaging/selftest-signing.sh` drives it against a stage with an
#   unsigned file in it, and against one whose bytes were edited after signing -- and because it is
#   the check a person wants against an artefact they DOWNLOADED, where signing is not on offer.
#
# ENVIRONMENT
#   PINE_SIGNTOOL                        Path to signtool.exe. `packaging/find-signtool.sh` finds one.
#   PINE_SIGNING_CERT                    Path to the .pfx holding the code-signing certificate.
#   PINE_SIGNING_PASSWORD                Optional password for that .pfx.
#   PINE_TIMESTAMP_URL                   RFC 3161 timestamp authority, e.g. http://timestamp.digicert.com
#   PINE_SIGNING_ALLOW_UNTIMESTAMPED=1   Sign without a timestamp. Self-test only.
#   PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT=1 Accept a signature whose root is not installed. Self-test only.

set -euo pipefail

STAGE=""
FACTS=""
VERIFY_ONLY=0

while [ $# -gt 0 ]; do
  case "$1" in
    --facts)
      if [ "$#" -lt 2 ]; then
        echo "sign.sh: --facts needs a path." >&2
        exit 2
      fi
      FACTS="$2"; shift 2 ;;
    --verify-only) VERIFY_ONLY=1; shift ;;
    -h|--help) sed -n '2,60p' "$0"; exit 0 ;;
    -*) echo "sign.sh: unknown argument '$1'" >&2; exit 2 ;;
    *)
      if [ -n "$STAGE" ]; then
        echo "sign.sh: more than one stage directory given ('$STAGE' and '$1')." >&2
        exit 2
      fi
      STAGE="$1"; shift ;;
  esac
done

if [ -z "$STAGE" ]; then
  echo "sign.sh: no stage directory given." >&2
  echo "usage: packaging/sign.sh <stage-dir> [--facts <path>]" >&2
  exit 2
fi

if [ ! -d "$STAGE" ]; then
  echo "sign.sh: '$STAGE' is not a directory." >&2
  exit 2
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE_ABS="$(cd "$STAGE" && pwd)"

if [ "$VERIFY_ONLY" -eq 0 ] && { [ -z "${PINE_SIGNTOOL:-}" ] || [ -z "${PINE_SIGNING_CERT:-}" ]; }; then
  echo "sign.sh: PINE_SIGNTOOL and PINE_SIGNING_CERT must both be set." >&2
  echo "         This script never produces an unsigned artefact quietly; whether an unsigned" >&2
  echo "         build is acceptable is the caller's decision, not this one's." >&2
  exit 2
fi

if [ "$VERIFY_ONLY" -eq 0 ] && [ ! -x "$PINE_SIGNTOOL" ] && [ ! -f "$PINE_SIGNTOOL" ]; then
  echo "sign.sh: PINE_SIGNTOOL='$PINE_SIGNTOOL' is not a file." >&2
  exit 2
fi

if [ "$VERIFY_ONLY" -eq 0 ] && [ ! -f "$PINE_SIGNING_CERT" ]; then
  echo "sign.sh: PINE_SIGNING_CERT='$PINE_SIGNING_CERT' is not a file." >&2
  exit 2
fi

if [ "$VERIFY_ONLY" -eq 0 ] && [ -z "${PINE_TIMESTAMP_URL:-}" ] && [ "${PINE_SIGNING_ALLOW_UNTIMESTAMPED:-0}" != "1" ]; then
  echo "sign.sh: no PINE_TIMESTAMP_URL." >&2
  echo "         Without a countersigned timestamp, every signature in this artefact stops" >&2
  echo "         validating on the day the certificate expires -- a release that turns itself into" >&2
  echo "         a SmartScreen warning on a date nobody wrote down. Set PINE_TIMESTAMP_URL, or set" >&2
  echo "         PINE_SIGNING_ALLOW_UNTIMESTAMPED=1 and mean it." >&2
  exit 2
fi

# `cygpath -w` rather than trusting MSYS to convert: see MSYS2_ARG_CONV_EXCL below. The certificate
# path is the one path here that can legitimately be an absolute POSIX path, because it comes from
# the caller's environment rather than from this script.
CERT_WIN="${PINE_SIGNING_CERT:-}"
if [ -n "$CERT_WIN" ] && command -v cygpath >/dev/null 2>&1; then
  CERT_WIN="$(cygpath -w "$PINE_SIGNING_CERT")"
fi

survey() {
  # Always through PowerShell: `signtool verify` reports "unsigned" and "signed, root not installed"
  # with the same exit code and different English. See packaging/authenticode-status.ps1.
  #
  # `tr -d` STRIPS THE CARRIAGE RETURN, and it is not cosmetic. powershell.exe emits CRLF, and
  # command substitution removes only the final newline -- so without this every path in the survey
  # ends in \r. It happened to work (Windows tolerated the trailing CR on a path handed to
  # signtool) and it put a CR in the middle of every error message this script prints. Relying on an
  # OS to forgive a malformed path is not a thing to rely on. Found by the independent review of
  # plan item 2.2.
  powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass \
    -File "$REPO_ROOT/packaging/authenticode-status.ps1" -Stage "$STAGE_ABS" \
    | tr -d '\r'
}

echo "==> Surveying what is already signed"
BEFORE="$(survey)"

if [ -z "$BEFORE" ]; then
  echo "sign.sh: the stage directory contains no .exe or .dll at all." >&2
  echo "         Signing nothing and reporting success is the failure this script exists to refuse." >&2
  exit 1
fi

TOTAL="$(printf '%s\n' "$BEFORE" | wc -l | tr -d ' ')"

# Everything that is not already VALID. A file that is NotSigned is ours; a file that is
# HashMismatch or UnknownError is broken or foreign-but-untrusted, and signing it replaces whatever
# was there with a signature this build can stand behind.
TO_SIGN="$(printf '%s\n' "$BEFORE" | awk -F '\t' '$1 != "Valid" { print $5 }')"

SIGN_COUNT=0
if [ -n "$TO_SIGN" ]; then
  SIGN_COUNT="$(printf '%s\n' "$TO_SIGN" | wc -l | tr -d ' ')"
fi

echo "    ${TOTAL} loadable file(s); ${SIGN_COUNT} would need a signature from this build"

if [ "$VERIFY_ONLY" -eq 0 ] && [ "$SIGN_COUNT" -eq 0 ]; then
  echo "sign.sh: every file in the stage already carries a valid signature, so this build signed" >&2
  echo "         nothing. That is not a pass: it means the artefact contains none of Pine's own" >&2
  echo "         binaries, or it was signed by a previous run and is being shipped as this one's." >&2
  exit 1
fi

if [ "$VERIFY_ONLY" -eq 1 ]; then
  echo "==> --verify-only: nothing will be signed"
fi

if [ "$VERIFY_ONLY" -eq 0 ]; then
echo "==> Signing ${SIGN_COUNT} file(s) with ${PINE_SIGNTOOL}"

# MSYS2_ARG_CONV_EXCL='*' TURNS OFF PATH CONVERSION FOR THE WHOLE COMMAND LINE, AND IT IS THE POINT
# OF THIS BLOCK. Git Bash rewrites an argument that looks like an absolute POSIX path before the
# child process sees it, and `/fd`, `/f`, `/p`, `/tr` and `/td` all look like one. The wildcard form
# is used rather than a list of switch prefixes so that nothing here depends on MSYS's judgement
# about which arguments are paths: every path this script passes is either relative to the stage
# directory (below) or has been through `cygpath -w` already.
#
# The file arguments are relative because of that: the loop runs from inside the stage directory and
# passes the paths the survey printed, which are relative to it.
(
  cd "$STAGE_ABS"
  export MSYS2_ARG_CONV_EXCL='*'

  timestamp_args=()
  if [ -n "${PINE_TIMESTAMP_URL:-}" ]; then
    timestamp_args=(/tr "$PINE_TIMESTAMP_URL" /td SHA256)
  fi

  password_args=()
  if [ -n "${PINE_SIGNING_PASSWORD:-}" ]; then
    password_args=(/p "$PINE_SIGNING_PASSWORD")
  fi

  # -n 20: signtool takes many files per invocation, and one invocation per file would mean one
  # timestamp-authority round trip per file. xargs propagates a non-zero exit from any batch, and
  # `set -e` in this subshell turns that into a failure of the script.
  printf '%s\n' "$TO_SIGN" \
    | xargs -d '\n' -n 20 "$PINE_SIGNTOOL" sign \
        /fd SHA256 \
        /f "$CERT_WIN" \
        "${password_args[@]}" \
        "${timestamp_args[@]}"
)
fi

echo "==> Verifying the artefact, file by file"
if [ "$VERIFY_ONLY" -eq 1 ]; then
  # Nothing was signed, so nothing can have changed; re-surveying would only cost a second pass over
  # the whole artefact and invite the two passes to disagree.
  AFTER="$BEFORE"
else
  AFTER="$(survey)"
fi

AFTER_TOTAL="$(printf '%s\n' "$AFTER" | wc -l | tr -d ' ')"
# NOT COVERED BY packaging/selftest-signing.sh, and said so rather than left to be discovered.
# Driving this would need a file to appear in the stage between the two surveys, which the self-test
# has no hook for. Deleting this check leaves every case green. The same is true of the
# exactly-one-certificate check and the dropped-countersignature check below: all three guard states
# the tool should never produce, and none is reachable from a fixture. They are kept because the
# cost is three comparisons and the failure they describe is silent; they are NOT claimed as tested.
if [ "$AFTER_TOTAL" -ne "$TOTAL" ]; then
  echo "sign.sh: the stage held ${TOTAL} loadable files before signing and ${AFTER_TOTAL} after." >&2
  echo "         Something wrote into the artefact while it was being signed. Refusing." >&2
  exit 1
fi

UNSIGNED="$(printf '%s\n' "$AFTER" | awk -F '\t' '$1 == "NotSigned" { print $5 }')"
MISMATCHED="$(printf '%s\n' "$AFTER" | awk -F '\t' '$1 == "HashMismatch" { print $5 }')"
UNTRUSTED="$(printf '%s\n' "$AFTER" | awk -F '\t' '$1 != "Valid" && $1 != "NotSigned" && $1 != "HashMismatch" { print $1 "  " $5 }')"

FAILED=0

# Indented with sed rather than `printf '    %s\n' $list`, which word-splits: every path in this
# artefact is a Windows path, and `Program Files`-shaped names are the normal case, not the exotic one.
indent() { sed 's/^/    /' >&2; }

if [ -n "$UNSIGNED" ]; then
  echo "::error::$(printf '%s\n' "$UNSIGNED" | wc -l | tr -d ' ') file(s) in the artefact are NOT SIGNED:" >&2
  printf '%s\n' "$UNSIGNED" | indent
  FAILED=1
fi

if [ -n "$MISMATCHED" ]; then
  echo "::error::$(printf '%s\n' "$MISMATCHED" | wc -l | tr -d ' ') file(s) carry a signature that does not match their bytes:" >&2
  printf '%s\n' "$MISMATCHED" | indent
  FAILED=1
fi

if [ -n "$UNTRUSTED" ]; then
  if [ "${PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT:-0}" = "1" ]; then
    echo "    !! PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT=1: $(printf '%s\n' "$UNTRUSTED" | wc -l | tr -d ' ') file(s) are signed by a"
    echo "    !! certificate this machine does not trust, and that is being accepted. A build"
    echo "    !! produced this way MUST NOT be published: to a user it is an unsigned build with"
    echo "    !! extra steps."
  else
    echo "::error::signed, but the certificate does not chain to a trusted root:" >&2
    printf '%s\n' "$UNTRUSTED" | indent
    echo "    A signature nobody can validate protects nobody. If this is a self-signed test" >&2
    echo "    certificate, that is what PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT=1 is for." >&2
    FAILED=1
  fi
fi

if [ "$FAILED" -ne 0 ]; then
  exit 1
fi

if [ "$VERIFY_ONLY" -eq 1 ]; then
  # The three gates above are the whole of what --verify-only can honestly assert: every loadable
  # file carries a signature, every signature matches the bytes under it, and every certificate
  # chains to a root this machine trusts. It deliberately does NOT assert who signed it -- a
  # downloaded artefact signed by a valid certificate belonging to someone else would pass here, and
  # the thumbprint in the release manifest is what answers that question.
  echo
  echo "    verified      : ${TOTAL} loadable file(s), all signed, all matching their bytes"
  printf '%s
' "$AFTER" | awk -F '	' '{ print $2 "  " $3 }' | sort -u | sed 's/^/    signer        : /'
  exit 0
fi

# Every file this run signed has to carry ONE certificate. Two thumbprints among them would mean
# signtool used a certificate other than the one configured for at least one batch.
#
# The list of paths reaches awk through a FILE, not through `-v`. `awk -v x="sub\Pine.Core.dll"`
# processes backslash escapes in the value, and every path in this artefact is a Windows path: `\P`
# would have been silently eaten and the file quietly excluded from this check.
WANTED_LIST="$(mktemp)"
trap 'rm -f "$WANTED_LIST"' EXIT
printf '%s\n' "$TO_SIGN" > "$WANTED_LIST"

SIGNED_THUMBS="$(
  printf '%s\n' "$AFTER" \
    | awk -F '\t' '
        NR == FNR { wanted[$0] = 1; next }
        $5 in wanted { print $2 }
      ' "$WANTED_LIST" - \
    | sort -u
)"

THUMB_COUNT="$(printf '%s\n' "$SIGNED_THUMBS" | grep -c . || true)"
# Not covered by the self-test -- see the note above the AFTER_TOTAL comparison.
if [ "$THUMB_COUNT" -ne 1 ]; then
  echo "sign.sh: the files this run signed carry ${THUMB_COUNT} distinct certificates:" >&2
  printf '%s\n' "$SIGNED_THUMBS" | indent
  echo "         Exactly one was configured, so at least one file was signed by something else." >&2
  exit 1
fi

THUMBPRINT="$SIGNED_THUMBS"
SUBJECT="$(printf '%s\n' "$AFTER" | awk -F '\t' -v t="$THUMBPRINT" '$2 == t { print $3; exit }')"

# Over the files THIS RUN SIGNED, not over every file carrying the thumbprint. A stage that already
# held a file signed earlier with the same certificate would otherwise be counted here and
# subtracted from SIGN_COUNT, which counts only the files that were not already valid -- reporting
# fewer timestamped files than were signed, and in principle a negative count in the manifest.
UNTIMESTAMPED="$(
  printf '%s\n' "$AFTER" \
    | awk -F '\t' -v t="$THUMBPRINT" '
        NR == FNR { wanted[$0] = 1; next }
        $5 in wanted && $2 == t && $4 != "yes" { print $5 }
      ' "$WANTED_LIST" -
)"

UNTIMESTAMPED_COUNT=0
if [ -n "$UNTIMESTAMPED" ]; then
  UNTIMESTAMPED_COUNT="$(printf '%s\n' "$UNTIMESTAMPED" | wc -l | tr -d ' ')"
fi

# Not covered by the self-test -- see the note above the AFTER_TOTAL comparison. Driving it would
# need a timestamp authority that answers successfully and returns no countersignature.
if [ "$UNTIMESTAMPED_COUNT" -gt 0 ] && [ "${PINE_SIGNING_ALLOW_UNTIMESTAMPED:-0}" != "1" ]; then
  echo "sign.sh: ${UNTIMESTAMPED_COUNT} file(s) were signed without a timestamp, though a timestamp" >&2
  echo "         authority was configured. signtool reported success, so the countersignature was" >&2
  echo "         dropped rather than refused. Refusing to call this signed." >&2
  exit 1
fi

TRUSTED="yes"
if [ -n "$UNTRUSTED" ]; then
  TRUSTED="no"
fi

echo
echo "    signed        : ${SIGN_COUNT} of ${TOTAL} loadable files (the rest already carried a valid signature)"
echo "    certificate   : ${SUBJECT}"
echo "    thumbprint    : ${THUMBPRINT}"
echo "    timestamped   : $(( SIGN_COUNT - UNTIMESTAMPED_COUNT )) of ${SIGN_COUNT}"
echo "    trusted root  : ${TRUSTED}"

if [ -n "$FACTS" ]; then
  # Consumed by packaging/package.sh to write the manifest. Deliberately not JSON: the caller is a
  # shell script, and a shell script parsing JSON is how a manifest ends up with a field that says
  # `null` because `jq` was not installed.
  cat > "$FACTS" <<FACTS
signed=true
thumbprint=${THUMBPRINT}
subject=${SUBJECT}
signed_files=${SIGN_COUNT}
total_loadable_files=${TOTAL}
timestamped_files=$(( SIGN_COUNT - UNTIMESTAMPED_COUNT ))
trusted_root=${TRUSTED}
FACTS
fi
