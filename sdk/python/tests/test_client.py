import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from mux_sdk import Mux, MuxOptions, build_print_args  # noqa: E402

FAKE_MUX = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fake_mux.py")


def make_client(argv_file, **extra):
    env = {"FAKE_MUX_ARGV_FILE": argv_file}
    env.update(extra.pop("env", {}))
    options = MuxOptions(mux_path=sys.executable, mux_args=[FAKE_MUX], env=env, **extra)
    return Mux(options)


def read_argv_lines(argv_file):
    with open(argv_file, "r", encoding="utf-8") as handle:
        return [json.loads(line) for line in handle if line.strip()]


class BuildArgsTests(unittest.TestCase):
    def test_constructs_expected_flags(self):
        args = build_print_args(
            MuxOptions(
                endpoint="prod",
                yolo=True,
                sandbox="read-only",
                deny_tools=["delete_file", "run_process"],
                add_dir=["../a", "../b"],
                max_turns=3,
            ),
            "sess-1",
            "do the thing",
        )
        self.assertEqual(args[:3], ["print", "--output-format", "jsonl"])
        self.assertIn("--yolo", args)
        self.assertNotIn("--approval-policy", args)
        self.assertEqual(args[args.index("--sandbox") + 1], "read-only")
        self.assertEqual(args[args.index("--deny-tools") + 1], "delete_file,run_process")
        self.assertEqual(args[args.index("--session-id") + 1], "sess-1")
        self.assertEqual(args.count("--add-dir"), 2)
        self.assertEqual(args[-1], "do the thing")

    def test_approval_policy_used_without_yolo(self):
        args = build_print_args(MuxOptions(approval_policy="deny"), None, "p")
        self.assertEqual(args[args.index("--approval-policy") + 1], "deny")
        self.assertNotIn("--session-id", args)


class RunTests(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.mkdtemp(prefix="mux-py-sdk-")
        self.argv_file = os.path.join(self._dir, "argv.log")

    def test_run_aggregates_result(self):
        client = make_client(self.argv_file)
        result = client.run("hi")
        self.assertEqual(result.exit_code, 0)
        self.assertEqual(result.text, "hello world")
        self.assertEqual(result.status, "completed")
        self.assertEqual(result.session_id, "gen-sid")
        self.assertEqual(result.final_estimated_tokens, 42)
        self.assertTrue(any(e["eventType"] == "run_completed" for e in result.events))

    def test_run_streamed_order(self):
        client = make_client(self.argv_file)
        kinds = [e["eventType"] for e in client.run_streamed("hi")]
        self.assertEqual(kinds[0], "run_started")
        self.assertEqual(kinds[-1], "run_completed")
        self.assertIn("assistant_text", kinds)

    def test_non_zero_exit_surfaces_error(self):
        client = make_client(self.argv_file, env={"FAKE_MUX_EXIT": "2"})
        result = client.run("hi")
        self.assertEqual(result.exit_code, 2)
        self.assertTrue(any(e["eventType"] == "error" for e in result.events))

    def test_governance_flags_passed_through(self):
        client = make_client(self.argv_file, yolo=True, sandbox="workspace-write", deny_tools=["run_process"])
        client.run("hi")
        argv = read_argv_lines(self.argv_file)[0]
        self.assertIn("--yolo", argv)
        self.assertEqual(argv[argv.index("--sandbox") + 1], "workspace-write")
        self.assertEqual(argv[argv.index("--deny-tools") + 1], "run_process")

    def test_thread_reuses_session_id(self):
        client = make_client(self.argv_file)
        thread = client.start_thread("thread-123")
        self.assertEqual(thread.id, "thread-123")
        thread.run("turn one")
        thread.run("turn two")
        argv_lines = read_argv_lines(self.argv_file)
        self.assertEqual(len(argv_lines), 2)
        for argv in argv_lines:
            self.assertEqual(argv[argv.index("--session-id") + 1], "thread-123")

    def test_start_thread_generates_id(self):
        client = make_client(self.argv_file)
        thread = client.start_thread()
        self.assertTrue(len(thread.id) > 0)


if __name__ == "__main__":
    unittest.main()
