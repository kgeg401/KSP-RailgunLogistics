"""Synthetic authority-link tests; no gameplay or full-trip credit."""
import copy,pathlib,sys,unittest,xml.etree.ElementTree as ET
b=pathlib.Path(__file__).parent
from review_coordinated_route import verify_epoch_chain,review
def root(space,epoch):
 r=ET.Element('SharedFleet');ET.SubElement(r,'Subspace').text=str(space);ET.SubElement(r,'Epoch').text=epoch;slots=ET.SubElement(r,'Slots')
 for i in [1,2]:
  p=ET.SubElement(ET.SubElement(slots,'FleetSlot'),'Pod');ET.SubElement(p,'VesselId').text='original-pod-'+str(i);ET.SubElement(ET.SubElement(p,'PartIds'),'unsignedInt').text=str(i)
 return r
def case():return root(0,'epoch0'),root(2,'epoch2'),[root(1,'epoch1')],[{'source':0,'destination':1,'epoch':'epoch0','startCounterSeconds':1},{'source':1,'destination':2,'epoch':'epoch1','startCounterSeconds':2}]
class EpochTests(unittest.TestCase):
 def test_complete_chain_preserves_originals(self):self.assertTrue(verify_epoch_chain(*case())['verified'])
 def reject(self,change):
  a,z,w,t=case();change(a,z,w,t)
  with self.assertRaises((AssertionError,ValueError,KeyError)):verify_epoch_chain(a,z,w,t)
 def test_missing_handoff_rejected(self):self.reject(lambda a,z,w,t:t.pop())
 def test_foreign_epoch_rejected(self):self.reject(lambda a,z,w,t:t[1].update(epoch='foreign'))
 def test_unknown_destination_rejected(self):self.reject(lambda a,z,w,t:w.append(root(3,'epoch3')))
 def test_reset_in_same_timeline_rejected(self):self.reject(lambda a,z,w,t:w.append(root(1,'reset-epoch')))
 def test_inventory_change_rejected(self):self.reject(lambda a,z,w,t:w[0].find('Slots/FleetSlot/Pod/PartIds/unsignedInt').__setattr__('text','999'))
 def test_foreign_pod_rejected(self):self.reject(lambda a,z,w,t:w[0].find('Slots/FleetSlot/Pod/VesselId').__setattr__('text','replacement'))
 def test_reordered_timeline_rejected(self):self.reject(lambda a,z,w,t:t[1].update(source=99))
 def test_native_revisit_rejected(self):self.reject(lambda a,z,w,t:t[1].update(destination=0))
 def test_preflight_unbound_timeline_is_not_a_route_handoff(self):
  a,z,w,t=case();w.append(root(-2147483648,'preflight'));self.assertEqual(2,verify_epoch_chain(a,z,w,t)['nativeHandoffs'])
 def test_missing_physical_bundle_never_passes_complete_profile(self):self.assertFalse(review(b/'missing-route-bundle')['coordinatedNativeRouteVerified'])
if __name__=='__main__':unittest.main()
