"""Read-only diagnostics for a replayed post-delivery return, never a full trip.

Uses the real native assembly, resource trace, landing acceptance and independent
orientation samples. Does not convert a resumed checkpoint into fresh-flight proof.
"""
import argparse,json
from pathlib import Path
from review_roundtrip_components import rows
from audit_roundtrip import pods,ids,vessel,vessel_fields,home_distance,shell_rearmed,final_assembly
from audit_delivery import native_parts,resources,nodes,fields

def review(directory):
    d=Path(directory);checks={};result={'fullLogisticsRoundTrip':False,'returnComponentsPassed':False,'checks':checks}
    try:
        manifest=json.loads((d/'manifest.json').read_text());initial=pods(d/'initial/Fleet.xml');final=pods(d/'final/Fleet.xml');a=initial[0];z=final[0];order=a.findtext('OrderId');original=ids(a)
        checks['explicitDiagnosticReplay']=manifest.get('diagnostic') is True
        checks['startsAfterRealDelivery']=a.findtext('Phase')=='Landing' and a.findtext('CargoTransferred')=='true' and bool(order)
        checks['sameOriginalPod']=a.findtext('VesselId')==z.findtext('VesselId') and ids(z)==original
        checks['nativeReturnAccepted']=z.findtext('Phase')=='Ready' and int(z.findtext('FlightsCompleted'))==int(a.findtext('FlightsCompleted'))+1
        checks['spareStillAvailable']=final[1].findtext('Phase')=='Ready' and ids(final[1])==ids(initial[1])
        native=[];orientation=[];landings=[];shell=[]
        for role in ['A','B']:
            root=d/('client-'+role)
            native.extend(rows(root/'roundtrip/native.tsv'));orientation.extend(rows(root/'roundtrip/orientation.tsv'));shell.extend(rows(root/'roundtrip/shell-hardware.tsv'))
            landings.extend(r for r in rows(root/'LandingEvidence.tsv') if r['pod']==a.findtext('VesselId') and r['accepted']=='True')
        owned=sorted((r for r in native if r['pod']=='1' and r['phase']=='Landing' and r['loaded']=='True' and r['packed']=='False' and r['nativeOwner']=='True' and r['pilotLease']=='True'),key=lambda r:float(r['ut']))
        result.update(peakHeatRatio=max((float(r['heatRatio']) for r in owned),default=None),lastLandingLiquidFuel=float(owned[-1]['liquidFuel']) if owned else None)
        checks['originalsAndThermalsDuringGuidedDescent']=len(owned)>=100 and all(int(r['originalParts'])==len(original) and 0<float(r['heatRatio'])<1 for r in owned)
        checks['noInflightPropulsionRefill']=len(owned)>=100 and all(float(y[k])<=float(x[k])+1e-5 for x,y in zip(owned,owned[1:]) for k in ['liquidFuel','oxidizer'])
        checks['emptyCargoReceiptThroughoutReturn']=bool(owned) and all(float(r['cargo'])==0 and r['receipt']==order for r in owned)
        checks['strictNativeTouchdown']=len(landings)==1 and all(float(r['stableSeconds'])>=5 and 0<=float(r['padMetres'])<=25 and 0<=float(r['uprightDegrees'])<=8 and 0<=float(r['surfaceSpeed'])<=.5 and int(r['intact'])==int(r['original'])==len(original) and r['nativeLanded']=='True' and float(r['warp'])==1 and abs(float(r['physicsDt'])-.02)<1e-7 for r in landings)
        if len(landings)==1:
            ut=float(landings[0]['ut']);stable=sorted((r for r in orientation if r['pod']=='1' and ut-5<=float(r['ut'])<=ut),key=lambda r:float(r['ut']))
            checks['independentStableTouchdown']=len(stable)>=12 and float(stable[-1]['ut'])-float(stable[0]['ut'])>=4.5 and all(r['landed']=='True' and r['body']=='Kerbin' and 0<=float(r['speed'])<=.5 and 0<=float(r['uprightDegrees'])<=8 for r in stable) and all(float(y['ut'])-float(x['ut'])<=.8 for x,y in zip(stable,stable[1:]))
        else:checks['independentStableTouchdown']=False
        text=vessel(d/'final',z.findtext('VesselId'));f=vessel_fields(text);parts=native_parts(text)
        checks['nativeOriginalAssemblyHome']=final_assembly(z,text) and f.get('sit')=='LANDED' and f.get('landed')=='True' and home_distance(vessel(d/'initial',initial[1].findtext('VesselId')),text)<=25
        checks['newPhysicalClosedShieldingShell']=shell_rearmed(a,z,text,shell)
        tank=resources(parts[int(a.findtext('CargoPart'))])[a.findtext('Resource')]
        markers=[fields(m) for p in parts.values() for m in nodes('MODULE',p) if fields(m).get('name')=='RailgunFlightPod' and fields(m).get('fleetId')==a.findtext('VesselId')]
        checks['nativeEmptyLockedCargoAndReceipt']=tank[0]==0 and not tank[2] and len(markers)==1 and markers[0].get('completedOrder')==order
        entry=[r for r in owned if float(r['altitude'])<70000]
        checks['normalNativeEntryPhysics']=bool(entry) and all(float(r['warp'])==1 and abs(float(r['fixedDt'])-.02)<1e-7 for r in entry)
        flag_count=0;flags_ok=True
        for line in (d/'status.jsonl').open():
            sample=json.loads(line)
            for text_status in sample['statuses'].values():
                for row in text_status.splitlines():
                    if row.startswith('NativeChecks: '):
                        flags=dict(x.split('=') for x in row.removeprefix('NativeChecks: ').split());flag_count+=1
                        flags_ok &= all(flags.get(k)=='False' for k in ['infinitePropellant','infiniteElectricity','ignoreTemperature','unbreakableJoints','noCrashDamage'])
        checks['normalDamageAndPropulsionSettings']=flag_count>0 and flags_ok
        result.update(returnComponentsPassed=all(checks.values()),landing=landings,peakHeatRatio=max((float(r['heatRatio']) for r in owned),default=None),lastLandingLiquidFuel=float(owned[-1]['liquidFuel']) if owned else None,limits=['Post-delivery checkpoint replay; no fresh launch or uninterrupted round-trip proof.','One return does not establish repeated reliability.'])
    except Exception as e:result['incompleteOrError']=str(e)
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);a=p.parse_args();print(json.dumps(review(a.directory),indent=2))
