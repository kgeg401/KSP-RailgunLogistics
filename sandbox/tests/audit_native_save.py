"""Read-only check of two actual KSP vessel snapshots against their persistent fleet ledger."""
from pathlib import Path
import json
import re
import sys
import xml.etree.ElementTree as ET

save = Path(sys.argv[1])
fleet = ET.parse(save / 'RailgunReusableFleet.xml').getroot()
text = (save / 'persistent.sfs').read_text(encoding='utf-8-sig')
lines = text.splitlines()

def blocks(kind, source):
    for start, line in enumerate(source):
        if line.strip() != kind or start + 1 >= len(source) or source[start+1].strip() != '{':
            continue
        depth, end = 1, start + 2
        while depth:
            depth += source[end].count('{') - source[end].count('}')
            end += 1
        yield source[start+2:end-1]

vessels = list(blocks('VESSEL', lines))
records = fleet.findall('Pods/PodRecord')
assert len(records) == 2
report = {'twoPodLimit': True, 'nativePods': [], 'fullRoundTripVerified': False}
for record in records:
    number = int(record.findtext('Number'))
    identity = record.findtext('VesselId')
    matches = [v for v in vessels if any(l.strip() == 'fleetId = ' + identity for l in v)]
    assert len(matches) == 1, (number, 'Missing or duplicated physical pod')
    vessel = matches[0]
    parts = list(blocks('PART', vessel))
    expected_ids = {int(n.text) for n in record.findall('PartIds/unsignedInt')}
    actual_ids = {int(next(l.split('=', 1)[1].strip() for l in p if l.strip().startswith('uid = '))) for p in parts}
    assert len(parts) == int(record.findtext('OriginalParts')) == 25
    assert actual_ids == expected_ids
    assert record.findtext('Created') == 'true'
    assert not any('name = RailgunCargoPod' in l for p in parts for l in p), 'Legacy procedural pod must not be used'
    resources = []
    for part in parts:
        name = next(l.split('=', 1)[1].strip() for l in part if l.strip().startswith('name = '))
        if name in {'krlReusableMonoCargo', 'krlReusableFuelCargo'}:
            for resource in blocks('RESOURCE', part):
                props = dict(l.strip().split(' = ', 1) for l in resource if ' = ' in l)
                assert props['flowState'] == 'False'
                resources.append({'resource': props['name'], 'amount': float(props['amount']), 'capacity': float(props['maxAmount'])})
    assert {r['resource'] for r in resources} == {'MonoPropellant', 'LiquidFuel', 'Oxidizer'}
    report['nativePods'].append({'number': number, 'identity': identity, 'nativeParts': len(parts), 'partIdsMatchLedger': True,
        'phase': record.findtext('Phase'), 'cargoTanks': resources})
print(json.dumps(report, indent=2))
