#!/usr/bin/env bash
#
# Answers one question: which projects does CI build, and on which leg (TD-54, plan item 0.2).
#
# WHAT THIS REPLACES.
#   `.github/workflows/dotnet-ci.yml` used to carry the answer as three hand-written lists -- seven
#   project paths in the `desktop` job, two in the `platform-heads` matrix, two in `test`. Every one
#   of them was correct, and a comment in the file recorded that they had been verified TFM-by-TFM
#   on 2026-08-26. That is not the problem a list has: the problem is that adding a twelfth project
#   changes none of them, so the new project is built by nothing, tested by nothing, and the
#   pipeline stays green while covering less than it did the day before. `Pine.slnx` is the file a
#   developer actually edits when adding a project, so the lists are derived from it here instead.
#
# THE CHAIN THIS CLOSES, END TO END.
#   1. `BuildHygieneTests.EveryProjectFileIsInTheSolution` -- every .csproj on disk is in Pine.slnx.
#   2. This script -- every project in Pine.slnx lands on exactly one CI leg, or the script fails.
#   3. `BuildHygieneTests.EveryProjectInTheSolutionIsBuiltBySomeCiLeg` -- runs this script and checks
#      that the legs cover the solution exactly: nothing in two legs, nothing in none.
#   Break any link and the suite goes red. That is the whole design: a project cannot be added to
#   this repository and stay outside CI without a test failing first.
#
# WHY IT DOES NOT SIMPLY RUN `dotnet build Pine.slnx`, WHICH IS THE WORDING THE ROW USED.
#   The solution cannot be built in one command on any machine this project has, and the audit has
#   measured that three separate times: `dotnet build Pine.slnx -c Release` aborts at restore with
#   two NETSDK1147 errors, because two of the eleven projects target `net10.0-android` and
#   `net10.0-ios` and a bare runner has neither workload. NETSDK1147 is an error, not a warning --
#   nothing compiles. A whole-solution build would therefore have to run on macOS with both
#   workloads installed, on every push, for every project, which is a slower and dearer pipeline
#   that verifies nothing extra. Deriving the legs from the solution closes the drift the row is
#   actually about, and closes it for all four legs rather than for one.
#
# CLASSIFICATION, IN ORDER. The first rule that matches wins.
#   1. TFM carries a platform suffix (`net10.0-android`, `net10.0-ios`) -> that platform's leg.
#   2. Project references `Microsoft.NET.Test.Sdk`                      -> the `test` leg.
#   3. Anything else                                                    -> the `desktop` leg.
#
#   A project is classified by what it IS, not by where it sits: the test rule reads the package
#   reference that makes `dotnet test` work at all, rather than the `tests/` folder, because a
#   directory name is a convention and this has to be a fact. Rule 1 comes first for the same reason
#   a mobile test project would still need its workload before anything could run it.
#
# IT FAILS RATHER THAN SKIPS, EVERYWHERE.
#   A project whose file is missing, whose TFM cannot be read, or whose platform suffix is not one
#   this pipeline has a leg for, stops the script with a non-zero exit and a message naming it. The
#   failure being designed out is a silent omission, so an unclassifiable project must never quietly
#   become an empty line.
#
# USAGE
#   scripts/ci-projects.sh <leg>       leg = desktop | test | android | ios | all
#
#   Prints one repository-relative project path per line, sorted, with forward slashes. A leg name
#   that does not exist exits 2. A leg that exists but matches no project exits 1, deliberately: a
#   job that builds nothing still reports green, so an emptied leg has to be noticed. If a head is
#   genuinely gone, delete its leg from the workflow rather than letting it run over an empty list.

set -euo pipefail

readonly LEGS="desktop test android ios"

usage() {
  echo "usage: scripts/ci-projects.sh <leg>    leg = desktop | test | android | ios | all" >&2
}

if [ "$#" -ne 1 ]; then
  usage
  exit 2
fi

requested="$1"

case " $LEGS all " in
  *" $requested "*) ;;
  *) echo "error: unknown leg '$requested'" >&2; usage; exit 2 ;;
esac

# The repository root is derived from this script's own location, so it works from any working
# directory -- CI runs it from the checkout root, a developer may not.
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
solution="$root/Pine.slnx"

if [ ! -f "$solution" ]; then
  echo "error: $solution does not exist" >&2
  exit 1
fi

# Pine.slnx is XML. `dotnet sln Pine.slnx list` would also answer this, but its output is localised
# -- it prints a banner in the SDK's display language, "Proyectos" on the machine this was written
# on -- and it emits the host's directory separator. So the file is read directly. One
# `Path="..."` attribute per project is the whole of the schema this solution uses.
projects="$(
  grep -o 'Path="[^"]*"' "$solution" \
    | sed -e 's/^Path="//' -e 's/"$//' \
    | tr '\\' '/' \
    | sort
)"

if [ -z "$projects" ]; then
  echo "error: no <Project Path=\"...\"/> entries found in $solution" >&2
  exit 1
fi

emitted=0

emit() {
  # $1 = the leg this project belongs to, $2 = the project path
  if [ "$requested" = "all" ] || [ "$requested" = "$1" ]; then
    echo "$2"
    emitted=$((emitted + 1))
  fi
}

while IFS= read -r project; do
  [ -n "$project" ] || continue

  absolute="$root/$project"

  if [ ! -f "$absolute" ]; then
    echo "error: $solution names $project, which does not exist on disk" >&2
    exit 1
  fi

  # Both spellings are read, because either is legal and a project that grows a second TFM must not
  # fall through to the desktop leg unnoticed. Line breaks are squashed first so that an element
  # split across lines still matches.
  #
  # XML COMMENTS ARE REMOVED BEFORE ANYTHING IS MATCHED, and that is not tidiness. An adversarial
  # pass found that a csproj carrying
  #     <!-- <TargetFramework>net10.0-ios</TargetFramework> -->
  #     <TargetFramework>net10.0</TargetFramework>
  # was classified from the COMMENTED line: a desktop project routed to the iOS leg, or an
  # unbuildable one hard-erroring, on the strength of text the compiler never reads. A commented-out
  # TFM above the real one is exactly what a developer leaves behind while changing targets.
  #
  # `|| true` is load-bearing, and was added after a falsification run. Under `set -euo pipefail` a
  # `grep` that matches nothing fails the whole substitution, so a project with no TFM element at
  # all killed the script with a bare exit 1 and printed nothing -- the silent failure this file
  # exists to prevent, reproduced inside the file itself. The message below is what should happen.
  declarations="$(
    tr -d '\r\n' < "$absolute" \
      | sed -e 's/<!--[^>]*-->//g' -e 's/<!--.*-->//g' \
      | grep -o '<TargetFrameworks\?>[^<]*<' \
      | sed -e 's/^<TargetFrameworks\?>//' -e 's/<$//' || true
  )"

  if [ -z "$declarations" ]; then
    echo "error: $project declares no <TargetFramework> or <TargetFrameworks>; CI cannot place it" >&2
    exit 1
  fi

  # MORE THAN ONE SURVIVING DECLARATION IS REFUSED, NOT RESOLVED. A project can legally carry two
  # <TargetFramework> elements in PropertyGroups with different Conditions, and which one applies
  # then depends on the runner. Taking the first -- which this script used to do -- reads whichever
  # the developer happened to type first and calls it the answer. Deciding it properly means
  # evaluating MSBuild conditions, which a shell script must not pretend to do, so an ambiguous
  # project stops the pipeline and asks for a straight answer instead of guessing one.
  if [ "$(echo "$declarations" | wc -l)" -gt 1 ]; then
    echo "error: $project declares more than one target framework element ($(echo "$declarations" | tr '\n' ' ')). Which one applies depends on MSBuild conditions this script cannot evaluate, so it will not choose. Give the project one unconditional <TargetFramework(s)>, or teach this script the condition deliberately" >&2
    exit 1
  fi

  frameworks="$declarations"

  leg=""

  # Rule 1. A platform suffix decides, and a suffix with no leg is a hard error rather than a
  # default: `net10.0` has no suffix, `net10.0-windows10.0.19041.0` would, and guessing would put
  # that project on a runner that cannot build it.
  case "$frameworks" in
    *-android*) leg="android" ;;
    *-ios*)     leg="ios" ;;
    net[0-9]*-*)
      echo "error: $project targets '$frameworks', a platform suffix this pipeline has no leg for. Add a leg to the workflow and to this script, or the project is built by nothing" >&2
      exit 1
      ;;
  esac

  # Rule 2. What makes `dotnet test` work is the test SDK, so that is what is read.
  if [ -z "$leg" ] && grep -q 'Include="Microsoft.NET.Test.Sdk"' "$absolute"; then
    leg="test"
  fi

  # Rule 3.
  if [ -z "$leg" ]; then
    leg="desktop"
  fi

  emit "$leg" "$project"
done <<< "$projects"

# A here-string rather than a pipe feeds the loop above, so this count is the loop's own and not a
# subshell's. An empty leg is an error for the reason given in USAGE: the job would still run, still
# pass, and still report a green tick over nothing built.
if [ "$emitted" -eq 0 ]; then
  echo "error: leg '$requested' matches no project in $solution. If that head or suite was deleted, delete its job from .github/workflows/dotnet-ci.yml as well -- a job that builds nothing reports green" >&2
  exit 1
fi
