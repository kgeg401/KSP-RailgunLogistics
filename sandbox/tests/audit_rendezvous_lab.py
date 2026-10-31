"""Independent saved-object audit of a private native approach replay."""
from pathlib import Path
import argparse,csv,hashlib,json,math,xml.etree.ElementTree as ET
from audit_rail_flight import fields,nodes
from audit_delivery import native_parts,resources

def audit(directory):
    d=Path(directory);t=d/'trace';checks={}
    result={'success':False,'nativeApproachHandoffVerified':False,'fullLogisticsRoundTrip':False,
            'deliveryVerified':False,'fleetReuseVerified':False,'checks':checks}
    try:
        process=json.loads((d/'process.json').read_text());done=json.loads((d/'completion.json').read_text())
        checks['nativeProcessCompleted']=done.get('nativeExitCode')==0 and done.get('observerCompleted') is True and not done.get('timeout')
        manifest=json.loads((d/'build/manifest.json').read_text())
        checks['pinnedBuilds']=set(manifest)=={'RailgunLogistics.dll','RailgunMultiplayer.Validation.dll'} and all(hashlib.sha256((d/'build'/name).read_bytes()).hexdigest()==value for name,value in manifest.items())
        fixture=process['fixture'];checks['declaredDiagnostic']=fixture.get('diagnostic') is True and fixture.get('fullLogisticsRoundTrip') is False
        checks['authenticInitialSave']=hashlib.sha256((t/'initial.sfs').read_bytes()).hexdigest()==fixture['fixtureFiles']['case-rendezvous-guard.sfs']
        initial=ET.parse(t/'initial.xml').getroot().find('Pods/PodRecord');final=ET.parse(t/'final.xml').getroot().find('Pods/PodRecord')
        identity=initial.findtext('VesselId');ids={int(x.text) for x in initial.findall('PartIds/unsignedInt')};cargo=int(initial.findtext('CargoPart'));resource=initial.findtext('Resource')
        checks['originalLedger']=len(ids)==29 and ids=={int(x.text) for x in final.findall('PartIds/unsignedInt')} and all(initial.findtext(k)==final.findtext(k) for k in ['VesselId','OrderId','CargoPart','Resource','CargoUnits','OwnPort','TargetAnchor','TargetPort'])
        checks['approachOnlyPhases']=initial.findtext('Phase')=='Rendezvous' and final.findtext('Phase')=='Docking'
        checks['noDeliveryOrReuse']=all(p.findtext('CargoTransferred')=='false' and p.findtext('FlightsCompleted')=='0' and p.findtext('RecoveryTest')=='false' for p in [initial,final])
        snapshots=[]
        for name in ['initial.sfs','final.sfs']:
            vs=[v for v in nodes('VESSEL',(t/name).read_text(encoding='utf-8-sig').splitlines()) if any(s.strip()=='fleetId = '+identity for s in v)]
            assert len(vs)==1,'Original physical pod missing or duplicated'
            snapshots.append((fields(vs[0]),native_parts('\n'.join(vs[0])),fields(next(nodes('ORBIT',vs[0])))))
        checks['originalNativeInventory']=all(set(parts)==ids for _,parts,_ in snapshots)
        nativeCargo=[resources(parts[cargo])[resource] for _,parts,_ in snapshots]
        checks['cargoLockedAndUnchanged']=nativeCargo[0]==nativeCargo[1] and nativeCargo[0][0]==100 and not nativeCargo[0][2]
        orbit=snapshots[-1][2];pe=float(orbit['SMA'])*(1-float(orbit['ECC']))-600000
        checks['actualSafeFinalOrbit']=orbit['REF']=='1' and math.isfinite(pe) and pe>73000 and 0<=float(orbit['ECC'])<1 and snapshots[-1][0]['landed']=='False'
        rows=list(csv.DictReader((t/'native.tsv').open(),delimiter='\t'));control=list(csv.DictReader((t/'native-control.tsv').open(),delimiter='\t'))
        checks['forwardPairedTelemetry']=len(rows)>50 and len(rows)==len(control) and all(a['ut']==b['ut'] for a,b in zip(rows,control)) and all(float(b['ut'])>float(a['ut']) for a,b in zip(rows,rows[1:]))
        checks['normalPhysicsThroughout']=bool(rows) and all(int(x['originalParts'])==29 and float(x['warp'])==1 and abs(float(x['dt'])-.02)<1e-7 and x['cheats']=='False' for x in rows)
        checks['nativeCargoAndHeatThroughout']=bool(control) and all(float(x['cargo'])==100 and x['cargoFlow']=='False' and 0<float(x['maxHeat'])<1 for x in control)
        checks['noResourceAddition']=bool(rows) and all(float(b['fuel'])<=float(a['fuel'])+1e-6 for a,b in zip(rows,rows[1:])) and all(all(float(b[k])<=float(a[k])+1e-6 for a,b in zip(control,control[1:])) for k in ['oxidizer','mono'])
        initialParts=snapshots[0][1]
        initialAmount=lambda name:sum(resources(p).get(name,(0,0,False))[0] for uid,p in initialParts.items() if uid!=cargo)
        checks['noInitialResourceAddition']=bool(rows) and float(rows[0]['fuel'])<=initialAmount('LiquidFuel')+1e-6 and float(control[0]['oxidizer'])<=initialAmount('Oxidizer')+1e-6 and float(control[0]['mono'])<=initialAmount('MonoPropellant')+1e-6
        checks['actualPoweredManeuver']=any(float(x['thrust'])>.05 for x in rows) and float(rows[-1]['fuel'])<float(rows[0]['fuel'])
        checks['nativeArrivalHandoff']=rows[-1]['phase']=='Docking' and float(rows[-1]['distance'])<160 and float(rows[-1]['relativeSpeed'])<1.2 and (t/'outcome.txt').read_text().splitlines()[0]=='NativePhase: Docking'
        checks['onlyApproachAndHandoff']=all(x['phase'] in ['Rendezvous','Docking'] for x in rows)
        result['samples']=len(rows);result['finalNativePeriapsis']=pe
        result['finalDistance']=float(rows[-1]['distance']);result['finalRelativeSpeed']=float(rows[-1]['relativeSpeed'])
        result['fuelUsed']=float(rows[0]['fuel'])-float(rows[-1]['fuel'])
        result['success']=result['nativeApproachHandoffVerified']=all(checks.values())
        result['limits']=['Asynchronous captured-object diagnostic, not a synchronized original scene replay.','Native approach handoff only: no docking, delivery, return, refit, multiplayer or full-trip credit.']
    except Exception as error:result['error']=str(error)
    return result

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory');args=parser.parse_args();value=audit(args.directory)
    (Path(args.directory)/'independent-approach-audit.json').write_text(json.dumps(value,indent=2));print(json.dumps(value,indent=2));raise SystemExit(0 if value['success'] else 1)
