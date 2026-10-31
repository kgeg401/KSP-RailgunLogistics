"""Synthetic reader tests only; these records are never native flight evidence."""
import copy
import unittest
import tempfile
import csv,hashlib,json
from pathlib import Path
from audit_coordinated_warp import review,audit

def trace_pair():
    traces={}
    for role in ('A','B'):
        rows=[]
        for index in range(8):
            stages=['Idle','Preparing','Warping','Warping','Warping' if role=='A' else 'Synchronizing','Synchronizing','Synchronizing','Idle']
            stage=stages[index];accelerated=role=='A' and index in (3,4)
            ut=([100,100.1,100.2,110.2,120.2,120.3,120.4,120.5] if role=='A' else [100,100.1,100.2,100.3,100.4,120.3,120.4,120.5])[index]
            rows.append({
                'real':str(index*.1),'ut':str(ut),'utcTicks':str(638000000000000000+index*1000000),
                'player':'RG-Test-'+role,'epoch':'unit-epoch','session':'unit-session' if stage!='Idle' else '',
                'stage':stage,'leader':'RG-Test-A','pod':'1','members':'RG-Test-A;RG-Test-B',
                'sourceSubspace':'0','destination':'2' if stage=='Synchronizing' else '-1',
                'nativeSubspace':'2' if index>=5 else '-1' if accelerated else '0',
                'warp':'100' if accelerated else '1','dt':'.02','enabled':'True','holding':'False' if stage=='Idle' else 'True',
                'phase':'Rendezvous','activeVessel':'unit-vessel-'+role,'landed':'True' if role=='B' else 'False',
                'altitude':'150000','periapsis':'100000','atmosphere':'70000','surfaceSpeed':'0',
                'thrustKN':'0','throttle':'0','x':'0','y':'0','z':'0','nodeTriggered':'False','deorbitTriggered':'False',
                'stopUt':'150','infinitePropellant':'False','infiniteElectricity':'False','ignoreTemperature':'False',
                'unbreakableJoints':'False','noCrashDamage':'False','warpMode':'HIGH'})
        traces[role]=rows
    return traces

class CoordinatedWarpAuditTests(unittest.TestCase):
    def rejects(self,change):
        traces=trace_pair();change(traces);result=review(traces)
        self.assertFalse(result['success'],result)
        self.assertFalse(result['fullLogisticsRoundTrip'])

    def test_complete_paired_coast_is_only_warp_evidence(self):
        result=review(trace_pair())
        self.assertTrue(result['coordinatedNativeWarpEvidenceVerified'],result)
        self.assertFalse(result['fullLogisticsRoundTrip'])
        self.assertEqual(1,len(result['sessions']))

    def test_missing_peer_or_member_rejects(self):
        self.rejects(lambda t:t.pop('B'))
        self.rejects(lambda t:t['B'][3].update(members='RG-Test-B'))

    def test_missing_consent_or_hold_rejects(self):
        self.rejects(lambda t:t['B'][3].update(enabled='False'))
        self.rejects(lambda t:t['A'][3].update(holding='False'))

    def test_powered_or_atmospheric_warp_rejects(self):
        self.rejects(lambda t:t['A'][3].update(thrustKN='1'))
        self.rejects(lambda t:t['A'][3].update(altitude='90000'))
        self.rejects(lambda t:t['B'][3].update(surfaceSpeed='.2'))
        self.rejects(lambda t:t['A'][3].update(x='.1'))

    def test_nonrails_excessive_rate_or_triggered_burn_rejects(self):
        self.rejects(lambda t:t['A'][3].update(warpMode='LOW'))
        self.rejects(lambda t:t['A'][3].update(warp='1000'))
        self.rejects(lambda t:t['A'][3].update(nodeTriggered='True'))

    def test_unexplained_jump_rewind_and_stop_overshoot_reject(self):
        self.rejects(lambda t:t['B'][7].update(ut='500'))
        self.rejects(lambda t:t['A'][3].update(ut='99'))
        self.rejects(lambda t:t['A'][3].update(ut='10000'))

    def test_wrong_destination_or_incomplete_sync_rejects(self):
        self.rejects(lambda t:t['B'][7].update(nativeSubspace='3'))
        self.rejects(lambda t:[x.update(stage='Warping') for x in t['B'] if x['stage']=='Synchronizing'])
        self.rejects(lambda t:t['B'].pop())

    def test_unsafe_flags_timestep_or_missing_field_rejects(self):
        self.rejects(lambda t:t['A'][3].update(infinitePropellant='True'))
        self.rejects(lambda t:t['A'][2].update(dt='.01'))
        self.rejects(lambda t:t['B'][3].pop('utcTicks'))

    def test_missing_native_interval_and_pilot_disagreement_reject(self):
        self.rejects(lambda t:t['B'][3].update(real='10'))
        self.rejects(lambda t:t['B'][3].update(leader='RG-Test-B'))

    def test_no_acceleration_cannot_claim_warp_verified(self):
        self.rejects(lambda t:[x.update(warp='1') for rows in t.values() for x in rows])

    def test_missing_native_bundle_is_never_accepted(self):
        with tempfile.TemporaryDirectory() as folder:
            result=audit(folder)
            self.assertFalse(result['coordinatedNativeWarpEvidenceVerified'])
            self.assertFalse(result['fullLogisticsRoundTrip'])

    def test_file_reader_preserves_separate_proof_and_rejects_metadata_mismatch(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);raw=b'<ConnectionSettingsDefinition><ListenAddress>127.0.0.1</ListenAddress><Port>8818</Port><Upnp>false</Upnp></ConnectionSettingsDefinition>'
            (root/'server-connection.xml').write_bytes(raw)
            manifest={'coastTraceSchema':'native-coast-v2','port':8818,'serverConnectionSHA256':hashlib.sha256(raw).hexdigest(),
                'clientSHA256':{'A':'a'*64,'B':'a'*64},'probeSHA256':{'A':'b'*64,'B':'b'*64},'processIds':{'server':1,'A':2,'B':3}}
            for role,rows in trace_pair().items():
                path=root/('client-'+role)/'roundtrip/coast-warp.tsv';path.parent.mkdir(parents=True)
                with path.open('w',newline='',encoding='utf-8') as stream:
                    writer=csv.DictWriter(stream,fieldnames=list(rows[0]),delimiter='\t');writer.writeheader();writer.writerows(rows)
            (root/'manifest.json').write_text(json.dumps(manifest))
            result=audit(root)
            self.assertTrue(result['coordinatedNativeWarpEvidenceVerified'],result)
            self.assertFalse(result['fullLogisticsRoundTrip'])
            manifest['probeSHA256']['B']='c'*64
            (root/'manifest.json').write_text(json.dumps(manifest))
            self.assertFalse(audit(root)['coordinatedNativeWarpEvidenceVerified'])

if __name__=='__main__':unittest.main()
