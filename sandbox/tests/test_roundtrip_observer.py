import unittest
import hashlib
import tempfile
import os
import xml.etree.ElementTree as ET
from pathlib import Path
from observe_roundtrip import rendezvous_hold, receiver_hardware_ready, original_receiver_selected
from audit_roundtrip import private_endpoint


class ObserverHoldTests(unittest.TestCase):
    line="Pod 1: Rendezvous / Rendezvous held: maneuver would spend the home-return fuel reserve / planned 50 m/s / id=original-pod / created=True / cargoTransferred=False"

    def test_original_native_safety_hold_is_identified(self):
        self.assertIn("home-return fuel reserve",rendezvous_hold(self.line,"original-pod"))

    def test_replaced_or_stale_subject_does_not_stop_a_new_flight(self):
        self.assertIsNone(rendezvous_hold(self.line,"other-pod"))

    def test_unrelated_pod_cannot_stop_selected_flight(self):
        self.assertIsNone(rendezvous_hold(self.line.replace("Pod 1:","Pod 2:"),"original-pod"))

    def test_other_phase_or_unidentified_subject_does_not_qualify(self):
        self.assertIsNone(rendezvous_hold(self.line.replace("Rendezvous /","Ascent /",1),"original-pod"))
        self.assertIsNone(rendezvous_hold(self.line,""))

    def test_normal_native_coast_is_not_a_safety_hold(self):
        self.assertIsNone(rendezvous_hold(self.line.replace("Rendezvous held:","Planning Hohmann transfer:"),"original-pod"))

    def test_new_private_port_requires_actual_pinned_configuration(self):
        self.assertFalse(private_endpoint("unused",{"port":8818}))
        self.assertTrue(private_endpoint("unused",{"port":8817}))
        with tempfile.TemporaryDirectory() as folder:
            raw=b'<ConnectionSettingsDefinition><ListenAddress>127.0.0.1</ListenAddress><Port>8818</Port><Upnp>false</Upnp></ConnectionSettingsDefinition>'
            path=Path(folder)/"server-connection.xml";path.write_bytes(raw)
            manifest={"port":8818,"serverConnectionSHA256":hashlib.sha256(raw).hexdigest()}
            self.assertTrue(private_endpoint(folder,manifest))
            self.assertFalse(private_endpoint(folder,{**manifest,"port":8817}))
            path.write_bytes(raw.replace(b"127.0.0.1",b"0.0.0.0"))
            self.assertFalse(private_endpoint(folder,manifest))
            manifest["serverConnectionSHA256"]=hashlib.sha256(path.read_bytes()).hexdigest()
            self.assertFalse(private_endpoint(folder,manifest))


class RenamedReceiverTests(unittest.TestCase):
    active = '49e16cd4-9fd0-4c82-899a-cd18e68c00f1'
    saved = '''pid = 49e16cd49fd04c82899acd18e68c00f1
name = ARGO M1 Science Core
sit = ORBITING
ORBIT
{
REF = 1
}
PART
{
uid = 101
}
PART
{
uid = 102
MODULE
{
name = ModuleDockingNode
state = Ready
}
}
'''

    def test_native_rename_keeps_original_hardware_selection(self):
        self.assertTrue(receiver_hardware_ready(self.saved, self.active, 101, 102))
        self.assertTrue(receiver_hardware_ready(self.saved.replace('ARGO M1 Science Core','Any native name'), self.active, 101, 102))

    def test_other_active_vessel_and_missing_original_hardware_fail_closed(self):
        self.assertFalse(receiver_hardware_ready(self.saved, '00000000-0000-0000-0000-000000000001', 101, 102))
        self.assertFalse(receiver_hardware_ready(self.saved, self.active, 103, 102))
        self.assertFalse(receiver_hardware_ready(self.saved, self.active, 101, 103))
        self.assertFalse(receiver_hardware_ready(self.saved, self.active, 0, 102))

    def test_docked_wrong_body_landed_and_duplicate_identity_are_rejected(self):
        for old, new in [('state = Ready','state = Docked (docker)'),('REF = 1','REF = 2'),('sit = ORBITING','sit = LANDED'),('uid = 102','uid = 101')]:
            self.assertFalse(receiver_hardware_ready(self.saved.replace(old,new), self.active, 101, 102))

    def test_pod_marker_and_malformed_snapshot_are_rejected(self):
        joined = self.saved + 'PART\n{\nuid = 103\nMODULE\n{\nname = RailgunFlightPod\n}\n}\n'
        self.assertFalse(receiver_hardware_ready(joined, self.active, 101, 102))
        self.assertFalse(receiver_hardware_ready('partial', self.active, 101, 102))

    def test_fresh_same_client_focus_is_required(self):
        with tempfile.TemporaryDirectory() as folder:
            server=Path(folder)/'server'; root=Path(folder)/'client'
            vessels=server/'Universe/Vessels'; vessels.mkdir(parents=True)
            (vessels/(self.active+'.txt')).write_text(self.saved)
            trace=root/'Railgun-MP-Test/roundtrip/coast-warp.tsv'; trace.parent.mkdir(parents=True)
            trace.write_text('activeVessel\n'+self.active+'\n'); os.utime(trace,(1000,1000))
            pod=ET.fromstring('<Pod><TargetAnchor>101</TargetAnchor><TargetPort>102</TargetPort></Pod>')
            self.assertTrue(original_receiver_selected(server,root,pod,1001))
            self.assertFalse(original_receiver_selected(server,root,pod,1004))
            self.assertFalse(original_receiver_selected(server,root,pod,999))
            trace.write_text('activeVessel\ninvalid\n'); os.utime(trace,(1000,1000))
            self.assertFalse(original_receiver_selected(server,root,pod,1001))
            trace.write_text('activeVessel\n'+self.active); os.utime(trace,(1000,1000))
            self.assertFalse(original_receiver_selected(server,root,pod,1001))


if __name__=="__main__":unittest.main()
