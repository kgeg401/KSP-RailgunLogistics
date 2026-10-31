"""Separate complete-route evidence profile; never relabels strict 1x results."""
from pathlib import Path
import argparse,csv,hashlib,json,math,re,sys,xml.etree.ElementTree as ET

from audit_coordinated_warp_qpc import audit as warp_audit,wall
from audit_roundtrip import ids,private_endpoint,pods,vessel,CHEATS,PHASES,final_assembly,home_distance
from audit_delivery import native_parts,resources,fields,nodes,module
from review_roundtrip_components import review as physical_review

def verify_epoch_chain(initial,final,witnesses,transitions):
    """Every committed epoch change must have a paired native timeline handoff."""
    original=initial.findall('Slots/FleetSlot/Pod');identity=[p.findtext('VesselId') for p in original]
    epoch_by_space={};space_by_epoch={};start=int(initial.findtext('Subspace'))
    assert start>=0 and len(original)==2,'Missing fresh fleet coordination root'
    for root in [initial,*witnesses,final]:
        space=int(root.findtext('Subspace'));epoch=root.findtext('Epoch')
        if space<0:continue # Retained preflight messages are outside the committed route.
        ps=root.findall('Slots/FleetSlot/Pod');assert len(ps)==2 and [p.findtext('VesselId') for p in ps]==identity,'Foreign fleet identity in authority witness'
        if epoch!=initial.findtext('Epoch'):
            assert all(ids(a)==ids(z) for a,z in zip(original,ps)),'Physical inventory changed across timeline handoff'
        assert space not in epoch_by_space or epoch_by_space[space]==epoch,'Unexplained epoch reset within one native timeline'
        assert epoch not in space_by_epoch or space_by_epoch[epoch]==space,'One epoch was attached to multiple native timelines'
        epoch_by_space[space]=epoch;space_by_epoch[epoch]=space
    current=start;seen={start}
    for transition in sorted(transitions,key=lambda t:t['startCounterSeconds']):
        assert transition['source']==current,'Missing or reordered native timeline handoff'
        assert transition['epoch']==epoch_by_space.get(current),'Coast coordinator was not the source fleet epoch'
        destination=transition['destination'];assert destination>=0 and destination not in seen,'Native timeline revisited or invalid'
        current=destination;seen.add(current)
    assert set(epoch_by_space)==seen,'Authority epoch has no paired native timeline evidence'
    assert current==int(final.findtext('Subspace')) and final.findtext('Epoch')==epoch_by_space[current],'Final authority and native timeline disagree'
    return {'verified':True,'committedTimelines':len(seen),'nativeHandoffs':len(transitions)}

def owned_ground_stability(directory,touchdown):
    d=Path(directory);owned=[];mirrored=0
    for role in ['A','B']:
        root=d/('client-'+role)/'roundtrip'
        with (root/'native.tsv').open() as f:
            owners={float(r['ut']) for r in csv.DictReader(f,delimiter='\t') if r['pod']=='1' and r['loaded']==r['nativeOwner']==r['pilotLease']=='True' and r['packed']=='False'}
        with (root/'orientation.tsv').open() as f:
            for row in csv.DictReader(f,delimiter='\t'):
                if row['pod']!='1' or not touchdown-5<=float(row['ut'])<=touchdown:continue
                if float(row['ut']) in owners:owned.append(dict(row,client=role))
                else:mirrored+=1
    owned.sort(key=lambda r:float(r['ut']))
    ok=len(owned)>=12 and float(owned[-1]['ut'])-float(owned[0]['ut'])>=4.5 and all(r['landed']=='True' and r['body']=='Kerbin' and 0<=float(r['speed'])<=.5 and 0<=float(r['uprightDegrees'])<=8 for r in owned) and all(0<float(b['ut'])-float(a['ut'])<=.8 for a,b in zip(owned,owned[1:]))
    return {'verified':ok,'ownerSamples':len(owned),'mirroredSamplesRetainedOutsidePhysicsVerdict':mirrored,'spanSeconds':float(owned[-1]['ut'])-float(owned[0]['ut']) if owned else 0,'maxOwnerSpeed':max((float(r['speed']) for r in owned),default=None),'maxOwnerTilt':max((float(r['uprightDegrees']) for r in owned),default=None)}

def persistence_supplement(directory,supplement):
    d=Path(directory);p=Path(supplement);assert p.resolve().parent==d.resolve().parent,'Persistence supplement belongs to another route'
    m=json.loads((p/'manifest.json').read_text());parent=json.loads((d/'manifest.json').read_text());sha=lambda x:hashlib.sha256(x.read_bytes()).hexdigest()
    assert m['schema']=='late-postflight-persistence-v1' and m['parentManifestSHA256']==sha(d/'manifest.json') and m['parentFinalFleetSHA256']==sha(d/'final/Fleet.xml'),'Unlinked late persistence evidence'
    assert m['sameLiveCohortIdentityVerified'] is True and m['processIds']==parent['processIds'] and m['changesGameplay'] is False and m['newMissionStarted'] is False,'Persistence capture changed the native cohort or gameplay'
    assert sha(p/'Fleet.xml')==m['fleetSHA256'] and all(sha(p/'Vessels'/name)==value for name,value in m['vesselSHA256'].items()),'Persistence payload changed'
    old=pods(d/'final/Fleet.xml');latest=pods(p/'Fleet.xml')
    assert len(old)==len(latest)==2
    for index,(a,z) in enumerate(zip(old,latest)):
        assert z.findtext('Phase')=='Ready' and all(a.findtext(k)==z.findtext(k) for k in ['VesselId','OrderId','FlightsCompleted','CargoTransferred','LaunchShellPartId']) and ids(a)==ids(z),'A different mission or assembly was substituted after flight'
        text=vessel(p,z.findtext('VesselId'));native=fields(text.splitlines())
        assert final_assembly(z,text) and native.get('sit')=='LANDED' and native.get('landed')=='True' and home_distance(vessel(d/'initial',a.findtext('VesselId')),text)<=25,'Late snapshot is not the same original parked assembly'
        parts=native_parts(text);shell=int(z.findtext('LaunchShellPartId'))
        fairing=module(parts[shell],'ModuleProceduralFairing');decouple=module(parts[shell],'ModuleDecouple')
        assert fairing.get('fsm')=='st_idle' and fairing.get('isCapped')=='True' and decouple.get('isDecoupled')=='False','Late protective shell is not physically closed'
        if index==0:
            oldParts=native_parts(vessel(d/'final',a.findtext('VesselId')))
            def receipt(parts):
                markers=[fields(n) for part in parts.values() for n in nodes('MODULE',part) if fields(n).get('name')=='RailgunFlightPod' and fields(n).get('fleetId')==a.findtext('VesselId')]
                assert len(markers)==1,'Missing or duplicate original receipt marker'
                return markers[0].get('completedOrder')
            assert receipt(parts)==receipt(oldParts) and receipt(parts),'Late native delivery receipt changed'
            cargo=resources(parts[int(z.findtext('CargoPart'))])[z.findtext('Resource')]
            assert cargo[0]==0 and not cargo[2],'Late delivery tank is not empty and locked'
            refill=[value for part in parts.values() if fields(part).get('name') not in {'krlReusableMonoCargo','krlReusableFuelCargo'} for name,value in resources(part).items() if name in {'LiquidFuel','Oxidizer','MonoPropellant','ElectricCharge'}]
            assert refill and all(abs(v[0]-v[1])<1e-6 for v in refill),'Late native propulsion/resources are not fully refitted'
    return p,m

def review(directory,persistence_directory=None):
    d=Path(directory);checks={};result={'profile':'coordinated-route-v6','coordinatedNativeRouteVerified':False,'fullLogisticsRoundTrip':False,'strictOneXProfileUnchanged':True,'checks':checks}
    try:
        manifest=json.loads((d/'manifest.json').read_text());physical=physical_review(d);warp=warp_audit(d)
        result['physicalComponents']=physical;result['coordinatedCoasts']=warp
        qualified=dict(physical.get('checks',{}));landings=physical.get('landing',[])
        if len(landings)==1:
            result['authoritativeLandingStability']=owned_ground_stability(d,float(landings[0]['ut']))
            qualified['independentGroundStability']=result['authoritativeLandingStability']['verified']
        result['physicalChecksWithOwnership']=qualified
        checks['allPhysicalRouteComponents']=bool(qualified) and all(qualified.values())
        checks['allCoordinatedCoastEvidence']=warp.get('coordinatedNativeWarpEvidenceVerified') is True
        checks['privateEndpointAndInitialFleet']=private_endpoint(d,manifest) and hashlib.sha256((d/'initial/Fleet.xml').read_bytes()).hexdigest()==manifest['initialFleetSHA256']
        events=[json.loads(s) for s in (d/'events.jsonl').read_text().splitlines() if s.strip()]
        commands=[e['value'] for e in events if e.get('event')=='command']
        checks['oneOrdinaryDispatchAndNoInterruption']=commands.count('dispatch1')==1 and all(c in {'dispatch1','watch1','receiver','snapshot','save-and-exit'} or c.startswith('showcase-') for c in commands) and not any(e.get('event') in {'process-exited-early','observer-error','resume','clock-change','timeout','bounded-stop','native-peer-failure'} for e in events)
        samples=[json.loads(s) for s in (d/'status.jsonl').read_text().splitlines() if s.strip()]
        checks['allObservedDamageAndFuelFlagsOff']=bool(samples) and all(all(dict(token.split('=',1) for token in next(line for line in s['statuses'][role].splitlines() if line.startswith('NativeChecks: ')).removeprefix('NativeChecks: ').split()).get(k)=='False' for k in CHEATS) for s in samples for role in ['A','B'])
        start=manifest['captureStartQpcTicks']/manifest['captureQpcFrequency'];end=start+samples[-1]['elapsed']
        traces={}
        for role in ['A','B']:
            with (d/('client-'+role)/'roundtrip/coast-warp.tsv').open() as f:rows=[r for r in csv.DictReader(f,delimiter='\t') if wall(r)>=start]
            traces[role]=rows
        checks['wholeMissionCommonClockCoverage']=all(len(rows)>=100 and 0<=wall(rows[0])-start<=1 and wall(rows[-1])>=end-1 and all(0<wall(b)-wall(a)<=6 for a,b in zip(rows,rows[1:])) for rows in traces.values())
        roots=[ET.parse(p).getroot() for p in sorted((d/'stages').glob('*/Fleet.xml'))]
        for role in ['A','B']:
            for p in sorted((d/('client-'+role)/'roundtrip/authority').glob('*/Fleet.xml')):
                assert 'source=received-server-message' in (p.parent/'receipt.txt').read_text(),'Unverified authority witness'
                roots.append(ET.parse(p).getroot())
        if checks['allCoordinatedCoastEvidence']:
            result['epochChain']=verify_epoch_chain(ET.parse(d/'initial/Fleet.xml').getroot(),ET.parse(d/'final/Fleet.xml').getroot(),roots,warp['timelineTransitions'])
        checks['allEpochChangesBackedByNativeHandoffs']=bool(result.get('epochChain',{}).get('verified'))
        final_directory=d/'final'
        if persistence_directory is not None:
            final_directory,metadata=persistence_supplement(d,persistence_directory)
            result['latePersistenceSupplement']={'directory':str(final_directory),'capturedUtc':metadata['capturedUtc'],'verified':True,'originalEarlyFinalSnapshotRetained':True}
        original=pods(d/'initial/Fleet.xml')[0];final=pods(final_directory/'Fleet.xml')[0];parts=native_parts(vessel(final_directory,final.findtext('VesselId')))
        propulsion=[value for part in parts.values() if fields(part).get('name') not in {'krlReusableMonoCargo','krlReusableFuelCargo'} for name,value in resources(part).items() if name in {'LiquidFuel','Oxidizer','MonoPropellant','ElectricCharge'}]
        checks['physicallyRefilledAfterLanding']=bool(propulsion) and all(abs(v[0]-v[1])<1e-6 for v in propulsion)
        native=[row for role in ['A','B'] for row in csv.DictReader((d/('client-'+role)/'roundtrip/native.tsv').open(),delimiter='\t')]
        owned=[r for r in native if r['pod']=='1' and r['loaded']=='True' and r['packed']=='False' and r['pilotLease']=='True' and r['nativeOwner']=='True']
        checks['everyPhysicalStageRecorded']=set(PHASES[1:-1])<={r['phase'] for r in owned}
        checks['normalUnpackedFlightPhysics']=bool(owned) and all(float(r['warp'])==1 and abs(float(r['fixedDt'])-.02)<1e-7 for r in owned)
        flying=sorted((r for r in owned if r['phase'] not in {'Ready','Reserved','Refitting'}),key=lambda r:float(r['ut']))
        checks['noMidflightMonoRefill']=len(flying)>=100 and all(float(b['mono'])<=float(a['mono'])+1e-5 for a,b in zip(flying,flying[1:]))
        result['coordinatedNativeRouteVerified']=all(checks.values())
        result['limits']=['Separate coordinated-coast profile; original strict1x audit and its failures are retained.','One completed native route does not establish repeated reliability.']
    except Exception as error:result['incompleteOrError']=str(error)
    return result

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('directory');parser.add_argument('--persistence-directory');args=parser.parse_args();value=review(args.directory,args.persistence_directory)
    (Path(args.directory)/'coordinated-route-review.json').write_text(json.dumps(value,indent=2));print(json.dumps({'coordinatedNativeRouteVerified':value['coordinatedNativeRouteVerified'],'checks':value['checks'],'stability':value.get('authoritativeLandingStability'),'latePersistence':value.get('latePersistenceSupplement'),'error':value.get('incompleteOrError'),'fullLogisticsRoundTrip':False},indent=2))
    raise SystemExit(0 if value["coordinatedNativeRouteVerified"] else 1)
