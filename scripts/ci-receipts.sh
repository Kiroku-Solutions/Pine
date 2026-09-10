#!/usr/bin/env bash
#
# Proves that a gate RAN, rather than that it exists (plan item 0.3).
#
# THE FAILURE THIS EXISTS TO CATCH.
#   Every other guard in this repository asserts that a gate is *present*: the coverage floor is
#   invoked by a `run:` line, the triggers are armed, the legs cover the solution. None of them can
#   tell you that anything happened during a particular run. A step carrying `if:` that evaluates
#   false is SKIPPED, and a skipped step does not fail its job -- the run reports green with the
#   gate never having executed. So does a loop that iterates zero times, and so did every build loop
#   in this workflow until item 0.2's own falsification pass found it. A workflow that runs and
#   skips its gate is worse than one that never runs, because the tick is evidence to a reader.
#
# HOW IT WORKS.
#   A gate calls `record` at the moment it has actually succeeded -- after the command it guards,
#   never before, so `set -e` makes the ordering load-bearing. At the end of the job an unconditional
#   step calls `verify` with the receipts that job was supposed to produce, computed independently of
#   the conditions on the steps themselves. If the two disagree, the job fails and names what did not
#   run.
#
#   The independence is the point. The coverage floor runs `if: matrix.os == 'ubuntu-latest'`; the
#   verify step states, separately, that on ubuntu the `coverage` receipt must exist. Deleting the
#   gate, neutering its `run:` line, or changing its condition breaks the agreement between two
#   statements that a mutation has to edit both of.
#
# ONE LOG FILE, EXACT LINES, AND WHY NOT ONE FILE PER RECEIPT.
#   The first version wrote a file per receipt, with `/` and `:` folded to `_` to make the name safe
#   as a filename. An adversarial pass found that this made the mapping NON-INJECTIVE: `gate/a` and
#   `gate:a` became the same file, so a receipt recorded by one gate satisfied a verify for another.
#   A guard whose identity comparison can be fooled is not a guard. Receipts are now appended as
#   whole lines to one log and matched with `grep -Fxq` -- fixed strings, whole line, no pattern
#   interpretation -- so two names are the same receipt if and only if they are the same text.
#
# WHY A FILE AND NOT `steps.<id>.outcome`.
#   GitHub can report a step's outcome, and that would be a shorter spelling for the skipped-step
#   half of this. It cannot see inside a step: a loop that ran zero times, a script that returned
#   before comparing anything, or a gate whose `run:` was replaced by `echo skipped` all have outcome
#   `success`. A receipt is written by the gate itself, at the point of success, which is the only
#   place that knows.
#
# A RECEIPT IS ONLY MEANINGFUL WITHIN THE JOB THAT RESET THE LOG.
#   `.ci-receipts/` lives in the workspace and is git-ignored. Every job in the workflow resets it
#   immediately after checkout, so a receipt can only come from that job -- the GitHub-hosted runners
#   this workflow uses are ephemeral, but relying on that would be relying on a fact about someone
#   else's infrastructure rather than on something this repository states. `verify` also CONSUMES the
#   log once it has passed, which closes the developer case: running a gate locally, then running
#   `verify` again after changing something, cannot pass on yesterday's receipt.
#
# USAGE
#   scripts/ci-receipts.sh record <name>        Record that <name> ran and succeeded.
#   scripts/ci-receipts.sh verify <name>...     Fail unless every <name> was recorded. Consumes the
#                                               log on success.
#   scripts/ci-receipts.sh list                 Print what has been recorded, sorted.
#   scripts/ci-receipts.sh reset                Discard every receipt.
#
#   <name> is a short slug: letters, digits, and `.` `_` `-` `:` `/`. A name naming a project keeps
#   the project's path, so a missing one says which project was not built.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
receipts="$root/.ci-receipts"
log="$receipts/recorded"

usage() {
  echo "usage: scripts/ci-receipts.sh record <name> | verify <name>... | list | reset" >&2
}

if [ "$#" -lt 1 ]; then
  usage
  exit 2
fi

command="$1"
shift

# A receipt name is compared as text, so the only thing that has to be refused is a name that could
# not survive being written and read back as one line: whitespace, and anything outside the slug set.
require_valid_name() {
  case "$1" in
    ''|*[!A-Za-z0-9._:/-]*)
      echo "error: '$1' is not a valid receipt name (letters, digits, . _ - : / only, no spaces)" >&2
      exit 2
      ;;
  esac
}

case "$command" in
  record)
    if [ "$#" -ne 1 ]; then
      usage
      exit 2
    fi

    require_valid_name "$1"
    mkdir -p "$receipts"
    echo "$1" >> "$log"
    echo "receipt: $1"
    ;;

  verify)
    if [ "$#" -lt 1 ]; then
      echo "error: verify needs at least one receipt name. A verify step that checks nothing is the" >&2
      echo "thing this script exists to prevent, so it is refused rather than passed." >&2
      exit 2
    fi

    missing=""

    for name in "$@"; do
      require_valid_name "$name"

      # -F: fixed string, not a pattern. -x: the whole line must match, so `build:src/A` cannot be
      # satisfied by a recorded `build:src/AB`.
      if [ ! -f "$log" ] || ! grep -Fxq -- "$name" "$log"; then
        missing="$missing $name"
      fi
    done

    if [ -n "$missing" ]; then
      echo "::error::These gates did not run:$missing" >&2
      echo "" >&2
      echo "A step that is skipped, or a loop that iterates zero times, does not fail its job -- the" >&2
      echo "run would otherwise have reported success with the gate never having executed. Recorded" >&2
      echo "receipts were:" >&2

      if [ -s "$log" ]; then
        sort "$log" >&2
      else
        echo "  (none at all)" >&2
      fi

      exit 1
    fi

    echo "All $# gate(s) ran:$(printf ' %s' "$@")"

    # Consumed on success, so a later verify in a later run cannot be satisfied by this one's work.
    rm -rf "$receipts"
    ;;

  list)
    if [ -s "$log" ]; then
      sort "$log"
    fi
    ;;

  reset)
    rm -rf "$receipts"
    ;;

  *)
    echo "error: unknown command '$command'" >&2
    usage
    exit 2
    ;;
esac
