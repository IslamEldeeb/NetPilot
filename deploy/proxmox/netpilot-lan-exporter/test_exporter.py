import json
import unittest
from exporter import authorized, extract_flows, generate_rules

# Representative nft -j output; not captured from the user's unverified Proxmox host.
SAMPLE = '''{"nftables":[{"metainfo":{"json_schema_version":1}},
{"table":{"family":"bridge","name":"netpilot_lan","handle":1}},
{"set":{"family":"bridge","table":"netpilot_lan","name":"flows","type":["ipv4_addr","ipv4_addr"],
"elem":[{"elem":{"val":{"concat":["192.168.1.20","192.168.1.50"]},"counter":{"packets":20,"bytes":123456}}},
{"elem":{"val":{"concat":["192.168.1.50","192.168.1.20"]},"counter":{"packets":2,"bytes":500}}}]}},
{"rule":{"family":"bridge","table":"netpilot_lan","chain":"forward","expr":[]}}]}'''



class ExporterTests(unittest.TestCase):
    def test_directional_counters_and_unrelated_metadata(self):
        self.assertEqual(extract_flows(SAMPLE), [
            {"src": "192.168.1.20", "dst": "192.168.1.50", "bytes": 123456},
            {"src": "192.168.1.50", "dst": "192.168.1.20", "bytes": 500}])

    def test_malformed_or_incomplete_counters_fail_instead_of_returning_zero(self):
        for raw in ("{", "[]", '{}', '{"nftables":[]}',
                    SAMPLE.replace('"bytes":123456', '"bytes":-1'),
                    SAMPLE.replace('"bytes":123456', '"bytes":"100"'),
                    SAMPLE.replace('192.168.1.20', 'invalid'),
                    SAMPLE.replace('"counter":{"packets":20,"bytes":123456}', '"other":{}')):
            with self.subTest(raw=raw), self.assertRaises((ValueError, TypeError, KeyError)):
                extract_flows(raw)

    def test_duplicate_flow_rejected(self):
        payload = json.loads(SAMPLE)
        elements = payload["nftables"][2]["set"]["elem"]
        elements.append(elements[0])
        with self.assertRaises(ValueError):
            extract_flows(json.dumps(payload))

    def test_token_optional_but_exact_when_configured(self):
        for header, token, expected in [(None, "", True), (None, "secret", False),
                                        ("Bearer secret", "secret", True), ("Bearer wrong", "secret", False),
                                        ("secret", "secret", False), ("Bearer sécret", "secret", False)]:
            with self.subTest(header=header):
                self.assertEqual(authorized(header, token), expected)

    def test_rules_count_each_pair_in_both_directions_without_other_table_mutations(self):
        config = {"serverIp": "192.168.1.20", "serverMac": "02:00:00:00:00:20",
                  "clients": ["192.168.1.50", "192.168.1.50", "192.168.1.20"]}
        rules = generate_rules(config)
        self.assertEqual(rules.count("@flows"), 2)
        self.assertEqual(rules.count("192.168.1.20 . 192.168.1.50"), 1)
        self.assertIn('ether saddr 02:00:00:00:00:20 ip saddr . ip daddr @flows', rules)
        self.assertIn('ether daddr 02:00:00:00:00:20 ip saddr . ip daddr @flows', rules)
        self.assertIn('meta ibrname "vmbr0"', rules)
        self.assertTrue(rules.startswith("add table bridge netpilot_lan\ndelete table bridge netpilot_lan\n"))
        self.assertNotIn("flush ruleset", rules)
        config.pop("clients")
        config["clientCidr"] = "192.168.1.48/30"
        self.assertEqual(generate_rules(config).count("@flows"), 2)
        self.assertIn("192.168.1.20 . 192.168.1.49", generate_rules(config))
        self.assertIn("192.168.1.50 . 192.168.1.20", generate_rules(config))

    def test_rules_reject_injection_oversized_cidr_and_ambiguous_config(self):
        base = {"serverIp": "192.168.1.20", "serverMac": "02:00:00:00:00:20", "clients": ["192.168.1.50"]}
        for overrides in ({"bridge": 'vmbr0"; flush ruleset'}, {"serverMac": 'bad;flush ruleset'},
                          {"clients": ["8.8.8.8"]}, {"clientCidr": "192.168.0.0/16", "clients": []},
                          {"clientCidr": "192.168.1.0/24"}):
            with self.subTest(overrides=overrides), self.assertRaises(ValueError):
                generate_rules(base | overrides)


if __name__ == "__main__":
    unittest.main()
