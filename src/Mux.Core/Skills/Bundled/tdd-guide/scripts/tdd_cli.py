#!/usr/bin/env python3
"""tdd_cli.py: command-line entry point for the tdd-guide modules.

Added for mux. The other files in this folder are importable modules with no command-line interface of their own;
this script exposes the parts that work from a terminal. Standard library only. Prints JSON (or text with --text).

Subcommands:
  coverage  --report FILE [--format lcov|json|xml|cobertura] [--threshold 80]
  detect    --file FILE
  quality   --file TEST_FILE
  fixtures  --type int|string|array|date|... [--min N] [--max N]
  guidance  [--phase red|green|refactor]

Exit codes: 0 success, 1 coverage below the threshold, 2 invalid input.
"""

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from coverage_analyzer import CoverageAnalyzer  # noqa: E402
from fixture_generator import FixtureGenerator  # noqa: E402
from format_detector import FormatDetector  # noqa: E402
from metrics_calculator import MetricsCalculator  # noqa: E402
from tdd_workflow import TDDPhase, TDDWorkflow  # noqa: E402


def _read(path):
    if not os.path.isfile(path):
        print("error: file not found: " + path, file=sys.stderr)
        sys.exit(2)
    with open(path, encoding="utf-8", errors="replace") as handle:
        return handle.read()


def _emit(data, as_text):
    if as_text and isinstance(data, dict):
        for key, value in data.items():
            print(f"{key}: {value}")
    else:
        print(json.dumps(data, indent=2, default=str))


def cmd_coverage(args):
    content = _read(args.report)
    analyzer = CoverageAnalyzer()
    fmt = args.format or analyzer.detect_format(content)
    analyzer.parse_coverage_report(content, fmt)
    summary = analyzer.calculate_summary()
    gaps = analyzer.identify_gaps(args.threshold)
    _emit({"format": fmt, "summary": summary, "gaps": gaps, "recommendations": analyzer.generate_recommendations()}, args.text)
    line_rate = summary.get("line_coverage", summary.get("lines", 100))
    try:
        return 1 if float(line_rate) < args.threshold else 0
    except (TypeError, ValueError):
        return 0


def cmd_detect(args):
    code = _read(args.file)
    detector = FormatDetector()
    framework = detector.detect_test_framework(code)
    _emit({
        "file": args.file,
        "language": detector.detect_language(code),
        "test_framework": framework,
        "suggested_test_file": detector.suggest_test_file_name(args.file, framework),
        "test_patterns": detector.identify_test_patterns(code),
    }, args.text)
    return 0


def cmd_quality(args):
    _emit(MetricsCalculator().calculate_test_quality(_read(args.file)), args.text)
    return 0


def cmd_fixtures(args):
    constraints = {}
    if args.min is not None:
        constraints["min"] = args.min
    if args.max is not None:
        constraints["max"] = args.max
    _emit({"type": args.type, "boundary_values": FixtureGenerator().generate_boundary_values(args.type, constraints or None)}, args.text)
    return 0


def cmd_guidance(args):
    phase = None
    if args.phase:
        phase = {"red": TDDPhase.RED, "green": TDDPhase.GREEN, "refactor": TDDPhase.REFACTOR}[args.phase]
    _emit(TDDWorkflow().get_phase_guidance(phase), args.text)
    return 0


def main():
    parser = argparse.ArgumentParser(description="TDD helpers: coverage gaps, framework detection, test quality, fixtures, and phase guidance.")
    parser.add_argument("--text", action="store_true", help="print key: value text instead of JSON")
    sub = parser.add_subparsers(dest="command", required=True)

    coverage = sub.add_parser("coverage", help="summarize a coverage report and list files below the threshold")
    coverage.add_argument("--report", required=True)
    coverage.add_argument("--format", choices=["lcov", "json", "xml", "cobertura"])
    coverage.add_argument("--threshold", type=float, default=80.0)
    coverage.set_defaults(func=cmd_coverage)

    detect = sub.add_parser("detect", help="detect the language and test framework of a file")
    detect.add_argument("--file", required=True)
    detect.set_defaults(func=cmd_detect)

    quality = sub.add_parser("quality", help="score the quality of a test file")
    quality.add_argument("--file", required=True)
    quality.set_defaults(func=cmd_quality)

    fixtures = sub.add_parser("fixtures", help="generate boundary values for a data type")
    fixtures.add_argument("--type", required=True)
    fixtures.add_argument("--min", type=float)
    fixtures.add_argument("--max", type=float)
    fixtures.set_defaults(func=cmd_fixtures)

    guidance = sub.add_parser("guidance", help="print guidance for a red, green, or refactor phase")
    guidance.add_argument("--phase", choices=["red", "green", "refactor"])
    guidance.set_defaults(func=cmd_guidance)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
