<#
.SYNOPSIS
  Reports the Authenticode status of every PE file in a staged artefact. One line per file.

.DESCRIPTION
  THIS SCRIPT DECIDES NOTHING. It is a reporter, deliberately: `packaging/sign.sh` holds the whole
  signing policy, in one language, and this exists only because the fact it reports cannot be got at
  from bash. Every judgement -- what must be signed, what may be left alone, what is fatal -- lives
  there, so a reader does not have to hold two half-policies in two languages at once.

  WHY NOT `signtool verify`. It answers the question, but it answers it in English prose on stderr
  with a single exit code of 1 for every kind of failure. Measured on 2026-09-12, an unsigned file
  and a file signed by an untrusted root both exit 1, differing only in the sentence printed
  ("No signature found." versus "A certificate chain processed, but terminated in a root certificate
  which is not trusted..."). A gate that has to grep an error message for the difference between
  "not signed at all" and "signed, chain not installed here" is a gate built on a string that
  Microsoft is free to reword. `Get-AuthenticodeSignature` returns those as distinct typed values --
  `NotSigned`, `UnknownError`, `HashMismatch`, `Valid` -- which is what this prints.

.OUTPUTS
  One tab-separated line per file, on stdout:

      <status><TAB><thumbprint><TAB><subject><TAB><timestamped><TAB><relative path>

  `status`      is the SignatureStatus name: Valid, NotSigned, HashMismatch, UnknownError, ...
  `thumbprint`  is the signing certificate's thumbprint, or `-` when there is no signer.
  `subject`     is the signing certificate's subject with tabs stripped, or `-`.
  `timestamped` is `yes` when the signature carries a countersignature timestamp, else `no`.
  `path`        is relative to -Stage, and is LAST so that a path containing spaces cannot shift
                any other column.

  Exit code 0 means "the report is complete", NOT "the artefact is signed". A non-zero exit means
  the report itself could not be produced, which the caller must treat as fatal rather than as an
  absence of findings -- reading an empty report as "nothing wrong" is how a check reports green
  over work it never did.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Stage
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $Stage -PathType Container)) {
    Write-Error "authenticode-status.ps1: '$Stage' is not a directory."
    exit 2
}

$stageRoot = (Resolve-Path -LiteralPath $Stage).Path
$prefix = $stageRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar

# .exe and .dll only. These are the two extensions Windows will load as code out of this artefact,
# and they are the same two `packaging/sign.sh` signs -- the two lists have to be the same list or
# the verify pass is checking a different set from the one that was signed.
#
# THE FILTER IS APPLIED BY `Where-Object`, NOT BY `-Include`, AND THAT IS NOT A STYLE CHOICE.
# `-Include` is a PATH filter, and with `-LiteralPath` it is ignored outright -- silently, with no
# error. Measured on 2026-09-12 against a directory holding only `a.txt`, `b.json`, `c.pdb` and
# `sub\d.dat`: `Get-ChildItem -LiteralPath $d -Recurse -File -Include *.exe,*.dll` returned all four.
# The first version of this script used exactly that, so the survey covered EVERY file in the
# artefact: `signtool` would have been handed `Pine.Cli.runtimeconfig.json` on the first real signed
# release and failed with "this file format cannot be signed", and `--verify-only` reported a `.txt`
# as a signed loadable file. Found by the independent review of plan item 2.2.
$files = Get-ChildItem -LiteralPath $stageRoot -Recurse -File |
    Where-Object { $_.Extension -eq '.exe' -or $_.Extension -eq '.dll' }

foreach ($file in $files) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName

    $thumbprint = '-'
    $subject = '-'

    if ($null -ne $signature.SignerCertificate) {
        $thumbprint = $signature.SignerCertificate.Thumbprint
        # Tabs would break the column layout; a subject is free text from a certificate the build
        # did not necessarily issue, so it is sanitised rather than trusted to be well-behaved.
        $subject = ($signature.SignerCertificate.Subject -replace "[`t`r`n]", ' ')
    }

    $timestamped = 'no'
    if ($null -ne $signature.TimeStamperCertificate) {
        $timestamped = 'yes'
    }

    $relative = $file.FullName
    if ($relative.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        $relative = $relative.Substring($prefix.Length)
    }

    Write-Output ("{0}`t{1}`t{2}`t{3}`t{4}" -f $signature.Status, $thumbprint, $subject, $timestamped, $relative)
}
