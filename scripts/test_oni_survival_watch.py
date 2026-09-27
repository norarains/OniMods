"""Host checks for the bounded helper's call count and stop behavior."""
import importlib.util
import json
from pathlib import Path
import sys
import unittest

SKILL_SCRIPTS = Path(__file__).resolve().parents[1] / ".agents/skills/oni-mcp-autonomous-iteration/scripts"
sys.path.insert(0, str(SKILL_SCRIPTS))
spec = importlib.util.spec_from_file_location("bounded_survival_watch", SKILL_SCRIPTS / "survival_watch.py")
watch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(watch)


class FakeBridge:
    def __init__(self, results):
        self.results = iter(results)
        self.calls = []

    def request(self, message):
        self.calls.append(message["params"])
        result = next(self.results)
        if isinstance(result, Exception):
            raise result
        return [{"result": {"content": [{"type": "text", "text": json.dumps(result)}]}}]


class BoundedWatchTests(unittest.TestCase):
    def test_healthy_windows_need_only_continue_calls(self):
        bridge = FakeBridge([{"schemaVersion": 3, "stopReason": "duration_elapsed", "isPaused": True, "gameSecondsAdvanced": 30}] * 2)
        self.assertEqual(watch.run_windows(bridge, .1, 60, 15, 3, emit=lambda _: None), 0)
        self.assertEqual(len(bridge.calls), 2)
        self.assertTrue(all(c["name"] == "game_control" and c["arguments"]["action"] == "continue" for c in bridge.calls))
        self.assertTrue(all("resetMonitor" not in c["arguments"] for c in bridge.calls))

    def test_event_is_not_ignored_or_retried(self):
        bridge = FakeBridge([{"schemaVersion": 3, "stopReason": "event", "isPaused": True}])
        self.assertEqual(watch.run_windows(bridge, 100, 60, 15, 3, emit=lambda _: None), 2)
        self.assertEqual(len(bridge.calls), 1)
        self.assertNotIn("ignoreEvents", bridge.calls[0]["arguments"])

    def test_ignored_event_does_not_override_deadline(self):
        bridge = FakeBridge([{"schemaVersion": 3, "stopReason": "duration_elapsed", "isPaused": True,
                             "events": [{"code": "printing_pod_ready", "ignored": True}], "gameSecondsAdvanced": 60}])
        self.assertEqual(watch.run_windows(bridge, .1, 60, 15, 3, emit=lambda _: None), 0)

    def test_old_contract_fails_closed_without_acknowledgement(self):
        for result in ({"schemaVersion": 2, "recommendedAction": "continue", "isPaused": True},
                       {"decision": "continue", "isPaused": True}):
            bridge = FakeBridge([result])
            self.assertEqual(watch.run_windows(bridge, 1, 60, 15, 3, emit=lambda _: None), 1)
            self.assertEqual(len(bridge.calls), 1)

    def test_timeout_does_not_replay_an_advance(self):
        bridge = FakeBridge([TimeoutError("lost result")])
        with self.assertRaises(TimeoutError):
            watch.run_windows(bridge, 1, 60, 15, 3, emit=lambda _: None)
        self.assertEqual(len(bridge.calls), 1)

    def test_missing_pause_or_contract_stops(self):
        bridge = FakeBridge([{"decision": "continue", "isPaused": False}])
        with self.assertRaises(RuntimeError):
            watch.run_windows(bridge, 1, 60, 15, 3, emit=lambda _: None)
        bridge = FakeBridge([{"isPaused": True}])
        self.assertEqual(watch.run_windows(bridge, 1, 60, 15, 3, emit=lambda _: None), 1)


if __name__ == "__main__":
    unittest.main()
