#!/usr/bin/env bash
#
# Falsifies `packaging/sign.sh`, on this machine, in about fifteen seconds (TD-108, plan item 2.2).
#
# WHY THIS FILE EXISTS AT ALL.
#   The signing seam went five months and twenty-four iterations without ever being executed, because
#   executing it appeared to require a certificate this project does not have and a 680 MB publish
#   nobody wants to wait for. Neither is true. A throwaway self-signed certificate and three copied
#   binaries reproduce every decision the real path makes, and `62-Post-Audit-Plan` §5.1 has already
#   recorded what a throwaway probe costs: the one that retired TD-140's stated hypothesis was
#   deleted, so that step now rests on a run nobody can repeat. This one is committed instead.
#
# WHAT IT PROVES, AND WHAT IT CANNOT.
#   It proves that signing works, that each refusal fires on the input it was written for, and that
#   the gate still passes a good artefact -- case G is there because a check that only ever says no
#   is indistinguishable from one that is broken.
#   It CANNOT prove anything about a real certificate: the test certificate is self-signed, so the
#   signed cases run with PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT=1, and what a user's SmartScreen would
#   make of a genuine Authenticode chain is outside anything this repository can measure.
#
# REQUIREMENTS: Windows, PowerShell, and a Windows SDK signtool.exe. No certificate, no network, no
# build. It creates its certificate in the CurrentUser store, exports it, and deletes it at the end
# whether or not the run passes.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

if ! command -v powershell.exe >/dev/null 2>&1; then
  echo "selftest-signing.sh: no powershell.exe. This test only runs on Windows." >&2
  exit 2
fi

SIGNTOOL="${PINE_SIGNTOOL:-$(bash packaging/find-signtool.sh)}"
echo "signtool  : $SIGNTOOL"

WORK="$(mktemp -d)"
CERT_THUMB=""

cleanup() {
  if [ -n "$CERT_THUMB" ]; then
    powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \
      "Remove-Item -LiteralPath 'Cert:\\CurrentUser\\My\\$CERT_THUMB' -Force -ErrorAction SilentlyContinue" >/dev/null 2>&1 || true
  fi
  rm -rf "$WORK"
}
trap cleanup EXIT

echo "workspace : $WORK"
echo

# ---------------------------------------------------------------------------------------------
# A throwaway code-signing certificate. It lives in the CurrentUser store for the length of this
# script and is removed by the trap above. It is NOT installed into any root store: the whole point
# of case F is that an untrusted root is refused, and installing it would delete that test.
# ---------------------------------------------------------------------------------------------
CERT_PFX="$WORK/selftest.pfx"
CERT_PASSWORD="pine-selftest"

CERT_PFX_WIN="$(cygpath -w "$CERT_PFX" 2>/dev/null || echo "$CERT_PFX")"

# Written as one line on purpose. A PowerShell backtick continuation inside a double-quoted bash
# string is eaten by bash before PowerShell ever sees it, and what reaches PowerShell is
# `New-SelfSignedCertificate` with none of its arguments -- which fails with a message about a
# missing signer name that says nothing at all about the real cause. Found the hard way here.
CERT_THUMB="$(powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "\$ErrorActionPreference='Stop'; \$c = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Pine signing self-test (throwaway, not an identity)' -CertStoreLocation Cert:\\CurrentUser\\My -KeyUsage DigitalSignature -NotAfter (Get-Date).AddDays(2); \$p = ConvertTo-SecureString -String '$CERT_PASSWORD' -Force -AsPlainText; Export-PfxCertificate -Cert \$c -FilePath '$CERT_PFX_WIN' -Password \$p | Out-Null; Write-Output \$c.Thumbprint" | tr -d '[:space:]')"

if [ -z "$CERT_THUMB" ] || [ ! -f "$CERT_PFX" ]; then
  echo "selftest-signing.sh: could not create a throwaway certificate." >&2
  exit 2
fi

echo "test cert : $CERT_THUMB"
echo

# ---------------------------------------------------------------------------------------------
# Fixtures. signtool.exe itself is Microsoft-signed, which makes it the only binary guaranteed to be
# present AND validly signed on any machine that can run this script -- so one copy stands in for
# "third-party code that is already signed" and a signature-stripped copy stands in for "our own
# freshly built binary". Nothing here has to be built.
# ---------------------------------------------------------------------------------------------
# EVERY STAGE CARRIES NON-PE FILES AND A SUBDIRECTORY, AND THAT IS THE FIX FOR THIS SCRIPT'S OWN
# BLIND SPOT. The first version built flat directories containing nothing but copies of
# signtool.exe, so four separate defects were invisible to all ten cases: a survey that matched
# every file rather than .exe/.dll (which it did -- `-Include` is inert with `-LiteralPath`, and a
# real signed release would have handed signtool a .runtimeconfig.json and failed), a survey that
# stopped recursing, and two refusals no case ever drove. A real artefact is ~660 MiB of nested
# directories full of .json, .pdb and asset files; a fixture that looks nothing like one tests
# nothing about one. Found by the independent review of plan item 2.2.
strip_signature() {
  MSYS2_ARG_CONV_EXCL='*' "$SIGNTOOL" remove /s \
    "$(cd "$(dirname "$1")" && pwd -W 2>/dev/null || dirname "$1")/$(basename "$1")" >/dev/null
}

make_stage() {
  local name="$1" want_valid="$2" want_unsigned="$3" want_nested="${4:-0}"
  local stage="$WORK/$name"
  rm -rf "$stage"
  mkdir -p "$stage/assets"

  # Not loadable code, and therefore not this artefact's business to sign. Both extensions are ones
  # `packaging/package.sh` publishes for real: a self-contained publish always emits
  # `*.runtimeconfig.json` and `*.deps.json` beside the binaries.
  printf 'not a binary\n' > "$stage/README.txt"
  printf '{ "runtimeOptions": {} }\n' > "$stage/Pine.Cli.runtimeconfig.json"
  printf 'nor is this\n' > "$stage/assets/notes.txt"

  if [ "$want_valid" -eq 1 ]; then
    cp "$SIGNTOOL" "$stage/already-signed-by-microsoft.exe"
  fi

  if [ "$want_unsigned" -eq 1 ]; then
    cp "$SIGNTOOL" "$stage/ours.exe"
    strip_signature "$stage/ours.exe"
  fi

  # In a subdirectory on purpose: a survey that stops at the top level would miss it, and a real
  # artefact keeps native libraries below the root.
  if [ "$want_nested" -eq 1 ]; then
    cp "$SIGNTOOL" "$stage/assets/nested.exe"
    strip_signature "$stage/assets/nested.exe"
  fi

  echo "$stage"
}

PASSES=0
FAILURES=0

# Runs one case and compares the exit code and, optionally, a substring of the output.
expect() {
  local label="$1" expected_exit="$2" expected_text="$3"
  shift 3

  local output status
  output="$("$@" 2>&1)"
  status=$?

  if [ "$status" -ne "$expected_exit" ]; then
    echo "FAIL  $label"
    echo "      expected exit $expected_exit, got $status"
    printf '%s\n' "$output" | sed 's/^/      | /'
    FAILURES=$(( FAILURES + 1 ))
    return
  fi

  if [ -n "$expected_text" ] && ! printf '%s' "$output" | grep -qF "$expected_text"; then
    echo "FAIL  $label"
    echo "      exit $status was right, but the output never says: $expected_text"
    printf '%s\n' "$output" | sed 's/^/      | /'
    FAILURES=$(( FAILURES + 1 ))
    return
  fi

  echo "pass  $label"
  PASSES=$(( PASSES + 1 ))
}

sign_env() {
  env \
    PINE_SIGNTOOL="$SIGNTOOL" \
    PINE_SIGNING_CERT="$CERT_PFX" \
    PINE_SIGNING_PASSWORD="$CERT_PASSWORD" \
    PINE_SIGNING_ALLOW_UNTIMESTAMPED=1 \
    PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT=1 \
    "$@"
}

# --- A. the happy path -------------------------------------------------------------------------
# "2 of 3" is the load-bearing number: three .exe files (one already Microsoft-signed, one ours, one
# ours in a subdirectory) and three non-PE files that must not be counted, signed or verified. A
# survey that matches every file says "5 of 6"; one that does not recurse says "1 of 2".
STAGE_A="$(make_stage a 1 1 1)"
expect "A  signs what is unsigned, recurses, and leaves a valid signature alone" 0 "signed        : 2 of 3" \
  sign_env bash packaging/sign.sh "$STAGE_A" --facts "$WORK/facts-a"

if [ -f "$WORK/facts-a" ] && grep -qx "thumbprint=$CERT_THUMB" "$WORK/facts-a"; then
  echo "pass  A' the facts file names the certificate that actually signed"
  PASSES=$(( PASSES + 1 ))
else
  echo "FAIL  A' the facts file does not name $CERT_THUMB"
  FAILURES=$(( FAILURES + 1 ))
fi

# The facts file is what packaging/package.sh turns into the release manifest, so a run that signed
# with an untrusted certificate and reported `trusted_root=yes` would put "signed": true over a
# certificate no user's machine trusts -- the same lie as "signed": true over no certificate at all.
if [ -f "$WORK/facts-a" ] && grep -qx "trusted_root=no" "$WORK/facts-a"; then
  echo "pass  A2 the facts file admits the certificate is not trusted here"
  PASSES=$(( PASSES + 1 ))
else
  echo "FAIL  A2 the facts file claims a trusted root for a self-signed certificate"
  FAILURES=$(( FAILURES + 1 ))
fi

# Three .exe files were surveyed and three non-PE files were not. Asserted on the artefact itself
# rather than on the script's own arithmetic: nothing that is not loadable code may come back
# carrying a signature from this build.
if [ -f "$STAGE_A/README.txt" ] \
  && ! grep -qs "$CERT_THUMB" "$STAGE_A/README.txt" \
  && ! grep -qs "$CERT_THUMB" "$STAGE_A/Pine.Cli.runtimeconfig.json"; then
  echo "pass  A3 the non-PE files were left alone"
  PASSES=$(( PASSES + 1 ))
else
  echo "FAIL  A3 a non-PE file was touched by the signing pass"
  FAILURES=$(( FAILURES + 1 ))
fi

# --- B. no certificate configured --------------------------------------------------------------
STAGE_B="$(make_stage b 1 1)"
expect "B  refuses to sign with no certificate configured" 2 "must both be set" \
  env -u PINE_SIGNTOOL -u PINE_SIGNING_CERT bash packaging/sign.sh "$STAGE_B"

# --- C. a certificate but no timestamp authority -----------------------------------------------
STAGE_C="$(make_stage c 1 1)"
expect "C  refuses to sign without a timestamp authority" 2 "no PINE_TIMESTAMP_URL" \
  env PINE_SIGNTOOL="$SIGNTOOL" PINE_SIGNING_CERT="$CERT_PFX" PINE_SIGNING_PASSWORD="$CERT_PASSWORD" \
      bash packaging/sign.sh "$STAGE_C"

# --- D. verification catches an unsigned file --------------------------------------------------
STAGE_D="$(make_stage d 1 1)"
expect "D  verification refuses an artefact with an unsigned file in it" 1 "are NOT SIGNED" \
  env PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT=1 bash packaging/sign.sh "$STAGE_D" --verify-only

# --- E. verification catches bytes edited after signing ----------------------------------------
STAGE_E="$(make_stage e 0 1)"
sign_env bash packaging/sign.sh "$STAGE_E" >/dev/null 2>&1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "
  \$p = '$(cygpath -w "$STAGE_E/ours.exe" 2>/dev/null || echo "$STAGE_E/ours.exe")'
  \$b = [IO.File]::ReadAllBytes(\$p)
  \$b[600] = \$b[600] -bxor 0xFF
  [IO.File]::WriteAllBytes(\$p, \$b)
" >/dev/null
expect "E  verification refuses bytes edited after signing" 1 "does not match their bytes" \
  env PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT=1 bash packaging/sign.sh "$STAGE_E" --verify-only

# --- F. verification refuses an untrusted root unless told otherwise ---------------------------
STAGE_F="$(make_stage f 0 1)"
sign_env bash packaging/sign.sh "$STAGE_F" >/dev/null 2>&1
expect "F  verification refuses a signature whose root is not trusted" 1 "does not chain to a trusted root" \
  env -u PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT bash packaging/sign.sh "$STAGE_F" --verify-only

# --- G. the positive control -------------------------------------------------------------------
# A gate that says no to everything is not a gate. This artefact is entirely Microsoft-signed, so
# verification must PASS -- and if it ever stops passing, the three refusals above stop meaning
# anything, because they would be firing on every input.
STAGE_G="$(make_stage g 1 0)"
expect "G  verification passes an artefact that is properly signed throughout" 0 "all matching their bytes" \
  env -u PINE_SIGNING_ACCEPT_UNTRUSTED_ROOT bash packaging/sign.sh "$STAGE_G" --verify-only

# --- H. an empty stage is not a signed stage ---------------------------------------------------
# The stage is NOT empty -- it holds a .txt and a .json in two directories. A survey that matches
# every file finds three "loadable" files here and sails past this refusal.
STAGE_H="$(make_stage h 0 0)"
expect "H  refuses a stage whose only files are not loadable code" 1 "no .exe or .dll at all" \
  sign_env bash packaging/sign.sh "$STAGE_H"

# --- J. a run that signs nothing is not a pass -------------------------------------------------
# §5.2 calls this "this repository's recurring failure shape rather than a hypothetical one", and
# until the independent review of plan item 2.2 no case drove it: deleting the refusal outright left
# all ten cases green. Everything in this stage already carries a valid Microsoft signature.
STAGE_J="$(make_stage j 1 0)"
expect "J  refuses a run that would sign nothing at all" 1 "signed" \
  sign_env bash packaging/sign.sh "$STAGE_J"

# --- I. the defect this item found, kept as a live measurement ---------------------------------
# The old in-line signing step, reproduced exactly: signtool invoked from Git Bash WITHOUT
# MSYS2_ARG_CONV_EXCL. `/fd` looks like an absolute POSIX path, so MSYS rewrites it, and signtool
# then complains that the flag it was given is missing. If this case ever starts PASSING -- i.e. the
# old form starts working -- then the exported variable in sign.sh is no longer load-bearing and the
# comment explaining it has become fiction.
STAGE_I="$(make_stage i 0 1)"
expect "I  the pre-fix command form still fails (MSYS rewrites /fd)" 1 "No file digest algorithm specified" \
  env -u MSYS2_ARG_CONV_EXCL "$SIGNTOOL" sign /fd SHA256 /f "$CERT_PFX" /p "$CERT_PASSWORD" "$STAGE_I/ours.exe"

echo
echo "=================================================="
echo " passed : $PASSES"
echo " failed : $FAILURES"
echo "=================================================="

if [ "$FAILURES" -ne 0 ]; then
  exit 1
fi
