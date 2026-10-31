"""Audit a seeded native orbital-return segment, never a full logistics trip."""
from pathlib import Path
import argparse,csv,json,math
from audit_descent_lab import audit as landing_audit
from audit_rail_flight import nodes,fields

def audit(directory):
    d=Path(directory);landing=landing_audit(d);checks={}
    report={'success':False,'orbitalReturnSegmentVerified':False,
            'fullLogisticsRoundTrip':False,'fleetReuseVerified':False,
            'landingAudit':landing,'checks':checks}
    try:
        checks['independentNativeLanding']=landing['success']
        source=(d/'trace/initial.sfs').read_text(encoding='utf-8-sig')
        vessels=[v for v in nodes('VESSEL',source.splitlines())
                 if any(l.strip()=='fleetId = 4d58f9c702c441f186fe5b01ea6a2b8e' for l in v)]
        if len(vessels)!=1:raise ValueError('Exactly one original native subject required')
        v=fields(vessels[0]);orbit=fields(next(nodes('ORBIT',vessels[0])))
        sma,ecc,alt=float(orbit['SMA']),float(orbit['ECC']),float(v['alt'])
        pe,ap=sma*(1-ecc)-600000,sma*(1+ecc)-600000
        report['initialOrbit']={'periapsisMetres':pe,'apoapsisMetres':ap,'altitudeMetres':alt}
        checks['initialStableKerbinOrbit']=(orbit['REF']=='1' and v['sit']=='ORBITING' and
            v['landed']=='False' and all(math.isfinite(x) for x in [sma,ecc,alt,pe,ap]) and
            0<=ecc<1 and pe>73000 and pe-100<=alt<=ap+100)
        rows=list(csv.DictReader((d/'trace/native.tsv').open(),delimiter='\t'))
        checks['initialNativeAltitudeContinuity']=bool(rows) and abs(float(rows[0]['altitude'])-alt)<5000
        transitions=list(csv.DictReader((d/'trace/native-ap-transitions.tsv').open(),delimiter='\t'))
        steps=[r['step'].rsplit('.',1)[-1] for r in transitions]
        expected=['DeorbitBurn','DecelerationBurn','FinalDescent'];cursor=0
        for step in steps:
            if cursor<len(expected) and step==expected[cursor]:cursor+=1
        checks['nativeReturnStagesInOrder']=cursor==len(expected)
        checks['observedEntryAndTouchdown']=bool(rows) and float(rows[0]['altitude'])>73000 and any(float(r['altitude'])<70000 for r in rows) and rows[-1]['landed']=='True'
        checks['forwardNormalPhysics']=all(landing['checks'].get(k) is True for k in
            ['continuousForwardNativeClock','nativePhysicsAndParts','cheatsDisabledThroughoutSamples','noResourceIncrease'])
        report['success']=report['orbitalReturnSegmentVerified']=all(checks.values())
        report['limits']=['Starts from an authentic saved post-delivery orbital state; inherited delivery is not newly verified.',
                          'Single-player native return segment only; no launch, delivery, refit, fleet reuse or multiplayer claim.']
    except Exception as e:report['error']=str(e)
    return report

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory');a=p.parse_args();result=audit(a.directory)
    (Path(a.directory)/'orbital-return-audit.json').write_text(json.dumps(result,indent=2))
    print(json.dumps(result,indent=2));raise SystemExit(0 if result['success'] else 1)
