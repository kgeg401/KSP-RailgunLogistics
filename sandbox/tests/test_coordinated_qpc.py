"""Synthetic evidence-reader tests; never native gameplay evidence."""
import copy,importlib.util,pathlib,sys,unittest,csv,hashlib,json,tempfile
b=pathlib.Path(__file__).parent
from audit_coordinated_warp_qpc import review,audit
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

def pair():
 t=trace_pair()
 for rows in t.values():
  for i,row in enumerate(rows):row.update(qpcTicks=str(1000000000+i*1000000),qpcFrequency='10000000')
 return t
class CounterReaderTests(unittest.TestCase):
 def cancelled_pair(self):
  t=pair()
  for role,rows in t.items():
   for i,row in enumerate(rows):
    row.update(warp='1',ut=str(100+i*.1),nativeSubspace='0',destination='0' if row['stage']=='Synchronizing' else '-1')
    if role=='B' and row['stage']=='Synchronizing':row.update(stage='Idle',session='',holding='False',destination='-1')
  return t
 def test_cancelled_one_x_missing_brief_sync_gets_no_handoff_credit(self):
  t=self.cancelled_pair();v=review(t)
  # A complete accelerated session is still required for an overall positive.
  self.assertFalse(v['success']);self.assertEqual('No completed actual native warp session observed',v['error'])
  self.assertEqual(1,len(v['unacceleratedSameTimelineSessions']));self.assertEqual([],v['timelineTransitions'])
  self.assertTrue(v['unacceleratedSameTimelineSessions'][0]['noAccelerationOrHandoffCredit'])
 def test_cancelled_one_x_foreign_idle_destination_rejected(self):
  t=self.cancelled_pair();t['B'][-1].update(nativeSubspace='9')
  # Alter the first Idle following the cancellation, not only a later sample.
  next(r for r in t['B'] if r['stage']=='Idle' and float(r['ut'])>100.2).update(nativeSubspace='9')
  self.assertFalse(review(t)['success'])
 def test_cancelled_one_x_with_acceleration_still_rejected(self):
  t=self.cancelled_pair();t['A'][3].update(warp='100');self.assertFalse(review(t)['success'])
 def test_cancelled_one_x_without_idle_still_rejected(self):
  t=self.cancelled_pair()
  for r in t['B']:
   if r['stage']=='Idle':r.update(stage='Warping',session='unit-session',holding='True')
  self.assertFalse(review(t)['success'])
 def cancelled_then_complete(self):
  t=self.cancelled_pair();later=pair()
  for role,rows in later.items():
   for row in rows:
    row.update(real=str(float(row['real'])+.8),ut=str(float(row['ut'])+.8),qpcTicks=str(int(row['qpcTicks'])+8000000),utcTicks=str(int(row['utcTicks'])+8000000))
    if row['session']:row['session']='complete-session'
   t[role]+=rows
  return t
 def test_cancelled_then_accelerated_counts_only_actual_handoff(self):
  v=review(self.cancelled_then_complete());self.assertTrue(v['success'],v)
  self.assertEqual(1,len(v['sessions']));self.assertEqual(1,len(v['timelineTransitions']))
  self.assertEqual(1,len(v['unacceleratedSameTimelineSessions']));self.assertFalse(v['fullLogisticsRoundTrip'])
 def test_cancelled_attempt_cannot_explain_a_clock_jump(self):
  t=self.cancelled_then_complete()
  for rows in t.values():
   for row in rows[3:]:row['ut']=str(float(row['ut'])+10)
  v=review(t);self.assertFalse(v['success']);self.assertEqual('Unexplained native clock jump',v['error'])
 def reject(self,change):
  t=pair();change(t);v=review(t);self.assertFalse(v['success'],v);self.assertFalse(v['fullLogisticsRoundTrip'])
 def test_valid_pair_is_only_session_evidence(self):
  v=review(pair());self.assertTrue(v['success'],v);self.assertFalse(v['fullLogisticsRoundTrip'])
 def test_raw_utc_backstep_does_not_change_native_counter(self):
  t=pair()
  for rows in t.values():
   for row in rows[3:]:row['utcTicks']=str(int(row['utcTicks'])-20000000)
  self.assertTrue(review(t)['success'])
 def test_counter_rewind_rejected(self):self.reject(lambda t:t['B'][3].update(qpcTicks='1'))
 def test_frequency_disagreement_rejected(self):self.reject(lambda t:t['B'][3].update(qpcFrequency='1'))
 def test_unbound_source_timeline_rejected(self):self.reject(lambda t:[row.update(sourceSubspace='-1') for rows in t.values() for row in rows])
 def test_missing_counter_rejected(self):self.reject(lambda t:t['B'][3].pop('qpcTicks'))
 def test_missing_original_utc_rejected(self):self.reject(lambda t:t['B'][3].pop('utcTicks'))
 def test_native_rewind_still_rejected(self):self.reject(lambda t:t['B'][3].update(ut='1'))
 def test_unexplained_native_jump_still_rejected(self):self.reject(lambda t:t['B'][7].update(ut='999'))
 def test_powered_warp_still_rejected(self):self.reject(lambda t:t['A'][3].update(thrustKN='1'))
 def test_consent_loss_still_rejected(self):self.reject(lambda t:t['B'][3].update(enabled='False'))
 def test_missing_bundle_rejected(self):self.assertFalse(audit(b/'missing-counter-bundle')['success'])
 def test_one_x_handoff_is_recorded_without_acceleration_credit(self):
  t=pair()
  for rows in t.values():
   for i,row in enumerate(rows):row.update(warp='1',ut=str(100+i*.1))
  value=review(t);self.assertFalse(value['success']);self.assertFalse(value['fullLogisticsRoundTrip']);self.assertEqual([],value['sessions']);self.assertEqual(1,len(value['timelineTransitions']))
 def test_same_timeline_one_x_completion_has_no_warp_or_handoff_credit(self):
  t=pair()
  for rows in t.values():
   for i,row in enumerate(rows):row.update(warp='1',ut=str(100+i*.1),nativeSubspace='0',destination='0' if row['stage']=='Synchronizing' else '-1')
  value=review(t);self.assertFalse(value['success']);self.assertEqual([],value['sessions']);self.assertEqual([],value['timelineTransitions']);self.assertEqual(1,len(value['unacceleratedSameTimelineSessions']))
 def test_accelerated_same_timeline_still_rejects(self):
  self.reject(lambda t:[row.update(destination='0' if row['stage']=='Synchronizing' else '-1') for rows in t.values() for row in rows])
 def test_accelerated_then_one_x_handoff_keep_distinct_credit(self):
  t=pair();extra=pair()
  for role,rows in extra.items():
   for i,row in enumerate(rows):
    row.update(real=str(.8+i*.1),ut=str(120.6+i*.1),qpcTicks=str(1000000000+(8+i)*1000000),warp='1',epoch='next-unit-epoch',sourceSubspace='2',nativeSubspace='3' if i>=5 else '2',destination='3' if row['stage']=='Synchronizing' else '-1')
    if row['session']:row['session']='next-unit-session'
   t[role]+=rows
  value=review(t);self.assertTrue(value['success'],value);self.assertEqual(1,len(value['sessions']));self.assertEqual(2,len(value['timelineTransitions']));self.assertFalse(value['fullLogisticsRoundTrip'])
 def test_file_boundary_is_native_counter_and_preserves_raw(self):
  with tempfile.TemporaryDirectory(prefix='qpc-reader-synthetic-',dir=b) as work:
   root=pathlib.Path(work).resolve();self.assertTrue(root.is_relative_to(b.resolve()))
   raw=b'<ConnectionSettingsDefinition><ListenAddress>127.0.0.1</ListenAddress><Port>8818</Port><Upnp>false</Upnp></ConnectionSettingsDefinition>'
   (root/'server-connection.xml').write_bytes(raw)
   manifest={'startedUtc':'2026-10-31T11:00:00+00:00','coastTraceSchema':'native-coast-v2','coastClockSchema':'same-host-qpc-v1','captureStartQpcTicks':1000000000,'captureQpcFrequency':10000000,'port':8818,'serverConnectionSHA256':hashlib.sha256(raw).hexdigest(),'clientSHA256':{'A':'a'*64,'B':'a'*64},'probeSHA256':{'A':'b'*64,'B':'b'*64},'processIds':{'server':1,'A':2,'B':3}}
   (root/'manifest.json').write_text(json.dumps(manifest));paths=[]
   for role,rows in pair().items():
    early={**rows[0],'qpcTicks':'999000000','ut':'1'};rows=[early]+rows
    path=root/('client-'+role)/'roundtrip/coast-warp.tsv';path.parent.mkdir(parents=True);paths.append(path)
    with path.open('w',newline='') as f:w=csv.DictWriter(f,fieldnames=list(rows[0]),delimiter='\t');w.writeheader();w.writerows(rows)
   before=[hashlib.sha256(p.read_bytes()).hexdigest() for p in paths]
   value=audit(root);self.assertTrue(value['success'],value);self.assertEqual({'A':1,'B':1},value['preservedRawRecordsBeforeMission']);self.assertFalse(value['fullLogisticsRoundTrip'])
   self.assertEqual(before,[hashlib.sha256(p.read_bytes()).hexdigest() for p in paths])
   manifest['captureQpcFrequency']=1;(root/'manifest.json').write_text(json.dumps(manifest));self.assertFalse(audit(root)['success'])
if __name__=='__main__':unittest.main()
