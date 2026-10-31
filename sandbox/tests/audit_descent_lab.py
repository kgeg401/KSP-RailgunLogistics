"""Independent native landing receipt checks for a saved diagnostic descent.

An inherited delivery receipt is allowed only when unchanged from the initial
runtime ledger. This audit cannot award a delivery, fleet reuse or full trip.
The older undelivered-cargo audit remains unchanged.
"""
from pathlib import Path
import argparse,csv,json,math,re,xml.etree.ElementTree as ET
from audit_rail_flight import nodes,fields
from audit_delivery import native_parts,resources

def audit(directory):
    d=Path(directory);checks={};report={'success':False,'fullLogisticsRoundTrip':False,'fleetReuseVerified':False,'checks':checks}
    try:
        initialFleet=ET.parse(d/'trace/runtime-ledger.xml').getroot();finalFleet=ET.parse(d/'outcome/RailgunReusableFleet.xml').getroot()
        initial=initialFleet.find('Pods/PodRecord');final=finalFleet.find('Pods/PodRecord')
        identity=initial.findtext('VesselId');run=initial.findtext('RecoveryTestRunId');ids={int(n.text) for n in initial.findall('PartIds/unsignedInt')}
        checks['freshRunIdentity']=bool(re.fullmatch('[0-9a-f]{32}',run or '')) and final.findtext('RecoveryTestRunId')==run and 'recoveryRunId='+run in (d/'trace/events.txt').read_text()
        checks['diagnosticOnly']=initial.findtext('RecoveryTest')==final.findtext('RecoveryTest')=='true' and initial.findtext('Phase')=='Landing' and final.findtext('Phase')=='RecoveryTestComplete'
        checks['inheritedManifestUnchanged']=all(initial.findtext(k)==final.findtext(k) for k in ['VesselId','OrderId','CargoTransferred','CargoPart','Resource','CargoUnits','FlightsCompleted','OwnPort','TargetAnchor','TargetPort']) and int(final.findtext('FlightsCompleted'))==0
        checks['originalLedgerIds']=ids=={int(n.text) for n in final.findall('PartIds/unsignedInt')} and len(ids)==int(initial.findtext('OriginalParts'))==int(final.findtext('OriginalParts'))==29
        saves=[]
        for path in [d/'trace/initial.sfs',d/'outcome/persistent.sfs']:
            vs=[v for v in nodes('VESSEL',path.read_text(encoding='utf-8-sig').splitlines()) if any(l.strip()=='fleetId = '+identity for l in v)]
            if len(vs)!=1:raise ValueError('Missing/duplicate original physical subject')
            parts=native_parts('\n'.join(vs[0]));saves.append((fields(vs[0]),parts))
        checks['originalNativeIds']=all(set(p)==ids for _,p in saves)
        checks['nativeSaveLanded']=saves[-1][0].get('landed')=='True' and saves[-1][0].get('sit')=='LANDED'
        checks['actualHomeUnchanged']=all(initialFleet.findtext(k)==finalFleet.findtext(k) for k in ['HomeBody','HomeLatitude','HomeLongitude']) and finalFleet.findtext('HomeBody')=='Kerbin'
        lat=float(finalFleet.findtext('HomeLatitude'))+6/600000*180/math.pi
        lon=float(finalFleet.findtext('HomeLongitude'))-36/(600000*math.cos(lat*math.pi/180))*180/math.pi
        actual=saves[-1][0];actualLat=float(actual['lat']);actualLon=float(actual['lon'])
        chord=1200000*math.sqrt(math.sin(math.radians(actualLat-lat)/2)**2+math.cos(math.radians(lat))*math.cos(math.radians(actualLat))*math.sin(math.radians(actualLon-lon)/2)**2)
        report['independentNativePadMetres']=chord
        checks['independentActualPad']=0<=chord<=25
        cargo=int(initial.findtext('CargoPart'));resource=initial.findtext('Resource')
        tanks=[resources(p[cargo])[resource] for _,p in saves]
        checks['cargoUnchangedAndLocked']=tanks[0]==tanks[1] and not tanks[0][2]
        receiptPath=d/'outcome/LandingEvidence.tsv'
        receipts=list(csv.DictReader(receiptPath.open(),delimiter='\t')) if receiptPath.exists() else []
        receipts=[r for r in receipts if r['runId']==run and r['pod']==identity and r['accepted']=='True']
        checks['oneMatchingAcceptance']=len(receipts)==1
        checks['strictNativeEnvelope']=False
        if len(receipts)==1:
            row=receipts[0];metric={k:float(row[k]) for k in ['stableSeconds','padMetres','uprightDegrees','surfaceSpeed']};report.update(metric)
            checks['strictNativeEnvelope']=all(math.isfinite(x) for x in metric.values()) and metric['stableSeconds']>=5 and 0<=metric['padMetres']<=25 and 0<=metric['uprightDegrees']<=8 and 0<=metric['surfaceSpeed']<=.5 and row['nativeLanded']=='True' and int(row['intact'])==int(row['original'])==len(ids) and float(row['warp'])==1 and abs(float(row['physicsDt'])-.02)<1e-7
        trace=list(csv.DictReader((d/'trace/native.tsv').open(),delimiter='\t'));times=[float(r['ut']) for r in trace]
        checks['continuousForwardNativeClock']=len(trace)>100 and all(b>a for a,b in zip(times,times[1:]))
        checks['nativePhysicsAndParts']=bool(trace) and all(int(r['originalParts'])==len(ids) and 0<float(r['maxHeat'])<1 and float(r['warp'])==1 and abs(float(r['dt'])-.02)<1e-7 for r in trace)
        checks['noResourceIncrease']=all(all(float(b[k])<=float(a[k])+1e-6 for a,b in zip(trace,trace[1:])) for k in ['fuel','oxidizer','mono'])
        checks['noInitialResourceAddition']=all(float(trace[0][key])<=sum(resources(p).get(name,(0,0,False))[0] for p in saves[0][1].values())+1e-6 for key,name in [('fuel','LiquidFuel'),('oxidizer','Oxidizer'),('mono','MonoPropellant')])
        status=(d/'outcome/RailgunSandbox.status.txt').read_text();flags=re.search(r'^NativeChecks: (.+)$',status,re.M)
        flags=dict(item.split('=',1) for item in flags.group(1).split()) if flags else {}
        names=['infinitePropellant','infiniteElectricity','ignoreTemperature','unbreakableJoints','noCrashDamage']
        checks['cheatsDisabledAtOutcome']=bool(flags) and all(flags.get(k)=='False' for k in names)
        physics=d/'trace/physics-flags.tsv'
        if physics.exists():
            samples=list(csv.DictReader(physics.open(),delimiter='\t'));byTime={r['ut']:r for r in samples}
            checks['cheatsDisabledThroughoutSamples']=bool(samples) and all(r['ut'] in byTime and all(byTime[r['ut']][k]=='False' for k in names) for r in trace)
        report['runId']=run;report['remainingFuel']=float(trace[-1]['fuel']);report['peakHeatRatio']=max(float(r['maxHeat']) for r in trace)
        report['inactiveSlots']=[{'number':int(p.findtext('Number')),'initialPhase':p.findtext('Phase')} for p in initialFleet.findall('Pods/PodRecord') if p.findtext('VesselId')!=identity]
        report['limits']=['Landing audit alone does not establish an uninterrupted orbital return or full logistics route.','Inactive fleet slots are outside this landing audit; fleet reuse is unverified.']
        if not physics.exists():report['limits'].append('This older trace samples cheat flags at outcome only.')
        report['success']=all(checks.values())
    except Exception as e:report['error']=str(e)
    return report

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('directory');args=parser.parse_args();result=audit(args.directory)
    (Path(args.directory)/'independent-landing-audit.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2));raise SystemExit(0 if result['success'] else 1)
