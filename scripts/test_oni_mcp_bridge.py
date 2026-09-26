"""Offline transport regressions: python3 -m unittest discover -s scripts -p 'test_oni_mcp_bridge.py'."""
import json
import os
import unittest
from unittest.mock import patch

import oni_mcp_bridge as bridge


class FakeTransport:
    def __init__(self):
        self.calls = []
        self.responses = []

    def post(self, url, body, headers, timeout):
        self.calls.append((url, json.loads(body), headers))
        return self.responses.pop(0)

    def close(self):
        pass


class TransportTests(unittest.TestCase):
    def setUp(self):
        self.client = bridge.Bridge('http://localhost:8788/mcp/', backend='direct')
        self.fake = FakeTransport()
        self.client.transport = self.fake

    def test_session_utf8_and_capability_negotiation(self):
        self.fake.responses = [dict(status=200, session='local-session', body=json.dumps({
            'jsonrpc': '2.0', 'id': 1, 'result': {'protocolVersion': '2025-03-26',
            'serverInfo': {'name': '流星'}, 'capabilities': {'experimental': {'push': True},
            'tools': {'listChanged': True}, 'resources': {'subscribe': True}}}})),
            dict(status=202, body=''), dict(status=200, body='{"id":2,"result":{"name":"湿围星"}}')]
        with patch.dict(os.environ, {'ONI_MCP_TOKEN': 'test-token'}):
            first = self.client.request({'id': 1, 'method': 'initialize', 'params': {'capabilities': {'sampling': {}}}})
            self.assertEqual(first[0]['result']['serverInfo']['name'], '流星')
            self.assertEqual(first[0]['result']['capabilities'], {'tools': {}, 'resources': {}})
            self.assertEqual(self.fake.calls[0][1]['params']['capabilities'], {})
            self.assertEqual(self.client.request({'method': 'notifications/initialized'}), [])
            last = self.client.request({'id': 2, 'method': 'tools/call', 'params': {'name': '湿围星'}})
            self.assertEqual(last[0]['result']['name'], '湿围星')
            self.assertEqual(self.fake.calls[2][2]['Mcp-Session-Id'], 'local-session')
            self.assertEqual(self.fake.calls[2][2]['Mcp-Protocol-Version'], '2025-03-26')
            self.assertEqual(self.fake.calls[2][2]['Authorization'], 'Bearer test-token')

    def test_never_replays_failed_mutation(self):
        self.fake.responses = [dict(status=503, body='secret server detail')]
        with self.assertRaisesRegex(RuntimeError, 'not replayed') as error:
            self.client.request({'id': 3, 'method': 'tools/call', 'params': {'confirm': True}})
        self.assertNotIn('secret', str(error.exception))
        self.assertEqual(len(self.fake.calls), 1)

    def test_sse_and_empty_notifications(self):
        self.assertEqual(bridge.parse_messages(''), [])
        self.assertEqual(bridge.parse_messages(':ping\r\ndata: {"id": 1,\r\ndata: "result": "梯子"}\r\n\r\n'),
                         [{'id': 1, 'result': '梯子'}])
        with self.assertRaises(ValueError):
            bridge.parse_messages('<html>Wrong endpoint</html>')

    def test_local_endpoint_only(self):
        for url in ('http://localhost:8788/mcp/', 'http://127.0.0.1:8788/mcp/', 'http://[::1]:8788/mcp/'):
            self.assertEqual(bridge.validate_url(url), url)
        for url in ('https://example.com/mcp', 'http://0.0.0.0:8788/mcp',
                    'http://token@localhost:8788/mcp', 'http://localhost:8788/mcp?token=secret'):
            with self.assertRaises(ValueError):
                bridge.validate_url(url)

    def test_direct_transport_disables_proxies_and_redirects(self):
        import urllib.request
        transport = bridge.DirectTransport()
        self.assertFalse(any(isinstance(h, urllib.request.ProxyHandler) and h.proxies for h in transport.opener.handlers))
        redirect = next(h for h in transport.opener.handlers if isinstance(h, urllib.request.HTTPRedirectHandler))
        self.assertIsNone(redirect.redirect_request(None, None, None, None, None, None))


if __name__ == '__main__':
    unittest.main()
