"""Review physical route components without claiming strict 1x/clock acceptance.

The original audit_roundtrip.py remains authoritative for its strict profile.
This review keeps fullLogisticsRoundTrip false, reports epoch/clock limits, and
never turns a resumed fixture into a fresh launch-to-refit observation.
"""
from pathlib import Path
import argparse,json,csv,xml.etree.ElementTree as ET
from audit_roundtrip import pods,ids,vessel,initial_assembly,final_assembly,shell_rearmed,mission_history,sequence,require_fresh_depot,vessel_fields,home_distance
from audit_delivery import audit as delivery_audit,native_parts,nodes,fields,resources

def rows(path):
    if not path.exists():return []
    with path.open(encoding='utf-8-sig') as f:return list(csv.DictReader(f,delimiter='\t'))

def review(directory):
    d=Path(directory);result={'fullLogisticsRoundTrip':False,'physicalRouteObserved':False,'profile':'component review; strict clock/warp acceptance excluded','checks':{}}
    c=result['checks']
    try:
        manifest=json.loads((d/'manifest.json').read_text(encoding='utf-8-sig'))
        first=pods(d/'initial/Fleet.xml');last=pods(d/'final/Fleet.xml');original=first[0];final=last[0]
        c['freshNativeStart']=manifest.get('mode')=='fresh-native' and manifest.get('diagnostic') is False
        require_fresh_depot(d/'initial',first)
        witnesses=[];epochs=set()
        for p in sorted((d/'stages').glob('*/Fleet.xml')):
            root=ET.parse(p).getroot();epochs.add(root.findtext('Epoch'));witnesses.append(root.find('Slots/FleetSlot/Pod'))
        for role in ['A','B']:
            for p in sorted((d/('client-'+role)/'roundtrip/authority').glob('*/Fleet.xml')):
                receipt=(p.parent/'receipt.txt').read_text()
                if 'source=received-server-message' not in receipt:raise ValueError('Unverified phase receipt')
                root=ET.parse(p).getroot();epochs.add(root.findtext('Epoch'));witnesses.append(root.find('Slots/FleetSlot/Pod'))
        history=mission_history(original,final,witnesses)
        c['allOrderedPhysicalPhases']=sequence([original.findtext('Phase')]+[p.findtext('Phase') for p in history]+[final.findtext('Phase')])
        launched=next(p for p in history if p.findtext('Phase')=='RailLaunch');order=launched.findtext('OrderId');cargo=int(launched.findtext('CargoPart'))
        reserved=next(p for p in history if p.findtext('Phase')=='Reserved')
        c['sameReservedManifest']=all(ids(p)==ids(original) and all(p.findtext(k)==reserved.findtext(k) for k in ['OrderId','TargetAnchor','TargetPort','Resource','CargoUnits','OwnPort']) for p in history if p.findtext('Phase')!='Ready')
        c['oneCompletedFlight']=int(final.findtext('FlightsCompleted'))==int(original.findtext('FlightsCompleted'))+1 and final.findtext('CargoTransferred')=='true' and final.findtext('OrderId','')=='' and final.findtext('Phase')=='Ready'
        c['sameTwoOriginalIdentities']=all(a.findtext('VesselId')==b.findtext('VesselId') and ids(a)==ids(b) for a,b in zip(first,last))
        c['spareAvailable']=last[1].findtext('Phase')=='Ready' and last[1].findtext('OrderId','')=='' and last[1].findtext('FlightsCompleted')==first[1].findtext('FlightsCompleted')
        tx=[];accepted=[];shell=[];native=[]
        for role in ['A','B']:
            root=d/('client-'+role)
            tx.extend(delivery_audit(p.parent) for p in (root/'roundtrip/transactions').glob('*/transaction.tsv'))
            accepted.extend(r for r in rows(root/'LandingEvidence.tsv') if r['pod']==original.findtext('VesselId') and r['accepted']=='True' and r['recoveryTest']=='False')
            shell.extend(rows(root/'roundtrip/shell-hardware.tsv'));native.extend(dict(r,client=role) for r in rows(root/'roundtrip/native.tsv'))
        c['oneConservedDelivery']=len(tx)==1 and tx[0]['success'] and tx[0]['order']==order
        c['oneNativeStableHomeLanding']=len(accepted)==1 and all(float(r['stableSeconds'])>=5 and 0<=float(r['padMetres'])<=25 and 0<=float(r['uprightDegrees'])<=8 and 0<=float(r['surfaceSpeed'])<=.5 and int(r['intact'])==int(r['original'])==len(ids(original)) and r['nativeLanded']=='True' and float(r['warp'])==1 and abs(float(r['physicsDt'])-.02)<1e-7 for r in accepted)
        orientation=[r for role in ['A','B'] for r in rows(d/('client-'+role)/'roundtrip/orientation.tsv') if r['pod']=='1']
        if len(accepted)==1:
            touchdown=float(accepted[0]['ut']);stable=sorted((r for r in orientation if touchdown-5<=float(r['ut'])<=touchdown),key=lambda r:float(r['ut']))
            c['independentGroundStability']=len(stable)>=12 and float(stable[-1]['ut'])-float(stable[0]['ut'])>=4.5 and all(r['landed']=='True' and r['body']=='Kerbin' and 0<=float(r['speed'])<=.5 and 0<=float(r['uprightDegrees'])<=8 for r in stable) and all(float(b['ut'])-float(a['ut'])<=.8 for a,b in zip(stable,stable[1:]))
        else:c['independentGroundStability']=False
        for n,p in enumerate(last):
            text=vessel(d/'final',p.findtext('VesselId'));initial=vessel(d/'initial',p.findtext('VesselId'));f=vessel_fields(text)
            c[f'pod{n+1}OriginalAssemblyHome']=final_assembly(p,text) and f.get('sit')=='LANDED' and f.get('landed')=='True' and home_distance(initial,text)<=25
            if n==0:
                c['newClosedNativeShell']=shell_rearmed(original,p,text,shell)
                parts=native_parts(text);tank=resources(parts[cargo])[launched.findtext('Resource')]
                markers=[fields(m) for part in parts.values() for m in nodes('MODULE',part) if fields(m).get('name')=='RailgunFlightPod' and fields(m).get('fleetId')==p.findtext('VesselId')]
                c['emptyCargoAndNativeReceiptPersist']=tank[0]==0 and not tank[2] and len(markers)==1 and markers[0].get('completedOrder')==order
        events=[json.loads(s) for s in (d/'events.jsonl').read_text().splitlines() if s.strip()]
        c['noReplayOrRestartObserved']=not any(e.get('event') in ['resume','clock-change','process-exited-early','observer-error','native-peer-failure'] for e in events)
        samples=[json.loads(s) for s in (d/'status.jsonl').read_text().splitlines() if s.strip()]
        c['sameThreeNativeProcesses']=bool(samples) and all(s['aliveProcesses']==manifest['processIds'] for s in samples)
        c['recordedPeersConnected']=bool(samples) and all(s['peerStates'][r].startswith('network=Running ') and s['statusAgeSeconds'][r]<=6 for s in samples for r in ['A','B'])
        owned=sorted((r for r in native if r['pod']=='1' and r['loaded']=='True' and r['packed']=='False' and r['nativeOwner']=='True' and r['pilotLease']=='True' and r['phase'] not in ['Ready','Reserved','Refitting']),key=lambda r:float(r['ut']))
        c['originalPartsThroughoutPhysicalFlight']=len(owned)>=100 and all(int(r['originalParts'])==len(ids(original)) and 0<float(r['heatRatio'])<1 for r in owned)
        c['noObservedPropulsionRefill']=len(owned)>=100 and all(float(b[k])<=float(a[k])+1e-5 for a,b in zip(owned,owned[1:]) for k in ['liquidFuel','oxidizer'])
        result.update(order=order,committedEpochs=len(epochs),readGapEvents=sum(e.get('event')=='read-gap' for e in events),landing=accepted,delivery=tx,physicalRouteObserved=all(c.values()),limits=['Coordinated native warp/clock continuity needs its own audit; this is not a strict1x acceptance or a repeated reliability rate.'])
    except Exception as e:result['incompleteOrError']=str(e)
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);a=p.parse_args();print(json.dumps(review(a.directory),indent=2))
