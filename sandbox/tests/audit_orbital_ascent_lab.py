"""Independent audit of a native seeded rail-coast/circularization segment."""
from pathlib import Path
import argparse,csv,json,math,re,xml.etree.ElementTree as ET
from audit_rail_flight import fields,nodes
from audit_delivery import native_parts,resources

def audit(directory):
    d=Path(directory);t=d/'trace';checks={}
    result={'success':False,'orbitalAscentSegmentVerified':False,'fullLogisticsRoundTrip':False,
            'deliveryVerified':False,'fleetReuseVerified':False,'checks':checks}
    try:
        process=json.loads((d/'process.json').read_text());events=(t/'events.txt').read_text()
        completion=json.loads((d/'completion.json').read_text())
        checks['nativeProcessCompleted']=completion.get('observerCompleted') is True and completion.get('nativeExitCode')==0 and not completion.get('timeout') and not completion.get('failure')
        checks['runIdentity']=bool(re.fullmatch(r'run-[A-Za-z0-9_-]+',process['name'])) and 'run='+process['name']+'\n' in events
        initial=ET.parse(t/'initial.xml').getroot().find('Pods/PodRecord');final=ET.parse(t/'final.xml').getroot().find('Pods/PodRecord')
        checks['authenticAscentPhase']=initial.findtext('Phase')=='Ascent' and initial.findtext('RailOnlyAscent')=='true' and initial.findtext('RecoveryTest')=='false'
        checks['noDeliveryOrReuseCredit']=all(p.findtext('CargoTransferred')=='false' and p.findtext('FlightsCompleted')=='0' for p in [initial,final])
        identity=initial.findtext('VesselId');ids={int(e.text) for e in initial.findall('PartIds/unsignedInt')};shell=int(initial.findtext('LaunchShellPartId'));cargo=int(initial.findtext('CargoPart'));resource=initial.findtext('Resource')
        checks['originalLedgerAndManifest']=len(ids)==29 and ids=={int(e.text) for e in final.findall('PartIds/unsignedInt')} and all(initial.findtext(k)==final.findtext(k) for k in ['VesselId','OrderId','CargoPart','Resource','CargoUnits','OwnPort','TargetAnchor','TargetPort'])
        snapshots=[]
        for filename in ['initial.sfs','final.sfs']:
            text=(t/filename).read_text(encoding='utf-8-sig');vs=[v for v in nodes('VESSEL',text.splitlines()) if any(l.strip()=='fleetId = '+identity for l in v)]
            if len(vs)!=1:raise ValueError('Exactly one original native physical pod required')
            snapshots.append((fields(vs[0]),native_parts('\n'.join(vs[0])),fields(next(nodes('ORBIT',vs[0])))))
        checks['initialOriginalInventory']=set(snapshots[0][1])==ids|({shell} if initial.findtext('LaunchShellBaseReleased')!='true' else set())
        checks['finalOriginalInventory']=set(snapshots[-1][1])==ids
        checks['shellReleased']=final.findtext('LaunchShellPanelsReleased')==final.findtext('LaunchShellBaseReleased')=='true' and float(final.findtext('LaunchShellClearAt'))>=0
        cargoStates=[resources(p[cargo])[resource] for _,p,_ in snapshots]
        checks['nativeCargoLockedAndUnchanged']=cargoStates[0]==cargoStates[1] and cargoStates[0][0]==float(initial.findtext('CargoUnits'))==100 and not cargoStates[0][2]
        orbit=snapshots[-1][2];sma,ecc=float(orbit['SMA']),float(orbit['ECC']);pe=sma*(1-ecc)-600000;ap=sma*(1+ecc)-600000
        result['nativeFinalOrbit']={'periapsisMetres':pe,'apoapsisMetres':ap,'eccentricity':ecc}
        checks['nativeStableKerbinOrbit']=orbit['REF']=='1' and all(math.isfinite(x) for x in [sma,ecc,pe,ap]) and 0<=ecc<1 and pe>73000 and snapshots[-1][0]['landed']=='False' and final.findtext('Phase')=='Rendezvous'
        dv=float(final.findtext('CircularizationDeltaV'));result['plannedDeltaV']=dv
        checks['actualCircularizationPlan']=final.findtext('CircularizationPlanned')==final.findtext('CircularizationStarted')=='true' and math.isfinite(dv) and 0<dv<=800
        outcome=dict(l.split('=',1) for l in (t/'outcome.txt').read_text().splitlines() if '=' in l)
        checks['nativeSegmentOutcome']=outcome.get('result')=='StableOrbit' and outcome.get('fullLogisticsRoundTrip')=='false'
        rows=list(csv.DictReader((t/'native.tsv').open(),delimiter='\t'));times=[float(r['ut']) for r in rows]
        checks['forwardClock']=len(rows)>50 and all(b>a for a,b in zip(times,times[1:]))
        checks['onlyExpectedPhases']=bool(rows) and all(r['phase'] in ['Ascent','Rendezvous'] for r in rows)
        checks['originalPartsThroughout']=bool(rows) and all(int(r['originalParts'])==len(ids) for r in rows)
        flags=['infinitePropellant','infiniteElectricity','ignoreTemperature','unbreakableJoints','noCrashDamage']
        checks['normalNativePhysics']=bool(rows) and all(float(r['warp'])==1 and abs(float(r['dt'])-.02)<1e-7 and 0<float(r['maxHeat'])<1 and all(r[k]=='False' for k in flags) for r in rows)
        checks['noResourceIncrease']=bool(rows) and all(all(float(b[k])<=float(a[k])+1e-6 for a,b in zip(rows,rows[1:])) for k in ['fuel','oxidizer','mono'])
        checks['noInitialResourceAddition']=bool(rows) and all(float(rows[0][key])<=sum(resources(p).get(name,(0,0,False))[0] for uid,p in snapshots[0][1].items() if uid!=cargo)+1e-6 for key,name in [('fuel','LiquidFuel'),('oxidizer','Oxidizer'),('mono','MonoPropellant')])
        checks['sampledCargoAlwaysLocked']=bool(rows) and all(float(r['cargoAmount'])==100 and r['cargoFlow']=='False' for r in rows)
        coast=[r for r in rows if r['circularizationStarted']=='False'];burn=[r for r in rows if float(r['thrustKN'])>.05]
        alreadyStarted=initial.findtext('CircularizationStarted')=='true'
        checks['unpoweredSampledCoast']=alreadyStarted or (len(coast)>10 and all(float(r['thrustKN'])<=.05 and r['rcs']=='False' for r in coast))
        checks['separateVacuumBurnObserved']=bool(burn) and all(r['circularizationStarted']=='True' and float(r['altitude'])>71000 and r['shellPanelsReleased']==r['shellBaseReleased']=='True' and float(r['shellClearUt'])>=0 for r in burn)
        checks['completedBeforeProbeCleanup']=bool(rows) and rows[-1]['phase']=='Rendezvous' and int(rows[-1]['nodeCount'])==0 and rows[-1]['nodeEnabled']=='False'
        referenceName=process.get('referenceRun')
        checks['resumeReferencePresent']=not alreadyStarted or bool(referenceName)
        if referenceName:
            assert re.fullmatch(r'run-[A-Za-z0-9_-]+',referenceName) and referenceName!=process['name']
            reference=d.parent/referenceName;ref=json.loads((reference/'independent-orbital-audit.json').read_text())
            checks['referenceIndependentlyVerified']=ref.get('success') is True and ref.get('orbitalAscentSegmentVerified') is True
            goal=ref['nativeFinalOrbit'];errors={'periapsisMetres':abs(pe-goal['periapsisMetres']),'apoapsisMetres':abs(ap-goal['apoapsisMetres'])}
            result['referenceRun']=referenceName;result['referenceOrbitErrors']=errors
            checks['referenceOrbitMatched']=all(x<=2000 for x in errors.values()) and ecc<=.002
        result['samples']=len(rows);result['fuelUsed']=float(rows[0]['fuel'])-float(rows[-1]['fuel']);result['peakHeatRatio']=max(float(r['maxHeat']) for r in rows)
        result['success']=result['orbitalAscentSegmentVerified']=all(checks.values())
        result['limits']=['Seeded after the rail muzzle: no complete launch, docking, delivery, landing, reuse or multiplayer credit.','Telemetry checks describe recorded samples; full-route validation remains separate.']
    except Exception as e:result['error']=str(e)
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory');a=p.parse_args();result=audit(a.directory)
    (Path(a.directory)/'independent-orbital-audit.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2));raise SystemExit(0 if result['success'] else 1)
