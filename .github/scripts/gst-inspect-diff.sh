#!/usr/bin/env bash
#
# Diffs the page samples/GstInspect prints against the page the real
# gst-inspect-1.0 of the same installation prints, for four elements. Run from
# the root of the checkout, after the solution has been built: the sample is
# started with --no-build.
#
# The gate the sample exists for: the page it prints has to be the page the
# real tool prints, byte for byte. Both read the same installation, so the
# plugin filename and the version line agree by construction and nothing is
# normalised away except the line ending, which the Windows leg needs and which
# costs nothing elsewhere. LC_ALL=C is what keeps the translated strings of the
# C tool -- "readable", "writable" -- in the English catalogue the port writes,
# and GST_DEBUG is cleared so that a debug line of the library cannot land in
# the middle of the page. Only stdout is compared: the C tool writes a
# GST_ERROR to stderr for a property of type long, and that is not part of the
# page.
#
# The four elements are four shapes of page: fakesink has signals, an
# enumeration table and a structure valued property whose fields are expanded,
# identity has a float range and a flags table, videotestsrc has several
# enumeration tables and the widest property listing of the four, and
# capsfilter has a caps valued property. All four come from the core and base
# plugins every leg installs.
#
# One script rather than three copies of it in ci.yml, so that the version
# guard below is written once.

# The runners give a plain "bash -e" on Linux and macOS, which lets a failing
# gst-inspect-1.0 hide behind the pipe into tr and turns the diff into a
# comparison of two error messages.
set -euo pipefail

unset GST_DEBUG
export LC_ALL=C

# The port reproduces the page format of the current C tool, and that format is
# younger than every gst-inspect-1.0 in the field: the "Element Flags:" section
# arrived in 1.26.0, the " (type)" suffix on every caps field in 1.28.0, and
# "string" in place of "gchararray" in 1.28.3. Against an older tool all four
# diffs report a difference that is the tool's age rather than a defect of the
# port, so such a leg is skipped with a warning. The test is the C tool's own
# --atleast-version, which exists in every version that could be installed here
# and needs neither a version parser nor the CR stripping below.
gst-inspect-1.0 --exists --atleast-version=1.28.3 fakesink || { echo "::warning::gst-inspect-1.0 predates the 1.28.3 page format, skipping"; exit 0; }

# Out of the checkout, so that the pages cannot show up in the git status a
# later step reads.
pages=$(mktemp -d)
status=0

for element in fakesink identity videotestsrc capsfilter; do
  gst-inspect-1.0 --no-colors "$element" | tr -d '\r' > "$pages/expected-$element.txt"
  dotnet run --project samples/GstInspect --no-restore --no-build -- "$element" \
    | tr -d '\r' > "$pages/actual-$element.txt"
  if ! diff -u "$pages/expected-$element.txt" "$pages/actual-$element.txt"; then
    echo "::error::gst-inspect-1.0 and samples/GstInspect disagree about $element"
    status=1
  fi
done

exit $status
